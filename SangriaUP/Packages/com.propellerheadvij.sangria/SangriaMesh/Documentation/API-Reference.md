# SangriaMesh — API Reference

## Namespace: SangriaMesh

### Core Types

#### NativeDetail (struct, IDisposable)

Central editable geometry container with sparse topology and typed attributes.

| Constructor | Description |
|-------------|-------------|
| `NativeDetail(int pointCapacity, int vertexCapacity, int primitiveCapacity, Allocator allocator)` | Create with per-domain capacities |
| `NativeDetail(int capacity, Allocator allocator)` | Create with uniform capacity |

| Property | Type | Description |
|----------|------|-------------|
| `PointCount` | `int` | Number of alive points |
| `VertexCount` | `int` | Number of alive vertices |
| `PrimitiveCount` | `int` | Number of alive primitives |
| `PointCapacity` | `int` | Total point slots (including dead) |
| `VertexCapacity` | `int` | Total vertex slots |
| `PrimitiveCapacity` | `int` | Total primitive slots |

| Method | Returns | Description |
|--------|---------|-------------|
| `AddPoint()` | `int` | Allocate a new point, returns index |
| `AddVertex(int pointIndex)` | `int` | Allocate a vertex referencing a point |
| `AddPrimitive(NativeArray<int> vertices)` | `int` | Add an N-gon primitive |
| `RemovePoint(int pointIndex)` | `bool` | Mark point as dead |
| `RemoveVertex(int vertexIndex, VertexDeletePolicy policy)` | `bool` | Remove vertex with policy |
| `RemovePrimitive(int primitiveIndex)` | `bool` | Remove primitive |
| `IsPointAlive(int index)` | `bool` | Check if point slot is alive |
| `IsVertexAlive(int index)` | `bool` | Check if vertex slot is alive |
| `IsPrimitiveAlive(int index)` | `bool` | Check if primitive slot is alive |
| `GetVertexPoint(int vertexIndex)` | `int` | Get point index for vertex |
| `GetPrimitiveVertices(int primitiveIndex)` | `NativeArray<int>.ReadOnly` | Get vertex indices for primitive |
| `GetAllValidPoints(NativeList<int> output)` | `void` | Enumerate alive point indices |
| `GetAllValidVertices(NativeList<int> output)` | `void` | Enumerate alive vertex indices |
| `GetAllValidPrimitives(NativeList<int> output)` | `void` | Enumerate alive primitive indices |
| `AddPointAttribute<T>(int attributeId)` | `CoreResult` | Add typed point attribute |
| `AddVertexAttribute<T>(int attributeId)` | `CoreResult` | Add typed vertex attribute |
| `AddPrimitiveAttribute<T>(int attributeId)` | `CoreResult` | Add typed primitive attribute |
| `HasPointAttribute(int attributeId)` | `bool` | Check point attribute existence |
| `HasVertexAttribute(int attributeId)` | `bool` | Check vertex attribute existence |
| `HasPrimitiveAttribute(int attributeId)` | `bool` | Check primitive attribute existence |
| `RemovePointAttribute(int attributeId)` | `CoreResult` | Remove point attribute |
| `RemoveVertexAttribute(int attributeId)` | `CoreResult` | Remove vertex attribute |
| `RemovePrimitiveAttribute(int attributeId)` | `CoreResult` | Remove primitive attribute |
| `TryGetPointAccessor<T>(int id, out NativeAttributeAccessor<T>)` | `CoreResult` | Get typed point accessor |
| `TryGetVertexAccessor<T>(int id, out NativeAttributeAccessor<T>)` | `CoreResult` | Get typed vertex accessor |
| `TryGetPrimitiveAccessor<T>(int id, out NativeAttributeAccessor<T>)` | `CoreResult` | Get typed primitive accessor |
| `SetResource<T>(int id, in T value)` | `CoreResult` | Set a typed resource |
| `TryGetResource<T>(int id, out T value)` | `CoreResult` | Get a typed resource |
| `ContainsResource(int id)` | `bool` | Check resource existence |
| `RemoveResource(int id)` | `CoreResult` | Remove a resource |
| `Compile(Allocator allocator)` | `NativeCompiledDetail` | Compile to dense snapshot |
| `Clear()` | `void` | Reset all elements to dead |
| `AllocateDenseTopologyUnchecked(...)` | `void` | Bulk allocate contiguous topology |
| `MarkTopologyAndAttributeChanged()` | `void` | Signal data change |
| `Dispose()` | `void` | Free all native memory |

---

#### NativeCompiledDetail (struct, IDisposable)

Read-only compiled mesh snapshot with packed topology, attributes, and resources.

| Property | Type | Description |
|----------|------|-------------|
| `PointCount` | `int` | Compiled point count |
| `VertexCount` | `int` | Compiled vertex count |
| `PrimitiveCount` | `int` | Compiled primitive count |
| `IsTriangleOnlyTopology` | `bool` | All primitives are triangles |
| `IsCreated` | `bool` | Has valid data |
| `IsDisposed` | `bool` | Already disposed |
| `VertexToPointDense` | `NativeArray<int>.ReadOnly` | Dense vertex→point map |
| `PrimitiveOffsetsDense` | `NativeArray<int>.ReadOnly` | Dense primitive offsets |
| `PrimitiveVerticesDense` | `NativeArray<int>.ReadOnly` | Dense primitive vertices |

| Method | Returns | Description |
|--------|---------|-------------|
| `GetAttributeDescriptors(MeshDomain)` | `NativeArray<CompiledAttributeDescriptor>.ReadOnly` | Get attribute descriptors |
| `TryGetAttributeAccessor<T>(MeshDomain, int, out CompiledAttributeAccessor<T>)` | `CoreResult` | Typed attribute access |
| `TryGetRawAttributeAccessor(MeshDomain, int, out CompiledAttributeRawAccessor)` | `CoreResult` | Raw attribute access |
| `TryGetResource<T>(int, out T)` | `CoreResult` | Read compiled resource |
| `Dispose()` | `void` | Free all native memory |

---

#### NativeAttributeAccessor\<T\> (struct)

Typed read/write accessor for attribute data.

| Property | Type | Description |
|----------|------|-------------|
| `Length` | `int` | Number of elements |
| `Stride` | `int` | Element size in bytes |
| `this[int]` | `T` | Read/write element |

| Method | Returns | Description |
|--------|---------|-------------|
| `GetBasePointer()` | `T*` | Unsafe pointer to data |

---

### Enums

#### CoreResult

| Value | Description |
|-------|-------------|
| `Success` | Operation succeeded |
| `NotFound` | Element/attribute not found |
| `TypeMismatch` | Type does not match stored type |
| `AlreadyExists` | Element already exists |
| `InvalidOperation` | Invalid state for operation |
| `IndexOutOfRange` | Index out of bounds |

#### MeshDomain

| Value | Description |
|-------|-------------|
| `Point` | Point-domain attributes |
| `Vertex` | Vertex-domain attributes |
| `Primitive` | Primitive-domain attributes |

#### VertexDeletePolicy

| Value | Description |
|-------|-------------|
| `RemoveFromIncidentPrimitives` | Remove vertex from referencing primitives |
| `RemoveVertexOnly` | Only mark vertex dead |

---

### AttributeID (static class)

| Constant | Value | Type |
|----------|-------|------|
| `Position` | 0 | `float3` |
| `Normal` | 1 | `float3` |
| `Tangent` | 2 | `float4` |
| `Color` | 3 | `float4` / `Color` |
| `UV0` | 4 | `float2` |
| `UV1` | 5 | `float2` |
| `UV2` | 6 | `float2` |
| `UV3` | 7 | `float2` |
| `UV4` | 8 | `float2` |
| `UV5` | 9 | `float2` |
| `UV6` | 10 | `float2` |
| `UV7` | 11 | `float2` |

---

### Generators

#### SangriaMeshBoxGenerator (static class)

| Method | Description |
|--------|-------------|
| `CreateBox(float3 size, Allocator)` | Create box, returns `NativeDetail` |
| `CreateBox(float w, float h, float d, Allocator)` | Create box with separate dimensions |
| `CreateBox(out NativeDetail, float3 size, Allocator)` | Create box, out parameter |
| `PopulateBox(ref NativeDetail, float3 size)` | Repopulate existing detail |
| `GetBoxTopologyCounts(out int, out int, out int)` | Query topology counts |

#### SangriaMeshSphereGenerator (static class)

| Method | Description |
|--------|-------------|
| `CreateUvSphere(float radius, int lon, int lat, Allocator)` | Create sphere, returns `NativeDetail` |
| `CreateUvSphere(out NativeDetail, float radius, int lon, int lat, Allocator)` | Create sphere, out parameter |
| `PopulateUvSphere(ref NativeDetail, float radius, int lon, int lat)` | Repopulate existing detail |
| `GetUvSphereTopologyCounts(int lon, int lat, out int, out int, out int)` | Query topology counts |

---

### Unity Mesh Extensions

#### SangriaMeshUnityMeshExtensions (static class)

| Method | Description |
|--------|-------------|
| `FillUnityMesh(this ref NativeDetail, Mesh, Allocator)` | Convert editable detail to Unity Mesh |
| `FillUnityMesh(this in NativeCompiledDetail, Mesh)` | Convert compiled detail to Unity Mesh |
| `FillUnityMeshTriangles(this in NativeCompiledDetail, Mesh)` | Fast triangle-only conversion |
| `ToUnityMesh(this ref NativeDetail, string, Allocator)` | Create new Unity Mesh from detail |
| `ToUnityMesh(this in NativeCompiledDetail, string)` | Create new Unity Mesh from compiled |
| `ToUnityMeshTriangles(this in NativeCompiledDetail, string)` | Create new Unity Mesh (triangles only) |

---

### Geometry

#### SangriaMeshGeometry (static class)

| Method | Description |
|--------|-------------|
| `TryBuildProjectedPolygon(NativeList<float3>, NativeList<float2>)` | Project 3D polygon to 2D |
| `TryTriangulateEarClip(...)` | Ear-clip triangulation |
| `WriteFanTriangulation(...)` | Fan triangulation fallback |
| `ComputeSignedArea2(...)` | Compute 2× signed area of 2D polygon |
| `PointInTriangleInclusive(float2, float2, float2, float2)` | Point-in-triangle test |
| `Cross2(float2, float2)` | 2D cross product |

---

### Triangulation

#### Triangulation (static class)

| Method | Description |
|--------|-------------|
| `TriangulateContours(in NativeContourSet, ref NativeDetail, in TriangulationOptions)` | Tessellate without provenance |
| `TriangulateContours(in NativeContourSet, ref NativeDetail, out ProvenanceMap, in TriangulationOptions)` | Tessellate with provenance |

#### NativeContourSet (readonly struct)

| Property / Method | Description |
|-------------------|-------------|
| `Positions` | `NativeArray<float3>.ReadOnly` — input point positions |
| `ContourOffsets` | `NativeArray<int>.ReadOnly` — prefix-sum contour offsets |
| `ContourPointIndices` | `NativeArray<int>.ReadOnly` — point indices per contour |
| `ContourCount` | Number of contours |
| `Validate()` | Validate input data, returns `CoreResult` |

#### TriangulationOptions (struct)

| Field | Type | Default |
|-------|------|---------|
| `WindingRule` | `TriangulationWindingRule` | `EvenOdd` |
| `ContourOrientation` | `TriangulationContourOrientation` | `Original` |
| `Normal` | `float3` | `(0,0,0)` |
| `RemoveEmptyPolygons` | `bool` | `false` |

#### AttributeTransferOp (static class)

| Method | Description |
|--------|-------------|
| `TransferPointAttributes(ref NativeDetail src, ref NativeDetail dst, in ProvenanceMap, in InterpolationPolicy)` | Bulk transfer all point attributes |
| `TransferAttribute<T>(NativeAttributeAccessor<T> src, NativeAttributeAccessor<T> dst, in ProvenanceMap, InterpolationMode)` | Transfer single typed attribute |

---

### Debug

#### DetailVisualizer (static class, extension methods on NativeDetail)

| Method | Description |
|--------|-------------|
| `DrawPointGizmos(float size, Color color)` | Draw wire cubes at point positions |
| `DrawPrimitiveLines(Color color)` | Draw wireframe edges |
| `DrawVertexNormalsGizmos(float length, Color color)` | Draw normal vectors |
| `DrawPointNumbers(Color color, float offset)` | Draw point index labels (Editor only) |
| `DrawPrimitiveNumbers(Color color, float offset)` | Draw primitive index labels (Editor only) |

---

## Namespace: NativeOctree

#### NativeOctree\<T\> (struct, IDisposable)

| Constructor | Description |
|-------------|-------------|
| `NativeOctree(AABB bounds, Allocator, int maxDepth, short maxLeafElements, int initialCapacity)` | Create octree |

| Method | Description |
|--------|-------------|
| `ClearAndBulkInsert(NativeArray<OctElement<T>>)` | Rebuild tree with new elements |
| `RangeQuery(AABB bounds, NativeList<OctElement<T>> results)` | Query elements in AABB |
| `Clear()` | Clear all elements |
| `Dispose()` | Free native memory |

#### OctreeJobs (static class)

| Job | Description |
|-----|-------------|
| `AddBulkJob<T> : IJob` | Burst-compiled bulk insert |
| `RangeQueryJob<T> : IJob` | Burst-compiled range query |
