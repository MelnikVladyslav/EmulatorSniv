using Assets.Scripts.Player;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.Map
{
    public class MapGenerator : MonoBehaviour
    {
        public GameObject loaderUI;
        public GameObject mapContainer;

        public Terrain terrain;

        public GameObject mapObject; // Map з GeoJsonLoader
        public Player.PlayerSpawner playerSpawner;

        // Текстури в порядку: пісок, зелена трава, трава, земля, асфальт 1, асфальт 2
        private readonly float[] heightThresholds = new float[]
        {
            0.30f, // зелена трава
            0.45f, // трава
            0.60f, // земля
            0.80f, // асфальт 1
            1.00f  // асфальт 2
        };

        private bool isGenerating = false;

        void Start()
        {
            StartCoroutine(GenerateMapCoroutine());
        }

        IEnumerator GenerateMapCoroutine()
        {
            isGenerating = true;

            yield return StartCoroutine(GenerateMap());
        }

        IEnumerator GenerateMap()
        {
            PaintTerrain();

            StartCoroutine(StartGenerationAfterLoader());

            yield return null;
            isGenerating = false;
            mapContainer.SetActive(true);
        }

        void PaintTerrain()
        {
            TerrainData terrainData = terrain.terrainData;
            int alphamapWidth = terrainData.alphamapWidth;
            int alphamapHeight = terrainData.alphamapHeight;
            int numTextures = terrainData.terrainLayers.Length;

            float[,,] map = new float[alphamapWidth, alphamapHeight, numTextures];

            float minHeight = 7.6f;
            float maxHeight = 78.3f;

            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    float normX = x / (float)(alphamapWidth - 1);
                    float normY = y / (float)(alphamapHeight - 1);

                    float worldHeight = terrainData.GetInterpolatedHeight(normX, normY);
                    float height = Mathf.InverseLerp(minHeight, maxHeight, worldHeight);

                    int textureIndex = 0;
                    for (int i = 0; i < heightThresholds.Length; i++)
                    {
                        if (height < heightThresholds[i])
                        {
                            textureIndex = i;
                            break;
                        }
                    }

                    float[] weights = new float[numTextures];
                    for (int i = 0; i < numTextures; i++)
                        weights[i] = (i == textureIndex) ? 1.0f : 0.0f;

                    for (int i = 0; i < numTextures; i++)
                        map[x, y, i] = weights[i];
                }
            }

            terrainData.SetAlphamaps(0, 0, map);
        }

        IEnumerator StartGenerationAfterLoader()
        {
            // Показуємо лоадер
            loaderUI.SetActive(true);

            // Чекаємо, щоб UI відмалювався
            yield return null;
            yield return null;

            // Активуємо Map і запускаємо генерацію
            if (mapObject != null)
            {
                mapObject.SetActive(true);

                GeoJsonLoader loader = mapObject.GetComponent<GeoJsonLoader>();
                if (loader != null)
                    loader.StartGeneration(); // метод замість Start()
            }
        }
    }
}