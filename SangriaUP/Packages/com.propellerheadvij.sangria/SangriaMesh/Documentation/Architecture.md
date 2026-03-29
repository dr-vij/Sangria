# SangriaMesh — Architecture

## Data Model

SangriaMesh follows a **Point / Vertex / Primitive** geometry model inspired by Houdini's SOP architecture:

```
┌─────────────────────────────────────────────────────────────┐
│  Primitive (polygon / N-gon)                                │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐       │
│  │ Vertex 0 │  │ Vertex 1 │  │ Vertex 2 │  │ Vertex 3 │    │
│  │ (UV, N)  │  │ (UV, N)  │  │ (UV, N)  │  │ (UV, N)  │    │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘    │
│       │              │              │              │          │
│       ▼              ▼              ▼              ▼          │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐       │
│  │ Point 0  │  │ Point 1  │  │ Point 2  │  │ Point 3  │    │
│  │ (pos, N) │  │ (pos, N) │  │ (pos, N) │  │ (pos, N) │    │
│  └─────────┘  └─────────┘  └─────────┘  └─────────┘       │
└─────────────────────────────────────────────────────────────┘
```

### Domains

| Domain | Description | Example Attributes |
|--------|-------------|-------------------|
| **Point** | Shared spatial data. Multiple vertices can reference the same point. | `Position`, `Normal` (smooth) |
| **Vertex** | Per-primitive corner. Each vertex belongs to exactly one primitive and references one point. | `UV0`–`UV7`, `Normal` (hard/per-face), `Color` |
| **Primitive** | An N-gon polygon defined by an ordered list of vertex indices. | `MaterialId`, custom per-face data |

### Key Relationships

- **Vertex → Point**: Every vertex references exactly one point via `VertexToPoint` mapping
- **Primitive → Vertices**: Each primitive stores an ordered list of vertex indices
- **Point → Vertices**: Implicit reverse lookup (which vertices share a point)
- Multiple vertices can share the same point (e.g., UV seams where the same spatial position has different UVs)

## Architecture Layers

```
┌──────────────────────────────────────────────────────────┐
│  Layer 5: Examples & Application Code                     │
│  SangriaMeshBoxExample, SangriaMeshSphereExample,         │
│  SangriaMeshContourExample, SangriaMeshSphereBvhLiveExample│
├──────────────────────────────────────────────────────────┤
│  Layer 4: Debug & Visualization                           │
│  DetailVisualizer (Gizmo drawing for points, wireframe,   │
│  normals, index labels)                                   │
├──────────────────────────────────────────────────────────┤
│  Layer 3: Unity Integration                               │
│  SangriaMeshUnityMeshExtensions (NativeDetail → Mesh)     │
│  Polygon triangulation (ear-clip + fan fallback)          │
├──────────────────────────────────────────────────────────┤
│  Layer 2: Generators & Operations                         │
│  SangriaMeshBoxGenerator, SangriaMeshSphereGenerator      │
│  Triangulation (sweep-line tessellation + provenance)     │
│  NativeDetailTriangulator (N-gon → triangle conversion)   │
│  NativeBvh (bounding-volume hierarchy)                    │
│  NativeKdTree (k-d tree nearest-neighbor search)          │
│  NativeOctree (spatial range queries)                     │
│  Ray-Triangle & Triangle-Triangle intersectors            │
│  MeshCutter (planned)                                     │
├──────────────────────────────────────────────────────────┤
│  Layer 1: Compilation Pipeline                            │
│  NativeDetail.Compile() → NativeCompiledDetail            │
│  Sparse → Dense packing, attribute/resource compilation   │
├──────────────────────────────────────────────────────────┤
│  Layer 0: Core Data Structures                            │
│  NativeDetail (sparse topology + alive flags)             │
│  AttributeStore / NativeAttributeAccessor<T>              │
│  PrimitiveStorage (N-gon topology)                        │
│  ResourceRegistry (typed blob store)                      │
└──────────────────────────────────────────────────────────┘
```

## Design Principles

### 1. Native-First, Zero Managed Allocations

All data structures use `NativeArray`, `NativeList`, `UnsafeList`, and `UnsafeParallelHashMap`. No managed heap allocations occur on hot paths. This enables:
- Burst compilation compatibility
- Deterministic memory management via `IDisposable`
- Cache-friendly data layouts

### 2. Sparse Editing, Dense Reading

- **Editing** (`NativeDetail`): Uses sparse storage with alive flags and free lists for O(1) add/remove of points, vertices, and primitives
- **Reading** (`NativeCompiledDetail`): Compiled into dense, contiguous arrays for cache-friendly iteration and direct GPU upload

### 3. Attribute-Agnostic Core

The topology core (points, vertices, primitives) knows nothing about attribute semantics. `Position` is just another point attribute with `AttributeID.Position`. This enables:
- Adding arbitrary custom attributes without modifying the core
- Type-safe access via `NativeAttributeAccessor<T>`
- Uniform compilation and transfer across all attribute types

### 4. Explicit Lifecycle Management

Every native structure implements `IDisposable`. The caller owns the memory and must dispose it. Key structs like `NativeDetail` and `NativeCompiledDetail` guard against double-dispose and use-after-dispose.

### 5. Separation of Topology and Attributes

Operations that modify topology (triangulation, cutting) produce new topology + provenance metadata. Attribute transfer is a separate, composable step. This mirrors Houdini's SOP model.

## Memory Layout

### NativeDetail (Editable)

```
NativeDetail
├── PointCount, VertexCount, PrimitiveCount (alive counts)
├── PointCapacity, VertexCapacity, PrimitiveCapacity
├── VertexToPoint[VertexCapacity]          // vertex → point mapping
├── PointAlive[PointCapacity]              // alive flags (1 byte each)
├── VertexAlive[VertexCapacity]
├── PointAttributes: AttributeStore        // per-point attribute columns
│   ├── Position (float3, always present)
│   ├── Normal (float3, optional)
│   └── [custom attributes...]
├── VertexAttributes: AttributeStore       // per-vertex attribute columns
│   ├── Normal (float3, optional)
│   ├── UV0 (float2, optional)
│   └── [custom attributes...]
├── PrimitiveAttributes: AttributeStore    // per-primitive attribute columns
├── PrimitiveStorage                       // N-gon vertex lists + offsets
│   ├── Offsets[PrimitiveCapacity + 1]
│   └── Vertices[total vertex refs]
└── Resources: ResourceRegistry            // typed blob store
```

### NativeCompiledDetail (Read-Only)

```
NativeCompiledDetail
├── VertexToPointDense[VertexCount]        // dense vertex → point mapping
├── PrimitiveOffsetsDense[PrimitiveCount+1] // dense primitive offsets
├── PrimitiveVerticesDense[total refs]     // dense primitive vertex indices
├── PointAttributes: CompiledAttributeSet  // packed point attribute data
├── VertexAttributes: CompiledAttributeSet // packed vertex attribute data
├── PrimitiveAttributes: CompiledAttributeSet
└── Resources: CompiledResourceSet         // packed resource blobs
```

## Typical Workflow

```
Create NativeDetail
       │
       ▼
Add Points / Vertices / Primitives
       │
       ▼
Set Attributes (Position, Normal, UV, Color, custom...)
       │
       ▼
Edit Topology (remove vertices, remove primitives)
       │
       ▼
Compile → NativeCompiledDetail
       │
       ▼
Convert to Unity Mesh  ──or──  Read attributes directly
       │
       ▼
Dispose all native memory
```

## Threading Model

- **Generators**: `SangriaMeshSphereGenerator` uses `IJobParallelFor` with Burst for parallel ring and primitive construction when mesh complexity exceeds thresholds
- **Triangulation**: Single-threaded sweep-line (inherently sequential algorithm)
- **NativeDetailTriangulator**: Single-threaded
- **Attribute Transfer**: Single-threaded (parallelizable in future)
- **BVH**: Supports Burst-compiled `IJob` for build, refit, and overlap queries
- **KD-Tree**: Supports Burst-compiled `IJob` for build, nearest, k-nearest, and radial queries
- **Octree**: Supports Burst-compiled `IJob` for bulk insert and range queries
- **Unity Mesh Conversion**: Main-thread only (Unity Mesh API constraint)
