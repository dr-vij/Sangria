using Unity.Collections;
using UnityEngine;
using SangriaMesh;
using Stopwatch = System.Diagnostics.Stopwatch;

public sealed class SangriaMeshExample : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float m_Radius = 0.5f;
    [SerializeField, Min(3)] private int m_LongitudeSegments = 32;
    [SerializeField, Min(3)] private int m_LatitudeSegments = 16;
    [Header("Realtime Bake")]
    [SerializeField] private bool m_RunRealtimeBake = true;
    [SerializeField] private MeshFilter m_TargetMeshFilter;
    [SerializeField] private bool m_LogTimings = true;
    [SerializeField, Min(1)] private int m_LogEveryNFrames = 1;
    [Header("Gizmo Preview")]
    [SerializeField] private bool m_DrawPreview = true;
    [SerializeField] private bool m_DrawPoints = true;
    [SerializeField] private bool m_DrawWireframe = true;
    [SerializeField] private bool m_DrawNormals;
    [SerializeField, Min(0.001f)] private float m_PointSize = 0.03f;
    [SerializeField, Min(0.001f)] private float m_NormalLength = 0.08f;
    [SerializeField] private Color m_PointColor = new Color(0.96f, 0.67f, 0.24f, 1f);
    [SerializeField] private Color m_WireColor = new Color(0.20f, 0.80f, 0.98f, 1f);
    [SerializeField] private Color m_NormalColor = new Color(0.45f, 1.00f, 0.45f, 1f);

    private readonly Stopwatch m_Stopwatch = new Stopwatch();
    private Mesh m_RuntimeMesh;
    private NativeDetail m_RuntimeDetail;
    private bool m_RuntimeDetailCreated;

    private void Update()
    {
        if (!m_RunRealtimeBake)
            return;

        EnsureRuntimeMesh();
        EnsureRuntimeDetail();

        NativeCompiledDetail compiled = default;
        long buildTicks = 0;
        long compileTicks = 0;
        long bakeTicks = 0;

        try
        {
            m_Stopwatch.Restart();
            SangriaMeshSphereGenerator.PopulateUvSphere(
                ref m_RuntimeDetail,
                m_Radius,
                m_LongitudeSegments,
                m_LatitudeSegments);
            buildTicks = m_Stopwatch.ElapsedTicks;

            m_Stopwatch.Restart();
            compiled = m_RuntimeDetail.Compile(Allocator.Temp);
            compileTicks = m_Stopwatch.ElapsedTicks;

            m_Stopwatch.Restart();
            compiled.FillUnityMeshTriangles(m_RuntimeMesh);
            bakeTicks = m_Stopwatch.ElapsedTicks;

            if (m_TargetMeshFilter != null && m_TargetMeshFilter.sharedMesh != m_RuntimeMesh)
                m_TargetMeshFilter.sharedMesh = m_RuntimeMesh;
        }
        finally
        {
            if (compiled.VertexToPointDense.IsCreated)
                compiled.Dispose();
        }

        if (m_LogTimings && (Time.frameCount % m_LogEveryNFrames == 0))
        {
            Debug.Log(
                $"[SangriaMeshExample] frame={Time.frameCount} " +
                $"build={TicksToMilliseconds(buildTicks):F3}ms " +
                $"compile={TicksToMilliseconds(compileTicks):F3}ms " +
                $"bake={TicksToMilliseconds(bakeTicks):F3}ms");
        }
    }

    private void OnDisable()
    {
        ReleaseRuntimeMesh();
        ReleaseRuntimeDetail();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeMesh();
        ReleaseRuntimeDetail();
    }

    [ContextMenu("Build SangriaMesh Sphere Example")]
    private void BuildSphereExample()
    {
        NativeDetail detail;
        CreateSphereWithPrecomputedNormalsAndUv(out detail, m_Radius, m_LongitudeSegments, m_LatitudeSegments, Allocator.Temp);

        try
        {
            NativeCompiledDetail compiled = detail.Compile(Allocator.Temp);
            try
            {
                Debug.Log($"SangriaMesh sphere created: points={compiled.PointCount}, vertices={compiled.VertexCount}, primitives={compiled.PrimitiveCount}");
            }
            finally
            {
                compiled.Dispose();
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    private void OnDrawGizmos()
    {
        if (!m_DrawPreview)
            return;

        var initialMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        try
        {
            if (Application.isPlaying && m_RuntimeDetailCreated)
            {
                DrawPreview(m_RuntimeDetail);
                return;
            }

            CreateSphereWithPrecomputedNormalsAndUv(out var detail, m_Radius, m_LongitudeSegments, m_LatitudeSegments, Allocator.Temp);

            try
            {
                DrawPreview(detail);
            }
            finally
            {
                detail.Dispose();
            }
        }
        finally
        {
            Gizmos.matrix = initialMatrix;
        }
    }

    private void DrawPreview(NativeDetail detail)
    {
        if (m_DrawWireframe)
            detail.DrawPrimitiveLines(m_WireColor);

        if (m_DrawPoints)
            detail.DrawPointGizmos(m_PointSize, m_PointColor);

        if (m_DrawNormals)
            detail.DrawVertexNormalsGizmos(m_NormalLength, m_NormalColor);
    }

    [ContextMenu("Build SangriaMesh Sphere And Convert To Unity Mesh")]
    private void BuildSphereAndConvertToUnityMesh()
    {
        CreateSphereWithPrecomputedNormalsAndUv(out var detail, m_Radius, m_LongitudeSegments, m_LatitudeSegments, Allocator.Temp);

        Mesh unityMesh = null;
        try
        {
            var compiled = detail.Compile(Allocator.Temp);
            try
            {
                unityMesh = compiled.ToUnityMeshTriangles("SangriaMeshSphere");
            }
            finally
            {
                compiled.Dispose();
            }
            Debug.Log($"Unity mesh created from SangriaMesh: vertices={unityMesh.vertexCount}, triangles={unityMesh.triangles.Length / 3}");
        }
        finally
        {
            if (unityMesh != null)
                DestroyImmediate(unityMesh);

            detail.Dispose();
        }
    }

    /// <summary>
    /// Example API usage: builds a UV sphere in SangriaMesh NativeDetail with precomputed normals and UV.
    /// Rendering is intentionally omitted.
    /// </summary>
    public static void CreateSphereWithPrecomputedNormalsAndUv(
        out NativeDetail detail,
        float radius,
        int longitudeSegments,
        int latitudeSegments,
        Allocator allocator = Allocator.Temp)
    {
        SangriaMeshSphereGenerator.CreateUvSphere(
            out detail,
            radius,
            longitudeSegments,
            latitudeSegments,
            allocator);
    }

    private void EnsureRuntimeMesh()
    {
        if (m_RuntimeMesh != null)
            return;

        if (m_TargetMeshFilter == null)
            TryGetComponent(out m_TargetMeshFilter);

        m_RuntimeMesh = new Mesh { name = "SangriaMeshRealtimeSphere" };
        m_RuntimeMesh.MarkDynamic();
        if (m_TargetMeshFilter != null)
            m_TargetMeshFilter.sharedMesh = m_RuntimeMesh;
    }

    private void EnsureRuntimeDetail()
    {
        if (m_RuntimeDetailCreated)
            return;

        SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(
            m_LongitudeSegments,
            m_LatitudeSegments,
            out int pointCount,
            out int vertexCount,
            out int primitiveCount);

        m_RuntimeDetail = new NativeDetail(pointCount, vertexCount, primitiveCount, Allocator.Persistent);
        m_RuntimeDetailCreated = true;
    }

    private void ReleaseRuntimeMesh()
    {
        if (m_TargetMeshFilter != null && m_TargetMeshFilter.sharedMesh == m_RuntimeMesh)
            m_TargetMeshFilter.sharedMesh = null;

        if (m_RuntimeMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(m_RuntimeMesh);
        else
            DestroyImmediate(m_RuntimeMesh);

        m_RuntimeMesh = null;
    }

    private void ReleaseRuntimeDetail()
    {
        if (!m_RuntimeDetailCreated)
            return;

        m_RuntimeDetail.Dispose();
        m_RuntimeDetailCreated = false;
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
