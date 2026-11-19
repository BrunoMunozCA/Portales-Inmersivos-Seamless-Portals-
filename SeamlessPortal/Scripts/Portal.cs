// Lo divertido de todo.
using System.Collections.Generic;
using UnityEngine;

public class Portal : MonoBehaviour
{
    public static readonly List<Portal> All = new();
    public static readonly Quaternion Flip = Quaternion.Euler(0f, 180f, 0f);

    [Header("Enlaces")]
    [Tooltip("Portal de destino (el enlace debe ser en las dos direcciones)")]
    public Portal linkedPortal;
    [Tooltip("Cubo hijo con el material PortalScreen (sin collider)")]
    public MeshRenderer screen;

    [Header("Tamano")]
    [Tooltip("Ancho y alto del hueco en metros. Usa este campo en vez de la escala del Transform (que debe ser 1).")]
    public Vector2 size = new Vector2(2f, 3f);
    [Tooltip("Grosor total del trigger. Debe ser mayor que el objeto mas grande que cruce.")]
    public float triggerDepth = 2f;
    [Tooltip("Margen extra del trigger alrededor del hueco.")]
    public float triggerPadding = 0.2f;

    public Material ScreenMaterial { get; private set; }

    readonly Dictionary<PortalTraveller, float> travellers = new();
    readonly List<PortalTraveller> buffer = new();

    void Reset()
    {
        if (screen == null) screen = GetComponentInChildren<MeshRenderer>();
        ApplySize();
    }

    void OnValidate() => ApplySize();

    /// Ajusta la pantalla y el trigger al tamano del portal
    public void ApplySize()
    {
        size.x = Mathf.Max(0.1f, size.x);
        size.y = Mathf.Max(0.1f, size.y);
        triggerDepth = Mathf.Max(0.1f, triggerDepth);
        triggerPadding = Mathf.Max(0f, triggerPadding);

        if (screen != null)
        {
            Transform t = screen.transform;
            float z = Application.isPlaying ? t.localScale.z : 0.1f; // en juego el grosor lo controla el anti-clipping
            t.localScale = new Vector3(size.x, size.y, z);
            if (!Application.isPlaying) t.localPosition = Vector3.zero;
        }

        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = new Vector3(size.x + triggerPadding * 2f, size.y + triggerPadding * 2f, triggerDepth);
        }
    }

    void Awake()
    {
        if (screen == null) screen = GetComponentInChildren<MeshRenderer>();
        ApplySize();
        ScreenMaterial = screen.material;
    }

    void Start()
    {
        if (transform.lossyScale != Vector3.one)
            Debug.LogWarning($"[Portal] '{name}' tiene escala {transform.lossyScale}. Dejala en (1,1,1) y usa el campo Size.", this);

        if (linkedPortal != null && linkedPortal.linkedPortal == null)
        {
            Debug.LogWarning($"[Portal] '{linkedPortal.name}' no tenia Linked Portal. Se enlazo con '{name}' automaticamente.", linkedPortal);
            linkedPortal.linkedPortal = this;
        }
        else if (linkedPortal != null && linkedPortal.linkedPortal != this)
        {
            Debug.LogWarning($"[Portal] '{name}' apunta a '{linkedPortal.name}', pero ese apunta a otro portal. Revisa los enlaces.", this);
        }
    }

    void OnDrawGizmos()
    {
        // Flecha azul = direccion de entrada 
        Gizmos.color = Color.blue;
        Vector3 p = transform.position;
        Vector3 tip = p + transform.forward * 1.2f;
        Gizmos.DrawLine(p, tip);
        Gizmos.DrawLine(tip, tip - transform.forward * 0.3f + transform.right * 0.15f);
        Gizmos.DrawLine(tip, tip - transform.forward * 0.3f - transform.right * 0.15f);
        if (linkedPortal != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(p, linkedPortal.transform.position);
        }
        // Contorno del hueco
        Gizmos.matrix = Matrix4x4.TRS(p, transform.rotation, Vector3.one);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.02f));
        Gizmos.matrix = Matrix4x4.identity;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// Transformacion de este portal al enlazado
    public Matrix4x4 Step =>
        linkedPortal.transform.localToWorldMatrix * Matrix4x4.Rotate(Flip) * transform.worldToLocalMatrix;

    public Bounds GetWorldBounds()
    {
        Vector3 half = new Vector3(size.x, size.y, 0.2f) * 0.5f;
        Matrix4x4 m = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        var b = new Bounds(transform.position, Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? -half.x : half.x,
                                (i & 2) == 0 ? -half.y : half.y,
                                (i & 4) == 0 ? -half.z : half.z);
            b.Encapsulate(m.MultiplyPoint3x4(c));
        }
        return b;
    }

    /// Estira y desplaza el cubo para que el near plane de la camara nunca lo atraviese.
    public void ProtectScreenFromClipping(Vector3 camPos, Camera refCam)
    {
        float halfH = refCam.nearClipPlane * Mathf.Tan(refCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * refCam.aspect;
        float dist = new Vector3(halfW, halfH, refCam.nearClipPlane).magnitude;

        bool camInFront = Vector3.Dot(transform.forward, transform.position - camPos) > 0;
        Transform s = screen.transform;
        s.localScale = new Vector3(s.localScale.x, s.localScale.y, dist);
        s.localPosition = Vector3.forward * dist * (camInFront ? 0.5f : -0.5f);
    }

    // Viajeros
    float Side(Vector3 p) => Mathf.Sign(Vector3.Dot(p - transform.position, transform.forward));

    void OnTriggerEnter(Collider other)
    {
        var t = other.GetComponentInParent<PortalTraveller>();
        if (t != null) AddTraveller(t);
    }

    void OnTriggerExit(Collider other)
    {
        var t = other.GetComponentInParent<PortalTraveller>();
        if (t != null && travellers.Remove(t)) t.ExitPortalThreshold();
    }

    void AddTraveller(PortalTraveller t)
    {
        if (travellers.ContainsKey(t)) return;
        travellers[t] = Side(t.transform.position);
        t.EnterPortalThreshold();
    }

    /// Lo llama el PortalManager antes de renderizar.
    public void HandleTravellers()
    {
        buffer.Clear();
        buffer.AddRange(travellers.Keys);

        foreach (var t in buffer)
        {
            if (t == null) { travellers.Remove(t); continue; }

            float side = Side(t.transform.position);
            if (travellers[t] < 0f && side >= 0f) 
            {
                travellers.Remove(t);
                t.Teleport(transform, linkedPortal.transform);
                linkedPortal.AddTraveller(t);
                t.UpdateSlice(linkedPortal, this);
            }
            else
            {
                travellers[t] = side;
                t.UpdateSlice(this, linkedPortal);
            }
        }
    }
}
