using UnityEngine;

public enum MapTheme
{
    Normal,
    Paradise,
    Hell
}

public class MapThemeManager : MonoBehaviour
{
    public static MapThemeManager Instance;

    public MapTheme currentTheme = MapTheme.Normal;

    [Header("Terrain Layers")]
    public int normalGrassLayer = 0;
    public int paradiseGrassLayer = 1;
    public int hellGroundLayer = 2;

    [Header("Road Materials")]
    public Material roadNormal;
    public Material roadParadise;
    public Material roadHell;

    [Header("Building Tint")]
    public Color buildingsNormal = Color.white;
    public Color buildingsParadise = new Color(0.9f, 1f, 0.9f);
    public Color buildingsHell = new Color(0.6f, 0.2f, 0.2f);

    [Header("Lighting")]
    public Light sun;
    public Color sunNormal = Color.white;
    public Color sunParadise = new Color(1f, 0.95f, 0.85f);
    public Color sunHell = new Color(1f, 0.3f, 0.2f);

    void Awake()
    {
        Instance = this;
    }

    // 🔥 ГОЛОВНИЙ МЕТОД
    public void ApplyTheme(
        Terrain terrain,
        GameObject roadRoot,
        Transform buildingsRoot
    )
    {
        ApplyTerrainLayers(terrain);
        ApplyRoads(roadRoot);
        ApplyBuildings(buildingsRoot);
        ApplyLighting();
    }

    // 🌍 TERRAIN SPLATMAP
    void ApplyTerrainLayers(Terrain terrain)
    {
        if (!terrain) return;

        TerrainData td = terrain.terrainData;
        int w = td.alphamapWidth;
        int h = td.alphamapHeight;
        int layers = td.alphamapLayers;

        float[,,] alphas = new float[h, w, layers];

        int targetLayer = currentTheme switch
        {
            MapTheme.Paradise => paradiseGrassLayer,
            MapTheme.Hell => hellGroundLayer,
            _ => normalGrassLayer
        };

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int l = 0; l < layers; l++)
                    alphas[y, x, l] = (l == targetLayer) ? 1f : 0f;
            }
        }

        td.SetAlphamaps(0, 0, alphas);
    }

    public void PickRandomTheme()
    {
        MapTheme[] values = (MapTheme[])System.Enum.GetValues(typeof(MapTheme));
        currentTheme = values[Random.Range(0, values.Length)];

        Debug.Log($"🎲 Random theme selected: {currentTheme}");
    }

    // 🛣 ДОРОГИ
    void ApplyRoads(GameObject roadRoot)
    {
        if (!roadRoot) return;

        var mr = roadRoot.GetComponent<MeshRenderer>();
        if (!mr) return;

        mr.sharedMaterial = currentTheme switch
        {
            MapTheme.Paradise => roadParadise,
            MapTheme.Hell => roadHell,
            _ => roadNormal
        };
    }

    // 🏢 БУДИНКИ
    void ApplyBuildings(Transform root)
    {
        if (!root) return;

        Color tint = currentTheme switch
        {
            MapTheme.Paradise => buildingsParadise,
            MapTheme.Hell => buildingsHell,
            _ => buildingsNormal
        };

        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r.material.HasProperty("_Color"))
                r.material.color = tint;
        }
    }

    // ☀️ СВІТЛО
    void ApplyLighting()
    {
        if (!sun) return;

        sun.color = currentTheme switch
        {
            MapTheme.Paradise => sunParadise,
            MapTheme.Hell => sunHell,
            _ => sunNormal
        };
    }
}
