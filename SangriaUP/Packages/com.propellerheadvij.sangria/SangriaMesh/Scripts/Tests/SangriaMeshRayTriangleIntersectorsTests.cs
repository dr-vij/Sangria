using NUnit.Framework;
using SangriaMesh;
using Unity.Mathematics;

public class SangriaMeshRayTriangleIntersectorsTests
{
    private static readonly float3 V0 = new float3(0f, 0f, 0f);
    private static readonly float3 V1 = new float3(1f, 0f, 0f);
    private static readonly float3 V2 = new float3(0f, 1f, 0f);

    private const float Eps = 1e-5f;

    [Test]
    public void Moeller_HitsSimpleTriangle()
    {
        bool hit = SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(
            rayOrigin: new float3(0.25f, 0.25f, -1f),
            rayDirection: new float3(0f, 0f, 1f),
            tNear: 0f,
            tFar: 10f,
            v0: V0,
            v1: V1,
            v2: V2,
            hit: out var info);

        Assert.IsTrue(hit);
        Assert.That(info.T, Is.EqualTo(1f).Within(Eps));
        Assert.That(info.U, Is.EqualTo(0.25f).Within(Eps));
        Assert.That(info.V, Is.EqualTo(0.25f).Within(Eps));
        Assert.That(info.W, Is.EqualTo(0.5f).Within(Eps));
    }

    [Test]
    public void Pluecker_HitsSimpleTriangle()
    {
        bool hit = SangriaMeshRayTriangleIntersectors.TryIntersectPluecker(
            rayOrigin: new float3(0.25f, 0.25f, -1f),
            rayDirection: new float3(0f, 0f, 1f),
            tNear: 0f,
            tFar: 10f,
            v0: V0,
            v1: V1,
            v2: V2,
            hit: out var info);

        Assert.IsTrue(hit);
        Assert.That(info.T, Is.EqualTo(1f).Within(Eps));
        Assert.That(info.U, Is.EqualTo(0.25f).Within(Eps));
        Assert.That(info.V, Is.EqualTo(0.25f).Within(Eps));
        Assert.That(info.W, Is.EqualTo(0.5f).Within(Eps));
    }

    [Test]
    public void Woop_HitsSimpleTriangle()
    {
        bool hit = SangriaMeshRayTriangleIntersectors.TryIntersectWoop(
            rayOrigin: new float3(0.25f, 0.25f, -1f),
            rayDirection: new float3(0f, 0f, 1f),
            tNear: 0f,
            tFar: 10f,
            v0: V0,
            v1: V1,
            v2: V2,
            hit: out var info);

        Assert.IsTrue(hit);
        Assert.That(info.T, Is.EqualTo(1f).Within(Eps));
        // Embree Woop path returns barycentrics in a different vertex association than Moeller/Pluecker.
        Assert.That(info.U, Is.EqualTo(0.5f).Within(Eps));
        Assert.That(info.V, Is.EqualTo(0.25f).Within(Eps));
        Assert.That(info.W, Is.EqualTo(0.25f).Within(Eps));
    }

    [Test]
    public void AllIntersectors_MissOutsideTriangle()
    {
        float3 origin = new float3(1.25f, 1.25f, -1f);
        float3 dir = new float3(0f, 0f, 1f);

        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(origin, dir, 0f, 10f, V0, V1, V2, out _));
        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectPluecker(origin, dir, 0f, 10f, V0, V1, V2, out _));
        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectWoop(origin, dir, 0f, 10f, V0, V1, V2, out _));
    }

    [Test]
    public void AllIntersectors_RespectDepthRange()
    {
        float3 origin = new float3(0.25f, 0.25f, -1f);
        float3 dir = new float3(0f, 0f, 1f);

        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(origin, dir, 0f, 0.5f, V0, V1, V2, out _));
        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectPluecker(origin, dir, 0f, 0.5f, V0, V1, V2, out _));
        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectWoop(origin, dir, 0f, 0.5f, V0, V1, V2, out _));
    }

    [Test]
    public void BackfaceCulling_BlocksBackSideRay()
    {
        float3 fromBackOrigin = new float3(0.25f, 0.25f, -1f);
        float3 fromBackDir = new float3(0f, 0f, 1f);

        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(fromBackOrigin, fromBackDir, 0f, 10f, V0, V1, V2, out _, backfaceCulling: true));
        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectPluecker(fromBackOrigin, fromBackDir, 0f, 10f, V0, V1, V2, out _, backfaceCulling: true));
        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectWoop(fromBackOrigin, fromBackDir, 0f, 10f, V0, V1, V2, out _, backfaceCulling: true));
    }

    [Test]
    public void BackfaceCulling_AllowsFrontSideRay()
    {
        float3 fromFrontOrigin = new float3(0.25f, 0.25f, 1f);
        float3 fromFrontDir = new float3(0f, 0f, -1f);

        Assert.IsTrue(SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(fromFrontOrigin, fromFrontDir, 0f, 10f, V0, V1, V2, out _, backfaceCulling: true));
        Assert.IsTrue(SangriaMeshRayTriangleIntersectors.TryIntersectPluecker(fromFrontOrigin, fromFrontDir, 0f, 10f, V0, V1, V2, out _, backfaceCulling: true));
        Assert.IsTrue(SangriaMeshRayTriangleIntersectors.TryIntersectWoop(fromFrontOrigin, fromFrontDir, 0f, 10f, V0, V1, V2, out _, backfaceCulling: true));
    }

    [Test]
    public void AllIntersectors_HitSharedEdge()
    {
        float3 origin = new float3(0.5f, 0f, -1f);
        float3 dir = new float3(0f, 0f, 1f);

        Assert.IsTrue(SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(origin, dir, 0f, 10f, V0, V1, V2, out var moellerHit));
        Assert.IsTrue(SangriaMeshRayTriangleIntersectors.TryIntersectPluecker(origin, dir, 0f, 10f, V0, V1, V2, out var plueckerHit));
        Assert.IsTrue(SangriaMeshRayTriangleIntersectors.TryIntersectWoop(origin, dir, 0f, 10f, V0, V1, V2, out var woopHit));

        Assert.That(moellerHit.T, Is.EqualTo(1f).Within(Eps));
        Assert.That(plueckerHit.T, Is.EqualTo(1f).Within(Eps));
        Assert.That(woopHit.T, Is.EqualTo(1f).Within(Eps));
    }

    [Test]
    public void DegenerateTriangle_IsRejectedByAllIntersectors()
    {
        float3 d0 = new float3(0f, 0f, 0f);
        float3 d1 = new float3(1f, 0f, 0f);
        float3 d2 = new float3(2f, 0f, 0f);

        float3 origin = new float3(0.5f, 0.1f, -1f);
        float3 dir = new float3(0f, 0f, 1f);

        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(origin, dir, 0f, 10f, d0, d1, d2, out _));
        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectPluecker(origin, dir, 0f, 10f, d0, d1, d2, out _));
        Assert.IsFalse(SangriaMeshRayTriangleIntersectors.TryIntersectWoop(origin, dir, 0f, 10f, d0, d1, d2, out _));
    }
}
