using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace SangriaMesh
{
    public static class KdTreeJobs
    {
        [BurstCompile]
        public struct BuildJob<T> : IJob where T : unmanaged
        {
            public NativeArray<KdElement<T>> Points;
            public int Count;
            public NativeList<int3> SortStack;

            public void Execute()
            {
                BuildIterative(Points, Count, SortStack);
            }
        }

        [BurstCompile]
        public struct FindNearestJob<T> : IJob where T : unmanaged
        {
            [ReadOnly] public NativeArray<KdElement<T>> Points;
            public int Count;
            [ReadOnly] public float3 Target;
            public NativeArray<int> ResultIndex;
            public NativeList<int3> TraversalStack;

            public void Execute()
            {
                ResultIndex[0] = FindNearestImpl(Points, Count, Target, TraversalStack);
            }
        }

        [BurstCompile]
        public struct FindKNearestJob<T> : IJob where T : unmanaged
        {
            [ReadOnly] public NativeArray<KdElement<T>> Points;
            public int Count;
            [ReadOnly] public float3 Target;
            public int K;
            public NativeList<int> ResultIndices;
            public NativeList<float> ResultDistancesSq;
            public NativeList<int3> TraversalStack;

            public void Execute()
            {
                ResultIndices.Clear();
                ResultDistancesSq.Clear();
                FindKNearestImpl(Points, Count, Target, K, ResultIndices, ResultDistancesSq, TraversalStack);
            }
        }

        [BurstCompile]
        public struct RadialSearchJob<T> : IJob where T : unmanaged
        {
            [ReadOnly] public NativeArray<KdElement<T>> Points;
            public int Count;
            [ReadOnly] public float3 Center;
            public float Radius;
            public NativeList<int> ResultIndices;
            public NativeList<float> ResultDistancesSq;
            public NativeList<int3> TraversalStack;

            public void Execute()
            {
                ResultIndices.Clear();
                ResultDistancesSq.Clear();
                RadialSearchImpl(Points, Count, Center, Radius, ResultIndices, ResultDistancesSq, TraversalStack);
            }
        }

        private static void BuildIterative<T>(NativeArray<KdElement<T>> points, int count, NativeList<int3> sortStack)
            where T : unmanaged
        {
            sortStack.Clear();
            sortStack.Add(new int3(0, count, 0));

            while (sortStack.Length > 0)
            {
                int last = sortStack.Length - 1;
                int3 task = sortStack[last];
                sortStack.RemoveAtSwapBack(last);

                int start = task.x;
                int end = task.y;
                int axis = task.z;

                if (end - start <= 1)
                    continue;

                int mid = (start + end) / 2;
                NthElement(points, start, end, mid, axis);

                int nextAxis = (axis + 1) % 3;
                if (mid + 1 < end)
                    sortStack.Add(new int3(mid + 1, end, nextAxis));
                if (start < mid)
                    sortStack.Add(new int3(start, mid, nextAxis));
            }
        }

        private static void NthElement<T>(NativeArray<KdElement<T>> points, int start, int end, int nth, int axis)
            where T : unmanaged
        {
            int left = start;
            int right = end - 1;

            while (left < right)
            {
                int pivotIndex = MedianOfThreeIndex(points, left, right, axis);
                float pivotVal = points[pivotIndex].Position[axis];

                Swap(points, pivotIndex, right);
                int storeIndex = left;

                for (int i = left; i < right; i++)
                {
                    if (points[i].Position[axis] < pivotVal)
                    {
                        Swap(points, i, storeIndex);
                        storeIndex++;
                    }
                }

                Swap(points, storeIndex, right);

                if (storeIndex == nth)
                    break;
                if (storeIndex < nth)
                    left = storeIndex + 1;
                else
                    right = storeIndex - 1;
            }
        }

        private static int MedianOfThreeIndex<T>(NativeArray<KdElement<T>> points, int left, int right, int axis)
            where T : unmanaged
        {
            int mid = (left + right) / 2;
            float a = points[left].Position[axis];
            float b = points[mid].Position[axis];
            float c = points[right].Position[axis];

            if (a <= b)
                return b <= c ? mid : (a <= c ? right : left);
            return a <= c ? left : (b <= c ? right : mid);
        }

        private static void Swap<T>(NativeArray<KdElement<T>> points, int i, int j) where T : unmanaged
        {
            if (i == j) return;
            KdElement<T> tmp = points[i];
            points[i] = points[j];
            points[j] = tmp;
        }

        private static int FindNearestImpl<T>(NativeArray<KdElement<T>> points, int count, float3 target,
            NativeList<int3> traversalStack) where T : unmanaged
        {
            if (count == 0)
                return -1;

            traversalStack.Clear();
            int bestIndex = -1;
            float bestDistSq = float.MaxValue;

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

                if (diff > 0)
                {
                    if (diff * diff <= bestDistSq && mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                    if (start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                }
                else
                {
                    if (diff * diff <= bestDistSq && start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                    if (mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                }
            }

            return bestIndex;
        }

        private static void FindKNearestImpl<T>(NativeArray<KdElement<T>> points, int count, float3 target, int k,
            NativeList<int> resultIndices, NativeList<float> resultDistancesSq,
            NativeList<int3> traversalStack) where T : unmanaged
        {
            if (count == 0 || k <= 0)
                return;

            traversalStack.Clear();
            float maxDistSq = float.MaxValue;
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

        private static void RadialSearchImpl<T>(NativeArray<KdElement<T>> points, int count, float3 center, float radius,
            NativeList<int> resultIndices, NativeList<float> resultDistancesSq,
            NativeList<int3> traversalStack) where T : unmanaged
        {
            if (count == 0 || radius <= 0f)
                return;

            float radiusSq = radius * radius;

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
                    if (start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                    if (diff * diff <= radiusSq && mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                }
                else
                {
                    if (mid + 1 < end)
                        traversalStack.Add(new int3(mid + 1, end, nextAxis));
                    if (diff * diff <= radiusSq && start < mid)
                        traversalStack.Add(new int3(start, mid, nextAxis));
                }
            }
        }

        private static void BoundedInsert(NativeList<int> indices, NativeList<float> distancesSq,
            int capacity, int index, float distSq, ref float maxDistSq)
        {
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
    }
}
