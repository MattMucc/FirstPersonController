using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float gamepadSensitivity = 300f;
    [SerializeField] [Range(-89f, -50f)] private float minPitch = -80f;
    [SerializeField] [Range(50f, 89f)] private float maxPitch = 80f;

    [Header("References")]
    private Transform playerRoot;
    private PlayerInputReader input;

    private float pitch;

    private void Awake()
    {
        if (!playerRoot) playerRoot = transform.root;
        if (!input) input = playerRoot.GetComponent<PlayerInputReader>();
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        float sensitivity = input.IsGamepad() ? gamepadSensitivity : mouseSensitivity;
        float mouseX = input.Look.x * sensitivity * Time.deltaTime;
        float mouseY = input.Look.y * sensitivity * Time.deltaTime;

        // Clamps the pitch (vertical rotation)
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // Applies rotation
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        playerRoot.Rotate(Vector3.up * mouseX);
    }
}