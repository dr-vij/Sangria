using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SangriaMesh;
using static Unity.Mathematics.math;

[ExecuteAlways]
public sealed class SangriaMeshSphereBvhLiveExample : MonoBehaviour
{
    [Header("Sphere")]
    [SerializeField, Min(0.01f)] private float m_Radius = 0.5f;
    [SerializeField, Min(3)] private int m_LongitudeSegments = 32;
    [SerializeField, Min(3)] private int m_LatitudeSegments = 16;
    [SerializeField] private MeshFilter m_TargetMeshFilter;

    [Header("BVH")]
    [SerializeField, Min(1)] private int m_MaxLeafSize = 4;

    [Header("Ray")]
    [SerializeField] private Transform m_RayOrigin;
    [SerializeField] private Transform m_RayTarget;

    [Header("BVH Gizmos")]
    [SerializeField] private bool m_DrawBvh = true;
    [SerializeField] private bool m_DrawInternalNodes = true;
    [SerializeField] private bool m_DrawLeafNodes = true;
    [SerializeField, Min(0)] private int m_MaxDepthToDraw = 16;
    [SerializeField] private Gradient m_DepthGradient;
    [SerializeField, Range(0f, 1f)] private float m_InternalAlpha = 0.25f;
    [SerializeField, Range(0f, 1f)] private float m_LeafAlpha = 0.9f;

    [Header("Runtime Info")]
    [SerializeField] private int m_PrimitiveCount;
    [SerializeField] private int m_NodeCount;
    [SerializeField] private int m_LeafCount;
    [SerializeField] private int m_RayHitPrimitiveCount;

    private NativeDetail m_Detail;
    private bool m_DetailCreated;
    private NativeBvh<int> m_Bvh;
    private bool m_BvhCreated;
    private Mesh m_RuntimeMesh;

    private void OnEnable()
    {
        EnsureGradient();
        SyncNow();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;

        EnsureGradient();
        SyncNow();
    }

    private void Update()
    {
        SyncNow();
    }

    private void OnDisable()
    {
        DisposeAll();
    }

    private void OnDestroy()
    {
        DisposeAll();
    }

    private void SyncNow()
    {
        EnsureDetail();
        SangriaMeshSphereGenerator.PopulateUvSphere(ref m_Detail, m_Radius, m_LongitudeSegments, m_LatitudeSegments);

        EnsureVertexColorAttribute();
        ResetVertexColors();

        BuildBvhFromPrimitives();
        PerformRaycast();
        BakeUnityMesh();
    }

    private void EnsureVertexColorAttribute()
    {
        if (!m_Detail.HasVertexAttribute(AttributeID.Color))
            m_Detail.AddVertexAttribute<float4>(AttributeID.Color);
    }

    private void ResetVertexColors()
    {
        if (m_Detail.TryGetVertexAccessor<float4>(AttributeID.Color, out var colorAccessor) != CoreResult.Success)
            return;

        float4 white = new float4(1f, 1f, 1f, 1f);
        for (int v = 0; v < m_Detail.VertexCapacity; v++)
            colorAccessor[v] = white;
    }

    private void BuildBvhFromPrimitives()
    {
        var validPrimitives = new NativeList<int>(m_Detail.PrimitiveCapacity, Allocator.Temp);
        try
        {
            m_Detail.GetAllValidPrimitives(validPrimitives);
            int primCount = validPrimitives.Length;
            m_PrimitiveCount = primCount;

            var elements = new NativeArray<BvhElement<int>>(primCount, Allocator.Temp);
            try
            {
                for (int i = 0; i < primCount; i++)
                {
                    int primIdx = validPrimitives[i];
                    elements[i] = new BvhElement<int>
                    {
                        Bounds = ComputePrimitiveBounds(primIdx),
                        Value = primIdx
                    };
                }

                EnsureBvh(primCount);
                m_Bvh.SetMaxLeafSize(max(1, m_MaxLeafSize));
                m_Bvh.Build(elements);

                m_NodeCount = m_Bvh.NodeCount;
            }
            finally
            {
                elements.Dispose();
            }
        }
        finally
        {
            validPrimitives.Dispose();
        }
    }

    private BvhAabb ComputePrimitiveBounds(int primitiveIndex)
    {
        if (!m_Detail.GetPrimitiveBounds(primitiveIndex, out float3 bMin, out float3 bMax))
            return default;

        return new BvhAabb { Min = bMin, Max = bMax };
    }

    private void PerformRaycast()
    {
        m_RayHitPrimitiveCount = 0;

        if (m_RayOrigin == null || m_RayTarget == null)
            return;
        if (!m_BvhCreated || !m_Bvh.IsCreated || m_Bvh.RootIndex < 0)
            return;

        float3 worldOrigin = m_RayOrigin.position;
        float3 worldTarget = m_RayTarget.position;

        float4x4 worldToLocal = Unity.Mathematics.float4x4.identity;
        if (transform != null)
            worldToLocal = inverse((float4x4)transform.localToWorldMatrix);

        float3 localOrigin = mul(worldToLocal, float4(worldOrigin, 1f)).xyz;
        float3 localTarget = mul(worldToLocal, float4(worldTarget, 1f)).xyz;
        float3 localDir = localTarget - localOrigin;
        float rayLength = length(localDir);
        if (rayLength < 1e-8f)
            return;
        localDir = localDir / rayLength;

        var candidateElements = new NativeList<int>(64, Allocator.Temp);
        var traversalStack = new NativeList<int>(64, Allocator.Temp);
        try
        {
            RayQueryBvh(localOrigin, localDir, rayLength, candidateElements, traversalStack);
            ProcessRayCandidates(localOrigin, localDir, rayLength, candidateElements);
        }
        finally
        {
            traversalStack.Dispose();
            candidateElements.Dispose();
        }
    }

    private void RayQueryBvh(float3 rayOrigin, float3 rayDir, float tMax,
        NativeList<int> candidateElements, NativeList<int> stack)
    {
        var nodes = m_Bvh.Nodes;
        var elements = m_Bvh.Elements;
        if (nodes.Length == 0)
            return;

        float3 invDir = rcp(rayDir);

        stack.Clear();
        stack.Add(m_Bvh.RootIndex);

        while (stack.Length > 0)
        {
            int last = stack.Length - 1;
            int nodeIndex = stack[last];
            stack.RemoveAtSwapBack(last);

            if ((uint)nodeIndex >= (uint)nodes.Length)
                continue;

            BvhNode node = nodes[nodeIndex];
            if (!RayIntersectsAabb(rayOrigin, invDir, 0f, tMax, node.Bounds))
                continue;

            if (node.Leaf)
            {
                for (int i = 0; i < node.ElementCount; i++)
                {
                    int elementIndex = node.FirstElement + i;
                    if ((uint)elementIndex < (uint)elements.Length)
                        candidateElements.Add(elementIndex);
                }
            }
            else
            {
                stack.Add(node.Left);
                stack.Add(node.Right);
            }
        }
    }

    private static bool RayIntersectsAabb(float3 rayOrigin, float3 invDir, float tNear, float tFar, BvhAabb aabb)
    {
        float3 t0 = (aabb.Min - rayOrigin) * invDir;
        float3 t1 = (aabb.Max - rayOrigin) * invDir;
        float3 tmin = min(t0, t1);
        float3 tmax = max(t0, t1);

        float enter = max(tNear, max(tmin.x, max(tmin.y, tmin.z)));
        float exit = min(tFar, min(tmax.x, min(tmax.y, tmax.z)));

        return enter <= exit;
    }

    private void ProcessRayCandidates(float3 rayOrigin, float3 rayDir, float tMax, NativeList<int> candidateElements)
    {
        var elements = m_Bvh.Elements;

        if (m_Detail.TryGetVertexAccessor<float4>(AttributeID.Color, out var colorAccessor) != CoreResult.Success)
            return;

        float4 red = new float4(1f, 0f, 0f, 1f);
        var outPositions = new NativeList<float3>(16, Allocator.Temp);
        var outIndices = new NativeList<int>(48, Allocator.Temp);

        try
        {
            for (int c = 0; c < candidateElements.Length; c++)
            {
                int elementIndex = candidateElements[c];
                int primitiveIndex = elements[elementIndex].Value;

                if (RayHitsPrimitive(primitiveIndex, rayOrigin, rayDir, tMax, ref outPositions, ref outIndices))
                {
                    m_RayHitPrimitiveCount++;
                    PaintPrimitiveVertices(primitiveIndex, red, colorAccessor);
                }
            }
        }
        finally
        {
            outIndices.Dispose();
            outPositions.Dispose();
        }
    }

    private bool RayHitsPrimitive(int primitiveIndex, float3 rayOrigin, float3 rayDir, float tMax,
        ref NativeList<float3> outPositions, ref NativeList<int> outIndices)
    {
        NativeSlice<int> vertices = m_Detail.GetPrimitiveVertices(primitiveIndex);
        int vertCount = vertices.Length;
        if (vertCount < 3)
            return false;

        if (vertCount == 3)
        {
            float3 v0 = m_Detail.GetPointPosition(m_Detail.GetVertexPoint(vertices[0]));
            float3 v1 = m_Detail.GetPointPosition(m_Detail.GetVertexPoint(vertices[1]));
            float3 v2 = m_Detail.GetPointPosition(m_Detail.GetVertexPoint(vertices[2]));

            return SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(
                rayOrigin, rayDir, 0f, tMax, v0, v1, v2, out _, false);
        }

        var positions = new NativeArray<float3>(vertCount, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, vertCount }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(vertCount, Allocator.Temp);
        try
        {
            for (int i = 0; i < vertCount; i++)
            {
                positions[i] = m_Detail.GetPointPosition(m_Detail.GetVertexPoint(vertices[i]));
                contourPointIndices[i] = i;
            }

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            outPositions.Clear();
            outIndices.Clear();
            CoreResult result = Triangulation.TriangulateRaw(in contours, ref outPositions, ref outIndices);
            if (result != CoreResult.Success)
                return false;

            int triCount = outIndices.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                float3 v0 = outPositions[outIndices[t * 3]];
                float3 v1 = outPositions[outIndices[t * 3 + 1]];
                float3 v2 = outPositions[outIndices[t * 3 + 2]];

                if (SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(
                        rayOrigin, rayDir, 0f, tMax, v0, v1, v2, out _, false))
                    return true;
            }
        }
        finally
        {
            contourPointIndices.Dispose();
            contourOffsets.Dispose();
            positions.Dispose();
        }

        return false;
    }

    private void PaintPrimitiveVertices(int primitiveIndex, float4 color, NativeAttributeAccessor<float4> colorAccessor)
    {
        NativeSlice<int> vertices = m_Detail.GetPrimitiveVertices(primitiveIndex);
        for (int i = 0; i < vertices.Length; i++)
            colorAccessor[vertices[i]] = color;
    }

    private void BakeUnityMesh()
    {
        EnsureRuntimeMesh();

        NativeCompiledDetail compiled = default;
        try
        {
            compiled = m_Detail.Compile(Allocator.TempJob);
            compiled.FillUnityMesh(m_RuntimeMesh);

            if (m_TargetMeshFilter != null && m_TargetMeshFilter.sharedMesh != m_RuntimeMesh)
                m_TargetMeshFilter.sharedMesh = m_RuntimeMesh;
        }
        finally
        {
            if (compiled.IsCreated)
                compiled.Dispose();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!m_DrawBvh || !m_BvhCreated || !m_Bvh.IsCreated || m_Bvh.RootIndex < 0)
            return;

        var prevMatrix = Gizmos.matrix;
        var prevColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;

        try
        {
            DrawBvhNodes();
            DrawRayGizmo();
        }
        finally
        {
            Gizmos.color = prevColor;
            Gizmos.matrix = prevMatrix;
        }
    }

    private void DrawRayGizmo()
    {
        if (m_RayOrigin == null || m_RayTarget == null)
            return;

        float4x4 worldToLocal = Unity.Mathematics.float4x4.identity;
        if (transform != null)
            worldToLocal = inverse((float4x4)transform.localToWorldMatrix);

        float3 localOrigin = mul(worldToLocal, float4((float3)m_RayOrigin.position, 1f)).xyz;
        float3 localTarget = mul(worldToLocal, float4((float3)m_RayTarget.position, 1f)).xyz;

        Gizmos.color = new Color(1f, 1f, 0f, 1f);
        Gizmos.DrawLine(localOrigin, localTarget);
        Gizmos.DrawWireSphere(localOrigin, 0.01f);
        Gizmos.DrawWireSphere(localTarget, 0.01f);
    }

    private void DrawBvhNodes()
    {
        var nodes = m_Bvh.Nodes;
        if (nodes.Length == 0)
            return;

        var stack = new NativeList<int2>(64, Allocator.Temp);
        try
        {
            stack.Add(new int2(m_Bvh.RootIndex, 0));
            int leafCount = 0;

            while (stack.Length > 0)
            {
                int last = stack.Length - 1;
                int2 entry = stack[last];
                stack.RemoveAtSwapBack(last);

                int nodeIndex = entry.x;
                int depth = entry.y;
                if ((uint)nodeIndex >= (uint)nodes.Length)
                    continue;

                BvhNode node = nodes[nodeIndex];
                if (node.Leaf)
                    leafCount++;

                bool drawNode = (node.Leaf && m_DrawLeafNodes) || (!node.Leaf && m_DrawInternalNodes);
                if (drawNode && depth <= m_MaxDepthToDraw)
                {
                    float t = m_MaxDepthToDraw > 0 ? saturate(depth / (float)max(1, m_MaxDepthToDraw)) : 0f;
                    Color c = m_DepthGradient.Evaluate(t);
                    c.a = node.Leaf ? m_LeafAlpha : m_InternalAlpha;
                    Gizmos.color = c;

                    float3 center = node.Bounds.Center;
                    float3 size = max(node.Bounds.Max - node.Bounds.Min, new float3(0.001f));
                    Gizmos.DrawWireCube(center, size);
                }

                if (!node.Leaf && depth < m_MaxDepthToDraw)
                {
                    stack.Add(new int2(node.Left, depth + 1));
                    stack.Add(new int2(node.Right, depth + 1));
                }
            }

            m_LeafCount = leafCount;
        }
        finally
        {
            if (stack.IsCreated)
                stack.Dispose();
        }
    }

    private void EnsureDetail()
    {
        if (m_DetailCreated)
            return;

        SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(
            m_LongitudeSegments, m_LatitudeSegments,
            out int pointCount, out int vertexCount, out int primitiveCount);

        m_Detail = new NativeDetail(pointCount, vertexCount, primitiveCount, Allocator.Persistent);
        m_DetailCreated = true;
    }

    private void EnsureBvh(int capacity)
    {
        if (m_BvhCreated)
            return;

        m_Bvh = new NativeBvh<int>(max(1, capacity), Allocator.Persistent, max(1, m_MaxLeafSize));
        m_BvhCreated = true;
    }

    private void EnsureRuntimeMesh()
    {
        if (m_RuntimeMesh != null)
            return;

        if (m_TargetMeshFilter == null)
            TryGetComponent(out m_TargetMeshFilter);

        m_RuntimeMesh = new Mesh { name = "SphereBvhLiveMesh" };
        m_RuntimeMesh.MarkDynamic();

        if (m_TargetMeshFilter != null)
            m_TargetMeshFilter.sharedMesh = m_RuntimeMesh;
    }

    private void EnsureGradient()
    {
        if (m_DepthGradient != null && m_DepthGradient.colorKeys.Length > 1)
            return;

        m_DepthGradient = new Gradient();
        m_DepthGradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.2f, 1f, 0.2f), 0f),
                new GradientColorKey(new Color(1f, 1f, 0.2f), 0.5f),
                new GradientColorKey(new Color(1f, 0.2f, 0.2f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
    }

    private void DisposeAll()
    {
        if (m_TargetMeshFilter != null && m_TargetMeshFilter.sharedMesh == m_RuntimeMesh)
            m_TargetMeshFilter.sharedMesh = null;

        if (m_RuntimeMesh != null)
        {
            if (Application.isPlaying)
                Destroy(m_RuntimeMesh);
            else
                DestroyImmediate(m_RuntimeMesh);

            m_RuntimeMesh = null;
        }

        if (m_BvhCreated)
        {
            m_Bvh.Dispose();
            m_BvhCreated = false;
        }

        if (m_DetailCreated)
        {
            m_Detail.Dispose();
            m_DetailCreated = false;
        }
    }
}
