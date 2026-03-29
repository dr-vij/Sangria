using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace SangriaMesh
{
    public struct NativeKdTree<T> : IDisposable where T : unmanaged
    {
        private NativeList<KdElement<T>> points;
        private int count;

        public NativeKdTree(int initialCapacity, Allocator allocator)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            int capacity = max(1, initialCapacity);
            points = new NativeList<KdElement<T>>(capacity, allocator);
            count = 0;
        }

        public bool IsCreated => points.IsCreated;
        public int Count => count;
        public NativeArray<KdElement<T>> Points => points.IsCreated ? points.AsArray().GetSubArray(0, count) : default;

        public void Clear()
        {
            RequireCreated();
            points.Clear();
            count = 0;
        }

        public void Build(NativeArray<KdElement<T>> source)
        {
            RequireCreated();

            var sortStack = new NativeList<int3>(64, Allocator.Temp);
            try
            {
                Build(source, sortStack);
            }
            finally
            {
                if (sortStack.IsCreated)
                    sortStack.Dispose();
            }
        }

        public void Build(NativeArray<KdElement<T>> source, NativeList<int3> sortStack)
        {
            RequireCreated();

            sortStack.Clear();
            points.Clear();
            count = 0;

            int sourceLength = source.Length;
            if (sourceLength == 0)
                return;

            if (points.Capacity < sourceLength)
                points.Capacity = sourceLength;

            points.ResizeUninitialized(sourceLength);
            points.AsArray().CopyFrom(source);
            count = sourceLength;

            BuildRecursiveIterative(sortStack);
        }

        public void Build(NativeList<KdElement<T>> source)
        {
            Build(source.AsArray());
        }

        public int FindNearest(float3 target)
        {
            RequireCreated();

            if (count == 0)
                return -1;

            var stack = new NativeList<int3>(64, Allocator.Temp);
            try
            {
                return FindNearest(target, stack);
            }
            finally
            {
                if (stack.IsCreated)
                    stack.Dispose();
            }
        }

        public int FindNearest(float3 target, NativeList<int3> traversalStack)
        {
            if (count == 0)
                return -1;

            return FindNearestImpl(target, float.MaxValue, traversalStack);
        }

        public void FindKNearest(float3 target, int k, NativeList<int> resultIndices, NativeList<float> resultDistancesSq)
        {
            RequireCreated();

            resultIndices.Clear();
            resultDistancesSq.Clear();

            if (count == 0 || k <= 0)
                return;

            var stack = new NativeList<int3>(64, Allocator.Temp);
            try
            {
                FindKNearest(target, k, resultIndices, resultDistancesSq, stack);
            }
            finally
            {
                if (stack.IsCreated)
                    stack.Dispose();
            }
        }

        public void FindKNearest(float3 target, int k, NativeList<int> resultIndices, NativeList<float> resultDistancesSq,
            NativeList<int3> traversalStack)
        {
            resultIndices.Clear();
            resultDistancesSq.Clear();

            if (count == 0 || k <= 0)
                return;

            FindKNearestImpl(target, k, float.MaxValue, resultIndices, resultDistancesSq, traversalStack);
        }

        public void RadialSearch(float3 center, float radius, NativeList<int> resultIndices, NativeList<float> resultDistancesSq)
        {
            RequireCreated();

            resultIndices.Clear();
            resultDistancesSq.Clear();

            if (count == 0 || radius <= 0f)
                return;

            var stack = new NativeList<int3>(64, Allocator.Temp);
            try
            {
                RadialSearch(center, radius, resultIndices, resultDistancesSq, stack);
            }
            finally
            {
                if (stack.IsCreated)
                    stack.Dispose();
            }
        }

        public void RadialSearch(float3 center, float radius, NativeList<int> resultIndices, NativeList<float> resultDistancesSq,
            NativeList<int3> traversalStack)
        {
            resultIndices.Clear();
            resultDistancesSq.Clear();

            if (count == 0 || radius <= 0f)
                return;

            float radiusSq = radius * radius;

            traversalStack.Clear();
            // x = start, y = end (exclusive), z = axis
            traversalStack.Add(new int3(0, count, 0));

            while (traversalStack.Length > 0)
            {
                int last = traversalStack.Length - 1;
                int3 task = traversalStack[last];
                traversalStack.RemoveAtSwapBack(last);

                int start = task.x;
                int end = task.y;
                int axis = task.z;

                if (start >= end)
                    continue;

                int mid = (start + end) / 2;
                float3 midPos = points[mid].Position;
                float distSq = distancesq(midPos, center);

                if (distSq <= radiusSq)
                {
                    resultIndices.Add(mid);
                    resultDistancesSq.Add(distSq);
                }

                int nextAxis = (axis + 1) % 3;
                float diff = midPos[axis] - center[axis];

                if (diff > 0)
                {
                    // target is on the left side
                    if (start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                    if (diff * diff <= radiusSq && mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                }
                else
                {
                    // target is on the right side
                    if (mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                    if (diff * diff <= radiusSq && start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                }
            }
        }

        public void Dispose()
        {
            if (points.IsCreated)
                points.Dispose();

            count = 0;
        }

        // --- Private implementation ---

        private void BuildRecursiveIterative(NativeList<int3> sortStack)
        {
            // Stack-based iterative median-of-three quickselect build.
            // Each task: x = start, y = end (exclusive), z = axis
            var buildStack = new NativeList<int3>(64, Allocator.Temp);
            try
            {
                buildStack.Add(new int3(0, count, 0));

                while (buildStack.Length > 0)
                {
                    int last = buildStack.Length - 1;
                    int3 task = buildStack[last];
                    buildStack.RemoveAtSwapBack(last);

                    int start = task.x;
                    int end = task.y;
                    int axis = task.z;

                    if (end - start <= 1)
                        continue;

                    int mid = (start + end) / 2;
                    NthElement(start, end, mid, axis, sortStack);

                    int nextAxis = (axis + 1) % 3;
                    if (mid + 1 < end)
                        buildStack.Add(new int3(mid + 1, end, nextAxis));
                    if (start < mid)
                        buildStack.Add(new int3(start, mid, nextAxis));
                }
            }
            finally
            {
                if (buildStack.IsCreated)
                    buildStack.Dispose();
            }
        }

        private void NthElement(int start, int end, int nth, int axis, NativeList<int3> stack)
        {
            // Iterative quickselect
            int left = start;
            int right = end - 1;

            while (left < right)
            {
                int pivotIndex = MedianOfThreeIndex(left, right, axis);
                float pivotVal = points[pivotIndex].Position[axis];

                Swap(pivotIndex, right);
                int storeIndex = left;

                for (int i = left; i < right; i++)
                {
                    if (points[i].Position[axis] < pivotVal)
                    {
                        Swap(i, storeIndex);
                        storeIndex++;
                    }
                }

                Swap(storeIndex, right);

                if (storeIndex == nth)
                    break;
                if (storeIndex < nth)
                    left = storeIndex + 1;
                else
                    right = storeIndex - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int MedianOfThreeIndex(int left, int right, int axis)
        {
            int mid = (left + right) / 2;
            float a = points[left].Position[axis];
            float b = points[mid].Position[axis];
            float c = points[right].Position[axis];

            if (a <= b)
                return b <= c ? mid : (a <= c ? right : left);
            return a <= c ? left : (b <= c ? right : mid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap(int i, int j)
        {
            if (i == j) return;
            KdElement<T> tmp = points[i];
            points[i] = points[j];
            points[j] = tmp;
        }

        private int FindNearestImpl(float3 target, float bestDistSq, NativeList<int3> traversalStack)
        {
            traversalStack.Clear();
            int bestIndex = -1;

            // x = start, y = end (exclusive), z = axis
            traversalStack.Add(new int3(0, count, 0));

            while (traversalStack.Length > 0)
            {
                int last = traversalStack.Length - 1;
                int3 task = traversalStack[last];
                traversalStack.RemoveAtSwapBack(last);

                int start = task.x;
                int end = task.y;
                int axis = task.z;

                if (start >= end)
                    continue;

                int mid = (start + end) / 2;
                float3 midPos = points[mid].Position;
                float distSq = distancesq(midPos, target);

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIndex = mid;
                }

                int nextAxis = (axis + 1) % 3;
                float diff = midPos[axis] - target[axis];

                // Visit nearer side first by pushing it last (stack is LIFO)
                if (diff > 0)
                {
                    // target is on left side; nearer = left, further = right
                    if (diff * diff <= bestDistSq && mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                    if (start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                }
                else
                {
                    // target is on right side; nearer = right, further = left
                    if (diff * diff <= bestDistSq && start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                    if (mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                }
            }

            return bestIndex;
        }

        private void FindKNearestImpl(float3 target, int k, float maxDistSq,
            NativeList<int> resultIndices, NativeList<float> resultDistancesSq,
            NativeList<int3> traversalStack)
        {
            traversalStack.Clear();
            traversalStack.Add(new int3(0, count, 0));

            while (traversalStack.Length > 0)
            {
                int last = traversalStack.Length - 1;
                int3 task = traversalStack[last];
                traversalStack.RemoveAtSwapBack(last);

                int start = task.x;
                int end = task.y;
                int axis = task.z;

                if (start >= end)
                    continue;

                int mid = (start + end) / 2;
                float3 midPos = points[mid].Position;
                float distSq = distancesq(midPos, target);

                if (distSq <= maxDistSq)
                    BoundedInsert(resultIndices, resultDistancesSq, k, mid, distSq, ref maxDistSq);

                int nextAxis = (axis + 1) % 3;
                float diff = midPos[axis] - target[axis];
                float diffSq = diff * diff;

                if (diff > 0)
                {
                    if (diffSq <= maxDistSq && mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                    if (start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                }
                else
                {
                    if (diffSq <= maxDistSq && start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                    if (mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                }
            }
        }

        private static void BoundedInsert(NativeList<int> indices, NativeList<float> distancesSq,
            int capacity, int index, float distSq, ref float maxDistSq)
        {
            // Find insertion position (sorted ascending by distance)
            int insertAt = indices.Length;
            for (int i = 0; i < indices.Length; i++)
            {
                if (distSq < distancesSq[i])
                {
                    insertAt = i;
                    break;
                }
            }

            if (indices.Length < capacity)
            {
                indices.InsertRangeWithBeginEnd(insertAt, insertAt + 1);
                distancesSq.InsertRangeWithBeginEnd(insertAt, insertAt + 1);
                indices[insertAt] = index;
                distancesSq[insertAt] = distSq;
            }
            else if (insertAt < capacity)
            {
                // Remove last (largest) and insert
                indices.RemoveAt(indices.Length - 1);
                distancesSq.RemoveAt(distancesSq.Length - 1);
                indices.InsertRangeWithBeginEnd(insertAt, insertAt + 1);
                distancesSq.InsertRangeWithBeginEnd(insertAt, insertAt + 1);
                indices[insertAt] = index;
                distancesSq[insertAt] = distSq;
            }

            if (indices.Length >= capacity)
                maxDistSq = distancesSq[indices.Length - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RequireCreated()
        {
            if (!IsCreated)
                throw new InvalidOperationException("NativeKdTree is not created.");
        }
    }
}
