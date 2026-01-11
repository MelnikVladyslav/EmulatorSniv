using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Assets.Scripts.Clases;

public class ParkGenerator : MonoBehaviour
{
    [Header("Files")]
    public TextAsset landuseGeoJson;

    [Header("Decor Prefabs")]
    public GameObject[] treePrefabs;
    public GameObject[] bushPrefabs;
    public GameObject benchPrefab;
    public GameObject lightPrefab;
    public GameObject playgroundPrefab;

    [Header("Terrain")]
    public Terrain terrain;

    [Header("Spawn settings")]
    public float treeDensity = 0.15f;     // trees per m²
    public float bushDensity = 0.05f;     // bushes per m²
    public float benchSpacing = 12f;      // distance between benches
    public float lightSpacing = 16f;      // distance between lamps
    public float minParkArea = 200f;      // ignore tiny polygons
    public float playgroundChance = 0.4f; // 40% parks have a playground

    // To block buildings
    public static List<Vector2> parkCenters = new List<Vector2>();
    public static float parkExclusionRadius = 80f;

    public int maxTreesPerPark = 300;
    public int maxBushesPerPark = 200;

    void Start()
    {
        if (!landuseGeoJson)
        {
            Debug.LogError("❌ landuse json = null");
            return;
        }

        GenerateParks();
    }

    void GenerateParks()
    {
        parkCenters.Clear();

        GeoJsonData land = JsonConvert.DeserializeObject<GeoJsonData>(landuseGeoJson.text);
        if (land?.features == null)
        {
            Debug.LogError("❌ landuse file has no features!");
            return;
        }

        foreach (var f in land.features)
        {
            if (!IsGreenLanduse(f.properties))
                continue;

            List<Vector3> polygon = ExtractPolygon(f.geometry);
            if (polygon == null || polygon.Count < 3)
                continue;

            float area = ComputePolygonArea(polygon);
            if (area < minParkArea)
                continue;

            Vector3 centroid = ComputeCentroid(polygon);
            centroid.y = terrain.SampleHeight(centroid) + terrain.transform.position.y;

            // Register for building-blocking
            parkCenters.Add(new Vector2(centroid.x, centroid.z));

            GenerateParkEnvironment(polygon, centroid);
        }

        Debug.Log("🌲 Parks generated: " + parkCenters.Count);
    }

    // ===========================
    //     PARK ENVIRONMENT
    // ===========================
    void GenerateParkEnvironment(List<Vector3> polygon, Vector3 centroid)
    {
        // 1) Spawn trees
        FillWithObjects(polygon, treePrefabs, treeDensity);

        // 2) Spawn bushes
        FillWithObjects(polygon, bushPrefabs, bushDensity);

        // 3) Benches along border
        SpawnAlongBorder(polygon, benchPrefab, benchSpacing);

        // 4) Lights along border
        SpawnAlongBorder(polygon, lightPrefab, lightSpacing);

        // 5) Playground in center
        if (playgroundPrefab && Random.value < playgroundChance)
        {
            Vector3 playPos = centroid;
            playPos.y = terrain.SampleHeight(playPos) + terrain.transform.position.y;
            Instantiate(playgroundPrefab, playPos, Quaternion.identity);
        }
    }

    // Fill area with objects using Poisson-like scattering
    void FillWithObjects(List<Vector3> polygon, GameObject[] prefabs, float density)
    {
        if (prefabs == null || prefabs.Length == 0)
            return;

        float area = ComputePolygonArea(polygon);
        int count = Mathf.RoundToInt(area * density);

        if (prefabs == treePrefabs)
            count = Mathf.Min(count, maxTreesPerPark);
        if (prefabs == bushPrefabs)
            count = Mathf.Min(count, maxBushesPerPark);

        for (int i = 0; i < count; i++)
        {
            Vector3 p = RandomPointInPolygon(polygon);
            float y = terrain.SampleHeight(p) + terrain.transform.position.y;
            p.y = y;

            var prefab = prefabs[Random.Range(0, prefabs.Length)];
            Instantiate(prefab, p, Quaternion.Euler(0, Random.Range(0, 360f), 0));
        }
    }

    void SpawnAlongBorder(List<Vector3> polygon, GameObject prefab, float spacing)
    {
        if (!prefab) return;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[(i + 1) % polygon.Count];

            float dist = Vector3.Distance(a, b);
            int steps = Mathf.FloorToInt(dist / spacing);

            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;

                Instantiate(prefab, p, Quaternion.identity);
            }
        }
    }

    // ===========================
    //       HELPERS
    // ===========================

    bool IsGreenLanduse(Dictionary<string, object> props)
    {
        if (props == null) return false;

        if (!props.TryGetValue("landuse", out object val))
            return false;

        string t = val.ToString();

        return t == "grass" ||
               t == "forest" ||
               t == "meadow" ||
               t == "recreation_ground" ||
               t == "park" ||
               t == "greenfield" ||
               t == "village_green";
    }

    List<Vector3> ExtractPolygon(Geometry g)
    {
        if (g == null || g.coordinates == null)
            return null;

        try
        {
            if (g.type == "Polygon")
            {
                var ring = ((JArray)g.coordinates)[0].ToObject<List<List<double>>>();
                return ToUnity(ring);
            }
            if (g.type == "MultiPolygon")
            {
                var ring = ((JArray)g.coordinates)[0][0].ToObject<List<List<double>>>();
                return ToUnity(ring);
            }
        }
        catch { }

        return null;
    }

    List<Vector3> ToUnity(List<List<double>> coords)
    {
        List<Vector3> pts = new List<Vector3>();

        foreach (var c in coords)
            pts.Add(GeoToWorld(c[1], c[0]));

        return pts;
    }

    Vector3 GeoToWorld(double lat, double lon)
    {
        double u = (lon - 30.4659854) / (30.6259086 - 30.4659854);
        double v = (lat - 50.3733126) / (50.5090745 - 50.3733126);

        float x = (float)(u * 2048f);
        float z = (float)(v * 2048f);

        float y = terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;

        return new Vector3(x, y, z);
    }

    float ComputePolygonArea(List<Vector3> pts)
    {
        float sum = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % pts.Count];
            sum += (a.x * b.z - b.x * a.z);
        }
        return Mathf.Abs(sum) * 0.5f;
    }

    Vector3 ComputeCentroid(List<Vector3> pts)
    {
        Vector3 c = Vector3.zero;
        foreach (var p in pts) c += p;
        c /= pts.Count;
        return c;
    }

    Vector3 RandomPointInPolygon(List<Vector3> poly)
    {
        int i = Random.Range(1, poly.Count - 2);

        Vector3 a = poly[0];
        Vector3 b = poly[i];
        Vector3 c = poly[i + 1];

        float r1 = Mathf.Sqrt(Random.value);
        float r2 = Random.value;

        return a * (1 - r1) + b * (r1 * (1 - r2)) + c * (r1 * r2);
    }

    bool PointInPolygon(Vector3 p, List<Vector3> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if (((poly[i].z > p.z) != (poly[j].z > p.z)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x))
                inside = !inside;
        }
        return inside;
    }
}