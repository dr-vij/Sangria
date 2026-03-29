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

        BuildBvhFromPrimitives();
        BakeUnityMesh();
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
        }
        finally
        {
            Gizmos.color = prevColor;
            Gizmos.matrix = prevMatrix;
        }
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
