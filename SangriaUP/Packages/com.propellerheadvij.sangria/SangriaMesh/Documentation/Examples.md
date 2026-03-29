# SangriaMesh — Examples

## Overview

SangriaMesh includes four example MonoBehaviours that demonstrate real-time mesh generation, editing, compilation, spatial indexing, and rendering workflows.

**Source files**: `SangriaMeshBoxExample.cs`, `SangriaMeshSphereExample.cs`, `SangriaMeshContourExample.cs`, `SangriaMeshSphereBvhLiveExample.cs`

## Box Example

**Class**: `SangriaMeshBoxExample`

Demonstrates the complete real-time pipeline: generate → edit → compile → bake to Unity Mesh.

### Features

- **Real-time box generation** with adjustable width, height, depth
- **Topology editing**: vertex removal with configurable delete policy, face removal by face index
- **Random vertex colors**: adds per-vertex Color attribute with random values each frame
- **Performance timing**: logs build, edit, compile, and bake times per frame
- **Gizmo visualization**: points, wireframe, normals, point/primitive numbers with configurable colors and sizes

### Inspector Settings

| Section | Settings |
|---------|----------|
| **Dimensions** | Width, Height, Depth |
| **Realtime Bake** | Enable/disable, target MeshFilter, timing log frequency |
| **Realtime Edit Stress** | Remove vertex 0, vertex delete policy, remove face primitives, face selection |
| **Gizmo Preview** | Toggle points, wireframe, normals, point/primitive numbers; colors and sizes |
| **Random Colors** | Enable per-vertex random coloring |

### Pipeline (per frame)

```
PopulateBox(width, height, depth)     → build timing
    │
RemoveVertex(0) + RemoveFacePrimitives → edit timing
    │
detail.Compile()                       → compile timing
    │
compiled.FillUnityMesh(mesh)           → bake timing
```

### Context Menu Actions

- **Build SangriaMesh Box Example**: Creates and logs a box without rendering
- **Build SangriaMesh Box And Convert To Unity Mesh**: Full pipeline with Unity Mesh output

## Sphere Example

**Class**: `SangriaMeshSphereExample`

Similar to Box Example but generates a UV sphere with configurable resolution.

### Features

- **Configurable resolution**: longitude and latitude segments
- **Real-time sphere regeneration** with adjustable radius
- **Topology editing**: vertex removal, primitive removal
- **Performance timing** with per-frame logging
- **Gizmo visualization**: same options as Box Example

### Inspector Settings

| Section | Settings |
|---------|----------|
| **Sphere** | Radius, Longitude Segments, Latitude Segments |
| **Realtime Bake** | Enable/disable, target MeshFilter, timing log |
| **Realtime Edit Stress** | Vertex/primitive removal options |
| **Gizmo Preview** | Points, wireframe, normals, numbers |

## Contour Example

**Class**: `SangriaMeshContourExample`

Demonstrates real-time contour tessellation using the sweep-line triangulation engine. Uses a Transform hierarchy to define contour shapes.

### Hierarchy Setup

```
SangriaMeshContourExample (this script)
  ├── Contour0          ← child Transform (contour group)
  │     ├── Point0      ← grandchild Transform (contour vertex)
  │     ├── Point1
  │     ├── Point2
  │     └── ...
  ├── Contour1
  │     ├── Point0
  │     ├── Point1
  │     └── ...
  └── ...
```

Each direct child represents one closed contour. Each grandchild's local position defines a contour vertex. The tessellator triangulates the contours every frame.

### Features

- **Real-time contour tessellation**: Move child Transforms to reshape contours interactively
- **Configurable triangulation**: winding rule, contour orientation, empty polygon removal
- **Input plane selection**: XY, XZ, or YZ plane for contour point extraction
- **LibTess comparison**: Optionally compares results with LibTessDotNet for validation
- **Performance timing**: Logs tessellation and bake times
- **Gizmo visualization**: Contour lines, tessellated wireframe, points, point numbers

### Inspector Settings

| Section | Settings |
|---------|----------|
| **Triangulation** | Winding rule, orientation, remove empty polygons, input plane |
| **Realtime Bake** | Enable/disable, target MeshFilter, timing log, LibTess comparison |
| **Gizmo Preview** | Contour lines, points, wireframe, point numbers |

### Winding Rule Behaviors

| Rule | Effect on Contours |
|------|-------------------|
| `EvenOdd` | Alternating fill/hole for overlapping contours |
| `NonZero` | Union of all same-winding contours |
| `Positive` | Only CCW contours fill; CW contours create holes |
| `Negative` | Only CW contours fill |
| `AbsGeqTwo` | Only doubly-overlapping regions fill |

## Sphere BVH Live Example

**Class**: `SangriaMeshSphereBvhLiveExample`

Demonstrates real-time BVH construction from a UV sphere mesh, with depth-colored gizmo visualization of the BVH hierarchy.

### Features

- **Real-time sphere + BVH pipeline**: Generates a UV sphere, builds per-primitive bounding boxes, and constructs a `NativeBvh<int>` each frame
- **BVH gizmo visualization**: Draws internal and leaf node bounds with a depth-based color gradient
- **Configurable BVH parameters**: Max leaf size, depth-to-draw limit, internal/leaf alpha
- **Runtime statistics**: Displays primitive count, node count, and leaf count in the Inspector

### Inspector Settings

| Section | Settings |
|---------|----------|
| **Sphere** | Radius, Longitude/Latitude Segments, Target MeshFilter |
| **BVH** | Max Leaf Size |
| **BVH Gizmos** | Draw BVH, Draw Internal/Leaf Nodes, Max Depth to Draw, Depth Gradient, Internal/Leaf Alpha |
| **Runtime Info** | Primitive Count, Node Count, Leaf Count (read-only) |

## Common Patterns Demonstrated

### Persistent Detail with Frame-by-Frame Repopulation

All examples allocate `NativeDetail` once (`Allocator.Persistent`) and repopulate each frame, avoiding per-frame allocation overhead:

```csharp
// OnEnable / first frame:
m_Detail = new NativeDetail(capacity, Allocator.Persistent);

// Update:
generator.Populate(ref m_Detail, ...);
var compiled = m_Detail.Compile(Allocator.TempJob);
compiled.FillUnityMesh(m_Mesh);
compiled.Dispose();

// OnDisable / OnDestroy:
m_Detail.Dispose();
```

### Dynamic Mesh with MarkDynamic

```csharp
m_Mesh = new Mesh { name = "RealtimeMesh" };
m_Mesh.MarkDynamic();  // Hint to GPU driver for frequent updates
```

### Gizmo Drawing with Local Transform

```csharp
var saved = Gizmos.matrix;
Gizmos.matrix = transform.localToWorldMatrix;
// ... draw gizmos ...
Gizmos.matrix = saved;
```
