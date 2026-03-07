# SangriaMesh Architecture (actual state)

Updated: 2026-03-06

This document describes the **current implementation**, not a target design.

## 1. Scope

The package currently ships two mesh stacks:

1. `SangriaMesh` (new core for node-native authoring + dense runtime snapshot)
2. `ViJMeshTools` (legacy mesh cutting and tessellation pipeline)

Both are compiled by the same assembly (`SangriaMesh`), but they are architecturally separate.

## 2. Module Boundaries

### 2.1 New core (`namespace SangriaMesh`)

Path: `PropellerheadMesh/Scripts/SangriaMesh/`

Main roles:
- mutable topology and attributes (`NativeDetail`)
- stable handles (`SparseHandleSet`, `ElementHandle`)
- variable-size primitive topology (`PrimitiveStorage`)
- per-domain typed attributes (`AttributeStore`)
- custom detail resources (`ResourceRegistry`)
- dense compiled snapshot (`NativeCompiledDetail` + `Compiled*Set`)

### 2.2 Unity bridge and generators

Path:
- `PropellerheadMesh/Scripts/Generators/`
- `PropellerheadMesh/Scripts/Debug/`
- `PropellerheadMesh/Scripts/ExampleUsage/`

Main roles:
- procedural dense sphere generation (`SangriaMeshSphereGenerator`)
- conversion of compiled snapshot to `UnityEngine.Mesh` (`SangriaMeshUnityMeshExtensions`)
- debug visualization in editor (`DetailVisualizer`)

### 2.3 Legacy cutter stack (`namespace ViJMeshTools`)

Path:
- `Scripts/MeshOpps/`
- `Scripts/Helpers/TessAdapter.cs`
- `Scripts/Structures/`

Main roles:
- plane-based mesh split
- contour extraction and tessellation
- manifold/watertight/orientation validation

Important: legacy stack is not migrated to `NativeDetail`/`NativeCompiledDetail` yet.

## 3. New Core Data Model

### 3.1 Domains

Core domains are explicit (`MeshDomain`):
- `Point`
- `Vertex`
- `Primitive`

`Vertex -> Point` mapping is stored in `m_VertexToPoint`.
Primitive topology is variable-length (`PrimitiveStorage`).

### 3.2 Mutable layer (`NativeDetail`)

`NativeDetail` owns:
- `SparseHandleSet` for points/vertices/primitives
- `NativeList<int> m_VertexToPoint`
- `PrimitiveStorage`
- 3x `AttributeStore` by domain
- `ResourceRegistry`
- change versions (`TopologyVersion`, `AttributeVersion`)

Mandatory behavior:
- `Position` point attribute is auto-registered at construction
- index reuse is generation-safe via `ElementHandle(Index, Generation)`
- reused slots are cleared in `AttributeStore.ClearElement`

Topology mutation behavior:
- removing point removes dependent vertices
- removing vertex updates primitives and removes degenerate primitives (<3 vertices)
- removing primitive clears its topology record

### 3.3 Compile layer

`NativeDetail.Compile()` has two paths:

1. Dense contiguous path (`CompileDenseContiguous`)
- used when all handle sets are contiguous and have no holes
- copies `VertexToPoint` with `MemCpy`
- emits primitive topology as CSR-like buffers
- has fast branch for dense triangle layout in `PrimitiveStorage`

2. Sparse path (`CompileSparse`)
- builds alive index lists
- builds sparse->dense remap arrays
- compacts topology and attributes to dense output

Compiled output (`NativeCompiledDetail`) contains:
- `VertexToPointDense`
- `PrimitiveOffsetsDense`
- `PrimitiveVerticesDense`
- compiled attribute sets for each domain
- compiled resources set

### 3.4 Attributes and resources

Attributes:
- dynamic registration by attribute id
- typed resolution to `AttributeHandle<T>`
- mutable typed accessor (`NativeAttributeAccessor<T>`)
- compiled typed accessor (`CompiledAttributeAccessor<T>`)

Resources:
- detail-level typed payloads by resource id
- packed at compile into `CompiledResourceSet`

## 4. Unity Conversion Paths

`SangriaMeshUnityMeshExtensions` provides two runtime paths:

1. `FillUnityMeshTriangles` (fast path)
- requires triangle-only topology
- uses Unity MeshData API (`AllocateWritableMeshData`)
- writes vertex/index streams with minimal abstraction overhead

2. `FillUnityMesh` (general path)
- supports polygon primitives
- triangulates non-triangle polygons with ear clipping and fan fallback
- uses managed arrays/lists and has higher CPU/GC overhead

## 5. Legacy Cutter Pipeline (current reality)

`MeshCutter.CutMeshWithTesselation` workflow:
1. `CutMeshWithPlaneJob` splits source mesh into positive/negative sides
2. cut segments are converted to contours in `TessAdapter`
3. contours are tessellated by LibTessDotNet
4. tessellated cap is merged into both output meshes

Current technical traits:
- heavy use of per-call native and managed allocations
- contour consolidation defaults to bruteforce linking
- no integration with new core compiled snapshot model

## 6. Performance-Critical Hot Paths

### 6.1 New core hot paths

- `NativeDetail.Compile*` is full-snapshot rebuild each call
- sparse compile does multiple passes (alive scan, remap, compact)
- polygon mesh conversion path is significantly slower than triangle fast path

### 6.2 Legacy hot paths

- `CutMeshWithPlaneJob.Execute` is single-threaded and vertex-duplicating
- `TessAdapter.ConsolidateContoursFromSegmentsBruteforce` is O(n^2)
- LibTess contour preprocessing is managed-side and allocation-heavy

## 7. Development Status Snapshot

Stable and used:
- new core storage model (`NativeDetail` + compile snapshot)
- dense sphere generation pipeline
- triangle-focused Unity conversion path

Partially mature:
- polygon triangulation conversion path (correctness-focused, not speed-first)
- attribute/resource extensibility in larger production graphs

Legacy/transition area:
- cutter/tessellator stack in `ViJMeshTools`
- validator jobs and test-zone tools

## 8. Known Structural Risks

- two separate architectures in one package increase maintenance cost
- legacy and new core use different mental models and data contracts
- performance-sensitive algorithms are split across Burst-friendly and managed-heavy code paths

