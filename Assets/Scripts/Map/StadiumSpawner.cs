using System;
using System.Collections.Generic;
using UnityEngine;

public class RealWorldStadiumSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject bigStadiumPrefab;
    public GameObject smallStadiumPrefab;

    [Header("Terrain")]
    public Terrain terrain;

    [Header("Map Bounds (Kyiv OSM)")]
    public double minLat = 50.3733126;
    public double maxLat = 50.4371938;
    public double minLon = 30.4659854;
    public double maxLon = 30.6290821;

    [Header("Stadium no-spawn zones (meters)")]
    public float stadiumRadius = 120f; // збільшив—щоб точно не перетинались

    // точки центрів стадіонів
    public static List<Vector2> stadiumCenters = new List<Vector2>();

    void Start()
    {
        try
        {
            SafeSpawnAll();
        }
        catch (System.Exception e)
        {
            Debug.LogError("🔥 Spawn crashed: " + e);
        }
    }

    void SafeSpawnAll()
    {
        stadiumCenters.Clear();

        // big
        SpawnStadium(bigStadiumPrefab, 50.433411, 30.521844, 0.6f);

        // small — всі в межах карти
        SpawnStadium(smallStadiumPrefab, 50.450436, 30.535178, 1.0f);
        SpawnStadium(smallStadiumPrefab, 50.444717, 30.478439, 1.0f);
        SpawnStadium(smallStadiumPrefab, 50.426639, 30.523500, 1.0f); 
    }

    float GetTerrainHeightUnderBounds(Bounds b, Terrain terrain)
    {
        float highest = float.MinValue;

        // 3×3 сітка точок (можна 5×5)
        const int steps = 3;

        for (int ix = 0; ix < steps; ix++)
        {
            for (int iz = 0; iz < steps; iz++)
            {
                float rx = (ix / (float)(steps - 1)); // 0..1
                float rz = (iz / (float)(steps - 1));

                float px = Mathf.Lerp(b.min.x, b.max.x, rx);
                float pz = Mathf.Lerp(b.min.z, b.max.z, rz);

                float tY = terrain.SampleHeight(new Vector3(px, 0, pz)) +
                           terrain.transform.position.y;

                if (tY > highest)
                    highest = tY;
            }
        }

        return highest;
    }

    void SnapStadiumToTerrain(GameObject go, Terrain terrain)
    {
        if (!go || !terrain) return;

        float ground = GetRealGroundHeightUnder(go, terrain);
        Collider col = go.GetComponentInChildren<Collider>();

        if (!col) return;

        float bottom = col.bounds.min.y;

        float delta = ground - bottom;
        go.transform.position += new Vector3(0, delta, 0);
    }

    float GetRealGroundHeightUnder(GameObject obj, Terrain terrain, int grid = 5)
    {
        TerrainData data = terrain.terrainData;

        // беремо чашку покриття моделі (під моделлю)
        Collider col = obj.GetComponentInChildren<Collider>();
        if (!col) return terrain.transform.position.y;

        Bounds b = col.bounds;

        float highest = float.MinValue;

        for (int x = 0; x < grid; x++)
        {
            for (int z = 0; z < grid; z++)
            {
                float rx = x / (float)(grid - 1);
                float rz = z / (float)(grid - 1);

                float px = Mathf.Lerp(b.min.x, b.max.x, rx);
                float pz = Mathf.Lerp(b.min.z, b.max.z, rz);

                float h = terrain.SampleHeight(new Vector3(px, 0, pz))
                         + terrain.transform.position.y;

                if (h > highest)
                    highest = h;
            }
        }

        return highest;
    }

    void SpawnStadium(GameObject prefab, double lat, double lon, float scale)
    {
        if (!prefab) return;

        Vector3 pos = LatLonToUnity(lat, lon);

        // враховуємо зміщення террейна
        pos += terrain.transform.position;

        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;

        // первинна висота (пробна)
        if (terrain)
            pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;

        // 1. Spawn
        GameObject go = Instantiate(prefab, pos, Quaternion.identity);

        // 2. Apply scale
        go.transform.localScale = Vector3.one * scale;

        // 3. Snap stadium to terrain (коректний)
        SnapStadiumToTerrain(go, terrain);

        // 4. Add exclusion point
        stadiumCenters.Add(new Vector2(go.transform.position.x, go.transform.position.z));
    }
    void FlattenTerrainUnderObject(GameObject obj, Bounds b, float padding)
    {
        if (!terrain) return;

        TerrainData data = terrain.terrainData;

        float width = b.size.x + padding;
        float length = b.size.z + padding;

        int hmWidth = data.heightmapResolution;
        int hmHeight = data.heightmapResolution;

        Vector3 center = b.center - terrain.transform.position;

        int xStart = Mathf.FloorToInt((center.x - width / 2f) / data.size.x * hmWidth);
        int xEnd = Mathf.FloorToInt((center.x + width / 2f) / data.size.x * hmWidth);
        int zStart = Mathf.FloorToInt((center.z - length / 2f) / data.size.z * hmHeight);
        int zEnd = Mathf.FloorToInt((center.z + length / 2f) / data.size.z * hmHeight);

        xStart = Mathf.Clamp(xStart, 0, hmWidth - 1);
        xEnd = Mathf.Clamp(xEnd, 0, hmWidth - 1);
        zStart = Mathf.Clamp(zStart, 0, hmHeight - 1);
        zEnd = Mathf.Clamp(zEnd, 0, hmHeight - 1);

        float[,] heights = data.GetHeights(xStart, zStart, xEnd - xStart, zEnd - zStart);

        float flatY = b.min.y / data.size.y;

        for (int x = 0; x < xEnd - xStart; x++)
            for (int z = 0; z < zEnd - zStart; z++)
                heights[z, x] = flatY;

        data.SetHeights(xStart, zStart, heights);
    }

    Vector3 LatLonToUnity(double lat, double lon)
    {
        const double MIN_LAT = 50.3733126;
        const double MAX_LAT = 50.5090745;

        const double MIN_LON = 30.4659854;
        const double MAX_LON = 30.6259086;

        float mapWidth = 2048f;
        float mapLength = 2048f;

        double latNorm = (lat - MIN_LAT) / (MAX_LAT - MIN_LAT);
        double lonNorm = (lon - MIN_LON) / (MAX_LON - MIN_LON);

        latNorm = Mathf.Clamp01((float)latNorm);
        lonNorm = Mathf.Clamp01((float)lonNorm);

        float unityX = (float)lonNorm * mapWidth;
        float unityZ = (float)latNorm * mapLength;

        return new Vector3(unityX, 0f, unityZ);
    }
}