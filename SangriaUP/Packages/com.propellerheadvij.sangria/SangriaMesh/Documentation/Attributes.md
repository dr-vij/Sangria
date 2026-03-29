# SangriaMesh — Attribute System

## Overview

SangriaMesh provides a typed, per-domain attribute system that stores arbitrary unmanaged data on Points, Vertices, and Primitives. Every attribute is identified by an integer ID and stored as a column in an `AttributeStore`.

**Source files**: `AttributeStore.cs`, `NativeAttributeAccessor.cs`, `CompiledAttributeSet.cs`, `CompiledAttributeRawAccessor.cs`, `NativeDetail.AttributesResources.cs`

## Attribute Domains

| Domain | Storage | Indexed By | Typical Attributes |
|--------|---------|------------|-------------------|
| **Point** | `NativeDetail.PointAttributes` | Point index | `Position` (always present), `Normal` (smooth) |
| **Vertex** | `NativeDetail.VertexAttributes` | Vertex index | `Normal` (hard), `UV0`–`UV7`, `Color`, `Tangent` |
| **Primitive** | `NativeDetail.PrimitiveAttributes` | Primitive index | Custom per-face data |

## Built-in Attribute IDs

The `AttributeID` static class defines well-known attribute identifiers:

| Constant | Value | Type | Domain | Description |
|----------|-------|------|--------|-------------|
| `AttributeID.Position` | 0 | `float3` | Point | Spatial position (auto-created) |
| `AttributeID.Normal` | 1 | `float3` | Point or Vertex | Surface normal |
| `AttributeID.Tangent` | 2 | `float4` | Vertex | Tangent vector |
| `AttributeID.Color` | 3 | `Color`/`float4` | Vertex | Vertex color |
| `AttributeID.UV0`–`UV7` | 4–11 | `float2` | Vertex | Texture coordinates |

Custom attributes can use any integer ID not reserved by built-in constants.

## Adding Attributes

```csharp
// Add a point attribute
CoreResult result = detail.AddPointAttribute<float3>(AttributeID.Normal);

// Add a vertex attribute
CoreResult result = detail.AddVertexAttribute<float2>(AttributeID.UV0);

// Add a primitive attribute (custom ID)
CoreResult result = detail.AddPrimitiveAttribute<int>(myCustomAttributeId);

// Add a vertex attribute with custom Color type
detail.AddVertexAttribute<Color>(AttributeID.Color);
```

Returns `CoreResult.Success` on success, or an error if the attribute already exists with a different type.

## Checking Attribute Existence

```csharp
bool hasNormal = detail.HasPointAttribute(AttributeID.Normal);
bool hasUV = detail.HasVertexAttribute(AttributeID.UV0);
bool hasPrimAttr = detail.HasPrimitiveAttribute(myAttributeId);
```

## Accessing Attributes

### Typed Accessor (Recommended)

`NativeAttributeAccessor<T>` provides safe, typed read/write access to attribute data:

```csharp
// Get accessor
CoreResult result = detail.TryGetPointAccessor<float3>(AttributeID.Position, out var positionAccessor);
if (result == CoreResult.Success)
{
    // Read
    float3 pos = positionAccessor[pointIndex];

    // Write
    positionAccessor[pointIndex] = new float3(1, 2, 3);

    // Unsafe pointer access for inner loops
    unsafe
    {
        float3* ptr = positionAccessor.GetBasePointer();
        ptr[pointIndex] = new float3(4, 5, 6);
    }
}

// Vertex domain
detail.TryGetVertexAccessor<float2>(AttributeID.UV0, out var uvAccessor);
uvAccessor[vertexIndex] = new float2(0.5f, 0.5f);

// Primitive domain
detail.TryGetPrimitiveAccessor<int>(myAttributeId, out var primAccessor);
```

### Accessor Properties

| Property | Description |
|----------|-------------|
| `Length` | Number of elements (matches domain capacity) |
| `Stride` | Size in bytes of each element |
| `this[int index]` | Read/write element by index |
| `GetBasePointer()` | Unsafe pointer to the start of the data buffer |

## Removing Attributes

```csharp
CoreResult result = detail.RemovePointAttribute(AttributeID.Normal);
CoreResult result = detail.RemoveVertexAttribute(AttributeID.UV0);
CoreResult result = detail.RemovePrimitiveAttribute(myAttributeId);
```

Note: The `Position` attribute on Points cannot be removed.

## Type Safety

Attributes are stored with a runtime type hash (`BurstRuntime.GetHashCode32<T>`). Attempting to access an attribute with the wrong type returns `CoreResult.TypeMismatch`:

```csharp
detail.AddPointAttribute<float3>(AttributeID.Normal);

// This succeeds:
detail.TryGetPointAccessor<float3>(AttributeID.Normal, out var accessor);

// This returns TypeMismatch:
CoreResult result = detail.TryGetPointAccessor<float2>(AttributeID.Normal, out var wrong);
// result == CoreResult.TypeMismatch
```

## Compiled Attributes

When `NativeDetail` is compiled to `NativeCompiledDetail`, attributes are packed into dense `CompiledAttributeSet` structures:

```csharp
// Read compiled attribute descriptors
var descriptors = compiled.GetAttributeDescriptors(MeshDomain.Point);
foreach (var desc in descriptors)
    Debug.Log($"Attribute {desc.AttributeId}: stride={desc.Stride}");

// Typed access on compiled data
compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var accessor);

// Raw (type-erased) access
compiled.TryGetRawAttributeAccessor(MeshDomain.Vertex, AttributeID.UV0, out var rawAccessor);
```

### CompiledAttributeDescriptor

| Field | Description |
|-------|-------------|
| `AttributeId` | Integer attribute identifier |
| `TypeHash` | Runtime type hash for type checking |
| `Stride` | Size in bytes of each element |
| `OffsetBytes` | Byte offset into the packed data buffer |

### CompiledAttributeRawAccessor

Provides type-erased access to compiled attribute data via raw pointers:

| Method | Description |
|--------|-------------|
| `GetPointerUnchecked(int index)` | Returns `void*` to element at index |
| `Stride` | Element size in bytes |
| `Count` | Number of elements |

## AttributeStore Internals

`AttributeStore` is the underlying column-oriented storage engine:

- Each attribute is stored as a contiguous `UnsafeList<byte>` column
- Columns are indexed by attribute ID via `UnsafeParallelHashMap`
- Adding elements extends all existing columns; removing marks slots as dead
- Compilation reads only alive slots and packs them contiguously

## CoreResult Error Codes

| Code | Meaning |
|------|---------|
| `Success` | Operation completed successfully |
| `NotFound` | Attribute with the given ID does not exist |
| `TypeMismatch` | Attribute exists but with a different type |
| `AlreadyExists` | Attribute with the given ID already exists |
| `InvalidOperation` | Operation is not valid in the current state |
| `IndexOutOfRange` | Index is outside valid range |
