using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SangriaMesh.Tests.KdTree
{
    public class SangriaMeshKdTreeTests
    {
        private const int ElementCount = 4096;
        private const float WorldExtent = 400f;

        [Test]
        public void FindNearestMatchesBruteForce()
        {
            var source = new NativeArray<KdElement<int>>(ElementCount, Allocator.TempJob);
            var tree = new NativeKdTree<int>(ElementCount, Allocator.TempJob);
            var stack = new NativeList<int3>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 100);
                tree.Build(source);

                Random.InitState(200);
                for (int q = 0; q < 50; q++)
                {
                    float3 target = RandomPoint();
                    int resultIndex = tree.FindNearest(target, stack);

                    Assert.IsTrue(resultIndex >= 0, "FindNearest returned -1 for non-empty tree.");

                    float bestDistSq = math.distancesq(tree.Points[resultIndex].Position, target);
                    var points = tree.Points;
                    for (int i = 0; i < points.Length; i++)
                    {
                        float distSq = math.distancesq(points[i].Position, target);
                        Assert.LessOrEqual(bestDistSq, distSq + 1e-5f,
                            $"Query {q}: brute-force found closer point at index {i} (distSq={distSq}) vs result (distSq={bestDistSq}).");
                    }
                }
            }
            finally
            {
                if (stack.IsCreated) stack.Dispose();
                if (tree.IsCreated) tree.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        [Test]
        public void FindKNearestMatchesBruteForce()
        {
            var source = new NativeArray<KdElement<int>>(ElementCount, Allocator.TempJob);
            var tree = new NativeKdTree<int>(ElementCount, Allocator.TempJob);
            var resultIndices = new NativeList<int>(Allocator.TempJob);
            var resultDists = new NativeList<float>(Allocator.TempJob);
            var stack = new NativeList<int3>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 101);
                tree.Build(source);

                int k = 10;
                Random.InitState(201);
                for (int q = 0; q < 30; q++)
                {
                    float3 target = RandomPoint();
                    tree.FindKNearest(target, k, resultIndices, resultDists, stack);

                    Assert.AreEqual(k, resultIndices.Length, $"Query {q}: expected {k} results.");

                    // Verify results are sorted
                    for (int i = 1; i < resultDists.Length; i++)
                        Assert.LessOrEqual(resultDists[i - 1], resultDists[i] + 1e-5f, "Results not sorted by distance.");

                    // Verify against brute force
                    float maxResultDistSq = resultDists[resultDists.Length - 1];
                    var points = tree.Points;

                    var inResult = new bool[points.Length];
                    for (int i = 0; i < resultIndices.Length; i++)
                        inResult[resultIndices[i]] = true;

                    int closerCount = 0;
                    for (int i = 0; i < points.Length; i++)
                    {
                        if (inResult[i]) continue;
                        float distSq = math.distancesq(points[i].Position, target);
                        if (distSq < maxResultDistSq - 1e-5f)
                            closerCount++;
                    }

                    Assert.AreEqual(0, closerCount,
                        $"Query {q}: brute-force found {closerCount} points closer than k-th neighbor.");
                }
            }
            finally
            {
                if (stack.IsCreated) stack.Dispose();
                if (resultDists.IsCreated) resultDists.Dispose();
                if (resultIndices.IsCreated) resultIndices.Dispose();
                if (tree.IsCreated) tree.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        [Test]
        public void RadialSearchMatchesBruteForce()
        {
            var source = new NativeArray<KdElement<int>>(ElementCount, Allocator.TempJob);
            var tree = new NativeKdTree<int>(ElementCount, Allocator.TempJob);
            var resultIndices = new NativeList<int>(Allocator.TempJob);
            var resultDists = new NativeList<float>(Allocator.TempJob);
            var stack = new NativeList<int3>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 102);
                tree.Build(source);

                Random.InitState(202);
                for (int q = 0; q < 30; q++)
                {
                    float3 center = RandomPoint();
                    float radius = Random.Range(20f, 100f);
                    float radiusSq = radius * radius;

                    tree.RadialSearch(center, radius, resultIndices, resultDists, stack);

                    var points = tree.Points;

                    // Brute force count
                    int expectedCount = 0;
                    for (int i = 0; i < points.Length; i++)
                    {
                        if (math.distancesq(points[i].Position, center) <= radiusSq)
                            expectedCount++;
                    }

                    Assert.AreEqual(expectedCount, resultIndices.Length,
                        $"Query {q}: radial search count mismatch (expected {expectedCount}, got {resultIndices.Length}).");

                    // All returned points should be within radius
                    for (int i = 0; i < resultIndices.Length; i++)
                    {
                        float distSq = math.distancesq(points[resultIndices[i]].Position, center);
                        Assert.LessOrEqual(distSq, radiusSq + 1e-3f,
                            $"Query {q}: result point {i} is outside radius.");
                    }
                }
            }
            finally
            {
                if (stack.IsCreated) stack.Dispose();
                if (resultDists.IsCreated) resultDists.Dispose();
                if (resultIndices.IsCreated) resultIndices.Dispose();
                if (tree.IsCreated) tree.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        [Test]
        public void FindNearestJobMatchesDirect()
        {
            var source = new NativeArray<KdElement<int>>(ElementCount, Allocator.TempJob);
            var tree = new NativeKdTree<int>(ElementCount, Allocator.TempJob);
            var directStack = new NativeList<int3>(Allocator.TempJob);
            var jobStack = new NativeList<int3>(Allocator.TempJob);
            var jobResult = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 103);
                tree.Build(source);

                Random.InitState(203);
                for (int q = 0; q < 20; q++)
                {
                    float3 target = RandomPoint();

                    int directIndex = tree.FindNearest(target, directStack);

                    var job = new KdTreeJobs.FindNearestJob<int>
                    {
                        Points = tree.Points,
                        Count = tree.Count,
                        Target = target,
                        ResultIndex = jobResult,
                        TraversalStack = jobStack
                    };

                    job.Schedule().Complete();

                    float directDist = math.distancesq(tree.Points[directIndex].Position, target);
                    float jobDist = math.distancesq(tree.Points[jobResult[0]].Position, target);

                    Assert.AreEqual(directDist, jobDist, 1e-4f,
                        $"Query {q}: job result distance differs from direct result.");
                }
            }
            finally
            {
                if (jobResult.IsCreated) jobResult.Dispose();
                if (jobStack.IsCreated) jobStack.Dispose();
                if (directStack.IsCreated) directStack.Dispose();
                if (tree.IsCreated) tree.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        [Test]
        public void KNearestJobMatchesDirect()
        {
            var source = new NativeArray<KdElement<int>>(ElementCount, Allocator.TempJob);
            var tree = new NativeKdTree<int>(ElementCount, Allocator.TempJob);
            var directIndices = new NativeList<int>(Allocator.TempJob);
            var directDists = new NativeList<float>(Allocator.TempJob);
            var directStack = new NativeList<int3>(Allocator.TempJob);
            var jobIndices = new NativeList<int>(Allocator.TempJob);
            var jobDists = new NativeList<float>(Allocator.TempJob);
            var jobStack = new NativeList<int3>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 104);
                tree.Build(source);

                int k = 8;
                Random.InitState(204);
                for (int q = 0; q < 20; q++)
                {
                    float3 target = RandomPoint();

                    tree.FindKNearest(target, k, directIndices, directDists, directStack);

                    var job = new KdTreeJobs.FindKNearestJob<int>
                    {
                        Points = tree.Points,
                        Count = tree.Count,
                        Target = target,
                        K = k,
                        ResultIndices = jobIndices,
                        ResultDistancesSq = jobDists,
                        TraversalStack = jobStack
                    };

                    job.Schedule().Complete();

                    Assert.AreEqual(directIndices.Length, jobIndices.Length,
                        $"Query {q}: job result count differs from direct.");

                    for (int i = 0; i < directIndices.Length; i++)
                    {
                        float dDist = math.distancesq(tree.Points[directIndices[i]].Position, target);
                        float jDist = math.distancesq(tree.Points[jobIndices[i]].Position, target);
                        Assert.AreEqual(dDist, jDist, 1e-4f,
                            $"Query {q}: distance mismatch at position {i}.");
                    }
                }
            }
            finally
            {
                if (jobStack.IsCreated) jobStack.Dispose();
                if (jobDists.IsCreated) jobDists.Dispose();
                if (jobIndices.IsCreated) jobIndices.Dispose();
                if (directStack.IsCreated) directStack.Dispose();
                if (directDists.IsCreated) directDists.Dispose();
                if (directIndices.IsCreated) directIndices.Dispose();
                if (tree.IsCreated) tree.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        [Test]
        public void EmptyTreeReturnsNoResults()
        {
            var tree = new NativeKdTree<int>(0, Allocator.TempJob);
            var resultIndices = new NativeList<int>(Allocator.TempJob);
            var resultDists = new NativeList<float>(Allocator.TempJob);

            try
            {
                var empty = new NativeArray<KdElement<int>>(0, Allocator.TempJob);
                tree.Build(empty);
                empty.Dispose();

                Assert.AreEqual(-1, tree.FindNearest(float3.zero));
                tree.FindKNearest(float3.zero, 5, resultIndices, resultDists);
                Assert.AreEqual(0, resultIndices.Length);
                tree.RadialSearch(float3.zero, 100f, resultIndices, resultDists);
                Assert.AreEqual(0, resultIndices.Length);
            }
            finally
            {
                if (resultDists.IsCreated) resultDists.Dispose();
                if (resultIndices.IsCreated) resultIndices.Dispose();
                if (tree.IsCreated) tree.Dispose();
            }
        }

        [Test]
        public void SingleElementTree()
        {
            var source = new NativeArray<KdElement<int>>(1, Allocator.TempJob);
            source[0] = new KdElement<int> { Position = new float3(1, 2, 3), Value = 42 };

            var tree = new NativeKdTree<int>(1, Allocator.TempJob);
            var resultIndices = new NativeList<int>(Allocator.TempJob);
            var resultDists = new NativeList<float>(Allocator.TempJob);

            try
            {
                tree.Build(source);

                int nearest = tree.FindNearest(float3.zero);
                Assert.AreEqual(0, nearest);
                Assert.AreEqual(42, tree.Points[nearest].Value);

                tree.FindKNearest(float3.zero, 5, resultIndices, resultDists);
                Assert.AreEqual(1, resultIndices.Length);

                tree.RadialSearch(new float3(1, 2, 3), 0.1f, resultIndices, resultDists);
                Assert.AreEqual(1, resultIndices.Length);

                tree.RadialSearch(new float3(100, 100, 100), 1f, resultIndices, resultDists);
                Assert.AreEqual(0, resultIndices.Length);
            }
            finally
            {
                if (resultDists.IsCreated) resultDists.Dispose();
                if (resultIndices.IsCreated) resultIndices.Dispose();
                if (tree.IsCreated) tree.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        [Test]
        public void ClearAndRebuild()
        {
            var source = new NativeArray<KdElement<int>>(ElementCount, Allocator.TempJob);
            var tree = new NativeKdTree<int>(ElementCount, Allocator.TempJob);
            var stack = new NativeList<int3>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 105);
                tree.Build(source);

                Assert.AreEqual(ElementCount, tree.Count);

                tree.Clear();
                Assert.AreEqual(0, tree.Count);
                Assert.AreEqual(-1, tree.FindNearest(float3.zero));

                // Rebuild with different data
                FillRandomElements(source, seed: 106);
                tree.Build(source);
                Assert.AreEqual(ElementCount, tree.Count);

                // Verify it still works correctly
                float3 target = new float3(10, 20, 30);
                int resultIndex = tree.FindNearest(target, stack);
                Assert.IsTrue(resultIndex >= 0);

                float bestDistSq = math.distancesq(tree.Points[resultIndex].Position, target);
                var points = tree.Points;
                for (int i = 0; i < points.Length; i++)
                {
                    float distSq = math.distancesq(points[i].Position, target);
                    Assert.LessOrEqual(bestDistSq, distSq + 1e-5f);
                }
            }
            finally
            {
                if (stack.IsCreated) stack.Dispose();
                if (tree.IsCreated) tree.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        [Test]
        public void RadialSearchJobMatchesDirect()
        {
            var source = new NativeArray<KdElement<int>>(ElementCount, Allocator.TempJob);
            var tree = new NativeKdTree<int>(ElementCount, Allocator.TempJob);
            var directIndices = new NativeList<int>(Allocator.TempJob);
            var directDists = new NativeList<float>(Allocator.TempJob);
            var directStack = new NativeList<int3>(Allocator.TempJob);
            var jobIndices = new NativeList<int>(Allocator.TempJob);
            var jobDists = new NativeList<float>(Allocator.TempJob);
            var jobStack = new NativeList<int3>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 107);
                tree.Build(source);

                Random.InitState(207);
                for (int q = 0; q < 20; q++)
                {
                    float3 center = RandomPoint();
                    float radius = Random.Range(30f, 120f);

                    tree.RadialSearch(center, radius, directIndices, directDists, directStack);

                    var job = new KdTreeJobs.RadialSearchJob<int>
                    {
                        Points = tree.Points,
                        Count = tree.Count,
                        Center = center,
                        Radius = radius,
                        ResultIndices = jobIndices,
                        ResultDistancesSq = jobDists,
                        TraversalStack = jobStack
                    };

                    job.Schedule().Complete();

                    Assert.AreEqual(directIndices.Length, jobIndices.Length,
                        $"Query {q}: job radial search count differs from direct.");

                    // Compare as sets (order may differ)
                    var directSet = new bool[tree.Count];
                    var jobSet = new bool[tree.Count];

                    for (int i = 0; i < directIndices.Length; i++)
                        directSet[directIndices[i]] = true;
                    for (int i = 0; i < jobIndices.Length; i++)
                        jobSet[jobIndices[i]] = true;

                    for (int i = 0; i < tree.Count; i++)
                        Assert.AreEqual(directSet[i], jobSet[i], $"Query {q}: set mismatch at index {i}.");
                }
            }
            finally
            {
                if (jobStack.IsCreated) jobStack.Dispose();
                if (jobDists.IsCreated) jobDists.Dispose();
                if (jobIndices.IsCreated) jobIndices.Dispose();
                if (directStack.IsCreated) directStack.Dispose();
                if (directDists.IsCreated) directDists.Dispose();
                if (directIndices.IsCreated) directIndices.Dispose();
                if (tree.IsCreated) tree.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        private static void FillRandomElements(NativeArray<KdElement<int>> target, int seed)
        {
            Random.InitState(seed);
            for (int i = 0; i < target.Length; i++)
            {
                target[i] = new KdElement<int>
                {
                    Position = new float3(
                        Random.Range(-WorldExtent, WorldExtent),
                        Random.Range(-WorldExtent, WorldExtent),
                        Random.Range(-WorldExtent, WorldExtent)),
                    Value = i
                };
            }
        }

        private static float3 RandomPoint()
        {
            return new float3(
                Random.Range(-WorldExtent, WorldExtent),
                Random.Range(-WorldExtent, WorldExtent),
                Random.Range(-WorldExtent, WorldExtent));
        }
    }
}
