using NUnit.Framework;
using NativeOctree;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SangriaMesh.Tests.Octree
{
    public class OctreeTests
    {
        AABB Bounds => new AABB{Center = 0, Extents = 1000};

        float3[] GetValues()
        {
            Random.InitState(0);
            var values = new float3[20000];

            for (int x = 0; x < values.Length; x++) {
                var val = new int3(Random.Range(-900, 900), Random.Range(-900, 900), Random.Range(-900, 900));
                values[x] = val;
            }

            return values;
        }

        NativeArray<OctElement<int>> CreateElements(float3[] values, Allocator allocator)
        {
            var elements = new NativeArray<OctElement<int>>(values.Length, allocator);
            for (int i = 0; i < values.Length; i++)
            {
                elements[i] = new OctElement<int>
                {
                    pos = values[i],
                    element = i
                };
            }
            return elements;
        }

        [Test]
        public void InsertTriggerDivideBulk()
        {
            var values = GetValues();
            var elements = CreateElements(values, Allocator.TempJob);

            var job = new OctreeJobs.AddBulkJob<int>
            {
                Elements = elements,
                Octree = new NativeOctree<int>(Bounds)
            };

            job.Run();

            // Query the full bounds — every inserted element must be returned.
            var results = new NativeList<OctElement<int>>(values.Length, Allocator.Temp);
            job.Octree.RangeQuery(Bounds, results);

            Assert.AreEqual(values.Length, results.Length,
                "Full-bounds query must return every inserted element.");

            var seen = new bool[values.Length];
            for (int i = 0; i < results.Length; i++)
            {
                int id = results[i].element;
                Assert.IsTrue(id >= 0 && id < values.Length,
                    $"Returned element id {id} is out of range.");
                Assert.IsFalse(seen[id],
                    $"Duplicate element id {id} in query results.");
                seen[id] = true;
            }

            results.Dispose();
            job.Octree.Dispose();
            elements.Dispose();
        }

        [Test]
        public void RangeQueryAfterBulk()
        {
            var values = GetValues();
            var elements = CreateElements(values, Allocator.TempJob);

            var octree = new NativeOctree<int>(Bounds);
            octree.ClearAndBulkInsert(elements);

            var queryBounds = new AABB {Center = float3.zero, Extents = new float3(200, 200, 200)};
            var results = new NativeList<OctElement<int>>(1000, Allocator.Temp);
            octree.RangeQuery(queryBounds, results);

            // Build brute-force expected set.
            var expected = new bool[values.Length];
            int expectedCount = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (queryBounds.Contains(values[i]))
                {
                    expected[i] = true;
                    expectedCount++;
                }
            }

            Assert.AreEqual(expectedCount, results.Length,
                "Range query result count differs from brute-force expectation.");

            var seen = new bool[values.Length];
            for (int i = 0; i < results.Length; i++)
            {
                int id = results[i].element;
                Assert.IsTrue(id >= 0 && id < values.Length,
                    $"Returned element id {id} is out of range.");
                Assert.IsTrue(expected[id],
                    $"Element {id} at {values[id]} returned by query but is outside bounds.");
                Assert.IsFalse(seen[id],
                    $"Duplicate element id {id} in query results.");
                seen[id] = true;
            }

            results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }

        [Test]
        public void InsertTriggerDivideNonBurstBulk()
        {
            var values = GetValues();
            var elements = CreateElements(values, Allocator.Temp);
            var octree = new NativeOctree<int>(Bounds);

            octree.ClearAndBulkInsert(elements);

            // Query the full bounds — every inserted element must be returned.
            var results = new NativeList<OctElement<int>>(values.Length, Allocator.Temp);
            octree.RangeQuery(Bounds, results);

            Assert.AreEqual(values.Length, results.Length,
                "Full-bounds query must return every inserted element.");

            var seen = new bool[values.Length];
            for (int i = 0; i < results.Length; i++)
            {
                int id = results[i].element;
                Assert.IsTrue(id >= 0 && id < values.Length,
                    $"Returned element id {id} is out of range.");
                Assert.IsFalse(seen[id],
                    $"Duplicate element id {id} in query results.");
                seen[id] = true;
            }

            results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }
    }
}
