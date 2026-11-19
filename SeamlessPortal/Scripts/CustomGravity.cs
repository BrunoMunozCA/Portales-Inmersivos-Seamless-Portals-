using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CustomGravity : MonoBehaviour
{
    public float gravity = 20f;
    public Vector3 down = Vector3.down;
    public float alignSpeed = 10f;
    public bool alignRotation = true; // true: jugador, false: objetos 

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.freezeRotation = alignRotation;
    }

    void FixedUpdate()
    {
        rb.AddForce(down * gravity, ForceMode.Acceleration);
        if (!alignRotation) return;

        // Alinea el "arriba" del jugador con -down 
        Quaternion target = Quaternion.FromToRotation(transform.up, -down) * rb.rotation;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, alignSpeed * Time.fixedDeltaTime));
    }

    // Se llama desde el teletransporte
    public void RotateGravity(Quaternion delta) => down = (delta * down).normalized;
}
