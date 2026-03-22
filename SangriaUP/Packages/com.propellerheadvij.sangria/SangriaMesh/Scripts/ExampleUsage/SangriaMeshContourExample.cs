using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SangriaMesh;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// Realtime contour tessellation example.
///
/// Hierarchy setup:
///   SangriaMeshContourExample (this script)
///     ├── Contour0          ← child Transform (contour group)
///     │     ├── Point0      ← child Transform (contour vertex)
///     │     ├── Point1
///     │     ├── Point2
///     │     └── ...
///     ├── Contour1
///     │     ├── Point0
///     │     ├── Point1
///     │     └── ...
///     └── ...
///
/// Each direct child represents one closed contour.
/// Each grandchild's local position is a contour vertex (XY plane, Z ignored).
/// The tessellator triangulates the contours every frame and bakes the result
/// into a Unity Mesh assigned to the <see cref="MeshFilter"/> on this GameObject.
/// </summary>
public sealed class SangriaMeshContourExample : MonoBehaviour
{
    private enum ContourInputPlane
    {
        XY = 0,
        XZ = 1,
        YZ = 2
    }

    [Header("Triangulation")]
    [SerializeField] private TriangulationWindingRule m_WindingRule = TriangulationWindingRule.EvenOdd;
    [SerializeField] private TriangulationContourOrientation m_Orientation = TriangulationContourOrientation.Original;
    [SerializeField] private bool m_RemoveEmptyPolygons = true;
    [SerializeField] private ContourInputPlane m_InputPlane = ContourInputPlane.XY;

    [Header("Realtime Bake")]
    [SerializeField] private bool m_RunRealtimeBake = true;
    [SerializeField] private MeshFilter m_TargetMeshFilter;
    [SerializeField] private bool m_LogTimings;
    [SerializeField, Min(1)] private int m_LogEveryNFrames = 60;

    [Header("Gizmo Preview")]
    [SerializeField] private bool m_DrawPreview = true;
    [SerializeField] private bool m_DrawContourLines = true;
    [SerializeField] private bool m_DrawPoints = true;
    [SerializeField] private bool m_DrawWireframe = true;
    [SerializeField] private bool m_DrawPointNumbers;
    [SerializeField, Min(0.001f)] private float m_PointSize = 0.03f;
    [SerializeField, Min(0f)] private float m_PointNumberOffset = 0.02f;
    [SerializeField] private Color m_ContourColor = new Color(1f, 1f, 0f, 0.6f);
    [SerializeField] private Color m_PointColor = new Color(0.96f, 0.67f, 0.24f, 1f);
    [SerializeField] private Color m_WireColor = new Color(0.20f, 0.80f, 0.98f, 1f);
    [SerializeField] private Color m_PointNumberColor = Color.white;

    private readonly Stopwatch m_Stopwatch = new Stopwatch();
    private Mesh m_RuntimeMesh;
    private NativeDetail m_OutputDetail;
    private bool m_OutputDetailCreated;

    private void Update()
    {
        if (!m_RunRealtimeBake)
            return;

        EnsureRuntimeMesh();
        Tessellate();
    }

    private void OnDisable()
    {
        ReleaseRuntimeMesh();
        ReleaseOutputDetail();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeMesh();
        ReleaseOutputDetail();
    }

    private void OnDrawGizmos()
    {
        if (!m_DrawPreview)
            return;

        var savedMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        try
        {
            if (m_DrawContourLines)
                DrawContourGizmos();

            if (Application.isPlaying && m_OutputDetailCreated)
            {
                DrawTessellationPreview(ref m_OutputDetail);
                return;
            }

            if (TessellateToTempDetail(out var tempDetail))
            {
                try
                {
                    DrawTessellationPreview(ref tempDetail);
                }
                finally
                {
                    tempDetail.Dispose();
                }
            }
        }
        finally
        {
            Gizmos.matrix = savedMatrix;
        }
    }

    private void Tessellate()
    {
        int totalPoints = CountTotalPoints();
        if (totalPoints == 0)
        {
            m_RuntimeMesh.Clear();
            return;
        }

        ReleaseOutputDetail();
        m_OutputDetail = new NativeDetail(totalPoints, Allocator.Persistent);
        m_OutputDetailCreated = true;

        long collectTicks = 0;
        long tessTicks = 0;
        long compileTicks = 0;
        long bakeTicks = 0;
        int compiledPointCount = 0;
        int compiledPrimitiveCount = 0;
        NativeCompiledDetail compiled = default;

        try
        {
            m_Stopwatch.Restart();
            CollectContours(
                out var positions,
                out var contourOffsets,
                out var contourPointIndices,
                Allocator.TempJob);
            collectTicks = m_Stopwatch.ElapsedTicks;

            try
            {
                m_Stopwatch.Restart();
                var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
                var options = new TriangulationOptions
                {
                    WindingRule = m_WindingRule,
                    ContourOrientation = m_Orientation,
                    Normal = GetTriangulationNormal(),
                    RemoveEmptyPolygons = m_RemoveEmptyPolygons
                };

                CoreResult result = Triangulation.TriangulateContours(
                    in contours, ref m_OutputDetail, in options);
                tessTicks = m_Stopwatch.ElapsedTicks;

                if (result != CoreResult.Success)
                {
                    if (m_LogTimings)
                        Debug.LogWarning($"[SangriaMeshContourExample] Triangulation failed: {result}");
                    m_RuntimeMesh.Clear();
                    return;
                }
            }
            finally
            {
                positions.Dispose();
                contourOffsets.Dispose();
                contourPointIndices.Dispose();
            }

            m_Stopwatch.Restart();
            compiled = m_OutputDetail.Compile(Allocator.TempJob);
            compileTicks = m_Stopwatch.ElapsedTicks;
            compiledPointCount = compiled.PointCount;
            compiledPrimitiveCount = compiled.PrimitiveCount;

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

        if (m_LogTimings && Time.frameCount % m_LogEveryNFrames == 0)
        {
            Debug.Log(
                $"[SangriaMeshContourExample] frame={Time.frameCount} " +
                $"collect={TicksToMs(collectTicks):F3}ms " +
                $"tess={TicksToMs(tessTicks):F3}ms " +
                $"compile={TicksToMs(compileTicks):F3}ms " +
                $"bake={TicksToMs(bakeTicks):F3}ms " +
                $"inputPoints={totalPoints} " +
                $"outputPoints={compiledPointCount} " +
                $"outputPrims={compiledPrimitiveCount}");
        }
    }

    private bool TessellateToTempDetail(out NativeDetail detail)
    {
        int totalPoints = CountTotalPoints();
        if (totalPoints == 0)
        {
            detail = default;
            return false;
        }

        detail = new NativeDetail(totalPoints, Allocator.TempJob);

        CollectContours(
            out var positions,
            out var contourOffsets,
            out var contourPointIndices,
            Allocator.TempJob);

        try
        {
            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            var options = new TriangulationOptions
            {
                WindingRule = m_WindingRule,
                ContourOrientation = m_Orientation,
                Normal = GetTriangulationNormal(),
                RemoveEmptyPolygons = m_RemoveEmptyPolygons
            };

            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref detail, in options);

            if (result != CoreResult.Success)
            {
                detail.Dispose();
                detail = default;
                return false;
            }
        }
        finally
        {
            positions.Dispose();
            contourOffsets.Dispose();
            contourPointIndices.Dispose();
        }

        return true;
    }

    private void CollectContours(
        out NativeArray<float3> positions,
        out NativeArray<int> contourOffsets,
        out NativeArray<int> contourPointIndices,
        Allocator allocator)
    {
        int contourCount = 0;
        int totalPoints = 0;

        for (int i = 0; i < transform.childCount; i++)
        {
            var contourTransform = transform.GetChild(i);
            if (!contourTransform.gameObject.activeSelf)
                continue;

            int pointCount = 0;
            for (int j = 0; j < contourTransform.childCount; j++)
            {
                if (contourTransform.GetChild(j).gameObject.activeSelf)
                    pointCount++;
            }

            if (pointCount < 3)
                continue;

            contourCount++;
            totalPoints += pointCount;
        }

        positions = new NativeArray<float3>(totalPoints, allocator);
        contourOffsets = new NativeArray<int>(contourCount + 1, allocator);
        contourPointIndices = new NativeArray<int>(totalPoints, allocator);

        int posIdx = 0;
        int contourIdx = 0;
        contourOffsets[0] = 0;

        for (int i = 0; i < transform.childCount; i++)
        {
            var contourTransform = transform.GetChild(i);
            if (!contourTransform.gameObject.activeSelf)
                continue;

            int pointCount = 0;
            for (int j = 0; j < contourTransform.childCount; j++)
            {
                if (contourTransform.GetChild(j).gameObject.activeSelf)
                    pointCount++;
            }

            if (pointCount < 3)
                continue;

            for (int j = 0; j < contourTransform.childCount; j++)
            {
                var pointTransform = contourTransform.GetChild(j);
                if (!pointTransform.gameObject.activeSelf)
                    continue;

                var localPos = pointTransform.localPosition;
                positions[posIdx] = new float3(localPos.x, localPos.y, localPos.z);
                contourPointIndices[posIdx] = posIdx;
                posIdx++;
            }

            contourIdx++;
            contourOffsets[contourIdx] = posIdx;
        }
    }

    private int CountTotalPoints()
    {
        int total = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            var contourTransform = transform.GetChild(i);
            if (!contourTransform.gameObject.activeSelf)
                continue;

            int pointCount = 0;
            for (int j = 0; j < contourTransform.childCount; j++)
            {
                if (contourTransform.GetChild(j).gameObject.activeSelf)
                    pointCount++;
            }

            if (pointCount >= 3)
                total += pointCount;
        }

        return total;
    }

    private float3 GetTriangulationNormal()
    {
        switch (m_InputPlane)
        {
            case ContourInputPlane.XZ:
                return new float3(0f, 1f, 0f);
            case ContourInputPlane.YZ:
                return new float3(1f, 0f, 0f);
            default:
                return new float3(0f, 0f, 1f);
        }
    }

    private void DrawContourGizmos()
    {
        Gizmos.color = m_ContourColor;

        for (int i = 0; i < transform.childCount; i++)
        {
            var contourTransform = transform.GetChild(i);
            if (!contourTransform.gameObject.activeSelf || contourTransform.childCount < 3)
                continue;

            int activeCount = 0;
            for (int j = 0; j < contourTransform.childCount; j++)
            {
                if (contourTransform.GetChild(j).gameObject.activeSelf)
                    activeCount++;
            }

            if (activeCount < 3)
                continue;

            Vector3 prev = Vector3.zero;
            Vector3 first = Vector3.zero;
            bool hasFirst = false;

            for (int j = 0; j < contourTransform.childCount; j++)
            {
                var pt = contourTransform.GetChild(j);
                if (!pt.gameObject.activeSelf)
                    continue;

                var pos = pt.localPosition;
                if (!hasFirst)
                {
                    first = pos;
                    prev = pos;
                    hasFirst = true;
                }
                else
                {
                    Gizmos.DrawLine(prev, pos);
                    prev = pos;
                }
            }

            if (hasFirst)
                Gizmos.DrawLine(prev, first);
        }
    }

    private void DrawTessellationPreview(ref NativeDetail detail)
    {
        if (m_DrawWireframe)
            detail.DrawPrimitiveLines(m_WireColor);

        if (m_DrawPoints)
            detail.DrawPointGizmos(m_PointSize, m_PointColor);

        if (m_DrawPointNumbers)
            detail.DrawPointNumbers(m_PointNumberColor, m_PointNumberOffset);
    }

    private void EnsureRuntimeMesh()
    {
        if (m_RuntimeMesh != null)
            return;

        if (m_TargetMeshFilter == null)
            TryGetComponent(out m_TargetMeshFilter);

        m_RuntimeMesh = new Mesh { name = "SangriaMeshContour" };
        m_RuntimeMesh.MarkDynamic();

        if (m_TargetMeshFilter != null)
            m_TargetMeshFilter.sharedMesh = m_RuntimeMesh;
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

    private void ReleaseOutputDetail()
    {
        if (!m_OutputDetailCreated)
            return;

        m_OutputDetail.Dispose();
        m_OutputDetailCreated = false;
    }

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
