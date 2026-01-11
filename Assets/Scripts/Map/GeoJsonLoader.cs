using Assets.Scripts.Clases;
using Assets.Scripts.Player;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Map
{
    public class GeoJsonLoader : MonoBehaviour
    {
        [Header("Files")]
        public TextAsset geoJsonFile;
        public TextAsset buildingsGeoJsonFile;
        public TextAsset roadsGeoJsonFile;
        public TextAsset bridgesGeoJsonFile; // тепер використовується для підйому террейну + малювання текстури
        public Terrain terrain;

        [Header("Prefabs")]
        public GameObject treePrefab;
        public Material riverMaterial;
        public Material roadMaterial;

        [Header("Chunk system")]
        public int chunkSize = 100;
        public int loadRadius = 3;
        public int maxBuildingsPerChunk = 60;

        [Header("Debug / UX")]
        public Text progressText;

        [Header("Prefabs (Buildings)")]
        public GameObject[] stalinkiPrefabs;
        public GameObject[] hrushchovkaPrefabs;
        public GameObject[] modernPrefabs;
        public GameObject[] glassTowerPrefabs;

        [Header("Optimization / Scaling")]
        [Range(0f, 1f)] public float buildingSpawnProbability = 0.3f;
        public int maxBuildingsPerFrame = 3;
        public float buildingScaleXZ = 4f;
        public float buildingScaleY = 3f;

        [Header("Bridge -> terrain painting")]
        [Tooltip("Index of terrain layer (splat) to use for 'bridge' texture")]
        public int bridgeSplatIndex = 1;
        public float bridgePaintRadius = 6f;
        public float bridgePaintStrength = 0.9f;
        [Tooltip("How high to raise terrain at center of bridge (meters)")]
        public float bridgeRaiseHeight = 2.5f;
        [Tooltip("Radius (meters) around entry/exit to affect heights")]
        public float bridgeRaiseRadius = 10f;
        [Tooltip("Falloff (0..1) for the raise (1 = linear, 0 = hard)")]
        [Range(0f, 1f)] public float bridgeRaiseFalloff = 0.6f;

        [Header("Global limits")]
        public int maxTotalBuildings = 2000;
        public int maxTotalRoads = 2000;

        [Header("Road Stitching")]
        [Tooltip("Meters — max distance to consider endpoints identical")]
        public float mergeThreshold = 8f;
        [Tooltip("Meters — subdivide original roads by this step for smoother snapping to terrain")]
        public float subdivideStep = 3f;

        // Bounds
        public double minLat, maxLat, minLon, maxLon;
        private Vector3 terrainSize;
        private TerrainData terrainData;
        private float[,] heightmap;
        private int heightmapWidth, heightmapHeight;

        private Transform player;

        private Dictionary<Vector2Int, ChunkData> chunks = new();
        private HashSet<Vector2Int> activeChunks = new();

        private int currentBuildings = 0;
        private int currentRoads = 0;

        // roads collected from geojson (polyline per feature)
        private List<List<Vector3>> globalRoads = new();

        public GameObject motherStatuePrefab;
        private class ChunkData
        {
            public List<List<Vector3>> riverSegments = new();
            public List<BuildingData> buildings = new();
            public GameObject container;
        }

        private class BuildingData
        {
            public List<Vector3> polygon;
            public float height;
            public float footprint;
        }

        public event Action OnGenerationComplete;

        [Header("Stadium Exclusion")]
        [Tooltip("Meters: radius around stadiums where buildings should NOT spawn")]
        public float stadiumExclusionRadius = 80f;
        [Tooltip("If true, try small nudges to find nearby free spot; otherwise skip building")]
        public bool attemptNudgeIfInside = true;
        [Tooltip("How many nudge attempts when trying to relocate a building (if attemptNudgeIfInside=true)")]
        public int nudgeMaxAttempts = 12;
        [Tooltip("Distance step (meters) used when nudging away from stadium")]
        public float nudgeStepDistance = 3f;

        public ProceduralParkingSpawner parkingSpawner;
        public ParkingCarSpawner parkingCarSpawner;
        bool isGenerating = false;
        Coroutine initCoroutine;
        Coroutine chunkCoroutine;
        public PlayerSpawner playerSpawner;

        float[,] cachedHeights;
        float[,,] cachedAlphas;
        bool terrainCached = false;

        // ------------------- lifecycle -------------------
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.N))
            {
                if (!isGenerating)
                    StartCoroutine(RegenerateCoroutine());
            }

            if (player == null)
            {
                var found = GameObject.FindWithTag("Player");
                if (found) player = found.transform;
            }
        }

        public void StartGeneration()
        {
            if (terrain == null) return;

            terrainData = terrain.terrainData;
            terrainSize = terrainData.size;

            if (!terrainCached)
            {
                cachedHeights = terrainData.GetHeights(
                    0, 0,
                    terrainData.heightmapResolution,
                    terrainData.heightmapResolution
                );

                cachedAlphas = terrainData.GetAlphamaps(
                    0, 0,
                    terrainData.alphamapWidth,
                    terrainData.alphamapHeight
                );

                terrainCached = true;
            }

            initCoroutine = StartCoroutine(InitCoroutine());
        }

        IEnumerator InitCoroutine()
        {
            PrepareTheme();

            UpdateProgress("📏 Step 1: Calculating bounds...");
            yield return StartCoroutine(CalculateBoundsCoroutine());

            UpdateProgress("🌍 Step 2: Parsing JSON...");
            yield return StartCoroutine(ParseAllDataAsync());
            yield return null;

            UpdateProgress("🛣 Step 3: Waiting for terrain to initialize...");
            float waitTimeout = 5f;
            float t = 0f;
            while ((terrain == null || terrain.terrainData == null || terrain.terrainData.heightmapResolution == 0) && t < waitTimeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);

            UpdateProgress("🌉 Step 4: Applying bridge terrain adjustments...");
            if (bridgesGeoJsonFile != null)
            {
                ApplyBridgesToTerrain();
                yield return null;
            }

            UpdateProgress("🛣 Step 5: Building roads...");
            BuildRoadsGlobal();

            yield return new WaitForSeconds(0.2f);

            DebugBridgeRaycasts();

            OnGenerationComplete += () =>
            {
                // Координати Батьківщини-Матері (Київ)
                SpawnMotherStatue(50.4264, 30.5560);
            };

            ApplyTheme();

            UpdateProgress("✅ Step 6: Generation complete!");
            OnGenerationComplete?.Invoke();

            chunkCoroutine = StartCoroutine(UpdateChunksCoroutine());
        }

        void ApplyTheme()
        {
            if (MapThemeManager.Instance == null)
            {
                Debug.LogWarning("MapThemeManager not found");
                return;
            }

            GameObject roadRoot = GameObject.Find("UnifiedRoadNetwork");

            MapThemeManager.Instance.ApplyTheme(
                terrain,
                roadRoot,
                transform // тут ВСІ chunks і будинки
            );
        }

        void PrepareTheme()
        {
            if (MapThemeManager.Instance != null)
                MapThemeManager.Instance.PickRandomTheme();
        }

        IEnumerator RegenerateCoroutine()
        {
            Debug.Log("🔄 REGENERATE START");
            isGenerating = true;

            // ❌ ЗУПИНЯЄМО ТІЛЬКИ ТЕ, ЩО ТРЕБА
            if (initCoroutine != null)
            {
                terrainCached = false;
                cachedHeights = null;
                cachedAlphas = null;

                StopCoroutine(initCoroutine);
                initCoroutine = null;
            }

            if (chunkCoroutine != null)
            {
                terrainCached = false;
                cachedHeights = null;
                cachedAlphas = null;

                StopCoroutine(chunkCoroutine);
                chunkCoroutine = null;
            }

            yield return null; // даємо Unity кадр

            Debug.Log("🧹 CLEAR WORLD");
            ClearWorld();

            yield return null;

            Debug.Log("🚀 START GENERATION");
            StartGeneration();

            playerSpawner.RespawnPlayer();

            isGenerating = false;
        }

        void ClearWorld()
        {
            foreach (var c in chunks.Values)
            {
                if (c.container)
                {
                    Destroy(c.container);
                }

                c.buildings.Clear();
                c.riverSegments.Clear();
            }

            chunks.Clear();
            activeChunks.Clear();
            globalRoads.Clear();

            Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }

        void FixRiverHeights(GameObject river)
        {
            var mf = river.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) return;

            // Робимо копію меша
            mf.mesh = Instantiate(mf.sharedMesh);
            Mesh mesh = mf.mesh;

            Vector3[] verts = mesh.vertices;

            float minY = float.MaxValue;
            float avgY = 0f;

            // 1️⃣ Переводимо всі точки в world-space і шукаємо реальний Y терейну
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = river.transform.TransformPoint(verts[i]);
                float terrainY = terrain.SampleHeight(w) + terrain.transform.position.y;

                w.y = terrainY + 0.01f;

                verts[i] = river.transform.InverseTransformPoint(w);

                avgY += w.y;
                if (w.y < minY) minY = w.y;
            }

            avgY /= verts.Length;

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // 2️⃣ Тепер нормалізуємо сам river.transform — щоб він не був у повітрі
            // Переміщаємо river так, щоб його pivot був на рівні мінімуму річки
            Vector3 pos = river.transform.position;
            pos.y = minY;
            river.transform.position = pos;

            // 3️⃣ Всі вершини тепер треба зсунути назад (бо pivot змінився)
            verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i].y -= (minY - 0.001f);
            }

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // ------------------- Motherland statue -------------------
        private void SpawnMotherStatue(double lat, double lon)
        {
            if (motherStatuePrefab == null)
            {
                Debug.LogError("❌ motherStatuePrefab is not assigned in inspector!");
                return;
            }

            // 1. Target position (WGS84 -> Unity)
            Vector3 pos = GeoToWorld(lat, lon);

            // 2. Шукаємо землю під точкою
            pos.y = SampleTerrainHeight(pos);

            // 3. Зсув, якщо точка попадає на будинок
            const float stepDistance = 2f;      // як далеко зсуватись
            const int maxSteps = 20;            // максимум 20 перевірок (радіус ~40м)
            bool foundPlace = false;
            Vector3 origin = pos; // важливо — зберігаємо початкову позицію

            for (int i = 0; i < maxSteps; i++)
            {
                Vector3 candidate = origin;

                if (i > 0)
                {
                    float angle = i * 25f;
                    Vector3 offset = new Vector3(
                        Mathf.Cos(angle * Mathf.Deg2Rad),
                        0,
                        Mathf.Sin(angle * Mathf.Deg2Rad)
                    ) * stepDistance * (i);
                    candidate = origin + offset;
                    candidate.y = SampleTerrainHeight(candidate);
                }

                if (!IsBuildingAt(candidate))
                {
                    pos = candidate;
                    foundPlace = true;
                    break;
                }
            }

            if (!foundPlace)
                Debug.LogWarning("⚠ Не вдалося знайти чисте місце, спавню де вийшло.");

            // 4. Спавнимо статую
            GameObject statue = Instantiate(motherStatuePrefab, pos, Quaternion.identity);
            statue.name = "MotherStatue";

            // Використовуємо правильну назву шару (якщо у тебе шар названо "Building")
            LayerMask buildingsMask = LayerMask.GetMask("Building"); // <- одиниця
            PlaceObjectSafely(statue, 25f, buildingsMask);

            // 5. Випрямляємо — краще identity або налаштовувана
            statue.transform.Rotate(90f, 0f, 0f, Space.Self);

            // 6. Масштаб (регулюй)
            statue.transform.localScale = Vector3.one * 1.0f;

            Debug.Log($"🗿Mother Statue placed at {pos}");
        }

        private bool PlaceObjectSafely(GameObject obj, float radius, LayerMask buildingMask)
        {
            const int maxIterations = 30;
            const float pushDistance = 4f;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                Vector3 pos = obj.transform.position;

                // 1. Беремо нижню точку bounds (ноги моделі)
                var rend = obj.GetComponentInChildren<Renderer>();
                if (!rend) return true;

                float baseY = rend.bounds.min.y + 0.2f;

                bool intersects = false;

                // 2. Перевіряємо 16 напрямків по колу
                for (int i = 0; i < 16; i++)
                {
                    float angle = (i / 16f) * Mathf.PI * 2f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;

                    Vector3 rayStart = new Vector3(pos.x + offset.x, baseY + 1f, pos.z + offset.z);
                    Vector3 rayDir = Vector3.down;

                    if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, 5f, buildingMask))
                    {
                        // влучили в будівлю — треба відсунути
                        intersects = true;

                        // 3. Відштовхуємо від центру будівлі
                        Vector3 pushDir = (pos - hit.point).normalized;
                        if (pushDir == Vector3.zero) pushDir = UnityEngine.Random.insideUnitSphere;

                        obj.transform.position += new Vector3(pushDir.x, 0, pushDir.z) * pushDistance;
                        break;
                    }
                }

                if (!intersects)
                    return true;
            }

            Debug.LogWarning("⚠ Не вдалося знайти чисте місце після 30 спроб");
            return false;
        }

        private bool IsBuildingAt(Vector3 pos)
        {
            // луч вниз на 500м
            Ray ray = new Ray(pos + Vector3.up * 500f, Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                // шукаємо за тегом або шаром
                if (hit.collider.CompareTag("Building"))
                    return true;
            }
            return false;
        }

        private float SampleTerrainHeight(Vector3 pos)
        {
            Ray ray = new Ray(pos + Vector3.up * 500f, Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                return hit.point.y;

            return pos.y; // запасний варіант
        }

        // ------------------- parsing -------------------
        IEnumerator CalculateBoundsCoroutine()
        {
            minLat = double.MaxValue; maxLat = double.MinValue;
            minLon = double.MaxValue; maxLon = double.MinValue;

            var geoData = JsonConvert.DeserializeObject<GeoJsonData>(geoJsonFile.text);
            var bData = JsonConvert.DeserializeObject<GeoJsonData>(buildingsGeoJsonFile.text);
            var rData = JsonConvert.DeserializeObject<GeoJsonData>(roadsGeoJsonFile.text);

            foreach (var f in geoData.features) UpdateBoundsFromFeature(f);
            foreach (var f in bData.features) UpdateBoundsFromFeature(f);
            foreach (var f in rData.features) UpdateBoundsFromFeature(f);

            Debug.Log("✅ Finished CalculateBoundsCoroutine");
            yield break;
        }

        void UpdateBoundsFromFeature(Feature feature)
        {
            if (feature?.geometry?.coordinates == null) return;
            var coords = feature.geometry.coordinates as JToken;

            switch (feature.geometry.type)
            {
                case "LineString":
                    foreach (var c in (JArray)coords) UpdateBounds((double)c[1], (double)c[0]);
                    break;
                case "Polygon":
                    foreach (var ring in (JArray)coords)
                        foreach (var c in (JArray)ring) UpdateBounds((double)c[1], (double)c[0]);
                    break;
            }
        }
        void UpdateBounds(double lat, double lon)
        {
            minLat = Math.Min(minLat, lat);
            maxLat = Math.Max(maxLat, lat);
            minLon = Math.Min(minLon, lon);
            maxLon = Math.Max(maxLon, lon);
        }

        IEnumerator ParseAllDataAsync()
        {
            var geoData = JsonConvert.DeserializeObject<GeoJsonData>(geoJsonFile.text);
            var bData = JsonConvert.DeserializeObject<GeoJsonData>(buildingsGeoJsonFile.text);
            var rData = JsonConvert.DeserializeObject<GeoJsonData>(roadsGeoJsonFile.text);

            int total = (geoData.features?.Length ?? 0)
                      + (bData.features?.Length ?? 0)
                      + (rData.features?.Length ?? 0);
            int processed = 0;
            int batch = 1000;

            // rivers
            foreach (var feature in geoData.features)
            {
                if (feature.geometry?.type == "LineString")
                {
                    var coords = JsonConvert.DeserializeObject<List<List<double>>>(feature.geometry.coordinates.ToString());
                    var pts = ConvertCoordsToWorld(coords);
                    AddRiverToChunks(pts);
                }

                processed++;
                if (processed % batch == 0) { UpdateProgress($"🌍 Parsed {processed}/{total}"); yield return null; }
            }

            // buildings
            foreach (var f in bData.features)
            {
                if (currentBuildings >= maxTotalBuildings) break;

                if (f.geometry?.type == "Polygon")
                {
                    if (UnityEngine.Random.value > buildingSpawnProbability)
                    {
                        processed++;
                        continue;
                    }

                    var rings = JsonConvert.DeserializeObject<List<List<List<double>>>>(f.geometry.coordinates.ToString());
                    if (rings != null && rings.Count > 0)
                    {
                        var pts = ConvertCoordsToWorld(rings[0]);
                        var bld = MakeBuildingData(pts);
                        if (bld != null)
                        {
                            AddBuildingToChunk(bld, pts);
                            currentBuildings++;
                        }
                    }
                }

                processed++;
                if (processed % batch == 0) { UpdateProgress($"🌍 Parsed {processed}/{total}"); yield return null; }
            }

            // roads: collect global roads (we will stitch later)
            foreach (var f in rData.features)
            {
                if (currentRoads >= maxTotalRoads) break;

                if (f.geometry?.type == "LineString")
                {
                    var coords = JsonConvert.DeserializeObject<List<List<double>>>(f.geometry.coordinates.ToString());
                    var pts = ConvertCoordsToWorld(coords);

                    // subdivide to ensure closer sampling along long segments
                    var subdiv = SubdividePolyline(pts, subdivideStep);
                    globalRoads.Add(subdiv);
                    currentRoads++;
                }

                processed++;
                if (processed % batch == 0)
                {
                    UpdateProgress($"🛣 Parsed {processed}/{total}");
                    yield return null;
                }
            }

            UpdateProgress($"✅ Finished parsing {processed} features");
        }

        // ------------------- adding to chunks -------------------
        void AddBuildingToChunk(BuildingData b, List<Vector3> pts)
        {
            if (b == null) return;
            var chunk = WorldToChunk(pts[0]);
            if (!chunks.ContainsKey(chunk)) chunks[chunk] = new ChunkData();

            if (chunks[chunk].buildings.Count < maxBuildingsPerChunk)
                chunks[chunk].buildings.Add(b);
        }
        void AddRiverToChunks(List<Vector3> pts)
        {
            var chunk = WorldToChunk(pts[0]);
            if (!chunks.ContainsKey(chunk)) chunks[chunk] = new ChunkData();
            chunks[chunk].riverSegments.Add(pts);
        }

        // ------------------- BRIDGES -> terrain painting (Variant 1) -------------------

        void ApplyBridgesToTerrain()
        {
            if (bridgesGeoJsonFile == null)
            {
                Debug.LogWarning("No bridgesGeoJsonFile provided.");
                return;
            }

            var json = JsonConvert.DeserializeObject<GeoJsonData>(bridgesGeoJsonFile.text);
            if (json?.features == null || json.features.Length == 0)
            {
                Debug.LogWarning("No bridge features in bridge file.");
                return;
            }

            Debug.Log($"Applying {json.features.Length} bridge(s) to terrain...");

            foreach (var feature in json.features)
            {
                if (feature.geometry == null) continue;
                if (feature.geometry.type != "Polygon" && feature.geometry.type != "MultiPolygon") continue;

                var coordsToken = feature.geometry.coordinates as JArray;
                if (coordsToken == null) continue;

                // Отримуємо список world точок полігону (беремо головне кільце)
                List<List<double>> ring = ExtractFirstRing(coordsToken);
                if (ring == null || ring.Count == 0) continue;

                var worldPts = ring.Select(c => GeoToWorld(c[1], c[0])).ToList();

                // Комп'ютуємо BoundingBox у світових координатах
                Bounds b = CalculateBounds(worldPts);

                // Довжина вздовж X і Z
                float sizeX = b.size.x;
                float sizeZ = b.size.z;

                bool axisIsX = sizeX >= sizeZ;

                // entry/exit — центри протилежних країв bounding box вздовж довшої осі
                Vector3 entry = axisIsX ? new Vector3(b.min.x, 0, b.center.z) : new Vector3(b.center.x, 0, b.min.z);
                Vector3 exit = axisIsX ? new Vector3(b.max.x, 0, b.center.z) : new Vector3(b.center.x, 0, b.max.z);

                // визначаємо висоту цільової поверхні як середній height у центрі bbox
                entry.y = terrain.SampleHeight(entry) + terrain.transform.position.y;
                exit.y = terrain.SampleHeight(exit) + terrain.transform.position.y;

                // Малюємо текстуру по лінії
                PaintTerrainAlongLine(entry, exit, bridgePaintRadius, bridgePaintStrength, bridgeSplatIndex);

                // Піднімаємо террейн на початку і кінці
                // Піднімаємо невеликий валик з falloff
                RaiseTerrainAlongLine(entry, exit, bridgeRaiseRadius, bridgeRaiseHeight, bridgeRaiseFalloff);
            }

            Debug.Log("Bridge terrain adjustments finished.");
        }

        // Беремо перший зовнішній кільце полігону (для Polygon і MultiPolygon)
        List<List<double>> ExtractFirstRing(JArray coords)
        {
            try
            {
                // MultiPolygon → [[[[lon, lat], ...]]]
                if (coords.First is JArray a && a.First is JArray b && b.First is JArray)
                {
                    // b.First - перший поліліст
                    var ring = ((JArray)b.First).ToObject<List<List<double>>>();
                    return ring;
                }
                // Polygon → [[[lon, lat], ...]]
                else if (coords.First is JArray poly && poly.First is JArray)
                {
                    var ring = ((JArray)poly.First).ToObject<List<List<double>>>();
                    return ring;
                }
                // Single ring → [[lon, lat], ...]
                else if (coords.First is JArray simple)
                {
                    var ring = coords.ToObject<List<List<double>>>();
                    return ring;
                }
            }
            catch { }
            return null;
        }

        Bounds CalculateBounds(List<Vector3> pts)
        {
            if (pts == null || pts.Count == 0) return new Bounds();

            Bounds b = new Bounds(pts[0], Vector3.zero);
            for (int i = 1; i < pts.Count; i++) b.Encapsulate(pts[i]);
            return b;
        }

        // ------------------- Terrain manipulation helpers -------------------

        // Підняття террейну вздовж відрізка entry->exit: ми модифікуємо heightmap частково.
        void RaiseTerrainAlongLine(Vector3 entryWorld, Vector3 exitWorld, float radiusMeters, float raiseMeters, float falloff)
        {
            if (terrain == null || terrain.terrainData == null) return;
            TerrainData td = terrain.terrainData;

            int hmW = td.heightmapResolution;
            int hmH = td.heightmapResolution;

            float[,] heights = cachedHeights;

            // Функція перетворення world -> heightmap indices
            Func<Vector3, Vector2Int> worldToHM = (Vector3 w) =>
            {
                Vector3 rel = w - terrain.transform.position;
                float normX = Mathf.Clamp01(rel.x / td.size.x);
                float normZ = Mathf.Clamp01(rel.z / td.size.z);
                int ix = Mathf.RoundToInt(normX * (hmW - 1));
                int iz = Mathf.RoundToInt(normZ * (hmH - 1));
                return new Vector2Int(ix, iz);
            };

            Vector2Int entryIdx = worldToHM(entryWorld);
            Vector2Int exitIdx = worldToHM(exitWorld);

            // Кількість пікселів радіусу
            int pixelRadius = Mathf.CeilToInt((radiusMeters / td.size.x) * hmW * 1.5f);

            // Пройдемо по прямокутнику, який накриває лінію + radius
            int minX = Mathf.Clamp(Mathf.Min(entryIdx.x, exitIdx.x) - pixelRadius, 0, hmW - 1);
            int maxX = Mathf.Clamp(Mathf.Max(entryIdx.x, exitIdx.x) + pixelRadius, 0, hmW - 1);
            int minY = Mathf.Clamp(Mathf.Min(entryIdx.y, exitIdx.y) - pixelRadius, 0, hmH - 1);
            int maxY = Mathf.Clamp(Mathf.Max(entryIdx.y, exitIdx.y) + pixelRadius, 0, hmH - 1);

            // Параметри нормалізації: висота в heightmap — від 0..1 відносно terrainData.size.y
            float heightNormalization = 1f / td.size.y;
            float raiseNormalized = raiseMeters * heightNormalization;

            // Пряма entry->exit в world для обчислення відстані до лінії
            Vector2 a2 = new Vector2(entryWorld.x, entryWorld.z);
            Vector2 b2 = new Vector2(exitWorld.x, exitWorld.z);

            for (int iz = minY; iz <= maxY; iz++)
            {
                for (int ix = minX; ix <= maxX; ix++)
                {
                    // координати цього пікселя в world
                    float normX = ix / (float)(hmW - 1);
                    float normZ = iz / (float)(hmH - 1);
                    Vector3 worldPos = new Vector3(terrain.transform.position.x + normX * td.size.x,
                                                   0,
                                                   terrain.transform.position.z + normZ * td.size.z);

                    Vector2 p2 = new Vector2(worldPos.x, worldPos.z);

                    // відстань до сегмента
                    float dist = DistancePointToSegment(p2, a2, b2);

                    if (dist <= radiusMeters)
                    {
                        // falloff: 0..1 (1 — сильніше піднімаємо)
                        float t = Mathf.Clamp01(1f - (dist / radiusMeters));
                        // застосуємо smooth falloff
                        float pow = Mathf.Pow(t, Mathf.Lerp(1f, 2.2f, 1f - falloff)); // трохи налаштовуваний
                        float add = raiseNormalized * pow;

                        // Записуємо: heightmap зберігає абсолютну висоту / size.y
                        float old = heights[iz, ix];
                        float newH = Mathf.Max(old, old + add); // піднімаємо, не опускаємо
                        heights[iz, ix] = newH;
                    }
                }
            }

            // Застосовуємо назад (оптимально — SetHeights у великому блоці)
            td.SetHeights(0, 0, heights);
            // Оновимо локально cached heightmap якщо потрібно
            heightmap = td.GetHeights(0, 0, td.heightmapResolution, td.heightmapResolution);
        }

        // Малює альфу terrain layers вздовж відрізка
        void PaintTerrainAlongLine(Vector3 entryWorld, Vector3 exitWorld, float radiusMeters, float strength, int splatIndex)
        {
            if (terrain == null || terrain.terrainData == null) return;
            TerrainData td = terrain.terrainData;

            int alphW = td.alphamapWidth;
            int alphH = td.alphamapHeight;
            int layers = td.alphamapLayers;

            if (splatIndex < 0 || splatIndex >= layers)
            {
                Debug.LogWarning($"bridgeSplatIndex {splatIndex} out of range (layers={layers}). Skipping paint.");
                return;
            }

            float[,,] alphas = cachedAlphas;

            Vector2 a2 = new Vector2(entryWorld.x, entryWorld.z);
            Vector2 b2 = new Vector2(exitWorld.x, exitWorld.z);

            // extent in alphamap pixels
            int pixelRadius = Mathf.CeilToInt((radiusMeters / td.size.x) * alphW * 1.5f);

            // entry/exit indices in alphamap coords
            Func<Vector3, Vector2Int> worldToAlpha = (Vector3 w) =>
            {
                Vector3 rel = w - terrain.transform.position;
                float normX = Mathf.Clamp01(rel.x / td.size.x);
                float normZ = Mathf.Clamp01(rel.z / td.size.z);
                int ix = Mathf.RoundToInt(normX * (alphW - 1));
                int iz = Mathf.RoundToInt(normZ * (alphH - 1));
                return new Vector2Int(ix, iz);
            };

            Vector2Int eIdx = worldToAlpha(entryWorld);
            Vector2Int xIdx = worldToAlpha(exitWorld);

            int minX = Mathf.Clamp(Mathf.Min(eIdx.x, xIdx.x) - pixelRadius, 0, alphW - 1);
            int maxX = Mathf.Clamp(Mathf.Max(eIdx.x, xIdx.x) + pixelRadius, 0, alphW - 1);
            int minY = Mathf.Clamp(Mathf.Min(eIdx.y, xIdx.y) - pixelRadius, 0, alphH - 1);
            int maxY = Mathf.Clamp(Mathf.Max(eIdx.y, xIdx.y) + pixelRadius, 0, alphH - 1);

            for (int iz = minY; iz <= maxY; iz++)
            {
                for (int ix = minX; ix <= maxX; ix++)
                {
                    float normX = ix / (float)(alphW - 1);
                    float normZ = iz / (float)(alphH - 1);
                    Vector3 worldPos = new Vector3(terrain.transform.position.x + normX * td.size.x,
                                                   0,
                                                   terrain.transform.position.z + normZ * td.size.z);
                    Vector2 p2 = new Vector2(worldPos.x, worldPos.z);

                    float dist = DistancePointToSegment(p2, a2, b2);

                    if (dist <= radiusMeters)
                    {
                        // weight 0..1
                        float t = Mathf.Clamp01(1f - (dist / radiusMeters));
                        float w = Mathf.Pow(t, 1.2f) * strength;

                        // normalize: increase target layer alpha, reduce others proportionally
                        float currentTarget = alphas[iz, ix, splatIndex];
                        float desired = Mathf.Clamp01(currentTarget + w);

                        // compute scale factor to reduce other channels so sum==1
                        float sumOthers = 0f;
                        for (int l = 0; l < layers; l++) if (l != splatIndex) sumOthers += alphas[iz, ix, l];

                        float remaining = Mathf.Clamp01(1f - desired);
                        if (sumOthers > 0f)
                        {
                            float scale = remaining / sumOthers;
                            for (int l = 0; l < layers; l++)
                            {
                                if (l == splatIndex) alphas[iz, ix, l] = desired;
                                else alphas[iz, ix, l] *= scale;
                            }
                        }
                        else
                        {
                            // усі інші нулі — просто ставимо target==1
                            for (int l = 0; l < layers; l++)
                                alphas[iz, ix, l] = (l == splatIndex) ? 1f : 0f;
                        }
                    }
                }
            }

            td.SetAlphamaps(0, 0, alphas);
        }

        static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            Vector2 ap = p - a;
            float ab2 = Vector2.Dot(ab, ab);
            if (ab2 == 0f) return Vector2.Distance(p, a);
            float t = Vector2.Dot(ap, ab) / ab2;
            t = Mathf.Clamp01(t);
            Vector2 proj = a + ab * t;
            return Vector2.Distance(p, proj);
        }

        // ------------------- ROAD stitching algorithm -------------------
        List<Vector3> SubdividePolyline(List<Vector3> poly, float step)
        {
            if (poly == null || poly.Count < 2) return poly;
            if (step <= 0f) return new List<Vector3>(poly);

            var outPts = new List<Vector3>();
            for (int i = 0; i < poly.Count - 1; i++)
            {
                Vector3 a = poly[i];
                Vector3 b = poly[i + 1];
                float dist = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
                int steps = Mathf.Max(1, Mathf.CeilToInt(dist / step));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    Vector3 p = Vector3.Lerp(a, b, t);
                    outPts.Add(p);
                }
            }
            outPts.Add(poly[^1]);
            return outPts;
        }

        private Vector3 SnapToSurface(Vector3 pos, float offsetY = 0f)
        {
            Ray ray = new Ray(pos + Vector3.up * 1000f, Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 2000f);

            float minY = float.MaxValue;
            Vector3 surfacePoint = pos;

            foreach (var hit in hits)
            {
                string tag = hit.collider.gameObject.tag;
                int layer = hit.collider.gameObject.layer;

                // Ігноруємо будівлі, річки, мости
                if (tag == "Building" || tag == "Bridge" || tag == "River") continue;
                if (layer == LayerMask.NameToLayer("Building") || layer == LayerMask.NameToLayer("Water")) continue;

                if (hit.point.y < minY)
                {
                    minY = hit.point.y;
                    surfacePoint = hit.point;
                }
            }

            if (minY < float.MaxValue)
                return surfacePoint + Vector3.up * offsetY;

            return pos; // fallback, якщо нічого не знайдено
        }

        void BuildRoadsGlobal()
        {
            if (globalRoads.Count == 0) return;
            UpdateProgress("🛣 Створюємо дороги...");

            // 1️⃣ Збираємо всі точки
            List<Vector3> allPoints = new();
            foreach (var seg in globalRoads)
            {
                if (seg != null && seg.Count > 1)
                    allPoints.AddRange(seg);
            }

            // 2️⃣ Готуємо root
            GameObject roadRoot = new GameObject("UnifiedRoadNetwork");
            roadRoot.tag = "UnifiedRoadNetwork";
            roadRoot.transform.parent = transform;

            var mf = roadRoot.AddComponent<MeshFilter>();
            var mr = roadRoot.AddComponent<MeshRenderer>();
            mr.sharedMaterial = roadMaterial;

            // 3️⃣ Згладжуємо полігон
            List<Vector3> refined = new();
            float step = 2f;
            for (int i = 0; i < allPoints.Count - 1; i++)
            {
                Vector3 a = allPoints[i];
                Vector3 b = allPoints[i + 1];
                float dist = Vector3.Distance(a, b);
                int sub = Mathf.Max(1, Mathf.CeilToInt(dist / step));
                for (int s = 0; s < sub; s++)
                {
                    refined.Add(Vector3.Lerp(a, b, s / (float)sub));
                }
            }
            if (allPoints.Count > 0) refined.Add(allPoints[^1]);

            for (int i = 0; i < refined.Count; i++)
            {
                float y = terrain.SampleHeight(refined[i]) + terrain.transform.position.y;
                refined[i] = new Vector3(refined[i].x, y + 0.05f, refined[i].z);
            }

            // 4. Згладжуємо ТІЛЬКИ висоту, не рухаючи XZ
            int smoothIterations = 8;

            for (int iter = 0; iter < smoothIterations; iter++)
            {
                for (int i = 1; i < refined.Count - 1; i++)
                {
                    float yPrev = refined[i - 1].y;
                    float yCurr = refined[i].y;
                    float yNext = refined[i + 1].y;

                    // Ніяких змін XZ!
                    float ySmooth = (yPrev + yCurr * 2f + yNext) / 4f;

                    refined[i] = new Vector3(
                        refined[i].x,
                        ySmooth,
                        refined[i].z
                    );
                }
            }

            // ⛔ Фінальна корекція — дорога не може бути нижче терейну
            for (int i = 0; i < refined.Count; i++)
            {
                float terrainY = terrain.SampleHeight(refined[i]) + terrain.transform.position.y;

                if (refined[i].y < terrainY + 0.05f)
                    refined[i] = new Vector3(refined[i].x, terrainY + 0.05f, refined[i].z);
            }

            // 5️⃣ (Попередньо ми вже підняли террейн для мостів у ApplyBridgesToTerrain)
            // тут нічого не робимо спеціально — дороги йдуть по терейну вже з піднятими входами.

            // 6️⃣ Генеруємо mesh дороги
            List<Vector3> verts = new();
            List<int> tris = new();
            List<Vector2> uvs = new();

            float halfW = 2.5f; // трохи товщі щоб було видно підйоми
            int vi = 0;

            for (int i = 0; i < refined.Count - 1; i++)
            {
                Vector3 p1 = refined[i];
                Vector3 p2 = refined[i + 1];
                Vector3 dir = (p2 - p1).normalized;
                Vector3 left = Vector3.Cross(Vector3.up, dir) * halfW;

                Vector3 v1 = p1 - left;
                Vector3 v2 = p1 + left;
                Vector3 v3 = p2 - left;
                Vector3 v4 = p2 + left;

                verts.AddRange(new[] { v1, v2, v3, v4 });
                uvs.AddRange(new[]
                {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1)
                });
                tris.AddRange(new[]
                {
                    vi, vi + 2, vi + 1,
                    vi + 2, vi + 3, vi + 1
                });

                vi += 4;
            }

            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.mesh = mesh;

            var mc = roadRoot.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = false;

            Debug.Log("✅ Дороги побудовані (по террейну, з підняттям входів мостів).");

            parkingSpawner.Init(roadRoot);
            parkingSpawner.OnParkingsSpawned += parkingCarSpawner.SpawnCars;
        }

        // ------------------- chunks spawn / buildings -------------------
        IEnumerator UpdateChunksCoroutine()
        {
            while (player == null) yield return null;

            while (true)
            {
                var playerChunk = WorldToChunk(player.position);

                for (int dx = -loadRadius; dx <= loadRadius; dx++)
                {
                    for (int dz = -loadRadius; dz <= loadRadius; dz++)
                    {
                        var pos = playerChunk + new Vector2Int(dx, dz);
                        if (!chunks.ContainsKey(pos)) continue;
                        if (!activeChunks.Contains(pos))
                        {
                            SpawnChunk(chunks[pos], pos);
                            activeChunks.Add(pos);
                        }
                    }
                }

                List<Vector2Int> toRemove = new();
                foreach (var chunk in activeChunks)
                {
                    if (Vector2Int.Distance(chunk, playerChunk) > loadRadius + 1)
                    {
                        Destroy(chunks[chunk].container);
                        chunks[chunk].container = null;
                        toRemove.Add(chunk);
                    }
                }
                foreach (var r in toRemove) activeChunks.Remove(r);

                yield return new WaitForSeconds(1f);
            }
        }

        void SpawnChunk(ChunkData data, Vector2Int pos)
        {
            if (data.container != null) return;
            data.container = new GameObject($"Chunk_{pos.x}_{pos.y}");
            data.container.transform.parent = transform;

            BuildRiversForChunk(data);
            StartCoroutine(SpawnBuildingsCoroutine(data));
        }

        IEnumerator SpawnBuildingsCoroutine(ChunkData data)
        {
            if (data == null || data.container == null) yield break;

            int spawnedThisFrame = 0;

            foreach (var bld in data.buildings)
            {
                if (bld == null || bld.polygon == null || bld.polygon.Count == 0)
                    continue;

                if (UnityEngine.Random.value > buildingSpawnProbability)
                    continue;

                // Обчислюємо середню висоту по всіх вершинах будівлі
                float totalY = 0f;
                for (int i = 0; i < bld.polygon.Count; i++)
                    totalY += SnapToSurface(bld.polygon[i]).y;
                float avgY = totalY / bld.polygon.Count;

                Vector3 pos = GetPolygonCentroid(bld.polygon);
                pos.y = avgY;

                if (bld == null || data.container == null)
                    continue;

                // --- NEW: перевіряємо чи позиція потрапляє в зону стадіону ---
                if (IsPositionInStadiumZone(pos, stadiumExclusionRadius))
                {
                    if (!attemptNudgeIfInside)
                    {
                        // Пропускаємо цю будівлю
                        continue;
                    }
                    else
                    {
                        bool relocated = TryNudgeAwayFromStadium(ref pos, stadiumExclusionRadius, nudgeMaxAttempts, nudgeStepDistance);
                        if (!relocated)
                        {
                            // Не вдалося знайти вільну позицію — скіпаємо
                            continue;
                        }
                        // оновимо висоту після зсуву
                        pos.y = SnapToSurface(pos).y;
                    }
                }

                CreateProxyBuilding(pos, bld.height, bld.footprint, data.container.transform);

                spawnedThisFrame++;
                if (spawnedThisFrame >= maxBuildingsPerFrame)
                {
                    spawnedThisFrame = 0;
                    yield return null;
                }
            }
        }

        // ----------------- ХЕЛПЕРИ -----------------

        bool IsPositionInStadiumZone(Vector3 worldPos, float radiusMeters)
        {
            var stadiums = RealWorldStadiumSpawner.stadiumCenters;

            // якщо зі списком щось не так — просто повертаємо false
            if (stadiums == null || stadiums.Count == 0)
                return false;

            Vector2 p2 = new Vector2(worldPos.x, worldPos.z);

            for (int i = 0; i < stadiums.Count; i++)
            {
                Vector2 s = stadiums[i];

                if (Vector2.Distance(p2, s) <= radiusMeters)
                    return true;
            }

            return false;
        }

        // Прагнемо змістити позицію по спіралі/круговим крокам подалі від стадіону.
        // Повертає true якщо знайшли вільну позицію (і worldPos змінено).
        bool TryNudgeAwayFromStadium(ref Vector3 worldPos, float exclusionRadius, int maxAttempts, float stepDistance)
        {
            Vector3 origin = worldPos;
            for (int i = 1; i <= maxAttempts; i++)
            {
                float angleDeg = (i * 37f) % 360f; // псевдомиксований кут
                float dist = stepDistance * i;
                Vector3 offset = new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad), 0f, Mathf.Sin(angleDeg * Mathf.Deg2Rad)) * dist;
                Vector3 candidate = origin + offset;
                candidate = SnapToTerrain(candidate, 0.02f);
                // перевіряємо проти stadium zone з трохи більшим радіусом, щоб не потрапити на край
                if (!IsPositionInStadiumZone(candidate, exclusionRadius * 0.95f))
                {
                    worldPos = candidate;
                    return true;
                }
            }
            return false;
        }

        void BuildRiversForChunk(ChunkData data)
        {
            if (data == null || data.container == null) return;

            GameObject riversContainer = new GameObject("Rivers");
            riversContainer.transform.parent = data.container.transform;
            riversContainer.transform.localPosition = Vector3.zero;
            riversContainer.transform.localRotation = Quaternion.identity;
            riversContainer.transform.localScale = Vector3.one;

            foreach (var seg in data.riverSegments)
            {
                for (int i = 0; i < seg.Count; i++)
                    seg[i] = SnapToTerrain(seg[i], 0f);

                var riverObj = CreateRiverMesh(seg, riversContainer.transform);
                if (riverObj != null)
                    FixRiverHeights(riverObj);
            }
        }

        // ------------------- helpers -------------------
        List<Vector3> ConvertCoordsToWorld(List<List<double>> coords)
        {
            var pts = new List<Vector3>();
            foreach (var c in coords)
                pts.Add(SnapToTerrain(GeoToWorld(c[1], c[0])));
            return pts;
        }

        BuildingData MakeBuildingData(List<Vector3> pts)
        {
            if (pts.Count < 3) return null;

            Vector3 center = GetPolygonCentroid(pts);
            float avgDist = 0f;
            foreach (var p in pts) avgDist += Vector3.Distance(center, p);
            avgDist /= pts.Count;

            float footprint = Mathf.Clamp(avgDist, 4f, 14f);
            float height = UnityEngine.Random.Range(15f, 60f);

            return new BuildingData { polygon = pts, footprint = footprint, height = height };
        }

        Vector2Int WorldToChunk(Vector3 pos)
        {
            if (terrain == null || terrain.terrainData == null)
                return Vector2Int.zero;

            int cx = Mathf.FloorToInt((pos.x - terrain.transform.position.x) / chunkSize);
            int cz = Mathf.FloorToInt((pos.z - terrain.transform.position.z) / chunkSize);
            return new Vector2Int(cx, cz);
        }

        Vector3 GetPolygonCentroid(List<Vector3> poly)
        {
            Vector3 c = Vector3.zero;
            foreach (var p in poly) c += p;
            c /= poly.Count;
            c.y = 0f;
            return SnapToTerrain(c);
        }

        public Vector3 GeoToWorld(double lat, double lon)
        {
            double u = (lon - minLon) / (maxLon - minLon);
            double v = (lat - minLat) / (maxLat - minLat);
            float x = (float)(u * terrainSize.x) + terrain.transform.position.x;
            float z = (float)(v * terrainSize.z) + terrain.transform.position.z;

            return new Vector3(x, 0f, z);
        }

        private Vector3 SnapToTerrain(Vector3 worldPos, float yOffset = 0.02f)
        {
            if (terrain == null)
                terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null)
                return worldPos;

            Vector3 terrainPos = terrain.transform.position;
            Vector3 tSize = terrain.terrainData.size;

            float x = Mathf.Clamp(worldPos.x, terrainPos.x, terrainPos.x + tSize.x);
            float z = Mathf.Clamp(worldPos.z, terrainPos.z, terrainPos.z + tSize.z);

            float height = terrain.SampleHeight(new Vector3(x, 0, z)) * terrain.transform.localScale.y
                           + terrain.transform.position.y;

            Vector3 snapped = new Vector3(x, height + yOffset, z);
            return snapped;
        }

        void CreateProxyBuilding(Vector3 pos, float height, float footprint, Transform parent)
        {
            if (parent == null) return;

            GameObject[] pool = null;
            if (height > 40f) pool = glassTowerPrefabs;
            else if (height > 25f) pool = modernPrefabs;
            else if (footprint > 12f) pool = stalinkiPrefabs;
            else pool = hrushchovkaPrefabs;

            GameObject go = null;

            if (pool != null && pool.Length > 0)
            {
                var prefab = pool[UnityEngine.Random.Range(0, pool.Length)];
                if (prefab == null) return;

                go = Instantiate(prefab, parent);
                go.transform.position = pos;

                float uniformScale = footprint * 0.08f;
                go.transform.localScale = Vector3.one * uniformScale;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.position = pos;
                go.transform.localScale = new Vector3(footprint, height, footprint);
                go.transform.SetParent(parent, true);
            }

            if (go == null) return;

            go.tag = "Building";
            go.layer = LayerMask.NameToLayer("Building");

            Vector3 groundedPos = SnapToSurface(go.transform.position, 0f);
            go.transform.position = groundedPos;

            Collider col = go.GetComponentInChildren<Collider>();
            if (col == null)
            {
                MeshFilter mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                }
                else
                {
                    go.AddComponent<BoxCollider>();
                }
            }
            else
            {
                col.isTrigger = false;
            }

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);

            Vector3 groundPos = SnapToSurface(go.transform.position, -0.05f);
            go.transform.position = groundPos;

            Physics.SyncTransforms();

            MeshFilter[] meshParts = go.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in meshParts)
            {
                if (mf.sharedMesh == null) continue;

                MeshCollider mc = mf.GetComponent<MeshCollider>();
                if (mc == null)
                    mc = mf.gameObject.AddComponent<MeshCollider>();

                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                mc.isTrigger = false;
            }

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds totalBounds = renderers[0].bounds;
                foreach (Renderer r in renderers)
                    totalBounds.Encapsulate(r.bounds);

                BoxCollider shell = go.GetComponent<BoxCollider>();
                if (shell == null)
                    shell = go.AddComponent<BoxCollider>();

                shell.center = go.transform.InverseTransformPoint(totalBounds.center);
                shell.size = totalBounds.size * 1.05f;
            }
        }

        GameObject CreateRiverMesh(List<Vector3> points, Transform parent)
        {
            if (points.Count < 2)
                return null;

            // ⛔ ПЕРЕД побудовою сітки – притискаємо ВСІ точки до терейну
            for (int i = 0; i < points.Count; i++)
            {
                float terrainY = terrain.SampleHeight(points[i]) + terrain.transform.position.y;
                points[i] = new Vector3(points[i].x, terrainY + 0.02f, points[i].z);
            }

            GameObject river = new GameObject("RiverSegment");
            river.transform.parent = parent;

            MeshFilter mf = river.AddComponent<MeshFilter>();
            MeshRenderer mr = river.AddComponent<MeshRenderer>();
            mr.sharedMaterial = riverMaterial;

            List<Vector3> verts = new();
            List<int> tris = new();
            List<Vector2> uvs = new();

            float halfWidth = 1.2f;
            int v = 0;

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[i + 1];

                Vector3 dir = (b - a).normalized;
                Vector3 left = Vector3.Cross(Vector3.up, dir) * halfWidth;

                Vector3 v1 = a - left;
                Vector3 v2 = a + left;
                Vector3 v3 = b - left;
                Vector3 v4 = b + left;

                verts.Add(v1);
                verts.Add(v2);
                verts.Add(v3);
                verts.Add(v4);

                tris.Add(v);
                tris.Add(v + 2);
                tris.Add(v + 1);

                tris.Add(v + 2);
                tris.Add(v + 3);
                tris.Add(v + 1);

                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(0, 1));
                uvs.Add(new Vector2(1, 1));

                v += 4;
            }

            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mf.mesh = mesh;

            return river;
        }

        void UpdateProgress(string msg)
        {
            if (progressText != null) progressText.text = msg;
            Debug.Log(msg);
        }

        // ------------------- helper: Union-Find -------------------
        class UnionFind
        {
            int[] parent;
            int[] rank;
            public UnionFind(int n)
            {
                parent = new int[n];
                rank = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
            }
            public int Find(int x)
            {
                if (parent[x] != x) parent[x] = Find(parent[x]);
                return parent[x];
            }
            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return;
                if (rank[ra] < rank[rb]) parent[ra] = rb;
                else if (rank[rb] < rank[ra]) parent[rb] = ra;
                else { parent[rb] = ra; rank[ra]++; }
            }
        }

        private void DebugBridgeRaycasts()
        {
            Debug.Log("=== 🧭 DEBUG BRIDGE RAYCASTS START ===");

            var roads = GameObject.FindGameObjectsWithTag("UnifiedRoadNetwork");
            if (roads.Length == 0)
            {
                var fallback = GameObject.Find("UnifiedRoadNetwork");
                if (fallback != null)
                {
                    fallback.tag = "UnifiedRoadNetwork";
                    roads = new[] { fallback };
                }
            }

            if (roads.Length == 0)
            {
                Debug.LogWarning("No roads found for debug raycasts.");
                return;
            }

            foreach (var roadRoot in roads)
            {
                MeshFilter mf = roadRoot.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh)
                {
                    Debug.LogWarning($"Road '{roadRoot.name}' has no valid mesh.");
                    continue;
                }

                Mesh mesh = mf.sharedMesh;
                Vector3[] verts = mesh.vertices;
                int step = Mathf.Max(1, verts.Length / 8);

                int hits = 0, misses = 0;

                for (int i = 0; i < verts.Length; i += step)
                {
                    Vector3 worldPos = roadRoot.transform.TransformPoint(verts[i]);
                    Ray ray = new Ray(worldPos + Vector3.up * 500f, Vector3.down);

                    if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                    {
                        Debug.DrawLine(ray.origin, hit.point, Color.green, 10f);
                        hits++;
                    }
                    else
                    {
                        Debug.DrawLine(ray.origin, ray.origin + Vector3.down * 1000f, Color.red, 10f);
                        misses++;
                    }
                }

                Debug.Log($"Road '{roadRoot.name}': hits={hits}, misses={misses}");
            }

            Debug.Log("=== ✅ DEBUG BRIDGE RAYCASTS END ===");
        }
    }
}