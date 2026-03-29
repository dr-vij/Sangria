# SangriaMesh — BVH (Bounding Volume Hierarchy)

## Overview

`NativeBvh<T>` is a generic, native bounding-volume hierarchy for fast AABB overlap and ray intersection queries. It stores elements with axis-aligned bounding boxes and user-defined payload values, supports incremental refit, and provides Burst-compatible job wrappers.

**Source files**: `NativeBvh.cs`, `BvhElement.cs`, `BvhAabb.cs`, `BvhNode.cs`, `BvhJobs.cs`, `BvhGizmoVisualizer.cs`

## Core Types

### BvhAabb

Axis-aligned bounding box.

```csharp
public struct BvhAabb
{
    public float3 Min;
    public float3 Max;

    public float3 Center { get; }
    public float3 Extents { get; }
    public float SurfaceArea();
    public bool Intersects(BvhAabb other);
    public bool IntersectsRay(float3 rayOrigin, float3 invDir, float tNear, float tFar);
    public bool Contains(BvhAabb other);
    public bool Contains(float3 point);
    public BvhAabb Expanded(float value);

    public static BvhAabb FromCenterExtents(float3 center, float3 extents);
    public static BvhAabb Union(BvhAabb a, BvhAabb b);
}
```

### BvhElement\<T\>

An element stored in the BVH: an AABB plus a user-defined unmanaged payload.

```csharp
public struct BvhElement<T> where T : unmanaged
{
    public BvhAabb Bounds;
    public T Value;
}
```

### BvhNode

Internal tree node with bounds and child/element references.

```csharp
public struct BvhNode
{
    public BvhAabb Bounds;
    public int Left;          // Left child or first element index
    public int Right;         // Right child or element count (leaf)
    public int FirstElement;  // First element for leaf nodes
    public int ElementCount;  // Number of elements (0 for internal nodes)
}
```

## NativeBvh\<T\>

### Construction

```csharp
var bvh = new NativeBvh<int>(initialCapacity: 1024, Allocator.Persistent, maxLeafSize: 4);
```

| Parameter | Description |
|-----------|-------------|
| `initialCapacity` | Pre-allocated element capacity |
| `allocator` | Memory allocator |
| `maxLeafSize` | Maximum elements per leaf node before splitting (default: 4) |

### Building

Build the tree from an array of elements. Rebuilds the entire hierarchy:

```csharp
var elements = new NativeArray<BvhElement<int>>(count, Allocator.TempJob);
// ... fill elements ...
bvh.Build(elements);
elements.Dispose();
```

Also accepts `NativeList<BvhElement<T>>`. For zero-allocation rebuilds, provide pre-allocated stacks:

```csharp
var buildStack = new NativeList<int4>(64, Allocator.TempJob);
var sortStack = new NativeList<int2>(64, Allocator.TempJob);
bvh.Build(elements, buildStack, sortStack);
buildStack.Dispose();
sortStack.Dispose();
```

### Querying

AABB overlap queries return indices or elements that intersect the given bounds:

```csharp
// Query by element index
var indices = new NativeList<int>(64, Allocator.TempJob);
bvh.Query(queryBounds, indices);

// Query returning full elements
var results = new NativeList<BvhElement<int>>(64, Allocator.TempJob);
bvh.Query(queryBounds, results);
```

For zero-allocation queries, provide a pre-allocated traversal stack:

```csharp
var stack = new NativeList<int>(64, Allocator.TempJob);
bvh.Query(queryBounds, indices, stack);
```

### Ray Queries

Ray queries return element indices whose bounding boxes intersect the given ray:

```csharp
var hits = new NativeList<int>(64, Allocator.TempJob);
bvh.RayQuery(rayOrigin, rayDir, tMax, hits);
```

For zero-allocation ray queries, provide a pre-allocated traversal stack:

```csharp
var stack = new NativeList<int>(64, Allocator.TempJob);
bvh.RayQuery(rayOrigin, rayDir, tMax, hits, stack);
```

### Refitting

Update node bounds after element bounds change, without rebuilding the tree structure:

```csharp
bvh.SetElementBounds(index, newBounds);
bvh.Refit();
```

For zero-allocation refit:

```csharp
var traversalStack = new NativeList<int2>(64, Allocator.TempJob);
bvh.Refit(traversalStack);
```

### Element Access

```csharp
bool found = bvh.TryGetElement(index, out BvhElement<int> element);
bvh.SetElementBounds(index, newBounds);
bvh.SetElementValue(index, newValue);
```

### Configuration

```csharp
bvh.SetMaxLeafSize(8);    // Change max leaf size (takes effect on next Build)
bvh.EnsureCapacity(2048);  // Pre-allocate for more elements
```

### Disposal

```csharp
bvh.Clear();    // Remove all elements
bvh.Dispose();  // Free native memory
```

## Burst Jobs

**Class**: `BvhJobs` (static)

| Job | Description |
|-----|-------------|
| `RefitJob<T> : IJob` | Burst-compiled bottom-up refit |
| `OverlapIndicesJob<T> : IJob` | Burst-compiled AABB overlap query returning element indices |
| `OverlapElementsJob<T> : IJob` | Burst-compiled AABB overlap query returning full elements |

## Gizmo Visualization

**Class**: `BvhGizmoVisualizer` (MonoBehaviour)

Draws BVH node bounds as wireframe gizmos in the Scene view. Useful for debugging spatial partitioning.

## Usage Example

```csharp
// Create BVH and populate with triangle bounding boxes
var bvh = new NativeBvh<int>(triangleCount, Allocator.Persistent);

var elements = new NativeArray<BvhElement<int>>(triangleCount, Allocator.TempJob);
for (int i = 0; i < triangleCount; i++)
{
    elements[i] = new BvhElement<int>
    {
        Bounds = ComputeTriangleBounds(i),
        Value = i
    };
}

bvh.Build(elements);
elements.Dispose();

// Query for triangles near a point
var queryBounds = BvhAabb.FromCenterExtents(point, new float3(0.1f));
var hits = new NativeList<int>(16, Allocator.TempJob);
bvh.Query(queryBounds, hits);

// Process hits...
hits.Dispose();
bvh.Dispose();
```

## Performance Notes

- Build uses a top-down median-split strategy with O(n log n) complexity
- Queries traverse the tree with early AABB rejection
- Refit is O(n) — updates bounds bottom-up without restructuring
- `maxLeafSize` trades query speed (smaller) vs. build speed and memory (larger)
- Use `Allocator.TempJob` for frame-scoped BVHs, `Allocator.Persistent` for long-lived ones
