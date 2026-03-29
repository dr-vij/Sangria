# SangriaMesh — Procedural Generators

## Overview

SangriaMesh includes built-in procedural mesh generators that create `NativeDetail` geometry with proper topology, normals, and UV coordinates.

**Source files**: `SangriaMeshBoxGenerator.cs`, `SangriaMeshSphereGenerator.cs`

## Box Generator

`SangriaMeshBoxGenerator` creates an axis-aligned box centered at the origin.

### Topology

- **8 points** (shared corner positions)
- **24 vertices** (4 per face × 6 faces, for hard normals and UV seams)
- **6 primitives** (quad N-gons, one per face)

### Attributes Created

| Domain | Attribute | Type | Description |
|--------|-----------|------|-------------|
| Point | `Position` | `float3` | Corner positions |
| Point | `Normal` | `float3` | Normalized position direction |
| Vertex | `Normal` | `float3` | Per-face flat normal |
| Vertex | `UV0` | `float2` | Per-face UV coordinates (0,0)→(1,1) |

### Usage

```csharp
// Simple creation
var box = SangriaMeshBoxGenerator.CreateBox(new float3(1, 2, 3), Allocator.TempJob);

// With separate dimensions
var box = SangriaMeshBoxGenerator.CreateBox(width: 1f, height: 2f, depth: 3f);

// Pre-allocate and populate (reusable detail)
SangriaMeshBoxGenerator.CreateBox(out var detail, new float3(1, 1, 1), Allocator.Persistent);

// Populate existing detail (for real-time updates)
SangriaMeshBoxGenerator.PopulateBox(ref existingDetail, new float3(2, 2, 2));

// Query topology counts without creating geometry
SangriaMeshBoxGenerator.GetBoxTopologyCounts(out int points, out int vertices, out int primitives);
```

### Real-Time Repopulation

`PopulateBox` clears the existing detail and repopulates it with new dimensions. This avoids reallocation when updating box size every frame:

```csharp
// In Update():
SangriaMeshBoxGenerator.PopulateBox(ref m_Detail, m_Width, m_Height, m_Depth);
var compiled = m_Detail.Compile(Allocator.TempJob);
compiled.FillUnityMesh(m_Mesh);
compiled.Dispose();
```

## Sphere Generator

`SangriaMeshSphereGenerator` creates a UV sphere with configurable resolution.

### Topology

- **Points**: 2 (poles) + (latitudeSegments - 1) × longitudeSegments (interior rings)
- **Vertices**: 2 (poles) + (latitudeSegments - 1) × (longitudeSegments + 1) (extra column for UV seam)
- **Primitives**: longitudeSegments × latitudeSegments (triangles at poles, quads in middle)

### Attributes Created

| Domain | Attribute | Type | Description |
|--------|-----------|------|-------------|
| Point | `Position` | `float3` | Sphere surface positions |
| Point | `Normal` | `float3` | Unit sphere normals |
| Vertex | `Normal` | `float3` | Per-vertex normals (matches point normals) |
| Vertex | `UV0` | `float2` | Spherical UV mapping (u = longitude, v = latitude) |

### Usage

```csharp
// Simple creation
var sphere = SangriaMeshSphereGenerator.CreateUvSphere(
    radius: 1f,
    longitudeSegments: 32,
    latitudeSegments: 16,
    allocator: Allocator.TempJob);

// Pre-allocate and populate
SangriaMeshSphereGenerator.CreateUvSphere(
    out var detail,
    radius: 2f,
    longitudeSegments: 64,
    latitudeSegments: 32);

// Populate existing detail
SangriaMeshSphereGenerator.PopulateUvSphere(ref existingDetail, radius: 1f, longitudeSegments: 32, latitudeSegments: 16);

// Query topology counts
SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(
    longitudeSegments: 32,
    latitudeSegments: 16,
    out int points,
    out int vertices,
    out int primitives);
```

### Input Constraints

| Parameter | Minimum | Description |
|-----------|---------|-------------|
| `radius` | > 0 | Sphere radius |
| `longitudeSegments` | ≥ 3 | Number of meridian segments |
| `latitudeSegments` | ≥ 3 | Number of parallel segments |

### Burst-Parallel Construction

The sphere generator uses Burst-compiled `IJobParallelFor` jobs for high-resolution spheres:

- **`SphereInteriorRingBuildJob`**: Computes positions, normals, and UVs for each latitude ring in parallel
- **`SpherePrimitivePolygonBuildJob`**: Builds primitive vertex lists for all polygons in parallel

Parallelism is automatically enabled when:
- Interior ring count ≥ 8
- Primitive count ≥ 4096

Below these thresholds, the jobs run sequentially on the main thread to avoid scheduling overhead.

## Common Patterns

### Generator → Compile → Unity Mesh

```csharp
var detail = SangriaMeshBoxGenerator.CreateBox(new float3(1, 1, 1), Allocator.TempJob);
var compiled = detail.Compile(Allocator.TempJob);
var mesh = compiled.ToUnityMesh("MyBox");
compiled.Dispose();
detail.Dispose();
// mesh is now a standard Unity Mesh
```

### Generator → Edit → Compile

```csharp
var detail = SangriaMeshBoxGenerator.CreateBox(new float3(1, 1, 1), Allocator.TempJob);

// Remove a vertex
detail.RemoveVertex(0, VertexDeletePolicy.RemoveFromIncidentPrimitives);

// Add vertex colors
detail.AddVertexAttribute<Color>(AttributeID.Color);
// ... set colors ...

var compiled = detail.Compile(Allocator.TempJob);
compiled.FillUnityMesh(mesh);
compiled.Dispose();
detail.Dispose();
```

### Reusable Detail for Real-Time

```csharp
// OnEnable: allocate once
SangriaMeshBoxGenerator.GetBoxTopologyCounts(out int pc, out int vc, out int primc);
m_Detail = new NativeDetail(pc, vc, primc, Allocator.Persistent);

// Update: repopulate each frame
SangriaMeshBoxGenerator.PopulateBox(ref m_Detail, m_Width, m_Height, m_Depth);
var compiled = m_Detail.Compile(Allocator.TempJob);
compiled.FillUnityMesh(m_Mesh);
compiled.Dispose();

// OnDisable: dispose
m_Detail.Dispose();
```
