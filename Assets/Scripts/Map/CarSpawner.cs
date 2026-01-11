using UnityEngine;

public class ParkingCarSpawner : MonoBehaviour
{
    public GameObject carPrefab;

    [Range(0f, 1f)]
    public float fillRatio = 0.7f;

    bool spawned = false;

    public void ResetSpawner()
    {
        StopAllCoroutines();

        var cars = GameObject.FindGameObjectsWithTag("Car");
        foreach (var c in cars)
            Destroy(c);
    }

    public void SpawnCars()
    {
        if (spawned) return;
        spawned = true;

        if (!carPrefab)
        {
            Debug.LogError("❌ Car prefab missing");
            return;
        }

        ParkingZone[] zones = FindObjectsOfType<ParkingZone>();
        if (zones == null || zones.Length == 0)
        {
            Debug.LogWarning("⚠ No parking zones found");
            return;
        }

        foreach (var zone in zones)
        {
            if (zone == null || zone.Capacity == 0)
                continue;

            int carsToSpawn = Mathf.CeilToInt(zone.Capacity * fillRatio);

            for (int i = 0; i < carsToSpawn; i++)
            {
                Transform slot = zone.GetFreeSlot();
                if (!slot)
                    break;

                Instantiate(
                    carPrefab,
                    slot.position,
                    slot.rotation,
                    slot
                );
            }
        }

        Debug.Log("🚗 Cars spawned");
    }
}