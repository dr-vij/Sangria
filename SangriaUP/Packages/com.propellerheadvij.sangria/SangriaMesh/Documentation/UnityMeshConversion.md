# SangriaMesh — Unity Mesh Conversion

## Overview

SangriaMesh provides extension methods to convert `NativeDetail` and `NativeCompiledDetail` into standard Unity `Mesh` objects. The conversion handles attribute mapping, polygon triangulation, and vertex buffer layout automatically.

**Source file**: `SangriaMeshUnityMeshExtensions.cs`

## Conversion Methods

### From Editable Detail

```csharp
// Fill existing mesh
detail.FillUnityMesh(mesh, Allocator.TempJob);

// Create new mesh
Mesh mesh = detail.ToUnityMesh("MyMesh", Allocator.TempJob);
```

These methods internally compile the `NativeDetail` to a `NativeCompiledDetail`, perform the conversion, and dispose the compiled snapshot.

### From Compiled Detail

```csharp
// Fill existing mesh
compiled.FillUnityMesh(mesh);

// Create new mesh
Mesh mesh = compiled.ToUnityMesh("MyMesh");

// Fast path for triangle-only topology
Mesh mesh = compiled.ToUnityMeshTriangles("MyMesh");
compiled.FillUnityMeshTriangles(mesh);
```

## Attribute Mapping

The converter maps SangriaMesh attributes to Unity vertex attributes:

| SangriaMesh Attribute | Unity VertexAttribute | Format | Dimension |
|----------------------|----------------------|--------|-----------|
| `AttributeID.Position` | `Position` | Float32 | 3 |
| `AttributeID.Normal` | `Normal` | Float32 | 3 |
| `AttributeID.Tangent` | `Tangent` | Float32 | 4 |
| `AttributeID.Color` | `Color` | Float32 | 4 |
| `AttributeID.UV0` | `TexCoord0` | Float32 | 2 |
| `AttributeID.UV1` | `TexCoord1` | Float32 | 2 |
| `AttributeID.UV2`–`UV7` | `TexCoord2`–`TexCoord7` | Float32 | 2 |

### Domain Priority

For each attribute, the converter checks the **Vertex** domain first, then falls back to the **Point** domain:

1. If the attribute exists on Vertex domain → use vertex-indexed data
2. Else if the attribute exists on Point domain → use point-indexed data (via `VertexToPoint` mapping)
3. Else → attribute is not included in the Unity Mesh

This means vertex-domain normals (hard/per-face normals) take priority over point-domain normals (smooth normals).

## Polygon Triangulation

SangriaMesh primitives can be N-gons (quads, pentagons, etc.), but Unity meshes only support triangles. The converter triangulates polygons automatically:

### Triangle-Only Fast Path

When `compiled.IsTriangleOnlyTopology` is `true`, the converter skips triangulation entirely and copies primitive vertex indices directly. This is the fastest path.

### Polygon Triangulation Pipeline

For mixed topology (triangles + quads + N-gons):

1. **Triangles** (3 vertices): Copied directly
2. **Polygons** (4+ vertices):
   a. Gather vertex positions via `VertexToPoint` → `Position` attribute
   b. **Project to 2D**: Use Newell's method to compute polygon normal, drop the axis with largest normal component
   c. **Ear-clip triangulation**: Attempt ear clipping on the projected 2D polygon
   d. **Fan fallback**: If ear clipping fails (degenerate polygon), use fan triangulation from vertex 0

### Triangulation Details

- **Ear clipping** (`SangriaMeshGeometry.TryTriangulateEarClip`): O(n²) algorithm that works for simple (non-self-intersecting) concave polygons
- **Fan triangulation** (`SangriaMeshGeometry.WriteFanTriangulation`): O(n) fallback that works correctly only for convex polygons but is used as a degenerate-geometry safety net
- **Validation**: In debug/development builds, `ValidatePrimitiveTriangulationWrite` asserts that the correct number of triangle indices were written

## Mesh Data API

The converter uses Unity's `Mesh.MeshDataArray` API for efficient mesh construction:

1. Allocates writable mesh data via `Mesh.AllocateWritableMeshData(1)`
2. Sets vertex buffer params with per-attribute streams
3. Copies attribute data into vertex streams (one stream per attribute)
4. Copies triangle indices into index buffer
5. Computes bounds from position data
6. Applies via `Mesh.ApplyAndDisposeWritableMeshData`

### Index Format

- Meshes with ≤ 65535 vertices use `IndexFormat.UInt16`
- Meshes with > 65535 vertices use `IndexFormat.UInt32`

### Normals

- If normals are present in the SangriaMesh data, they are used directly
- If normals are absent, `mesh.RecalculateNormals()` is called after conversion

## Performance Considerations

- Use `FillUnityMeshTriangles` when you know the topology is triangle-only (avoids triangulation overhead)
- The `Mesh.MeshDataArray` API avoids managed allocations and provides the fastest Unity mesh upload path
- For real-time updates, reuse the same `Mesh` instance with `FillUnityMesh` instead of creating new meshes
- Mark meshes as `MarkDynamic()` for frequently updated geometry

## Error Handling

- `ArgumentNullException` if mesh is null
- `InvalidOperationException` if no supported attributes are found (Position is required)
- `InvalidOperationException` if `FillUnityMeshTriangles` is called on non-triangle topology
- Stride mismatches between SangriaMesh and Unity formats throw `InvalidOperationException`
