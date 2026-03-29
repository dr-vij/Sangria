using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace SangriaMesh
{
    public struct NativeBvh<T> : IDisposable where T : unmanaged
    {
        private NativeList<BvhNode> nodes;
        private NativeList<BvhElement<T>> elements;
        private int rootIndex;
        private int maxLeafSize;

        public NativeBvh(int initialCapacity, Allocator allocator, int maxLeafSize = 4)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            if (maxLeafSize < 1)
                throw new ArgumentOutOfRangeException(nameof(maxLeafSize));

            int elementCapacity = max(1, initialCapacity);
            int nodeCapacity = max(1, initialCapacity * 2);

            nodes = new NativeList<BvhNode>(nodeCapacity, allocator);
            elements = new NativeList<BvhElement<T>>(elementCapacity, allocator);
            rootIndex = -1;
            this.maxLeafSize = maxLeafSize;
        }

        public bool IsCreated => nodes.IsCreated && elements.IsCreated;
        public int NodeCount => nodes.IsCreated ? nodes.Length : 0;
        public int ElementCount => elements.IsCreated ? elements.Length : 0;
        public int RootIndex => rootIndex;
        public int MaxLeafSize => maxLeafSize;
        public NativeArray<BvhNode> Nodes => nodes.IsCreated ? nodes.AsArray() : default;
        public NativeArray<BvhElement<T>> Elements => elements.IsCreated ? elements.AsArray() : default;

        public void SetMaxLeafSize(int value)
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value));

            maxLeafSize = value;
        }

        public void Clear()
        {
            RequireCreated();

            nodes.Clear();
            elements.Clear();
            rootIndex = -1;
        }

        public void Build(NativeArray<BvhElement<T>> source)
        {
            RequireCreated();

            var buildStack = new NativeList<int4>(64, Allocator.Temp);
            var sortStack = new NativeList<int2>(64, Allocator.Temp);
            try
            {
                Build(source, buildStack, sortStack);
            }
            finally
            {
                if (sortStack.IsCreated)
                    sortStack.Dispose();
                if (buildStack.IsCreated)
                    buildStack.Dispose();
            }
        }

        public void Build(NativeList<BvhElement<T>> source)
        {
            Build(source.AsArray());
        }

        public void Build(
            NativeArray<BvhElement<T>> source,
            NativeList<int4> buildStack,
            NativeList<int2> sortStack)
        {
            if (!IsCreated)
                return;

            buildStack.Clear();
            sortStack.Clear();
            nodes.Clear();
            elements.Clear();
            rootIndex = -1;

            int sourceLength = source.Length;
            if (sourceLength == 0)
                return;

            EnsureCapacity(sourceLength);

            elements.ResizeUninitialized(sourceLength);
            elements.AsArray().CopyFrom(source);

            nodes.ResizeUninitialized(1);
            nodes[0] = default;
            rootIndex = 0;

            buildStack.Add(new int4(rootIndex, 0, sourceLength, 0));

            while (buildStack.Length > 0)
            {
                int last = buildStack.Length - 1;
                int4 task = buildStack[last];
                buildStack.RemoveAtSwapBack(last);

                int nodeIndex = task.x;
                int first = task.y;
                int count = task.z;

                ComputeBounds(first, count, out BvhAabb nodeBounds, out BvhAabb centroidBounds);
                float3 centroidExtent = centroidBounds.Max - centroidBounds.Min;
                bool makeLeaf = count <= maxLeafSize || cmax(centroidExtent) <= 1e-6f;

                if (makeLeaf)
                {
                    nodes[nodeIndex] = new BvhNode
                    {
                        Bounds = nodeBounds,
                        Left = -1,
                        Right = -1,
                        FirstElement = first,
                        ElementCount = count,
                        IsLeaf = 1
                    };
                    continue;
                }

                int axis = SelectAxis(centroidExtent);
                float split = (centroidBounds.Min[axis] + centroidBounds.Max[axis]) * 0.5f;
                int mid = Partition(first, count, axis, split);

                if (mid <= first || mid >= first + count)
                {
                    SortRangeByAxis(first, count, axis, sortStack);
                    mid = first + (count >> 1);
                }

                int leftCount = mid - first;
                int rightCount = count - leftCount;

                if (leftCount <= 0 || rightCount <= 0)
                {
                    nodes[nodeIndex] = new BvhNode
                    {
                        Bounds = nodeBounds,
                        Left = -1,
                        Right = -1,
                        FirstElement = first,
                        ElementCount = count,
                        IsLeaf = 1
                    };
                    continue;
                }

                int leftNode = nodes.Length;
                nodes.Add(default);
                int rightNode = nodes.Length;
                nodes.Add(default);

                nodes[nodeIndex] = new BvhNode
                {
                    Bounds = nodeBounds,
                    Left = leftNode,
                    Right = rightNode,
                    FirstElement = first,
                    ElementCount = count,
                    IsLeaf = 0
                };

                buildStack.Add(new int4(rightNode, mid, rightCount, 0));
                buildStack.Add(new int4(leftNode, first, leftCount, 0));
            }
        }

        public void Refit()
        {
            RequireCreated();

            var traversalStack = new NativeList<int2>(64, Allocator.Temp);
            try
            {
                Refit(traversalStack);
            }
            finally
            {
                if (traversalStack.IsCreated)
                    traversalStack.Dispose();
            }
        }

        public void Refit(NativeList<int2> traversalStack)
        {
            if (!IsCreated)
                return;

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
                    BvhNode left = nodes[node.Left];
                    BvhNode right = nodes[node.Right];
                    node.Bounds = BvhAabb.Union(left.Bounds, right.Bounds);
                }

                nodes[nodeIndex] = node;
            }
        }

        public void Query(BvhAabb bounds, NativeList<int> elementIndices)
        {
            RequireCreated();

            var traversalStack = new NativeList<int>(64, Allocator.Temp);
            try
            {
                Query(bounds, elementIndices, traversalStack);
            }
            finally
            {
                if (traversalStack.IsCreated)
                    traversalStack.Dispose();
            }
        }

        public void Query(BvhAabb bounds, NativeList<int> elementIndices, NativeList<int> traversalStack)
        {
            elementIndices.Clear();
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
                            elementIndices.Add(elementIndex);
                    }
                }
                else
                {
                    traversalStack.Add(node.Left);
                    traversalStack.Add(node.Right);
                }
            }
        }

        public void Query(BvhAabb bounds, NativeList<BvhElement<T>> results)
        {
            RequireCreated();

            var traversalStack = new NativeList<int>(64, Allocator.Temp);
            try
            {
                Query(bounds, results, traversalStack);
            }
            finally
            {
                if (traversalStack.IsCreated)
                    traversalStack.Dispose();
            }
        }

        public void Query(BvhAabb bounds, NativeList<BvhElement<T>> results, NativeList<int> traversalStack)
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

        public bool TryGetElement(int index, out BvhElement<T> element)
        {
            if (!elements.IsCreated || (uint)index >= (uint)elements.Length)
            {
                element = default;
                return false;
            }

            element = elements[index];
            return true;
        }

        public void SetElementBounds(int index, BvhAabb bounds)
        {
            if ((uint)index >= (uint)elements.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            BvhElement<T> element = elements[index];
            element.Bounds = bounds;
            elements[index] = element;
        }

        public void SetElementValue(int index, T value)
        {
            if ((uint)index >= (uint)elements.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            BvhElement<T> element = elements[index];
            element.Value = value;
            elements[index] = element;
        }

        public void Dispose()
        {
            if (nodes.IsCreated)
                nodes.Dispose();
            if (elements.IsCreated)
                elements.Dispose();

            rootIndex = -1;
            maxLeafSize = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int elementCount)
        {
            if (elements.Capacity < elementCount)
                elements.Capacity = elementCount;

            int neededNodes = max(1, elementCount * 2);
            if (nodes.Capacity < neededNodes)
                nodes.Capacity = neededNodes;
        }

        private void ComputeBounds(int first, int count, out BvhAabb nodeBounds, out BvhAabb centroidBounds)
        {
            BvhAabb firstBounds = elements[first].Bounds;
            float3 centroid = firstBounds.Center;

            float3 nodeMin = firstBounds.Min;
            float3 nodeMax = firstBounds.Max;
            float3 centroidMin = centroid;
            float3 centroidMax = centroid;

            int end = first + count;
            for (int i = first + 1; i < end; i++)
            {
                BvhAabb bounds = elements[i].Bounds;
                nodeMin = min(nodeMin, bounds.Min);
                nodeMax = max(nodeMax, bounds.Max);

                float3 c = bounds.Center;
                centroidMin = min(centroidMin, c);
                centroidMax = max(centroidMax, c);
            }

            nodeBounds = new BvhAabb { Min = nodeMin, Max = nodeMax };
            centroidBounds = new BvhAabb { Min = centroidMin, Max = centroidMax };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SelectAxis(float3 extent)
        {
            if (extent.x >= extent.y && extent.x >= extent.z)
                return 0;
            if (extent.y >= extent.z)
                return 1;
            return 2;
        }

        private int Partition(int first, int count, int axis, float split)
        {
            int left = first;
            int right = first + count - 1;

            while (left <= right)
            {
                while (left <= right && GetCentroidAlongAxis(left, axis) < split)
                    left++;

                while (left <= right && GetCentroidAlongAxis(right, axis) >= split)
                    right--;

                if (left < right)
                {
                    SwapElements(left, right);
                    left++;
                    right--;
                }
            }

            return left;
        }

        private void SortRangeByAxis(int first, int count, int axis, NativeList<int2> sortStack)
        {
            sortStack.Clear();
            sortStack.Add(new int2(first, first + count - 1));

            while (sortStack.Length > 0)
            {
                int last = sortStack.Length - 1;
                int2 range = sortStack[last];
                sortStack.RemoveAtSwapBack(last);

                int left = range.x;
                int right = range.y;

                while (left < right)
                {
                    int i = left;
                    int j = right;
                    float pivot = GetCentroidAlongAxis((left + right) >> 1, axis);

                    while (i <= j)
                    {
                        while (GetCentroidAlongAxis(i, axis) < pivot)
                            i++;
                        while (GetCentroidAlongAxis(j, axis) > pivot)
                            j--;

                        if (i <= j)
                        {
                            SwapElements(i, j);
                            i++;
                            j--;
                        }
                    }

                    if (j - left < right - i)
                    {
                        if (i < right)
                            sortStack.Add(new int2(i, right));
                        right = j;
                    }
                    else
                    {
                        if (left < j)
                            sortStack.Add(new int2(left, j));
                        left = i;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetCentroidAlongAxis(int elementIndex, int axis)
        {
            BvhAabb bounds = elements[elementIndex].Bounds;
            return (bounds.Min[axis] + bounds.Max[axis]) * 0.5f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SwapElements(int left, int right)
        {
            if (left == right)
                return;

            BvhElement<T> tmp = elements[left];
            elements[left] = elements[right];
            elements[right] = tmp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RequireCreated()
        {
            if (!IsCreated)
                throw new InvalidOperationException("NativeBvh is not created.");
        }
    }
}
