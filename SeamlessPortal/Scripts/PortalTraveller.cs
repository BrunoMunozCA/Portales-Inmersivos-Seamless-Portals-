using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PortalTraveller : MonoBehaviour
{
    [Tooltip("Hijo con SOLO los renderers (sin colliders ni scripts). Vacío en el jugador.")]
    public GameObject graphicsObject;

    [Tooltip("Crea un proxy físico kinematic con copias de los colliders, en el lado de salida")]
    public bool cloneColliders = true;

    static readonly int SliceCenter = Shader.PropertyToID("_SliceCenter");
    static readonly int SliceNormal = Shader.PropertyToID("_SliceNormal");

    Rigidbody rb;
    CustomGravity gravity;
    MaterialPropertyBlock block;

    // Visual
    GameObject graphicsClone;
    Renderer[] originalRenderers, cloneRenderers;

    // Física
    Collider[] originalColliders;
    GameObject proxy;
    Rigidbody proxyRb;
    readonly List<Collider> proxyColliders = new();
    bool proxyWanted, snapProxy;
    Portal currentIn, currentOut;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        gravity = GetComponent<CustomGravity>();
        if (graphicsObject != null)
            originalRenderers = graphicsObject.GetComponentsInChildren<Renderer>();

        var list = new List<Collider>();
        foreach (var c in GetComponentsInChildren<Collider>())
            if (!c.isTrigger) list.Add(c);
        originalColliders = list.ToArray();
    }

    // Teletransporte con gravedad (se pone wena la cosa)
    public void Teleport(Transform from, Transform to)
    {
        Matrix4x4 m = to.localToWorldMatrix
                    * Matrix4x4.Rotate(Portal.Flip)
                    * from.worldToLocalMatrix
                    * transform.localToWorldMatrix;

        // Rotación relativa entre portales con inclinacion
        Quaternion delta = to.rotation * Portal.Flip * Quaternion.Inverse(from.rotation);

        transform.SetPositionAndRotation(m.GetPosition(), m.rotation);
        rb.position = transform.position;
        rb.rotation = transform.rotation;

        rb.linearVelocity = delta * rb.linearVelocity;
        rb.angularVelocity = delta * rb.angularVelocity;
        if (gravity != null) gravity.RotateGravity(delta);

        Physics.SyncTransforms();
    }

    // Entrar / salir 
    public void EnterPortalThreshold()
    {
        if (graphicsObject != null)
        {
            if (graphicsClone == null)
            {
                graphicsClone = Instantiate(graphicsObject);
                graphicsClone.name = graphicsObject.name + " (clon)";
                graphicsClone.transform.localScale = graphicsObject.transform.lossyScale;
                cloneRenderers = graphicsClone.GetComponentsInChildren<Renderer>();
            }
            graphicsClone.SetActive(true);
        }

        if (cloneColliders && originalColliders.Length > 0)
        {
            if (proxy == null) CreateProxy();
            proxyWanted = true; // se activa y coloca en el siguiente FixedUpdate
        }
    }

    public void ExitPortalThreshold()
    {
        currentIn = currentOut = null;
        proxyWanted = false;
        if (proxy != null) proxy.SetActive(false);
        if (graphicsClone != null) graphicsClone.SetActive(false);
        if (originalRenderers != null) ApplySlice(originalRenderers, Vector3.zero, Vector3.zero);
    }

    // inPortal = portal que cruza, outPortal = su enlazado
    public void UpdateSlice(Portal inPortal, Portal outPortal)
    {
        // Si cambia el par de portales, el proxy "salta" en vez de barrer el espacio
        if (inPortal != currentIn || outPortal != currentOut) snapProxy = true;
        currentIn = inPortal;
        currentOut = outPortal;

        if (graphicsObject == null || graphicsClone == null) return;

        ApplySlice(originalRenderers, inPortal.transform.position, inPortal.transform.forward);

        Matrix4x4 m = ClonePose(graphicsObject.transform.localToWorldMatrix);
        graphicsClone.transform.SetPositionAndRotation(m.GetPosition(), m.rotation);

        ApplySlice(cloneRenderers, outPortal.transform.position, outPortal.transform.forward);
    }

    Matrix4x4 ClonePose(Matrix4x4 original) =>
        currentOut.transform.localToWorldMatrix
        * Matrix4x4.Rotate(Portal.Flip)
        * currentIn.transform.worldToLocalMatrix
        * original;

    // Proxy físico 
    void FixedUpdate()
    {
        if (!proxyWanted || proxy == null || currentIn == null || currentOut == null) return;

        // Pose física actual del original, transformada al otro lado
        Matrix4x4 m = ClonePose(Matrix4x4.TRS(rb.position, rb.rotation, Vector3.one));
        Vector3 p = m.GetPosition();
        Quaternion q = m.rotation;

        if (!proxy.activeSelf)
        {
            proxy.transform.SetPositionAndRotation(p, q);
            proxy.SetActive(true);
            ApplyIgnores();          // IgnoreCollision se pierde al desactivar se reaplica
            snapProxy = false;
        }
        else if (snapProxy)
        {
            proxy.transform.SetPositionAndRotation(p, q);
            proxyRb.position = p;
            proxyRb.rotation = q;
            snapProxy = false;
        }
        else
        {
            proxyRb.MovePosition(p);   // movimiento kinematic
            proxyRb.MoveRotation(q);
        }
    }
    // AAAAAAAAAAAAA

    void CreateProxy()
    {
        proxy = new GameObject(name + " (proxy físico)");
        proxy.SetActive(false);
        proxy.layer = gameObject.layer;

        proxyRb = proxy.AddComponent<Rigidbody>();
        proxyRb.isKinematic = true;
        proxyRb.useGravity = false;

        foreach (var col in originalColliders)
        {
            var child = new GameObject(col.name);
            child.layer = col.gameObject.layer;

            Transform t = child.transform;
            t.SetParent(proxy.transform, false);
            // Pose del collider relativa al viajero, en unidades de mundo
            t.localPosition = Quaternion.Inverse(transform.rotation) * (col.transform.position - transform.position);
            t.localRotation = Quaternion.Inverse(transform.rotation) * col.transform.rotation;
            t.localScale = col.transform.lossyScale;

            Collider copy = CopyCollider(col, child);
            if (copy == null) { Destroy(child); continue; } // tipo no soportado
            proxyColliders.Add(copy);
        }
    }

    void ApplyIgnores()
    {
        foreach (var o in originalColliders)
            foreach (var p in proxyColliders)
                Physics.IgnoreCollision(o, p, true);
    }

    static Collider CopyCollider(Collider src, GameObject dst)
    {
        Collider copy;
        switch (src)
        {
            case BoxCollider b:
                {
                    var c = dst.AddComponent<BoxCollider>();
                    c.center = b.center; c.size = b.size;
                    copy = c; break;
                }
            case SphereCollider s:
                {
                    var c = dst.AddComponent<SphereCollider>();
                    c.center = s.center; c.radius = s.radius;
                    copy = c; break;
                }
            case CapsuleCollider cap:
                {
                    var c = dst.AddComponent<CapsuleCollider>();
                    c.center = cap.center; c.radius = cap.radius;
                    c.height = cap.height; c.direction = cap.direction;
                    copy = c; break;
                }
            case MeshCollider m:
                {
                    var c = dst.AddComponent<MeshCollider>();
                    c.convex = m.convex;
                    c.sharedMesh = m.sharedMesh;
                    copy = c; break;
                }
            default:
                return null;
        }
        copy.sharedMaterial = src.sharedMaterial;
        copy.contactOffset = src.contactOffset;
        return copy;
    }

    // Corte
    void ApplySlice(Renderer[] renderers, Vector3 center, Vector3 normal)
    {
        block ??= new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(block);
            block.SetVector(SliceCenter, center);
            block.SetVector(SliceNormal, normal);
            r.SetPropertyBlock(block);
        }
    }

    void OnDestroy()
    {
        if (graphicsClone != null) Destroy(graphicsClone);
        if (proxy != null) Destroy(proxy);
    }
}
