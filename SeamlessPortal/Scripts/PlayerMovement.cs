using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 6f, mouseSens = 2f;
    public Transform cam;
    Rigidbody rb;
    float pitch;

    void Awake() { rb = GetComponent<Rigidbody>(); Cursor.lockState = CursorLockMode.Locked; }

    void Update()
    {
        // Yaw sobre el eje local up funciona con cualquier gravedad
        transform.Rotate(Vector3.up, Input.GetAxis("Mouse X") * mouseSens, Space.Self);
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSens, -89f, 89f);
        cam.localRotation = Quaternion.Euler(pitch, 0, 0);
    }

    void FixedUpdate()
    {
        Vector3 input = (transform.right * Input.GetAxisRaw("Horizontal")
                       + transform.forward * Input.GetAxisRaw("Vertical")).normalized * speed;
        Vector3 vertical = Vector3.Project(rb.linearVelocity, transform.up); // conserva caída y salto
        rb.linearVelocity = input + vertical;
    }
}