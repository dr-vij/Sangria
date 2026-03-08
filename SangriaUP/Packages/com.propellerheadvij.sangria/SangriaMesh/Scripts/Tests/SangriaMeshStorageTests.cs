using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;

public class SangriaMeshStorageTests
{
    [Test]
    public void PrimitiveStorageCollectGarbageIsExplicit()
    {
        var detail = new NativeDetail(16, Allocator.Temp);
        try
        {
            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));
            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);

            using var tri = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
            int primitiveIndex = detail.AddPrimitive(tri);
            Assert.GreaterOrEqual(primitiveIndex, 0);

            int initialDataLength = detail.PrimitiveDataLength;
            for (int i = 0; i < 256; i++)
                Assert.IsTrue(detail.AddVertexToPrimitive(primitiveIndex, v0));

            int expandedDataLength = detail.PrimitiveDataLength;
            Assert.Greater(expandedDataLength, initialDataLength);

            Assert.IsTrue(detail.RemovePrimitive(primitiveIndex));
            Assert.AreEqual(expandedDataLength, detail.PrimitiveDataLength);
            Assert.IsTrue(detail.PrimitiveHasGarbage);
            Assert.Greater(detail.PrimitiveGarbageLength, 0);

            Assert.IsFalse(detail.CollectGarbage(1.1f));
            Assert.AreEqual(expandedDataLength, detail.PrimitiveDataLength);

            Assert.IsTrue(detail.CollectGarbage());
            Assert.AreEqual(0, detail.PrimitiveDataLength);
            Assert.IsFalse(detail.PrimitiveHasGarbage);
            Assert.AreEqual(0, detail.PrimitiveGarbageLength);
        }
        finally
        {
            detail.Dispose();
        }
    }
}
