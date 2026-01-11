using UnityEngine;

public class CarController : MonoBehaviour
{
    [Header("Wheels")]
    public WheelCollider wheelFL, wheelFR, wheelRL, wheelRR;
    public Transform wheelFLMesh, wheelFRMesh, wheelRLMesh, wheelRRMesh;

    [Header("Driving")]
    public float motorPower = 1500f;
    public float steerAngle = 25f;
    public float brakePower = 2500f;

    [Header("Cameras")]
    public Camera carCamera;

    [Header("Exit")]
    public Transform exitPoint;
    public Transform seatPoint;

    [Header("Audio")]
    public AudioSource carMusic;

    bool playerInside;
    PlayerRigidbodyMovement playerController;
    Rigidbody playerRb;
    Camera playerCamera;

    public bool PlayerInside => playerInside;
    

    void Update()
    {
        if (!playerInside) return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        wheelFL.steerAngle = h * steerAngle;
        wheelFR.steerAngle = h * steerAngle;

        wheelFL.motorTorque = v * motorPower;
        wheelFR.motorTorque = v * motorPower;

        float brake = Input.GetKey(KeyCode.Space) ? brakePower : 0f;
        wheelFL.brakeTorque = brake;
        wheelFR.brakeTorque = brake;
        wheelRL.brakeTorque = brake;
        wheelRR.brakeTorque = brake;

        UpdateWheel(wheelFL, wheelFLMesh);
        UpdateWheel(wheelFR, wheelFRMesh);
        UpdateWheel(wheelRL, wheelRLMesh);
        UpdateWheel(wheelRR, wheelRRMesh);

        if (Input.GetKeyDown(KeyCode.O))
        {
            ToggleMusic();
        }

        // ❗ ВИХІД — тільки тут
        if (Input.GetKeyDown(KeyCode.E))
            ExitCar();
    }

    void Awake()
    {
        if (carMusic)
        {
            carMusic.playOnAwake = false;
            carMusic.Stop();
        }
    }

    void ToggleMusic()
    {
        if (!carMusic) return;

        if (carMusic.isPlaying)
            carMusic.Pause();
        else
            carMusic.Play();
    }

    void UpdateWheel(WheelCollider col, Transform mesh)
    {
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        mesh.SetPositionAndRotation(pos, rot);
    }

    public void EnterCar(PlayerRigidbodyMovement player)
    {
        if (playerInside) return;

        playerInside = true;

        if (carMusic && !carMusic.isPlaying)
            carMusic.Play();

        playerController = player;
        playerRb = player.GetComponent<Rigidbody>();
        playerCamera = player.playerCamera;

        // 🔒 вимикаємо керування
        playerController.enabled = false;
        playerRb.isKinematic = true;

        // 🧍‍♂️ переміщаємо гравця в машину
        player.transform.position = seatPoint.position;
        player.transform.rotation = seatPoint.rotation;
        player.transform.SetParent(transform);

        // 👻 ховаємо модель гравця
        foreach (var r in player.GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // 🧱 вимикаємо колайдер
        var col = player.GetComponent<Collider>();
        if (col) col.enabled = false;

        // 🎥 камери
        playerCamera.gameObject.SetActive(false);
        carCamera.gameObject.SetActive(true);

        Debug.Log("🚗 Player entered car");
    }

    void ExitCar()
    {
        playerInside = false;

        if (carMusic)
            carMusic.Stop();

        // 🧍‍♂️ повертаємо гравця у світ
        playerController.transform.SetParent(null);
        playerController.transform.position = exitPoint.position;

        playerRb.isKinematic = false;
        playerController.enabled = true;

        // 👀 показуємо модель
        foreach (var r in playerController.GetComponentsInChildren<Renderer>())
            r.enabled = true;

        // 🧱 колайдер назад
        var col = playerController.GetComponent<Collider>();
        if (col) col.enabled = true;

        // 🎥 камери
        playerCamera.gameObject.SetActive(true);
        carCamera.gameObject.SetActive(false);

        Debug.Log("🚶 Player exited car");
    }
}