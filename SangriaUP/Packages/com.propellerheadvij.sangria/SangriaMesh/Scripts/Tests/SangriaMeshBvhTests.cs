using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SangriaMesh.Tests.Bvh
{
    public class SangriaMeshBvhTests
    {
        private const int ElementCount = 4096;

        [Test]
        public void RandomizedQueriesMatchBruteForce()
        {
            var source = new NativeArray<BvhElement<int>>(ElementCount, Allocator.TempJob);
            var bvh = new NativeBvh<int>(ElementCount, Allocator.TempJob, maxLeafSize: 8);
            var hits = new NativeList<int>(Allocator.TempJob);
            var traversal = new NativeList<int>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 101, worldExtent: 400f, minSize: 0.1f, maxSize: 2.0f);
                bvh.Build(source);

                Random.InitState(1024);
                for (int i = 0; i < 30; i++)
                {
                    var query = BvhAabb.FromCenterExtents(
                        center: new float3(Random.Range(-350f, 350f), Random.Range(-350f, 350f), Random.Range(-350f, 350f)),
                        extents: new float3(Random.Range(10f, 90f), Random.Range(10f, 90f), Random.Range(10f, 90f)));

                    AssertQueryMatchesBruteForce(bvh, query, hits, traversal);
                }
            }
            finally
            {
                if (traversal.IsCreated)
                    traversal.Dispose();
                if (hits.IsCreated)
                    hits.Dispose();
                if (bvh.IsCreated)
                    bvh.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        [Test]
        public void RefitMatchesRebuildAfterRandomMoves()
        {
            var source = new NativeArray<BvhElement<int>>(ElementCount, Allocator.TempJob);
            var updated = new NativeArray<BvhElement<int>>(ElementCount, Allocator.TempJob);
            var bvhRefit = new NativeBvh<int>(ElementCount, Allocator.TempJob, maxLeafSize: 8);
            var bvhRebuild = new NativeBvh<int>(ElementCount, Allocator.TempJob, maxLeafSize: 8);
            var refitHits = new NativeList<int>(Allocator.TempJob);
            var rebuildHits = new NativeList<int>(Allocator.TempJob);
            var refitTraversal = new NativeList<int>(Allocator.TempJob);
            var rebuildTraversal = new NativeList<int>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 202, worldExtent: 450f, minSize: 0.1f, maxSize: 2.2f);
                bvhRefit.Build(source);

                Random.InitState(203);
                for (int i = 0; i < 600; i++)
                {
                    int index = Random.Range(0, bvhRefit.ElementCount);
                    BvhElement<int> element = bvhRefit.Elements[index];

                    float3 shift = new float3(
                        Random.Range(-8f, 8f),
                        Random.Range(-8f, 8f),
                        Random.Range(-8f, 8f));

                    BvhAabb moved = BvhAabb.FromCenterExtents(element.Bounds.Center + shift, element.Bounds.Extents);
                    bvhRefit.SetElementBounds(index, moved);
                }

                bvhRefit.Refit();

                updated.CopyFrom(bvhRefit.Elements);
                bvhRebuild.Build(updated);

                Random.InitState(204);
                for (int i = 0; i < 20; i++)
                {
                    var query = BvhAabb.FromCenterExtents(
                        center: new float3(Random.Range(-300f, 300f), Random.Range(-300f, 300f), Random.Range(-300f, 300f)),
                        extents: new float3(Random.Range(30f, 140f), Random.Range(30f, 140f), Random.Range(30f, 140f)));

                    bvhRefit.Query(query, refitHits, refitTraversal);
                    bvhRebuild.Query(query, rebuildHits, rebuildTraversal);

                    AssertValueSetEqual(bvhRefit, refitHits, bvhRebuild, rebuildHits, ElementCount);
                }
            }
            finally
            {
                if (rebuildTraversal.IsCreated)
                    rebuildTraversal.Dispose();
                if (refitTraversal.IsCreated)
                    refitTraversal.Dispose();
                if (rebuildHits.IsCreated)
                    rebuildHits.Dispose();
                if (refitHits.IsCreated)
                    refitHits.Dispose();
                if (bvhRebuild.IsCreated)
                    bvhRebuild.Dispose();
                if (bvhRefit.IsCreated)
                    bvhRefit.Dispose();
                if (updated.IsCreated)
                    updated.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        [Test]
        public void QueryJobMatchesDirectQuery()
        {
            var source = new NativeArray<BvhElement<int>>(ElementCount, Allocator.TempJob);
            var bvh = new NativeBvh<int>(ElementCount, Allocator.TempJob, maxLeafSize: 8);
            var directHits = new NativeList<int>(Allocator.TempJob);
            var directTraversal = new NativeList<int>(Allocator.TempJob);
            var jobHits = new NativeList<int>(Allocator.TempJob);
            var jobTraversal = new NativeList<int>(Allocator.TempJob);

            try
            {
                FillRandomElements(source, seed: 303, worldExtent: 600f, minSize: 0.2f, maxSize: 3.0f);
                bvh.Build(source);

                var query = BvhAabb.FromCenterExtents(new float3(120f, -75f, 30f), new float3(180f, 220f, 140f));
                bvh.Query(query, directHits, directTraversal);

                var job = new BvhJobs.OverlapIndicesJob<int>
                {
                    Nodes = bvh.Nodes,
                    Elements = bvh.Elements,
                    RootIndex = bvh.RootIndex,
                    Bounds = query,
                    Results = jobHits,
                    TraversalStack = jobTraversal
                };

                JobHandle handle = job.Schedule();
                handle.Complete();

                AssertValueSetEqual(bvh, directHits, bvh, jobHits, ElementCount);
            }
            finally
            {
                if (jobTraversal.IsCreated)
                    jobTraversal.Dispose();
                if (jobHits.IsCreated)
                    jobHits.Dispose();
                if (directTraversal.IsCreated)
                    directTraversal.Dispose();
                if (directHits.IsCreated)
                    directHits.Dispose();
                if (bvh.IsCreated)
                    bvh.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        private static void FillRandomElements(
            NativeArray<BvhElement<int>> target,
            int seed,
            float worldExtent,
            float minSize,
            float maxSize)
        {
            Random.InitState(seed);
            for (int i = 0; i < target.Length; i++)
            {
                float3 center = new float3(
                    Random.Range(-worldExtent, worldExtent),
                    Random.Range(-worldExtent, worldExtent),
                    Random.Range(-worldExtent, worldExtent));

                float3 extents = new float3(
                    Random.Range(minSize, maxSize),
                    Random.Range(minSize, maxSize),
                    Random.Range(minSize, maxSize));

                target[i] = new BvhElement<int>
                {
                    Bounds = BvhAabb.FromCenterExtents(center, extents),
                    Value = i
                };
            }
        }

        private static void AssertQueryMatchesBruteForce(
            NativeBvh<int> bvh,
            BvhAabb query,
            NativeList<int> hits,
            NativeList<int> traversal)
        {
            var elements = bvh.Elements;
            var expected = new bool[elements.Length];
            int expectedCount = 0;

            for (int i = 0; i < elements.Length; i++)
            {
                if (!elements[i].Bounds.Intersects(query))
                    continue;

                expected[i] = true;
                expectedCount++;
            }

            bvh.Query(query, hits, traversal);
            Assert.AreEqual(expectedCount, hits.Length, "BVH query result count differs from brute force.");

            var seen = new bool[elements.Length];
            for (int i = 0; i < hits.Length; i++)
            {
                int hit = hits[i];
                Assert.IsTrue((uint)hit < (uint)elements.Length, "Hit index out of range.");
                Assert.IsTrue(expected[hit], "BVH reported index not intersecting brute force query.");
                Assert.IsFalse(seen[hit], "BVH returned duplicate hit index.");
                seen[hit] = true;
            }
        }

        private static void AssertValueSetEqual(
            NativeBvh<int> leftTree,
            NativeList<int> leftHits,
            NativeBvh<int> rightTree,
            NativeList<int> rightHits,
            int valueCount)
        {
            var left = new bool[valueCount];
            var right = new bool[valueCount];

            for (int i = 0; i < leftHits.Length; i++)
            {
                int idx = leftHits[i];
                int value = leftTree.Elements[idx].Value;
                left[value] = true;
            }

            for (int i = 0; i < rightHits.Length; i++)
            {
                int idx = rightHits[i];
                int value = rightTree.Elements[idx].Value;
                right[value] = true;
            }

            for (int i = 0; i < valueCount; i++)
                Assert.AreEqual(left[i], right[i], $"Mismatch on value {i}");
        }
    }
}
