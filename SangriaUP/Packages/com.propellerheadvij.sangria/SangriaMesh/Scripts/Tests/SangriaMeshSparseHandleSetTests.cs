using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;

public class SangriaMeshSparseHandleSetTests
{
    [Test]
    public void GetAliveIndicesAcrossBitWordBoundariesReturnsSortedAliveIndices()
    {
        var set = new SparseHandleSet(8, Allocator.Temp);
        var alive = new NativeList<int>(Allocator.Temp);

        try
        {
            set.AllocateDenseRange(130);

            int[] removed = { 0, 63, 64, 65, 127, 128, 129 };
            for (int i = 0; i < removed.Length; i++)
                Assert.IsTrue(set.Release(removed[i]));

            set.GetAliveIndices(alive);

            Assert.AreEqual(set.Count, alive.Length);

            int previous = -1;
            for (int i = 0; i < alive.Length; i++)
            {
                int index = alive[i];
                Assert.Greater(index, previous);
                Assert.IsTrue(set.IsAlive(index));
                previous = index;
            }

            for (int i = 0; i < removed.Length; i++)
                Assert.IsFalse(set.IsAlive(removed[i]));
        }
        finally
        {
            if (alive.IsCreated)
                alive.Dispose();
            set.Dispose();
        }
    }

    [Test]
    public void GetAliveIndicesForHighlySparseSetMatchesExpected()
    {
        var set = new SparseHandleSet(8, Allocator.Temp);
        var alive = new NativeList<int>(Allocator.Temp);

        try
        {
            set.AllocateDenseRange(257);

            int[] keep = { 5, 70, 130, 199, 256 };
            var keepMask = new bool[257];
            for (int i = 0; i < keep.Length; i++)
                keepMask[keep[i]] = true;

            for (int i = 0; i < keepMask.Length; i++)
            {
                if (!keepMask[i])
                    Assert.IsTrue(set.Release(i));
            }

            set.GetAliveIndices(alive);

            Assert.AreEqual(keep.Length, set.Count);
            Assert.AreEqual(keep.Length, alive.Length);
            for (int i = 0; i < keep.Length; i++)
                Assert.AreEqual(keep[i], alive[i]);
        }
        finally
        {
            if (alive.IsCreated)
                alive.Dispose();
            set.Dispose();
        }
    }

    [Test]
    public void GetAliveIndicesClearsOutputWhenSetIsEmpty()
    {
        var set = new SparseHandleSet(8, Allocator.Temp);
        var alive = new NativeList<int>(Allocator.Temp);

        try
        {
            set.AllocateDenseRange(8);
            set.GetAliveIndices(alive);
            Assert.AreEqual(8, alive.Length);

            set.Clear();
            set.GetAliveIndices(alive);
            Assert.AreEqual(0, alive.Length);
        }
        finally
        {
            if (alive.IsCreated)
                alive.Dispose();
            set.Dispose();
        }
    }
}
