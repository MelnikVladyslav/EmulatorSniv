using Assets.Scripts.Map;
using UnityEngine;

namespace Assets.Scripts.Player
{
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("Player")]
        public GameObject playerPrefab;
        GameObject playerInstance;

        [Header("UI")]
        public Canvas gameCanvas;
        public GameObject loaderUI;

        [Header("World")]
        public GeoJsonLoader mapLoader;
        public Terrain terrain;

        [Header("Spawn settings")]
        public LayerMask blockedLayers; // Buildings, Roads, Parking
        public float checkRadius = 1.2f;
        public float heightOffset = 1.5f;
        public int spawnAttempts = 40;

        Camera mainCamera;

        void Start()
        {
            mainCamera = Camera.main;

            if (!mapLoader)
            {
                Debug.LogError("❌ PlayerSpawner: GeoJsonLoader missing");
                return;
            }

            mapLoader.OnGenerationComplete -= OnWorldReady;
            mapLoader.OnGenerationComplete += OnWorldReady;

            Debug.Log("🔗 PlayerSpawner subscribed to OnGenerationComplete");
        }

        void OnWorldReady()
        {
            Debug.Log("🌍 World ready → spawning player");

            if (!playerInstance)
                CreatePlayer();

            RespawnPlayer();

            HideLoader();
        }

        void CreatePlayer()
        {
            playerInstance = Instantiate(playerPrefab);
            Debug.Log("👤 Player created");

            // камера
            Camera playerCam = playerInstance.GetComponentInChildren<Camera>();
            if (mainCamera) mainCamera.gameObject.SetActive(false);
            if (playerCam) playerCam.enabled = true;

            // canvas
            if (gameCanvas && playerCam && gameCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                gameCanvas.worldCamera = playerCam;
        }

        public void RespawnPlayer()
        {
            for (int i = 0; i < spawnAttempts; i++)
            {
                Vector3 pos = GetRandomTerrainPoint();
                if (IsFree(pos))
                {
                    playerInstance.transform.position = pos + Vector3.up * heightOffset;
                    Debug.Log($"✅ Player respawned at {pos}");
                    return;
                }
            }

            Debug.LogWarning("⚠ Failed to find safe spawn point");
        }

        Vector3 GetRandomTerrainPoint()
        {
            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;

            float x = Random.Range(0, size.x);
            float z = Random.Range(0, size.z);

            float y = terrain.SampleHeight(new Vector3(origin.x + x, 0, origin.z + z)) + origin.y;

            return new Vector3(origin.x + x, y, origin.z + z);
        }

        bool IsFree(Vector3 pos)
        {
            return !Physics.CheckSphere(
                pos + Vector3.up * 0.5f,
                checkRadius,
                blockedLayers
            );
        }

        void HideLoader()
        {
            if (loaderUI)
                loaderUI.SetActive(false);

            Debug.Log("🎮 Loader hidden, game started");
        }
    }
}