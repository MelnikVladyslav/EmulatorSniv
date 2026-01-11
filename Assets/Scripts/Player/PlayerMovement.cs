using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerRigidbodyMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float runMultiplier = 1.8f;
    public float groundCheckDistance = 0.3f;
    public LayerMask groundMask;

    [Header("Mouse Look")]
    [Range(0.1f, 10f)] public float mouseSensitivity = 2f;
    public Transform cameraTransform;

    [Header("Camera")]
    public Camera playerCamera;

    Rigidbody rb;
    float xRotation;
    bool isGrounded;

    // 🔑 ВАЖЛИВО: кешуємо input
    float inputX;
    float inputZ;
    bool isRunning;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (!cameraTransform)
            cameraTransform = GetComponentInChildren<Camera>()?.transform;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // ✅ INPUT ТІЛЬКИ ТУТ
        inputX = Input.GetAxisRaw("Horizontal");
        inputZ = Input.GetAxisRaw("Vertical");

        isRunning = Input.GetKey(KeyCode.R);

        Look();
        CheckGround();
    }

    void FixedUpdate()
    {
        Move();
    }

    void Move()
    {
        float speed = isRunning ? walkSpeed * runMultiplier : walkSpeed;

        Vector3 moveDir =
            (transform.right * inputX + transform.forward * inputZ).normalized;

        Vector3 targetVelocity = new Vector3(
            moveDir.x * speed,
            rb.velocity.y,
            moveDir.z * speed
        );

        rb.velocity = targetVelocity;
    }

    void Look()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    void CheckGround()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        isGrounded = Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundMask);
    }
}