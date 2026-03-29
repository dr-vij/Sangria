using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace SangriaMesh
{
    [ExecuteAlways]
    public sealed class BvhGizmoVisualizer : MonoBehaviour
    {
        [Header("Build")]
        [SerializeField] private int seed = 12345;
        [SerializeField, Min(1)] private int elementCount = 2500;
        [SerializeField] private Vector3 worldExtents = new Vector3(120f, 120f, 120f);
        [SerializeField] private Vector2 elementExtentRange = new Vector2(0.4f, 2.2f);
        [SerializeField, Min(1)] private int maxLeafSize = 8;
        [SerializeField] private bool rebuildOnValidate = true;
        [SerializeField] private bool rebuildEveryFrame;

        [Header("Tree Gizmos")]
        [SerializeField] private bool drawOnlyWhenSelected = true;
        [SerializeField] private bool drawInternalNodes = true;
        [SerializeField] private bool drawLeafNodes = true;
        [SerializeField, Min(0)] private int maxDepthToDraw = 16;
        [SerializeField] private Gradient depthGradient;
        [SerializeField] private float internalAlpha = 0.28f;
        [SerializeField] private float leafAlpha = 0.9f;
        [SerializeField] private bool drawElementCenters;
        [SerializeField] private float elementCenterSize = 0.12f;
        [SerializeField] private Color elementCenterColor = new Color(1f, 0.6f, 0.12f, 1f);

        [Header("Query Gizmos")]
        [SerializeField] private bool drawQuery;
        [SerializeField] private Vector3 queryCenter = Vector3.zero;
        [SerializeField] private Vector3 queryExtents = new Vector3(25f, 25f, 25f);
        [SerializeField] private Color queryColor = new Color(0.1f, 0.75f, 1f, 1f);
        [SerializeField] private Color queryHitColor = new Color(0.2f, 1f, 0.2f, 1f);

        [Header("Runtime Info")]
        [SerializeField] private int lastNodeCount;
        [SerializeField] private int lastLeafCount;
        [SerializeField] private int lastHitCount;

        private NativeArray<BvhElement<int>> source;
        private NativeBvh<int> bvh;
        private NativeList<int> queryHits;
        private NativeList<int> queryTraversal;

        public void Rebuild()
        {
            DisposeNative();
            EnsureGradient();

            int count = max(1, elementCount);
            int leafSize = max(1, maxLeafSize);

            source = new NativeArray<BvhElement<int>>(count, Allocator.Persistent);
            FillRandomElements(source, seed, worldExtents, elementExtentRange);

            bvh = new NativeBvh<int>(count, Allocator.Persistent, leafSize);
            bvh.Build(source);

            queryHits = new NativeList<int>(Allocator.Persistent);
            queryTraversal = new NativeList<int>(Allocator.Persistent);
            UpdateStats();
        }

        private void Reset()
        {
            EnsureGradient();
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            DisposeNative();
        }

        private void OnDestroy()
        {
            DisposeNative();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled || !rebuildOnValidate)
                return;

            Rebuild();
        }

        private void Update()
        {
            if (!rebuildEveryFrame)
                return;

            Rebuild();
        }

        private void OnDrawGizmos()
        {
            if (drawOnlyWhenSelected)
                return;

            DrawGizmosInternal();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawOnlyWhenSelected)
                return;

            DrawGizmosInternal();
        }

        private void DrawGizmosInternal()
        {
            if (!bvh.IsCreated || bvh.RootIndex < 0)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            try
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                DrawTreeNodes();

                if (drawElementCenters)
                    DrawElementCenters();

                if (drawQuery)
                    DrawQuery();
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
            }
        }

        private void DrawTreeNodes()
        {
            var nodes = bvh.Nodes;
            if (nodes.Length == 0)
                return;

            var stack = new NativeList<int2>(64, Allocator.Temp);
            try
            {
                stack.Add(new int2(bvh.RootIndex, 0));

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

                    bool drawNode = (node.Leaf && drawLeafNodes) || (!node.Leaf && drawInternalNodes);
                    if (drawNode && depth <= maxDepthToDraw)
                    {
                        float t = maxDepthToDraw > 0 ? saturate(depth / (float)max(1, maxDepthToDraw)) : 0f;
                        Color nodeColor = depthGradient.Evaluate(t);
                        nodeColor.a = node.Leaf ? leafAlpha : internalAlpha;
                        Gizmos.color = nodeColor;
                        DrawBounds(node.Bounds);
                    }

                    if (!node.Leaf && depth < maxDepthToDraw)
                    {
                        stack.Add(new int2(node.Left, depth + 1));
                        stack.Add(new int2(node.Right, depth + 1));
                    }
                }

                lastLeafCount = leafCount;
                lastNodeCount = nodes.Length;
            }
            finally
            {
                if (stack.IsCreated)
                    stack.Dispose();
            }
        }

        private void DrawElementCenters()
        {
            var elements = bvh.Elements;
            Gizmos.color = elementCenterColor;
            float size = max(0.001f, elementCenterSize);
            Vector3 cube = new Vector3(size, size, size);

            for (int i = 0; i < elements.Length; i++)
            {
                float3 center = elements[i].Bounds.Center;
                Gizmos.DrawWireCube(center, cube);
            }
        }

        private void DrawQuery()
        {
            float3 ext = max((float3)queryExtents, new float3(0.001f));
            var query = BvhAabb.FromCenterExtents((float3)queryCenter, ext);

            Gizmos.color = queryColor;
            DrawBounds(query);

            bvh.Query(query, queryHits, queryTraversal);
            lastHitCount = queryHits.Length;

            var elements = bvh.Elements;
            Gizmos.color = queryHitColor;
            for (int i = 0; i < queryHits.Length; i++)
            {
                int index = queryHits[i];
                if ((uint)index >= (uint)elements.Length)
                    continue;

                DrawBounds(elements[index].Bounds);
            }
        }

        private static void DrawBounds(BvhAabb bounds)
        {
            float3 center = bounds.Center;
            float3 size3 = max(bounds.Max - bounds.Min, new float3(0.001f));
            Gizmos.DrawWireCube(center, size3);
        }

        private static void FillRandomElements(
            NativeArray<BvhElement<int>> target,
            int seed,
            Vector3 worldExt,
            Vector2 extentRange)
        {
            float3 ext = max((float3)worldExt, new float3(0.001f));
            float minExtent = max(0.001f, min(extentRange.x, extentRange.y));
            float maxExtent = max(minExtent, max(extentRange.x, extentRange.y));
            uint randomSeed = (uint)(seed == 0 ? 1 : seed);
            var random = new Unity.Mathematics.Random(randomSeed);

            for (int i = 0; i < target.Length; i++)
            {
                float3 center = new float3(
                    random.NextFloat(-ext.x, ext.x),
                    random.NextFloat(-ext.y, ext.y),
                    random.NextFloat(-ext.z, ext.z));

                float3 elementExt = new float3(
                    random.NextFloat(minExtent, maxExtent),
                    random.NextFloat(minExtent, maxExtent),
                    random.NextFloat(minExtent, maxExtent));

                target[i] = new BvhElement<int>
                {
                    Bounds = BvhAabb.FromCenterExtents(center, elementExt),
                    Value = i
                };
            }
        }

        private void UpdateStats()
        {
            if (!bvh.IsCreated)
            {
                lastNodeCount = 0;
                lastLeafCount = 0;
                lastHitCount = 0;
                return;
            }

            lastNodeCount = bvh.NodeCount;
            lastLeafCount = 0;
            lastHitCount = 0;
        }

        private void EnsureGradient()
        {
            if (depthGradient != null && depthGradient.colorKeys != null && depthGradient.colorKeys.Length > 0)
                return;

            depthGradient = new Gradient();
            depthGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.25f, 0.85f, 1f), 0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.2f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
        }

        private void DisposeNative()
        {
            if (queryTraversal.IsCreated)
                queryTraversal.Dispose();
            if (queryHits.IsCreated)
                queryHits.Dispose();
            if (bvh.IsCreated)
                bvh.Dispose();
            if (source.IsCreated)
                source.Dispose();
        }
    }
}
