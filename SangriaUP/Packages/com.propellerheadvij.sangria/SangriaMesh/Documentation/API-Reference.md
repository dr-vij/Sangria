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
| `PrimitiveDataLength` | `int` | Total primitive data length |
| `PrimitiveGarbageLength` | `int` | Garbage bytes in primitive storage |
| `PrimitiveHasGarbage` | `bool` | Whether primitive storage has garbage |
| `TopologyVersion` | `uint` | Incremented on topology changes |
| `AttributeVersion` | `uint` | Incremented on attribute changes |

| Method | Returns | Description |
|--------|---------|-------------|
| `AddPoint(float3 position)` | `int` | Allocate a new point with position, returns index |
| `AddPoint(float3 position, out ElementHandle)` | `int` | Allocate a point, returns index and handle |
| `AddVertex(int pointIndex)` | `int` | Allocate a vertex referencing a point |
| `AddPrimitive(NativeArray<int> vertices)` | `int` | Add an N-gon primitive |
| `GetPointPosition(int pointIndex)` | `float3` | Get point position |
| `SetPointPosition(int pointIndex, float3 position)` | `bool` | Set point position |
| `CanRemovePoint(int, PointDeletePolicy, ...)` | `bool` | Check if point can be removed |
| `RemovePoint(int pointIndex)` | `bool` | Mark point as dead |
| `RemovePoint(int, PointDeletePolicy, ...)` | `bool` | Remove point with policy |
| `CanRemoveVertex(int, VertexDeletePolicy, ...)` | `bool` | Check if vertex can be removed |
| `RemoveVertex(int vertexIndex)` | `bool` | Remove vertex (default policy) |
| `RemoveVertex(int vertexIndex, VertexDeletePolicy policy)` | `bool` | Remove vertex with policy |
| `RemovePrimitive(int primitiveIndex)` | `bool` | Remove primitive |
| `AddVertexToPrimitive(int primitiveIndex, int vertexIndex)` | `bool` | Add vertex to existing primitive |
| `RemoveVertexFromPrimitive(int primitiveIndex, int offset)` | `bool` | Remove vertex from primitive by offset |
| `IsPointAlive(int index)` | `bool` | Check if point slot is alive |
| `IsVertexAlive(int index)` | `bool` | Check if vertex slot is alive |
| `IsPrimitiveAlive(int index)` | `bool` | Check if primitive slot is alive |
| `GetVertexPoint(int vertexIndex)` | `int` | Get point index for vertex |
| `GetPrimitiveVertices(int primitiveIndex)` | `NativeSlice<int>` | Get vertex indices for primitive |
| `GetPrimitiveVertexCount(int primitiveIndex)` | `int` | Get vertex count for primitive |
| `GetPrimitiveBounds(int, out float3, out float3)` | `bool` | Compute primitive AABB |
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
| `GetPointHandle(int index)` | `ElementHandle` | Get handle for point |
| `GetVertexHandle(int index)` | `ElementHandle` | Get handle for vertex |
| `GetPrimitiveHandle(int index)` | `ElementHandle` | Get handle for primitive |
| `IsPointHandleValid(ElementHandle)` | `bool` | Validate point handle |
| `IsVertexHandleValid(ElementHandle)` | `bool` | Validate vertex handle |
| `IsPrimitiveHandleValid(ElementHandle)` | `bool` | Validate primitive handle |
| `CollectGarbage(float minGarbageRatio)` | `bool` | Compact primitive storage |
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

#### PointDeletePolicy

| Value | Description |
|-------|-------------|
| `KeepReferencingVertices` | Only mark point as dead; vertices still reference slot |
| `RemoveReferencingVertices` | Also remove all vertices referencing this point |

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

#### SangriaMeshRayTriangleIntersectors (static class)

| Method | Description |
|--------|-------------|
| `TryIntersectMoeller(...)` | Möller–Trumbore ray-triangle intersection |
| `TryIntersectPluecker(...)` | Plücker-coordinate ray-triangle intersection |
| `TryIntersectWoop(...)` | Woop watertight ray-triangle intersection |

#### RayTriangleHit (struct)

| Field | Type | Description |
|-------|------|-------------|
| `T` | `float` | Parametric distance along ray |
| `U` | `float` | Barycentric coordinate |
| `V` | `float` | Barycentric coordinate |
| `W` | `float` | 1 − U − V (readonly) |
| `Determinant` | `float` | Triangle determinant |
| `GeometricNormal` | `float3` | Unnormalized face normal |

#### SangriaMeshTriangleTriangleIntersector (static class)

| Method | Description |
|--------|-------------|
| `Intersects(in float3 a0..a2, in float3 b0..b2, float epsilon)` | Test triangle-triangle intersection |

---

### Triangulation

#### NativeDetailTriangulator (static class)

| Method | Description |
|--------|-------------|
| `Triangulate(ref NativeDetail src, ref NativeDetail dst, TriangulationMode, InterpolationPolicy, TriangulationOptions)` | Triangulate all N-gons into new detail |
| `TriangulateInPlace(ref NativeDetail, TriangulationMode, Allocator, InterpolationPolicy, TriangulationOptions)` | Triangulate in-place (swap) |

#### TriangulationMode (enum)

| Value | Description |
|-------|-------------|
| `Fan` | Fan from first vertex (convex only) |
| `EarClipping` | Ear-clipping (concave simple polygons) |
| `Tess` | Sweep-line tessellation (complex polygons) |

#### Triangulation (static class)

| Method | Description |
|--------|-------------|
| `TriangulateContours(in NativeContourSet, ref NativeDetail, in TriangulationOptions)` | Tessellate without provenance |
| `TriangulateContours(in NativeContourSet, ref NativeDetail, out ProvenanceMap, in TriangulationOptions)` | Tessellate with provenance |
| `TriangulateRaw(in NativeContourSet, ref NativeList<float3>, ref NativeList<int>, in TriangulationOptions)` | Lightweight triangulation returning positions and triangle indices directly, no NativeDetail |

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
| `TransferVertexAttributes(ref NativeDetail src, ref NativeDetail dst, NativeHashMap, in InterpolationPolicy)` | Bulk transfer all vertex attributes |
| `TransferPrimitiveAttributes(ref NativeDetail src, ref NativeDetail dst, NativeHashMap, in InterpolationPolicy)` | Bulk transfer all primitive attributes |
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

### Handles

#### ElementHandle (readonly struct)

| Field | Type | Description |
|-------|------|-------------|
| `Index` | `int` | Slot index |
| `Generation` | `uint` | Generation counter |
| `IsValid` | `bool` | True if index ≥ 0 and generation ≠ 0 |

#### AttributeHandle\<T\> (readonly struct)

| Field | Type | Description |
|-------|------|-------------|
| `AttributeId` | `int` | Attribute identifier |
| `IsValid` | `bool` | True if column index ≥ 0 |

---

## Namespace: SangriaMesh (Forest)

### BVH

#### NativeBvh\<T\> (struct, IDisposable)

| Constructor | Description |
|-------------|-------------|
| `NativeBvh(int initialCapacity, Allocator, int maxLeafSize = 4)` | Create BVH |

| Method | Description |
|--------|-------------|
| `Build(NativeArray<BvhElement<T>>)` | Build tree from elements |
| `Build(NativeList<BvhElement<T>>)` | Build tree from list |
| `Build(NativeArray<BvhElement<T>>, NativeList<int4>, NativeList<int2>)` | Build with pre-allocated stacks |
| `Refit()` | Update node bounds bottom-up |
| `Refit(NativeList<int2>)` | Refit with pre-allocated stack |
| `Query(BvhAabb, NativeList<int>)` | AABB overlap query (indices) |
| `Query(BvhAabb, NativeList<int>, NativeList<int>)` | Query with pre-allocated stack |
| `Query(BvhAabb, NativeList<BvhElement<T>>)` | AABB overlap query (elements) |
| `Query(BvhAabb, NativeList<BvhElement<T>>, NativeList<int>)` | Query with pre-allocated stack |
| `TryGetElement(int, out BvhElement<T>)` | Get element by index |
| `SetElementBounds(int, BvhAabb)` | Update element bounds |
| `SetElementValue(int, T)` | Update element value |
| `SetMaxLeafSize(int)` | Set max leaf size |
| `EnsureCapacity(int)` | Pre-allocate capacity |
| `Clear()` | Clear all elements |
| `Dispose()` | Free native memory |

#### BvhAabb (struct)

| Field / Method | Description |
|----------------|-------------|
| `Min`, `Max` | AABB bounds |
| `Center`, `Extents` | Computed properties |
| `SurfaceArea()` | Compute surface area |
| `Intersects(BvhAabb)` | AABB-AABB overlap test |
| `Contains(BvhAabb)`, `Contains(float3)` | Containment tests |
| `Expanded(float)` | Return expanded AABB |
| `FromCenterExtents(float3, float3)` | Create from center + extents |
| `Union(BvhAabb, BvhAabb)` | Merge two AABBs |

#### BvhElement\<T\> (struct)

| Field | Type | Description |
|-------|------|-------------|
| `Bounds` | `BvhAabb` | Element bounding box |
| `Value` | `T` | User payload |

#### BvhJobs (static class)

| Job | Description |
|-----|-------------|
| `RefitJob<T> : IJob` | Burst-compiled refit |
| `OverlapIndicesJob<T> : IJob` | Burst-compiled index query |
| `OverlapElementsJob<T> : IJob` | Burst-compiled element query |

---

### KD-Tree

#### NativeKdTree\<T\> (struct, IDisposable)

| Constructor | Description |
|-------------|-------------|
| `NativeKdTree(int initialCapacity, Allocator)` | Create KD-tree |

| Property | Type | Description |
|----------|------|-------------|
| `IsCreated` | `bool` | Whether allocated |
| `Count` | `int` | Number of elements |
| `Points` | `NativeArray<KdElement<T>>` | Sorted point array |

| Method | Description |
|--------|-------------|
| `Build(NativeArray<KdElement<T>>)` | Build tree from elements |
| `Build(NativeArray<KdElement<T>>, NativeList<int3>)` | Build with pre-allocated stack |
| `Build(NativeList<KdElement<T>>)` | Build from list |
| `FindNearest(float3)` | Find nearest neighbor index |
| `FindNearest(float3, NativeList<int3>)` | Find nearest with pre-allocated stack |
| `FindKNearest(float3, int, NativeList<int>, NativeList<float>)` | K-nearest neighbors |
| `FindKNearest(float3, int, NativeList<int>, NativeList<float>, NativeList<int3>)` | K-nearest with stack |
| `RadialSearch(float3, float, NativeList<int>, NativeList<float>)` | All points within radius |
| `RadialSearch(float3, float, NativeList<int>, NativeList<float>, NativeList<int3>)` | Radial with stack |
| `Clear()` | Clear all elements |
| `Dispose()` | Free native memory |

#### KdElement\<T\> (struct)

| Field | Type | Description |
|-------|------|-------------|
| `Position` | `float3` | Point position |
| `Value` | `T` | User payload |

#### KdTreeJobs (static class)

| Job | Description |
|-----|-------------|
| `BuildJob<T> : IJob` | Burst-compiled build |
| `FindNearestJob<T> : IJob` | Burst-compiled nearest query |
| `FindKNearestJob<T> : IJob` | Burst-compiled k-nearest query |
| `RadialSearchJob<T> : IJob` | Burst-compiled radial search |

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
