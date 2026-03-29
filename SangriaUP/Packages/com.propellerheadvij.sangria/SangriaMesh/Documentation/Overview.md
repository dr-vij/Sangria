# SangriaMesh — Overview

## What is SangriaMesh?

SangriaMesh is a high-performance, native (unmanaged) procedural mesh library for Unity. It provides a Houdini-inspired geometry model built entirely on Unity's Native Collections and designed for Burst compatibility.

The library operates on a **Point / Vertex / Primitive** data model where:

- **Points** carry shared spatial data (position, normals, etc.)
- **Vertices** are per-primitive corners that reference points and carry their own attributes (UVs, per-face normals)
- **Primitives** are N-gon polygons defined by ordered lists of vertices

This separation mirrors the domain model used in professional DCC tools (Houdini SOP, Blender BMesh) and enables clean workflows for procedural generation, real-time editing, and mesh cutting.

## Key Features

| Feature | Description |
|---------|-------------|
| **NativeDetail** | Core geometry container with sparse topology, typed attribute storage, and reference-counted memory management |
| **Typed Attribute System** | Per-domain (Point, Vertex, Primitive) attributes with `NativeAttributeAccessor<T>` for safe, typed, pointer-based access |
| **Compilation Pipeline** | Packs sparse editable geometry into dense `NativeCompiledDetail` snapshots optimized for read-only consumption |
| **Resource Registry** | Typed key-value store for arbitrary unmanaged data blobs attached to geometry (materials, metadata, etc.) |
| **Procedural Generators** | Built-in Box and UV-Sphere generators with Burst-parallel construction |
| **Unity Mesh Conversion** | Automatic conversion from SangriaMesh to Unity `Mesh` with polygon triangulation (ear-clip + fan fallback) |
| **Contour Triangulation** | Full sweep-line tessellation engine (LibTess port) with provenance tracking and attribute transfer |
| **Octree** | Native spatial partitioning structure for range queries |
| **Debug Visualization** | Gizmo-based visualization of points, wireframe, normals, and index labels |

## Package Structure

```
SangriaMesh/
├── Scripts/
│   ├── Core/                    # NativeDetail, attributes, compilation, resources
│   │   ├── NativeDetail*.cs     # Main geometry container (partial classes)
│   │   ├── Attributes/          # AttributeStore, NativeAttributeAccessor, CompiledAttributeSet
│   │   ├── Compile/             # NativeCompiledDetail
│   │   └── Resources/           # ResourceRegistry, CompiledResourceSet
│   ├── Generators/              # Box, Sphere generators + Unity Mesh extensions
│   ├── Math/                    # Geometry algorithms (ear-clip triangulation, projection)
│   ├── Triangulation/           # Sweep-line tessellation engine with provenance
│   ├── Octree/                  # NativeOctree spatial partitioning
│   ├── Cutter/                  # MeshCutter (placeholder)
│   ├── Debug/                   # DetailVisualizer gizmo drawing
│   ├── ExampleUsage/            # Box, Sphere, Contour example MonoBehaviours
│   └── Scripts-legacy/          # Legacy code (not documented)
├── Documentation/               # This documentation
└── Tests/                       # Unit and integration tests
```

## Requirements

- **Unity**: 6000.4.0f1 or compatible
- **Target Framework**: .NET 4.7.1
- **Dependencies**: Unity Mathematics, Unity Collections, Unity Burst

## Quick Start

```csharp
using Unity.Collections;
using Unity.Mathematics;
using SangriaMesh;

// 1. Create a box
var detail = SangriaMeshBoxGenerator.CreateBox(new float3(1, 1, 1), Allocator.TempJob);

// 2. Read point positions
detail.TryGetPointAccessor<float3>(AttributeID.Position, out var positions);
for (int i = 0; i < detail.PointCount; i++)
    Debug.Log($"Point {i}: {positions[i]}");

// 3. Compile to read-only snapshot
var compiled = detail.Compile(Allocator.TempJob);

// 4. Convert to Unity Mesh
var mesh = compiled.ToUnityMesh("MyBox");

// 5. Cleanup
compiled.Dispose();
detail.Dispose();
```

## Documentation Index

| Document | Description |
|----------|-------------|
| [Architecture](Architecture.md) | System architecture, data model, and design principles |
| [NativeDetail](NativeDetail.md) | Core geometry container API and usage |
| [Attributes](Attributes.md) | Typed attribute system across Point/Vertex/Primitive domains |
| [Compilation](Compilation.md) | Sparse-to-dense compilation pipeline |
| [Resources](Resources.md) | Typed resource registry for metadata blobs |
| [Generators](Generators.md) | Built-in procedural mesh generators (Box, Sphere) |
| [Unity Mesh Conversion](UnityMeshConversion.md) | Converting SangriaMesh geometry to Unity Mesh |
| [Geometry & Math](Geometry.md) | Geometry algorithms (triangulation, projection) |
| [Triangulation](Triangulation.md) | Sweep-line contour tessellation with provenance |
| [Octree](Octree.md) | Native spatial partitioning for range queries |
| [Debug Visualization](Debug.md) | Gizmo-based geometry visualization tools |
| [Examples](Examples.md) | Example MonoBehaviours and usage patterns |
| [API Reference](API-Reference.md) | Complete type and method reference |
