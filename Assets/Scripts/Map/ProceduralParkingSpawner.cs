using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralParkingSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject parkingZonePrefab;

    [Header("Terrain")]
    public Terrain terrain;

    [Header("Placement")]
    public float parkingOffset = 8f;
    public float minDistanceBetweenParkings = 15f;
    public float minRoadSegmentLength = 0.5f;

    [Header("Limits")]
    public int maxParkingsTotal = 300;
    public int parkingsPerFrame = 3;

    List<List<Vector3>> roadPolylines;
    readonly List<Vector3> spawnedPositions = new();

    public System.Action OnParkingsSpawned;

    public void Init(GameObject roadRoot)
    {
        StartCoroutine(SpawnFromRoadMesh(roadRoot));
    }

    public void ResetSpawner()
    {
        StopAllCoroutines();

        var spawned = GameObject.FindGameObjectsWithTag("Parking");
        foreach (var p in spawned)
            Destroy(p);

        OnParkingsSpawned = null;
    }

    bool CanSpawnAt(Vector3 pos)
    {
        foreach (var p in spawnedPositions)
        {
            if (Vector3.Distance(p, pos) < minDistanceBetweenParkings)
                return false;
        }
        return true;
    }

    IEnumerator SpawnFromRoadMesh(GameObject roadRoot)
    {
        if (!roadRoot)
        {
            Debug.LogError("❌ Road root is null");
            yield break;
        }

        var mf = roadRoot.GetComponent<MeshFilter>();
        if (!mf || !mf.mesh)
        {
            Debug.LogError("❌ Road mesh missing");
            yield break;
        }

        Mesh mesh = mf.mesh;
        Vector3[] verts = mesh.vertices;

        int spawned = 0;

        for (int i = 0; i < verts.Length - 8; i += 12)
        {
            if (spawned >= maxParkingsTotal)
                break;

            if (Time.timeScale == 0f)
            {
                yield return new WaitUntil(() => Time.timeScale != 0f);
            }

            // беремо середину сегмента дороги
            Vector3 localMid =
                (verts[i] + verts[i + 1] + verts[i + 2] + verts[i + 3]) / 4f;

            Vector3 worldMid = roadRoot.transform.TransformPoint(localMid);

            // напрям дороги
            Vector3 worldA = roadRoot.transform.TransformPoint(verts[i]);
            Vector3 worldB = roadRoot.transform.TransformPoint(verts[i + 2]);

            Vector3 worldDir = (worldB - worldA).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, worldDir);

            Vector3 pos = worldMid + right * parkingOffset;
            Debug.Log($"Trying parking at {pos}");

            if (terrain)
                pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;

            if (!CanSpawnAt(pos))
                continue;

            Quaternion rot = Quaternion.LookRotation(worldDir);
            rot *= Quaternion.Euler(0, 90f, 0);

            Instantiate(
                parkingZonePrefab,
                pos,
                rot
            );

            spawnedPositions.Add(pos);

            spawned++;

            if (spawned % parkingsPerFrame == 0)
                yield return null;
        }

        Debug.Log($"✅ Parking zones spawned: {spawned}");
        OnParkingsSpawned?.Invoke();
    }
}