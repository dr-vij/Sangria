using NUnit.Framework;
using Unity.Mathematics;
using Unity.Collections;
using PropellerheadMesh;

public class AttributeMapTests
{
    private const int PositionAttributeId = 0;
    private const int SumAttributeId = 1;
    
    /// <summary>
    /// Creates float3 positions and float sums, fills positions with test data, calculates sums, verifies results
    /// </summary>
    [Test]
    public unsafe void BasicFunctionality()
    {
        var attributeMap = new AttributeMap(10, Allocator.Temp);
        
        var result1 = attributeMap.RegisterAttribute<float3>(PositionAttributeId, 5);
        var result2 = attributeMap.RegisterAttribute<float>(SumAttributeId, 5);
        Assert.AreEqual(AttributeMapResult.Success, result1);
        Assert.AreEqual(AttributeMapResult.Success, result2);
        
        Assert.IsTrue(attributeMap.TryGetBasePointer<float3>(PositionAttributeId, out var posPtr) == AttributeMapResult.Success);
        Assert.IsTrue(attributeMap.TryGetBasePointer<float>(SumAttributeId, out var sumPtr) == AttributeMapResult.Success);
        
        for (int i = 0; i < 5; i++)
        {
            posPtr[i] = new float3(i, i * 2, i * 3);
        }
        
        for (int i = 0; i < 5; i++)
        {
            var pos = posPtr[i];
            sumPtr[i] = pos.x + pos.y + pos.z;
        }
        
        Assert.AreEqual(new float3(0, 0, 0), posPtr[0]);
        Assert.AreEqual(new float3(1, 2, 3), posPtr[1]);
        Assert.AreEqual(0f, sumPtr[0]);
        Assert.AreEqual(6f, sumPtr[1]);
        
        attributeMap.Dispose();
    }
    
    /// <summary>
    /// Creates small attributes, fills them, resizes to huge capacity, verifies data preservation, then shrinks back
    /// </summary>
    [Test]
    public unsafe void ResizeStress()
    {
        var attributeMap = new AttributeMap(10, Allocator.Temp);
        
        attributeMap.RegisterAttribute<float3>(PositionAttributeId, 2);
        attributeMap.RegisterAttribute<float>(SumAttributeId, 2);
        
        Assert.IsTrue(attributeMap.TryGetBasePointer<float3>(PositionAttributeId, out var posPtr) == AttributeMapResult.Success);
        posPtr[0] = new float3(1, 2, 3);
        posPtr[1] = new float3(4, 5, 6);
        
        var resizeResult = attributeMap.ResizeAttribute<float3>(PositionAttributeId, 1000);
        Assert.AreEqual(AttributeMapResult.Success, resizeResult);
        
        resizeResult = attributeMap.ResizeAttribute<float>(SumAttributeId, 1000);
        Assert.AreEqual(AttributeMapResult.Success, resizeResult);
        
        Assert.IsTrue(attributeMap.TryGetBasePointer<float3>(PositionAttributeId, out posPtr) == AttributeMapResult.Success);
        Assert.AreEqual(new float3(1, 2, 3), posPtr[0]);
        Assert.AreEqual(new float3(4, 5, 6), posPtr[1]);
        
        resizeResult = attributeMap.ResizeAttribute<float3>(PositionAttributeId, 1);
        Assert.AreEqual(AttributeMapResult.Success, resizeResult);
        
        attributeMap.ResizeAllAttributes(500);
        
        attributeMap.Dispose();
    }
    
    /// <summary>
    /// Registers float3 attribute then tries to access it as different types, expects TypeMismatch errors
    /// </summary>
    [Test]
    public unsafe void TypeMismatch()
    {
        var attributeMap = new AttributeMap(10, Allocator.Temp);
        
        attributeMap.RegisterAttribute<float3>(PositionAttributeId, 5);
        
        var result = attributeMap.TryGetBasePointer<float>(PositionAttributeId, out var floatPtr);
        Assert.AreEqual(AttributeMapResult.TypeMismatch, result);
        
        var resizeResult = attributeMap.ResizeAttribute<int>(PositionAttributeId, 10);
        Assert.AreEqual(AttributeMapResult.TypeMismatch, resizeResult);
        
        var accessorResult = attributeMap.TryGetAccessor<double>(PositionAttributeId, out var accessor);
        Assert.AreEqual(AttributeMapResult.TypeMismatch, accessorResult);
        
        attributeMap.Dispose();
    }
    
    /// <summary>
    /// Tries to access non-existent attributes, create duplicates, access out of bounds, and remove non-existent attributes
    /// </summary>
    [Test]
    public unsafe void InvalidAccess()
    {
        var attributeMap = new AttributeMap(10, Allocator.Temp);
        
        var result = attributeMap.TryGetBasePointer<float3>(999, out var ptr);
        Assert.AreEqual(AttributeMapResult.AttributeNotFound, result);
        
        attributeMap.RegisterAttribute<float3>(PositionAttributeId, 5);
        
        var duplicateResult = attributeMap.RegisterAttribute<float3>(PositionAttributeId, 5);
        Assert.AreEqual(AttributeMapResult.AttributeAlreadyExists, duplicateResult);
        
        var pointerResult = attributeMap.TryGetPointer<float3>(PositionAttributeId, 10, out var elementPtr);
        Assert.AreEqual(AttributeMapResult.IndexOutOfRange, pointerResult);
        
        var removeResult = attributeMap.RemoveAttribute(PositionAttributeId);
        Assert.AreEqual(AttributeMapResult.Success, removeResult);
        
        result = attributeMap.TryGetBasePointer<float3>(PositionAttributeId, out ptr);
        Assert.AreEqual(AttributeMapResult.AttributeNotFound, result);
        
        removeResult = attributeMap.RemoveAttribute(999);
        Assert.AreEqual(AttributeMapResult.AttributeNotFound, removeResult);
        
        attributeMap.Dispose();
    }
    
    /// <summary>
    /// Creates large flat-packed arrays, fills positions sequentially, calculates sums, verifies all 100 results
    /// </summary>
    [Test]
    public unsafe void FlatPackedArrays()
    {
        var attributeMap = new AttributeMap(10, Allocator.Temp);
        
        attributeMap.RegisterAttribute<float3>(PositionAttributeId, 100);
        attributeMap.RegisterAttribute<float>(SumAttributeId, 100);
        
        Assert.IsTrue(attributeMap.TryGetBasePointer<float3>(PositionAttributeId, out var posPtr) == AttributeMapResult.Success);
        Assert.IsTrue(attributeMap.TryGetBasePointer<float>(SumAttributeId, out var sumPtr) == AttributeMapResult.Success);
        
        for (int i = 0; i < 100; i++)
        {
            posPtr[i] = new float3(i, i * 2, i * 3);
        }
        
        for (int i = 0; i < 100; i++)
        {
            var pos = posPtr[i];
            sumPtr[i] = pos.x + pos.y + pos.z;
        }
        
        for (int i = 0; i < 100; i++)
        {
            var expected = i + (i * 2) + (i * 3);
            Assert.AreEqual(expected, sumPtr[i]);
        }
        
        attributeMap.Dispose();
    }
}