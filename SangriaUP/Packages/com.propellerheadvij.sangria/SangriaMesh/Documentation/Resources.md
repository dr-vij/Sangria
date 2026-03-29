# SangriaMesh — Resource Registry

## Overview

The Resource Registry provides a typed key-value store for arbitrary unmanaged data blobs attached to a `NativeDetail`. Resources are identified by integer IDs and can store any `unmanaged` type (materials, metadata, configuration values, etc.).

**Source files**: `ResourceRegistry.cs`, `CompiledResourceDescriptor.cs`, `CompiledResourceSet.cs`

## Basic Usage

### Setting Resources

```csharp
// Store a float resource
detail.SetResource(myFloatId, 3.14f);

// Store a custom struct
struct MaterialData { public int TextureIndex; public float Roughness; }
detail.SetResource(myMaterialId, new MaterialData { TextureIndex = 5, Roughness = 0.8f });

// Overwrite an existing resource (type can change)
detail.SetResource(myFloatId, 42);  // Now stores an int instead of float
```

### Reading Resources

```csharp
CoreResult result = detail.TryGetResource<float>(myFloatId, out float value);
if (result == CoreResult.Success)
    Debug.Log($"Value: {value}");
```

### Checking and Removing

```csharp
bool exists = detail.ContainsResource(myResourceId);

CoreResult result = detail.RemoveResource(myResourceId);
// Returns NotFound if the resource doesn't exist
```

## Type Safety

Resources are stored with a runtime type hash. Reading with the wrong type returns `CoreResult.TypeMismatch`:

```csharp
detail.SetResource(id, 3.14f);  // Stored as float

// Correct type:
detail.TryGetResource<float>(id, out float f);    // Success

// Wrong type:
detail.TryGetResource<int>(id, out int i);         // TypeMismatch
```

## Compiled Resources

When `NativeDetail` is compiled, the `ResourceRegistry` produces a `CompiledResourceSet`:

```csharp
NativeCompiledDetail compiled = detail.Compile(Allocator.TempJob);

// Read resources from compiled snapshot
compiled.TryGetResource<float>(myFloatId, out float value);
```

### CompiledResourceDescriptor

| Field | Description |
|-------|-------------|
| `ResourceId` | Integer resource identifier |
| `TypeHash` | Runtime type hash for type checking |
| `SizeBytes` | Size of the resource blob in bytes |
| `OffsetBytes` | Byte offset into the packed data buffer |

### CompiledResourceSet

All resource blobs are packed into a single contiguous `NativeArray<byte>`. A `NativeParallelHashMap<int, int>` maps resource IDs to descriptor indices for O(1) lookup.

## ResourceRegistry Internals

- Resources are stored in an `UnsafeParallelHashMap<int, ResourceEntry>`
- Each `ResourceEntry` contains:
  - `Buffer`: `UnsafeList<byte>` holding the raw data
  - `TypeHash`: `int` for runtime type checking
  - `SizeBytes`: `int` size of the stored value
- Capacity grows automatically with power-of-2 doubling
- `Clear()` disposes all entry buffers and resets the map
- `Dispose()` disposes all entries and the hash map itself

## Use Cases

- Storing material indices or shader parameters per-detail
- Attaching metadata (creation timestamp, LOD level, etc.)
- Passing configuration through the compilation pipeline
- Any per-geometry data that doesn't fit the per-element attribute model
