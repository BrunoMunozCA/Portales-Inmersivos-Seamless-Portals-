using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Renderiza un arbol de vistas
[DefaultExecutionOrder(1000)] 
public class PortalManager : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Camara desactivada compartida. Si se deja vacia se crea sola copiando la principal.")]
    public Camera portalCam;

    [Header("Render")]
    [Range(1, 8)] public int recursionLimit = 4;
    [Range(0.25f, 1f)] public float resolutionScale = 1f;
    [Tooltip("Tope de renders extra por frame. Se reparte entre los portales visibles. Si un portal se queda sin presupuesto usa la imagen del frame anterior.")]
    public int maxRendersPerFrame = 16;
    public float nearClipOffset = 0.05f;
    [Tooltip("Multiplica el far clip de la camara del portal. El recorte oblicuo acorta el far plane efectivo; esto evita que lo lejano desaparezca.")]
    public float farClipMultiplier = 3f;

    [Header("Calidad")]
    [Tooltip("Texturas HDR: evita que las pantallas se vean mas oscuras que el mundo real.")]
    public bool hdrTextures = true;
    [Tooltip("Cuando no hay presupuesto o se llega al limite de recursion, usa la imagen del frame anterior en vez de negro. Si ves parpadeos, prueba a desactivarlo.")]
    public bool useLastFrameFallback = true;

    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly Plane[] frustum = new Plane[6];
    const int MaxDepth = 9;

    class View
    {
        public RenderTexture rt;
        public UniversalRenderPipeline.SingleCameraRequest request;
    }

    Camera playerCam;
    int texW, texH, rendersDone;
    bool warnedRequest;

    readonly Stack<View> pool = new();
    readonly List<View> topViews = new();
    readonly List<Portal> topVisible = new();
    readonly List<View> topResults = new();
    readonly List<Portal>[] visibleAt = new List<Portal>[MaxDepth];
    readonly List<View>[] childrenAt = new List<View>[MaxDepth];

    // Ultima imagen de cada portal
    readonly Dictionary<Portal, RenderTexture> lastFrames = new();

    RenderTextureFormat Format => hdrTextures ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;

    void Awake()
    {
        playerCam = Camera.main;
        if (portalCam == null) CreatePortalCamera();
        portalCam.enabled = false;

        var data = portalCam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.renderShadows = true;

        for (int i = 0; i < MaxDepth; i++)
        {
            visibleAt[i] = new List<Portal>();
            childrenAt[i] = new List<View>();
        }
    }

    void CreatePortalCamera()
    {
        var go = new GameObject("PortalCam");
        go.transform.SetParent(transform, false);
        go.tag = "Untagged";
        portalCam = go.AddComponent<Camera>();
        if (playerCam != null) portalCam.CopyFrom(playerCam);
        portalCam.enabled = false;
    }

    void OnDestroy()
    {
        foreach (var v in topViews) pool.Push(v);
        topViews.Clear();
        ReleasePool();
        ReleaseLastFrames();
    }

    void LateUpdate()
    {
        if (playerCam == null) { playerCam = Camera.main; if (playerCam == null) return; }

        foreach (var v in topViews) pool.Push(v);
        topViews.Clear();
        ResizeIfNeeded();

        for (int i = 0; i < Portal.All.Count; i++)
            if (Portal.All[i].linkedPortal != null) Portal.All[i].HandleTravellers();

        Matrix4x4 playerPose = playerCam.transform.localToWorldMatrix;
        topVisible.Clear();
        topResults.Clear();
        CollectVisible(playerPose, null, topVisible);

        rendersDone = 0;
        int remaining = maxRendersPerFrame;
        for (int i = 0; i < topVisible.Count; i++)
        {
            int share = remaining / (topVisible.Count - i);
            int before = rendersDone;
            View v = share > 0 ? RenderThrough(topVisible[i], playerPose, 1, share) : null;
            remaining -= rendersDone - before;

            if (v != null) topViews.Add(v);
            topResults.Add(v);
        }

        // Dejar las pantallas listas para la camara del jugador
        SetScreensEnabled(topVisible);
        Vector3 camPos = playerCam.transform.position;
        for (int i = 0; i < topVisible.Count; i++)
        {
            var p = topVisible[i];
            p.ScreenMaterial.SetTexture(MainTex, topResults[i] != null ? topResults[i].rt : Fallback(p));
            p.ProtectScreenFromClipping(camPos, playerCam);
        }

        // Guardar la imagen de este frame, sirve de reserva en el siguiente
        if (useLastFrameFallback)
            for (int i = 0; i < topVisible.Count; i++)
                if (topResults[i] != null) SaveLastFrame(topVisible[i], topResults[i].rt);
    }

    View RenderThrough(Portal portal, Matrix4x4 viewer, int depth, int budget)
    {
        if (budget <= 0) return null;
        rendersDone++;
        int remaining = budget - 1;

        Matrix4x4 pose = portal.Step * viewer;
        Vector3 camPos = pose.GetPosition();
        Portal exit = portal.linkedPortal;

        var visible = visibleAt[depth]; visible.Clear();
        var children = childrenAt[depth]; children.Clear();

        CollectVisible(pose, exit, visible);

        // Hijos primero (de lo mas profundo a lo mas superficial)
        int n = visible.Count;
        for (int i = 0; i < n; i++)
        {
            int share = remaining / (n - i);
            int before = rendersDone;
            children.Add(depth < recursionLimit && share > 0
                ? RenderThrough(visible[i], pose, depth + 1, share)
                : null);
            remaining -= rendersDone - before;
        }

        SetScreensEnabled(visible);
        for (int i = 0; i < n; i++)
        {
            var r = visible[i];
            r.ScreenMaterial.SetTexture(MainTex, children[i] != null ? children[i].rt : Fallback(r));
            r.ProtectScreenFromClipping(camPos, playerCam);
        }

        View view = GetView();
        portalCam.transform.SetPositionAndRotation(camPos, pose.rotation);
        SetNearClipPlane(exit.transform);
        if (RenderPipeline.SupportsRenderRequest(portalCam, view.request))
            RenderPipeline.SubmitRenderRequest(portalCam, view.request);
        else if (!warnedRequest)
        {
            warnedRequest = true;
            Debug.LogWarning("[PortalManager] La camara del portal no soporta Render Requests: las pantallas se veran negras. " +
                             "Comprueba que el proyecto use URP y que la camara sea de tipo Game.", portalCam);
        }

        foreach (var c in children) if (c != null) pool.Push(c);
        return view;
    }

    // exit == null: camara real del jugador. exit != null: camara virtual tras ese portal.
    void CollectVisible(Matrix4x4 pose, Portal exit, List<Portal> result)
    {
        Vector3 camPos = pose.GetPosition();
        Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1))
                       * Matrix4x4.TRS(camPos, pose.rotation, Vector3.one).inverse;
        GeometryUtility.CalculateFrustumPlanes(playerCam.projectionMatrix * view, frustum);

        int sign = 0;
        if (exit != null)
            sign = System.Math.Sign(Vector3.Dot(exit.transform.forward, exit.transform.position - camPos));

        float tolerance = exit == null ? 0.3f : 0f;

        foreach (var r in Portal.All)
        {
            if (r.linkedPortal == null || r == exit) continue;
            if (!GeometryUtility.TestPlanesAABB(frustum, r.GetWorldBounds())) continue;
            if (Vector3.Dot(r.transform.forward, camPos - r.transform.position) > tolerance) continue;

            if (exit != null && sign != 0 &&
                Vector3.Dot(r.transform.position - exit.transform.position, exit.transform.forward) * sign <= 0f)
                continue;

            result.Add(r);
        }
    }

    void SetScreensEnabled(List<Portal> visible)
    {
        foreach (var p in Portal.All) p.screen.enabled = visible.Contains(p);
    }

    void SetNearClipPlane(Transform plane)
    {
        // Proyeccion base con los ajustes de la camara del jugador
        portalCam.ResetProjectionMatrix();
        portalCam.orthographic = playerCam.orthographic;
        portalCam.usePhysicalProperties = playerCam.usePhysicalProperties;
        if (playerCam.usePhysicalProperties)
        {
            portalCam.sensorSize = playerCam.sensorSize;
            portalCam.lensShift = playerCam.lensShift;
            portalCam.focalLength = playerCam.focalLength;
            portalCam.gateFit = playerCam.gateFit;
        }
        else
        {
            portalCam.fieldOfView = playerCam.fieldOfView;
        }
        portalCam.aspect = playerCam.aspect;
        portalCam.nearClipPlane = playerCam.nearClipPlane;
        portalCam.farClipPlane = playerCam.farClipPlane * Mathf.Max(1f, farClipMultiplier);

        int sign = System.Math.Sign(Vector3.Dot(plane.forward, plane.position - portalCam.transform.position));

        Vector3 camPos = portalCam.worldToCameraMatrix.MultiplyPoint(plane.position);
        Vector3 camNormal = portalCam.worldToCameraMatrix.MultiplyVector(plane.forward) * sign;
        float camDist = -Vector3.Dot(camPos, camNormal) + nearClipOffset;

        if (Mathf.Abs(camDist) > 0.2f)
        {
            var clip = new Vector4(camNormal.x, camNormal.y, camNormal.z, camDist);
            portalCam.projectionMatrix = portalCam.CalculateObliqueMatrix(clip);
        }
    }

    //Textura de reserva (en vez de negro) 
    Texture Fallback(Portal p)
    {
        if (useLastFrameFallback && lastFrames.TryGetValue(p, out var rt) && rt != null && rt.IsCreated())
            return rt;
        return Texture2D.blackTexture;
    }

    void SaveLastFrame(Portal p, RenderTexture src)
    {
        if (!lastFrames.TryGetValue(p, out var dst) || dst == null)
        {
            dst = new RenderTexture(texW, texH, 0, Format);
            dst.Create();
            lastFrames[p] = dst;
        }
        Graphics.Blit(src, dst);
    }

    void ReleaseLastFrames()
    {
        foreach (var kv in lastFrames)
            if (kv.Value != null) { kv.Value.Release(); Destroy(kv.Value); }
        lastFrames.Clear();
    }

    // Pool de texturas
    View GetView()
    {
        if (pool.Count > 0) return pool.Pop();
        var rt = new RenderTexture(texW, texH, 24, Format);
        return new View { rt = rt, request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt } };
    }

    void ReleasePool()
    {
        while (pool.Count > 0) { var v = pool.Pop(); v.rt.Release(); Destroy(v.rt); }
    }

    void ResizeIfNeeded()
    {
        int w = Mathf.Max(16, Mathf.RoundToInt(Screen.width * resolutionScale));
        int h = Mathf.Max(16, Mathf.RoundToInt(Screen.height * resolutionScale));
        if (w == texW && h == texH) return;
        texW = w; texH = h;
        ReleasePool();
        ReleaseLastFrames();
    }
}
//sufrimiento