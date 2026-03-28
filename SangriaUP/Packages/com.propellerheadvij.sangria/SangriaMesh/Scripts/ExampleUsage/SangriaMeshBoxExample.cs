using Unity.Collections;
using UnityEngine;
using SangriaMesh;
using Stopwatch = System.Diagnostics.Stopwatch;

public sealed class SangriaMeshBoxExample : MonoBehaviour
{
    private enum BoxFace
    {
        Front = 0,
        Back = 1,
        Left = 2,
        Right = 3,
        Top = 4,
        Bottom = 5
    }

    [SerializeField, Min(0.01f)] private float m_Width = 1f;
    [SerializeField, Min(0.01f)] private float m_Height = 1f;
    [SerializeField, Min(0.01f)] private float m_Depth = 1f;
    [Header("Realtime Bake")]
    [SerializeField] private bool m_RunRealtimeBake = true;
    [SerializeField] private MeshFilter m_TargetMeshFilter;
    [SerializeField] private bool m_LogTimings = true;
    [SerializeField, Min(1)] private int m_LogEveryNFrames = 1;
    [Header("Realtime Edit Stress")]
    [SerializeField] private bool m_RemoveVertexZero = true;
    [SerializeField] private VertexDeletePolicy m_VertexDeletePolicy = VertexDeletePolicy.RemoveFromIncidentPrimitives;
    [SerializeField] private bool m_RemoveFacePrimitives = true;
    [SerializeField] private BoxFace m_RemovedFace = BoxFace.Front;
    [Header("Gizmo Preview")]
    [SerializeField] private bool m_DrawPreview = true;
    [SerializeField] private bool m_DrawPoints = true;
    [SerializeField] private bool m_DrawWireframe = true;
    [SerializeField] private bool m_DrawNormals;
    [SerializeField] private bool m_DrawPointNumbers;
    [SerializeField] private bool m_DrawPrimitiveNumbers;
    [SerializeField, Min(0.001f)] private float m_PointSize = 0.03f;
    [SerializeField, Min(0.001f)] private float m_NormalLength = 0.08f;
    [SerializeField, Min(0f)] private float m_PointNumberOffset = 0.02f;
    [SerializeField, Min(0f)] private float m_PrimitiveNumberOffset = 0.02f;
    [SerializeField] private Color m_PointColor = new Color(0.96f, 0.67f, 0.24f, 1f);
    [SerializeField] private Color m_WireColor = new Color(0.20f, 0.80f, 0.98f, 1f);
    [SerializeField] private Color m_NormalColor = new Color(0.45f, 1.00f, 0.45f, 1f);
    [SerializeField] private Color m_PointNumberColor = Color.white;
    [SerializeField] private Color m_PrimitiveNumberColor = Color.cyan;
    [Header("Random Colors")]
    [SerializeField] private bool m_ApplyRandomColors = true;

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
        long editTicks = 0;
        long compileTicks = 0;
        long bakeTicks = 0;
        bool removedVertex = false;
        int removedFacePrimitives = 0;

        try
        {
            m_Stopwatch.Restart();
            SangriaMeshBoxGenerator.PopulateBox(
                ref m_RuntimeDetail,
                m_Width,
                m_Height,
                m_Depth);
            buildTicks = m_Stopwatch.ElapsedTicks;

            m_Stopwatch.Restart();
            if (m_RemoveVertexZero &&
                m_RuntimeDetail.VertexCount > 0 &&
                m_RuntimeDetail.IsVertexAlive(0))
            {
                removedVertex = m_RuntimeDetail.RemoveVertex(0, m_VertexDeletePolicy);
            }

            if (m_RemoveFacePrimitives)
                removedFacePrimitives = RemoveFacePrimitivePair(ref m_RuntimeDetail);

            if (m_ApplyRandomColors)
                ApplyRandomVertexColors(ref m_RuntimeDetail);

            editTicks = m_Stopwatch.ElapsedTicks;

            m_Stopwatch.Restart();
            compiled = m_RuntimeDetail.Compile(Allocator.TempJob);
            compileTicks = m_Stopwatch.ElapsedTicks;

            m_Stopwatch.Restart();
            compiled.FillUnityMeshTriangles(m_RuntimeMesh);
            bakeTicks = m_Stopwatch.ElapsedTicks;

            if (m_TargetMeshFilter != null && m_TargetMeshFilter.sharedMesh != m_RuntimeMesh)
                m_TargetMeshFilter.sharedMesh = m_RuntimeMesh;
        }
        finally
        {
            if (compiled.IsCreated)
                compiled.Dispose();
        }

        if (m_LogTimings && (Time.frameCount % m_LogEveryNFrames == 0))
        {
            Debug.Log(
                $"[SangriaMeshBoxExample] frame={Time.frameCount} " +
                $"build={TicksToMilliseconds(buildTicks):F3}ms " +
                $"edit={TicksToMilliseconds(editTicks):F3}ms " +
                $"compile={TicksToMilliseconds(compileTicks):F3}ms " +
                $"bake={TicksToMilliseconds(bakeTicks):F3}ms " +
                $"removedVertex={removedVertex} " +
                $"removedFacePrimitives={removedFacePrimitives}");
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

    [ContextMenu("Build SangriaMesh Box Example")]
    private void BuildBoxExample()
    {
        NativeDetail detail;
        CreateBoxWithPrecomputedNormalsAndUv(out detail, m_Width, m_Height, m_Depth, Allocator.TempJob);

        try
        {
            NativeCompiledDetail compiled = detail.Compile(Allocator.TempJob);
            try
            {
                Debug.Log($"SangriaMesh box created: points={compiled.PointCount}, vertices={compiled.VertexCount}, primitives={compiled.PrimitiveCount}");
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
                DrawPreview(ref m_RuntimeDetail);
                return;
            }

            CreateBoxWithPrecomputedNormalsAndUv(out var detail, m_Width, m_Height, m_Depth, Allocator.TempJob);

            try
            {
                DrawPreview(ref detail);
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

    private void DrawPreview(ref NativeDetail detail)
    {
        if (m_DrawWireframe)
            detail.DrawPrimitiveLines(m_WireColor);

        if (m_DrawPoints)
            detail.DrawPointGizmos(m_PointSize, m_PointColor);

        if (m_DrawNormals)
            detail.DrawVertexNormalsGizmos(m_NormalLength, m_NormalColor);

        if (m_DrawPointNumbers)
            detail.DrawPointNumbers(m_PointNumberColor, m_PointNumberOffset);

        if (m_DrawPrimitiveNumbers)
            detail.DrawPrimitiveNumbers(m_PrimitiveNumberColor, m_PrimitiveNumberOffset);
    }

    [ContextMenu("Build SangriaMesh Box And Convert To Unity Mesh")]
    private void BuildBoxAndConvertToUnityMesh()
    {
        CreateBoxWithPrecomputedNormalsAndUv(out var detail, m_Width, m_Height, m_Depth, Allocator.TempJob);

        Mesh unityMesh = null;
        try
        {
            if (m_ApplyRandomColors)
                ApplyRandomVertexColors(ref detail);

            var compiled = detail.Compile(Allocator.TempJob);
            try
            {
                unityMesh = compiled.ToUnityMeshTriangles("SangriaMeshBox");
            }
            finally
            {
                compiled.Dispose();
            }

            Debug.Log($"Unity mesh created from SangriaMesh box: vertices={unityMesh.vertexCount}, triangles={unityMesh.triangles.Length / 3}");
        }
        finally
        {
            if (unityMesh != null)
                DestroyImmediate(unityMesh);

            detail.Dispose();
        }
    }

    /// <summary>
    /// Example API usage: builds a box in SangriaMesh NativeDetail with per-face normals and UV.
    /// Rendering is intentionally omitted.
    /// </summary>
    public static void CreateBoxWithPrecomputedNormalsAndUv(
        out NativeDetail detail,
        float width,
        float height,
        float depth,
        Allocator allocator = Allocator.TempJob)
    {
        SangriaMeshBoxGenerator.CreateBox(
            out detail,
            width,
            height,
            depth,
            allocator);
    }

    private void EnsureRuntimeMesh()
    {
        if (m_RuntimeMesh != null)
            return;

        if (m_TargetMeshFilter == null)
            TryGetComponent(out m_TargetMeshFilter);

        m_RuntimeMesh = new Mesh { name = "SangriaMeshRealtimeBox" };
        m_RuntimeMesh.MarkDynamic();
        if (m_TargetMeshFilter != null)
            m_TargetMeshFilter.sharedMesh = m_RuntimeMesh;
    }

    private void EnsureRuntimeDetail()
    {
        if (m_RuntimeDetailCreated)
            return;

        SangriaMeshBoxGenerator.GetBoxTopologyCounts(
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

    private int RemoveFacePrimitivePair(ref NativeDetail detail)
    {
        int primitiveStart = (int)m_RemovedFace * 2;
        int removedCount = 0;

        if (detail.IsPrimitiveAlive(primitiveStart) && detail.RemovePrimitive(primitiveStart))
            removedCount++;

        int primitiveNext = primitiveStart + 1;
        if (detail.IsPrimitiveAlive(primitiveNext) && detail.RemovePrimitive(primitiveNext))
            removedCount++;

        return removedCount;
    }

    private void ApplyRandomVertexColors(ref NativeDetail detail)
    {
        detail.AddVertexAttribute<Color>(AttributeID.Color);
        if (detail.TryGetVertexAccessor<Color>(AttributeID.Color, out var colorAccessor) != CoreResult.Success)
            return;

        unsafe
        {
            Color* colorPtr = colorAccessor.GetBasePointer();
            int vertexCapacity = detail.VertexCapacity;
            var random = new Unity.Mathematics.Random((uint)(Time.frameCount + 1));

            for (int i = 0; i < vertexCapacity; i++)
            {
                if (detail.IsVertexAlive(i))
                {
                    colorPtr[i] = new Color(random.NextFloat(), random.NextFloat(), random.NextFloat(), 1f);
                }
            }
        }
    }
}
