using NUnit.Framework;
using PropellerheadMesh;
using Unity.Collections;

namespace Plugins.MeshTools.PropellerheadMesh.Scripts.Tests
{
    public class NativeArray2DTest
    {
        [Test]
        public void AddAtFunctionality()
        {
            // PART 1
            // we create a 2d array
            using var array2D = new NativeArray2D<int>(4, 3, Allocator.Temp);

            // Add 2 elements to it
            int zeroElementIndex = array2D.CreateArrayRecord();
            Assert.AreEqual(0, zeroElementIndex);
            array2D.Append(10);
            array2D.Append(20);

            // add 1 more element, it should not resize now.
            array2D.AppendAt(zeroElementIndex, 30);
            Assert.AreEqual(3, array2D.GetLength(zeroElementIndex));
            Assert.AreEqual(30, array2D[zeroElementIndex, 2]);

            // now we add an extra element that will cause the resize of the page.
            array2D.AppendAt(zeroElementIndex, 40);

            // PART 2 
            // we create one more record. and feel its page
            var firstElementIndex = array2D.CreateArrayRecord();
            array2D.Append(100);
            array2D.Append(200);
            array2D.Append(300);

            // now we grow it no next page, but it is the last one
            array2D.AppendAt(firstElementIndex, 400);
            Assert.AreEqual(firstElementIndex, 1);
            Assert.AreEqual(4, array2D.GetLength(firstElementIndex));
            Assert.AreEqual(100, array2D[firstElementIndex, 0]);
            Assert.AreEqual(400, array2D[firstElementIndex, 3]);

            // PART 3
            // first fill up the first record to capacity (it has 4 elements, capacity is 6)
            array2D.AppendAt(zeroElementIndex, 50);
            array2D.AppendAt(zeroElementIndex, 60);

            // now we add to the first index when it's full and not current. that will cause relocation
            array2D.AppendAt(zeroElementIndex, 70);
            Assert.AreEqual(7, array2D.GetLength(zeroElementIndex));
            Assert.AreEqual(10, array2D[zeroElementIndex, 0]);
            Assert.AreEqual(20, array2D[zeroElementIndex, 1]);
            Assert.AreEqual(30, array2D[zeroElementIndex, 2]);
            Assert.AreEqual(40, array2D[zeroElementIndex, 3]);
            Assert.AreEqual(50, array2D[zeroElementIndex, 4]);
            Assert.AreEqual(60, array2D[zeroElementIndex, 5]);
            Assert.AreEqual(70, array2D[zeroElementIndex, 6]);

            // Part 4 foreach enumerator test
            Assert.AreEqual(2, array2D.Count);

            var enumerator = array2D.GetActivePageEnumerator();
            int activePageCount = 0;
            while (enumerator.MoveNext())
            {
                var page = enumerator.CurrentPageInfo;
                Assert.IsTrue(page.DataLength > 0);
                activePageCount++;
            }

            Assert.AreEqual(2, activePageCount);

            // Test ForEach methods
            var localArray = array2D;
            var forEachCount = 0;
            localArray.ForEachActivePage(pageIndex =>
            {
                forEachCount++;
                Assert.IsTrue(localArray.GetLength(pageIndex) > 0);
            });
            Assert.AreEqual(2, forEachCount);

            var sliceCount = 0;
            localArray.ForEachActivePageSlice((pageIndex, slice) =>
            {
                sliceCount++;
                Assert.IsTrue(slice.Length > 0);
            });
            Assert.AreEqual(2, sliceCount);
        }
    }
}