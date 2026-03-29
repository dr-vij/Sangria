# SangriaMesh — NativeOctree

## Overview

`NativeOctree<T>` is a generic, native (unmanaged) spatial partitioning structure for 3D range queries. It uses Morton codes for spatial sorting and supports Burst-compiled jobs for insertion and querying.

**Source files**: `NativeOctree.cs`, `NativeOctreeRangeQuery.cs`, `OctreeJobs.cs`

**Namespace**: `NativeOctree`

## Core Types

### AABB

Axis-aligned bounding box used for octree bounds and range queries:

```csharp
public struct AABB
{
    public float3 Center;
    public float Extents;  // Half-size (uniform in all axes)
}
```

### OctElement\<T\>

An element stored in the octree, carrying a position and user data:

```csharp
public struct OctElement<T> where T : unmanaged
{
    public float3 pos;
    public T element;
}
```

### OctNode

Internal node structure:

```csharp
public struct OctNode
{
    public int firstChildIndex;
    public short count;
}
```

## Construction

```csharp
var bounds = new AABB { Center = float3.zero, Extents = 100f };

var octree = new NativeOctree<int>(
    bounds: bounds,
    allocator: Allocator.TempJob,
    maxDepth: 6,            // Maximum subdivision depth (default: 6)
    maxLeafElements: 16,    // Max elements per leaf before subdivision (default: 16)
    initialElementsCapacity: 256);
```

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `bounds` | — | World-space AABB enclosing all elements |
| `allocator` | `Temp` | Memory allocator |
| `maxDepth` | 6 | Maximum octree depth (limits subdivision) |
| `maxLeafElements` | 16 | Elements per leaf before split |
| `initialElementsCapacity` | 256 | Initial capacity for element storage |

## Bulk Insert

Elements are inserted using a bulk operation that clears and rebuilds the tree:

```csharp
var elements = new NativeArray<OctElement<int>>(1000, Allocator.TempJob);
// ... fill elements with positions and data ...

octree.ClearAndBulkInsert(elements);
```

**Algorithm**:
1. Compute Morton codes for all elements
2. Sort elements by Morton code
3. Recursively partition into octree nodes
4. Store elements in leaf nodes

## Range Query

Query all elements within an AABB:

```csharp
var queryBounds = new AABB { Center = new float3(10, 0, 0), Extents = 5f };
var results = new NativeList<OctElement<int>>(Allocator.TempJob);

octree.RangeQuery(queryBounds, results);

for (int i = 0; i < results.Length; i++)
    Debug.Log($"Found element at {results[i].pos}: {results[i].element}");

results.Dispose();
```

The query uses recursive traversal with early-out:
- If a child node is fully contained in the query bounds → bulk-copy all elements
- If a child node doesn't intersect the query bounds → skip entirely
- Otherwise → recurse and test individual elements

## Burst Jobs

### AddBulkJob

Burst-compiled job for bulk insertion:

```csharp
var job = new OctreeJobs.AddBulkJob<int>
{
    Elements = elements,
    Octree = octree
};
job.Schedule().Complete();
```

### RangeQueryJob

Burst-compiled job for range queries:

```csharp
var job = new OctreeJobs.RangeQueryJob<int>
{
    Bounds = queryBounds,
    Octree = octree,
    Results = results
};
job.Schedule().Complete();
```

## Lifecycle

```csharp
// Create
var octree = new NativeOctree<int>(bounds, Allocator.Persistent);

// Use
octree.ClearAndBulkInsert(elements);
octree.RangeQuery(queryBounds, results);

// Clear (reuse without reallocating)
octree.Clear();

// Dispose
octree.Dispose();
```

## Performance Notes

- Morton code sorting provides good cache locality for spatial queries
- Bulk insert rebuilds the entire tree — suitable for static or infrequently changing data
- Range queries use recursive traversal with O(n) worst case but typically O(log n) for localized queries
- The `maxDepth` parameter bounds memory usage and prevents excessive subdivision
- Use `Allocator.TempJob` for frame-scoped octrees, `Allocator.Persistent` for long-lived ones
