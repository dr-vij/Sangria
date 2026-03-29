# SangriaMesh — KD-Tree

## Overview

`NativeKdTree<T>` is a generic, native KD-tree for fast nearest-neighbor, k-nearest-neighbor, and radial proximity searches in 3D space. It stores points with user-defined unmanaged payloads and provides Burst-compatible job wrappers.

**Source files**: `NativeKdTree.cs`, `KdElement.cs`, `KdTreeJobs.cs`

## Core Types

### KdElement\<T\>

An element stored in the KD-tree: a 3D position plus a user-defined unmanaged payload.

```csharp
public struct KdElement<T> where T : unmanaged
{
    public float3 Position;
    public T Value;
}
```

## NativeKdTree\<T\>

### Construction

```csharp
var tree = new NativeKdTree<int>(initialCapacity: 1024, Allocator.Persistent);
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsCreated` | `bool` | Whether the tree has been allocated |
| `Count` | `int` | Number of stored elements |
| `Points` | `NativeArray<KdElement<T>>` | Read-only access to the sorted point array |

### Building

Build the tree from an array of elements. Sorts points in-place using a median-of-three partitioning scheme:

```csharp
var points = new NativeArray<KdElement<int>>(count, Allocator.TempJob);
// ... fill points ...
tree.Build(points);
points.Dispose();
```

Also accepts `NativeList<KdElement<T>>`. For zero-allocation builds, provide a pre-allocated sort stack:

```csharp
var sortStack = new NativeList<int3>(64, Allocator.TempJob);
tree.Build(points, sortStack);
sortStack.Dispose();
```

### FindNearest

Find the single nearest neighbor to a query point:

```csharp
int nearestIndex = tree.FindNearest(queryPosition);
KdElement<int> nearest = tree.Points[nearestIndex];
```

For zero-allocation queries:

```csharp
var stack = new NativeList<int3>(64, Allocator.TempJob);
int nearestIndex = tree.FindNearest(queryPosition, stack);
stack.Dispose();
```

### FindKNearest

Find the k closest points to a query position:

```csharp
var resultIndices = new NativeList<int>(k, Allocator.TempJob);
var resultDistancesSq = new NativeList<float>(k, Allocator.TempJob);

tree.FindKNearest(queryPosition, k, resultIndices, resultDistancesSq);

// resultIndices contains indices into tree.Points, sorted by distance
resultIndices.Dispose();
resultDistancesSq.Dispose();
```

For zero-allocation queries:

```csharp
var stack = new NativeList<int3>(64, Allocator.TempJob);
tree.FindKNearest(queryPosition, k, resultIndices, resultDistancesSq, stack);
stack.Dispose();
```

### RadialSearch

Find all points within a given radius of a query position:

```csharp
var resultIndices = new NativeList<int>(64, Allocator.TempJob);
var resultDistancesSq = new NativeList<float>(64, Allocator.TempJob);

tree.RadialSearch(center, radius, resultIndices, resultDistancesSq);

// Process results...
resultIndices.Dispose();
resultDistancesSq.Dispose();
```

For zero-allocation queries:

```csharp
var stack = new NativeList<int3>(64, Allocator.TempJob);
tree.RadialSearch(center, radius, resultIndices, resultDistancesSq, stack);
stack.Dispose();
```

### Lifecycle

```csharp
tree.Clear();    // Remove all elements
tree.Dispose();  // Free native memory
```

## Burst Jobs

**Class**: `KdTreeJobs` (static)

| Job | Description |
|-----|-------------|
| `BuildJob<T> : IJob` | Burst-compiled tree construction |
| `FindNearestJob<T> : IJob` | Burst-compiled single nearest-neighbor query |
| `FindKNearestJob<T> : IJob` | Burst-compiled k-nearest-neighbor query |
| `RadialSearchJob<T> : IJob` | Burst-compiled radial search query |

## Usage Example

```csharp
// Build a KD-tree from mesh point positions
var tree = new NativeKdTree<int>(pointCount, Allocator.TempJob);

var elements = new NativeArray<KdElement<int>>(pointCount, Allocator.TempJob);
for (int i = 0; i < pointCount; i++)
{
    elements[i] = new KdElement<int>
    {
        Position = positions[i],
        Value = i
    };
}

tree.Build(elements);
elements.Dispose();

// Find 5 nearest neighbors
var indices = new NativeList<int>(5, Allocator.TempJob);
var distances = new NativeList<float>(5, Allocator.TempJob);
tree.FindKNearest(queryPoint, 5, indices, distances);

// Find all points within radius 2.0
var radialIndices = new NativeList<int>(32, Allocator.TempJob);
var radialDist = new NativeList<float>(32, Allocator.TempJob);
tree.RadialSearch(queryPoint, 2.0f, radialIndices, radialDist);

// Cleanup
indices.Dispose();
distances.Dispose();
radialIndices.Dispose();
radialDist.Dispose();
tree.Dispose();
```

## Performance Notes

- Build is O(n log n) using median-of-three partitioning
- Nearest-neighbor queries are O(log n) average case
- k-nearest queries maintain a bounded priority set during traversal
- Radial search prunes branches whose splitting plane is farther than the search radius
- The tree is balanced by construction — no incremental insertion/deletion
- Use `Allocator.TempJob` for frame-scoped trees, `Allocator.Persistent` for long-lived ones
