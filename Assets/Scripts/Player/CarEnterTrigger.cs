using UnityEngine;

public class CarEnterTrigger : MonoBehaviour
{
    public CarController car;

    bool playerInsideTrigger;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            playerInsideTrigger = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            playerInsideTrigger = false;
    }

    void Update()
    {
        if (!playerInsideTrigger) return;
        if (car.PlayerInside) return;

        if (Input.GetKeyDown(KeyCode.C))
        {
            var player = GameObject.FindWithTag("Player")
                .GetComponent<PlayerRigidbodyMovement>();

            car.EnterCar(player);
        }
    }
}