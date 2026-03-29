# SangriaMesh — Geometry & Math

## Overview

The `Math/` module provides geometry algorithms used internally by the Unity Mesh conversion pipeline and available for general use. It contains polygon triangulation utilities, ray-triangle intersection algorithms, and triangle-triangle intersection testing.

**Source files**: `SangriaMeshGeometry.cs`, `SangriaMeshRayTriangleIntersectors.cs`, `SangriaMeshTriangleTriangleIntersector.cs`

## Polygon Projection

### TryBuildProjectedPolygon

Projects a 3D polygon onto a 2D plane for triangulation.

```csharp
public static bool TryBuildProjectedPolygon(
    NativeList<float3> positions,
    NativeList<float2> projectedPolygon);
```

**Algorithm**:
1. Compute polygon normal using **Newell's method** (robust for non-planar polygons)
2. Find the axis with the largest absolute normal component
3. Drop that axis to project 3D positions to 2D

**Returns**: `false` if the polygon is degenerate (normal near zero), `true` otherwise.

**Example**:
- Normal = (0.1, 0.9, 0.2) → drop Y axis → project to XZ plane
- Normal = (0.8, 0.1, 0.3) → drop X axis → project to YZ plane

## Ear-Clip Triangulation

### TryTriangulateEarClip

Triangulates a simple (non-self-intersecting) polygon using the ear clipping method.

```csharp
public static bool TryTriangulateEarClip(
    NativeList<int> primitiveVertices,    // Vertex indices for output triangles
    NativeList<float2> projectedPolygon,  // 2D projected positions
    NativeList<int> polygonOrder,         // Working buffer (reusable)
    NativeArray<int> triangles,           // Output triangle index buffer
    ref int triangleWriteIndex);          // Current write position
```

**Algorithm**:
1. Compute signed area to determine winding (CCW or CW)
2. For each vertex, check if it forms a valid "ear":
   - The triangle (prev, current, next) has correct winding
   - No other polygon vertex lies inside the triangle
3. Clip the ear (emit triangle, remove vertex from polygon)
4. Repeat until only 3 vertices remain

**Complexity**: O(n²) worst case, where n = vertex count.

**Returns**: `false` if triangulation fails (degenerate polygon, zero area, or stuck in infinite loop).

**Guard limit**: The algorithm has a safety guard of n² iterations to prevent infinite loops on degenerate input.

## Fan Triangulation

### WriteFanTriangulation

Simple fan triangulation from the first vertex. Used as a fallback when ear clipping fails.

```csharp
public static int WriteFanTriangulation(
    NativeList<int> primitiveVertices,
    NativeArray<int> triangles,
    int triangleWriteIndex);
```

**Algorithm**: Creates triangles (v0, v1, v2), (v0, v2, v3), ..., (v0, vN-2, vN-1).

**Produces**: (n - 2) triangles for an n-vertex polygon.

**Limitation**: Only correct for convex polygons. Used as a degenerate-geometry safety net.

## Utility Functions

### ComputeSignedArea2

Computes twice the signed area of a 2D polygon.

```csharp
public static float ComputeSignedArea2(NativeList<float2> points, NativeList<int> order);
```

- Positive result → counter-clockwise winding
- Negative result → clockwise winding
- Near-zero → degenerate polygon

### PointInTriangleInclusive

Tests if a 2D point lies inside (or on the boundary of) a triangle.

```csharp
public static bool PointInTriangleInclusive(float2 p, float2 a, float2 b, float2 c);
```

Uses cross-product sign tests with epsilon tolerance (`EarClipEpsilon = 1e-6f`).

### Cross2

2D cross product (z-component of 3D cross product).

```csharp
public static float Cross2(float2 a, float2 b);
// Returns a.x * b.y - a.y * b.x
```

## Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `EarClipEpsilon` | `1e-6f` | Tolerance for degenerate polygon detection and point-in-triangle tests |

## Usage in Unity Mesh Conversion

The geometry algorithms are used by `SangriaMeshUnityMeshExtensions` during polygon triangulation:

```
N-gon primitive (4+ vertices)
    │
    ▼
TryBuildProjectedPolygon (3D → 2D)
    │
    ├── Success → TryTriangulateEarClip
    │                │
    │                ├── Success → triangles emitted
    │                └── Failure → WriteFanTriangulation (fallback)
    │
    └── Failure → WriteFanTriangulation (fallback)
```

## Ray-Triangle Intersection

**Class**: `SangriaMeshRayTriangleIntersectors` (static)

Provides three ray-triangle intersection algorithms, all returning a `RayTriangleHit` with parametric distance `T`, barycentric coordinates `U`, `V`, `W`, determinant, and geometric normal.

### RayTriangleHit

```csharp
public struct RayTriangleHit
{
    public float T;               // Parametric distance along ray
    public float U;               // Barycentric coordinate
    public float V;               // Barycentric coordinate
    public float Determinant;     // Triangle determinant (sign indicates facing)
    public float3 GeometricNormal;// Unnormalized face normal
    public readonly float W => 1f - U - V;
}
```

### TryIntersectMoeller

Möller–Trumbore algorithm. Fast general-purpose method.

```csharp
public static bool TryIntersectMoeller(
    in float3 rayOrigin, in float3 rayDirection,
    float tNear, float tFar,
    in float3 v0, in float3 v1, in float3 v2,
    out RayTriangleHit hit,
    bool backfaceCulling = false,
    float epsilon = DefaultEpsilon);
```

### TryIntersectPluecker

Plücker-coordinate method. Higher numerical precision for near-edge hits.

```csharp
public static bool TryIntersectPluecker(
    in float3 rayOrigin, in float3 rayDirection,
    float tNear, float tFar,
    in float3 v0, in float3 v1, in float3 v2,
    out RayTriangleHit hit,
    bool backfaceCulling = false,
    float epsilon = DefaultEpsilon);
```

### TryIntersectWoop

Woop's watertight algorithm. Guarantees no missed intersections at shared edges.

```csharp
public static bool TryIntersectWoop(
    in float3 rayOrigin, in float3 rayDirection,
    float tNear, float tFar,
    in float3 v0, in float3 v1, in float3 v2,
    out RayTriangleHit hit,
    bool backfaceCulling = false,
    float epsilon = DefaultEpsilon);
```

### Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `DefaultEpsilon` | `1e-8f` | Default tolerance for degenerate triangle detection |

## Triangle-Triangle Intersection

**Class**: `SangriaMeshTriangleTriangleIntersector` (static)

Tests whether two triangles in 3D space intersect, including coplanar cases.

### Intersects

```csharp
public static bool Intersects(
    in float3 a0, in float3 a1, in float3 a2,
    in float3 b0, in float3 b1, in float3 b2,
    float epsilon = DefaultEpsilon);
```

**Algorithm**: Uses the Möller interval-overlap method — projects both triangles onto the line of intersection of their supporting planes and checks for overlapping intervals. Coplanar triangles are handled via 2D projection and edge-crossing tests.

| Constant | Value | Description |
|----------|-------|-------------|
| `DefaultEpsilon` | `1e-5f` | Tolerance for coplanarity and sign tests |
