using UnityEngine;

public class ParkingZone : MonoBehaviour
{
    [SerializeField] Transform[] slots;
    int used = 0;

    public int Capacity => slots.Length;

    public Transform GetFreeSlot()
    {
        if (used >= slots.Length)
            return null;

        return slots[used++];
    }
}