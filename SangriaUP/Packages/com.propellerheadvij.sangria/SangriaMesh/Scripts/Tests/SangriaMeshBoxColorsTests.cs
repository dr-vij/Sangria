using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SangriaMesh;

[TestFixture]
public class SangriaMeshBoxColorsTests
{
    [Test]
    public void BoxCanBeColoredWithRandomVertexColors()
    {
        // Setup
        NativeDetail detail = default;
        SangriaMeshBoxGenerator.CreateBox(out detail, 1f, 1f, 1f, Allocator.TempJob);

        try
        {
            // Act - Apply random colors
            detail.AddVertexAttribute<Color>(AttributeID.Color);
            Assert.IsTrue(detail.HasVertexAttribute(AttributeID.Color));

            var result = detail.TryGetVertexAccessor<Color>(AttributeID.Color, out var colorAccessor);
            Assert.AreEqual(CoreResult.Success, result);

            unsafe
            {
                Color* colorPtr = colorAccessor.GetBasePointer();
                int vertexCapacity = detail.VertexCapacity;
                var random = new Unity.Mathematics.Random(12345u);

                for (int i = 0; i < vertexCapacity; i++)
                {
                    if (detail.IsVertexAlive(i))
                    {
                        colorPtr[i] = new Color(random.NextFloat(), random.NextFloat(), random.NextFloat(), 1f);
                    }
                }
            }

            // Compile
            var compiled = detail.Compile(Allocator.TempJob);
            try
            {
                // Assert
                Assert.AreEqual(CoreResult.Success, compiled.TryGetAttributeAccessor<Color>(MeshDomain.Vertex, AttributeID.Color, out var compiledColors));
                Assert.AreEqual(detail.VertexCount, compiledColors.Length);

                for (int i = 0; i < compiledColors.Length; i++)
                {
                    Color c = compiledColors[i];
                    // Check if it's not default black (very unlikely for random)
                    Assert.IsFalse(c.r == 0 && c.g == 0 && c.b == 0);
                    Assert.AreEqual(1f, c.a);
                }
            }
            finally
            {
                compiled.Dispose();
            }
        }
        finally
        {
            detail.Dispose();
        }
    }
}
