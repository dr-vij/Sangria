using NUnit.Framework;
using SangriaMesh;
using Unity.Mathematics;

public class SangriaMeshTriangleTriangleIntersectorTests
{
    [Test]
    public void Intersects_NonCoplanarCrossing_ReturnsTrue()
    {
        float3 a0 = new float3(0f, 0f, 0f);
        float3 a1 = new float3(1f, 0f, 0f);
        float3 a2 = new float3(0f, 1f, 0f);

        float3 b0 = new float3(0.25f, 0.25f, -1f);
        float3 b1 = new float3(0.25f, 0.25f, 1f);
        float3 b2 = new float3(0.25f, 1f, 0f);

        Assert.IsTrue(SangriaMeshTriangleTriangleIntersector.Intersects(a0, a1, a2, b0, b1, b2));
    }

    [Test]
    public void Intersects_ParallelSeparatedPlanes_ReturnsFalse()
    {
        float3 a0 = new float3(0f, 0f, 0f);
        float3 a1 = new float3(1f, 0f, 0f);
        float3 a2 = new float3(0f, 1f, 0f);

        float3 b0 = new float3(0f, 0f, 1f);
        float3 b1 = new float3(1f, 0f, 1f);
        float3 b2 = new float3(0f, 1f, 1f);

        Assert.IsFalse(SangriaMeshTriangleTriangleIntersector.Intersects(a0, a1, a2, b0, b1, b2));
    }

    [Test]
    public void Intersects_CoplanarContained_ReturnsTrue()
    {
        float3 a0 = new float3(0f, 0f, 0f);
        float3 a1 = new float3(1f, 0f, 0f);
        float3 a2 = new float3(0f, 1f, 0f);

        float3 b0 = new float3(0.2f, 0.2f, 0f);
        float3 b1 = new float3(0.8f, 0.2f, 0f);
        float3 b2 = new float3(0.2f, 0.8f, 0f);

        Assert.IsTrue(SangriaMeshTriangleTriangleIntersector.Intersects(a0, a1, a2, b0, b1, b2));
    }

    [Test]
    public void Intersects_CoplanarDisjoint_ReturnsFalse()
    {
        float3 a0 = new float3(0f, 0f, 0f);
        float3 a1 = new float3(1f, 0f, 0f);
        float3 a2 = new float3(0f, 1f, 0f);

        float3 b0 = new float3(2f, 2f, 0f);
        float3 b1 = new float3(3f, 2f, 0f);
        float3 b2 = new float3(2f, 3f, 0f);

        Assert.IsFalse(SangriaMeshTriangleTriangleIntersector.Intersects(a0, a1, a2, b0, b1, b2));
    }

    [Test]
    public void Intersects_CoplanarEdgeCrossing_ReturnsTrue()
    {
        float3 a0 = new float3(0f, 0f, 0f);
        float3 a1 = new float3(1f, 0f, 0f);
        float3 a2 = new float3(0f, 1f, 0f);

        float3 b0 = new float3(0.8f, -0.2f, 0f);
        float3 b1 = new float3(1.2f, 0.2f, 0f);
        float3 b2 = new float3(0.8f, 0.6f, 0f);

        Assert.IsTrue(SangriaMeshTriangleTriangleIntersector.Intersects(a0, a1, a2, b0, b1, b2));
    }
}
