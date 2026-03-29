using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace SangriaMesh
{
    public static class BvhJobs
    {
        [BurstCompile]
        public struct RefitJob<T> : IJob where T : unmanaged
        {
            [NativeDisableParallelForRestriction] public NativeArray<BvhNode> Nodes;
            [ReadOnly] public NativeArray<BvhElement<T>> Elements;
            public int RootIndex;
            public NativeList<int2> TraversalStack;

            public void Execute()
            {
                Refit(Nodes, Elements, RootIndex, TraversalStack);
            }
        }

        [BurstCompile]
        public struct OverlapIndicesJob<T> : IJob where T : unmanaged
        {
            [ReadOnly] public NativeArray<BvhNode> Nodes;
            [ReadOnly] public NativeArray<BvhElement<T>> Elements;
            public int RootIndex;
            [ReadOnly] public BvhAabb Bounds;

            public NativeList<int> Results;
            public NativeList<int> TraversalStack;

            public void Execute()
            {
                QueryIndices(Nodes, Elements, RootIndex, Bounds, Results, TraversalStack);
            }
        }

        [BurstCompile]
        public struct OverlapElementsJob<T> : IJob where T : unmanaged
        {
            [ReadOnly] public NativeArray<BvhNode> Nodes;
            [ReadOnly] public NativeArray<BvhElement<T>> Elements;
            public int RootIndex;
            [ReadOnly] public BvhAabb Bounds;

            public NativeList<BvhElement<T>> Results;
            public NativeList<int> TraversalStack;

            public void Execute()
            {
                QueryElements(Nodes, Elements, RootIndex, Bounds, Results, TraversalStack);
            }
        }

        private static void Refit<T>(
            NativeArray<BvhNode> nodes,
            NativeArray<BvhElement<T>> elements,
            int rootIndex,
            NativeList<int2> traversalStack) where T : unmanaged
        {
            if (rootIndex < 0 || nodes.Length == 0)
                return;

            traversalStack.Clear();
            traversalStack.Add(new int2(rootIndex, 0));

            while (traversalStack.Length > 0)
            {
                int last = traversalStack.Length - 1;
                int2 entry = traversalStack[last];
                traversalStack.RemoveAtSwapBack(last);

                int nodeIndex = entry.x;
                int state = entry.y;
                BvhNode node = nodes[nodeIndex];

                if (state == 0)
                {
                    traversalStack.Add(new int2(nodeIndex, 1));
                    if (!node.Leaf)
                    {
                        traversalStack.Add(new int2(node.Right, 0));
                        traversalStack.Add(new int2(node.Left, 0));
                    }

                    continue;
                }

                if (node.Leaf)
                {
                    int first = node.FirstElement;
                    int count = node.ElementCount;
                    if (count <= 0)
                    {
                        node.Bounds = default;
                    }
                    else
                    {
                        BvhAabb merged = elements[first].Bounds;
                        for (int i = 1; i < count; i++)
                            merged = BvhAabb.Union(merged, elements[first + i].Bounds);

                        node.Bounds = merged;
                    }
                }
                else
                {
                    node.Bounds = BvhAabb.Union(nodes[node.Left].Bounds, nodes[node.Right].Bounds);
                }

                nodes[nodeIndex] = node;
            }
        }

        private static void QueryIndices<T>(
            NativeArray<BvhNode> nodes,
            NativeArray<BvhElement<T>> elements,
            int rootIndex,
            BvhAabb bounds,
            NativeList<int> results,
            NativeList<int> traversalStack) where T : unmanaged
        {
            results.Clear();
            traversalStack.Clear();

            if (rootIndex < 0 || nodes.Length == 0)
                return;

            traversalStack.Add(rootIndex);

            while (traversalStack.Length > 0)
            {
                int last = traversalStack.Length - 1;
                int nodeIndex = traversalStack[last];
                traversalStack.RemoveAtSwapBack(last);

                BvhNode node = nodes[nodeIndex];
                if (!node.Bounds.Intersects(bounds))
                    continue;

                if (node.Leaf)
                {
                    int first = node.FirstElement;
                    int count = node.ElementCount;
                    for (int i = 0; i < count; i++)
                    {
                        int elementIndex = first + i;
                        if (elements[elementIndex].Bounds.Intersects(bounds))
                            results.Add(elementIndex);
                    }
                }
                else
                {
                    traversalStack.Add(node.Left);
                    traversalStack.Add(node.Right);
                }
            }
        }

        private static void QueryElements<T>(
            NativeArray<BvhNode> nodes,
            NativeArray<BvhElement<T>> elements,
            int rootIndex,
            BvhAabb bounds,
            NativeList<BvhElement<T>> results,
            NativeList<int> traversalStack) where T : unmanaged
        {
            results.Clear();
            traversalStack.Clear();

            if (rootIndex < 0 || nodes.Length == 0)
                return;

            traversalStack.Add(rootIndex);

            while (traversalStack.Length > 0)
            {
                int last = traversalStack.Length - 1;
                int nodeIndex = traversalStack[last];
                traversalStack.RemoveAtSwapBack(last);

                BvhNode node = nodes[nodeIndex];
                if (!node.Bounds.Intersects(bounds))
                    continue;

                if (node.Leaf)
                {
                    int first = node.FirstElement;
                    int count = node.ElementCount;
                    for (int i = 0; i < count; i++)
                    {
                        BvhElement<T> element = elements[first + i];
                        if (element.Bounds.Intersects(bounds))
                            results.Add(element);
                    }
                }
                else
                {
                    traversalStack.Add(node.Left);
                    traversalStack.Add(node.Right);
                }
            }
        }
    }
}
