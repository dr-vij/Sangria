# SangriaMesh

SangriaMesh currently contains two mesh stacks in one package:

1. `SangriaMesh` namespace (new node-oriented core)
2. `ViJMeshTools` namespace (legacy mesh cutting/tessellation pipeline)

This README documents the **current** state of the new core and where it intersects with legacy code.

## What Is Production-Ready

- Editable mesh core: `NativeDetail`
- Dense runtime snapshot: `NativeCompiledDetail`
- Domain attributes (`Point`, `Vertex`, `Primitive`) with typed handles/accessors
- Custom resources via `ResourceRegistry`
- Dense compile fast path when topology is contiguous
- Unity mesh conversion:
  - fast triangle-only path: `FillUnityMeshTriangles`
  - polygon path with triangulation fallback: `FillUnityMesh`
- Sphere generator optimized for dense writes: `SangriaMeshSphereGenerator`

## Current Module Layout

- Core types and storage:
  - `./NativeDetail.cs`
  - `./SparseHandleSet.cs`
  - `./PrimitiveStorage.cs`
  - `./AttributeStore.cs`
  - `./ResourceRegistry.cs`
- Compiled snapshot layer:
  - `./NativeCompiledDetail.cs`
  - `./CompiledAttributeSet.cs`
  - `./CompiledResourceSet.cs`
- Unity bridge and generators:
  - `../Generators/SangriaMeshUnityMeshExtensions.cs`
  - `../Generators/SangriaMeshSphereGenerator.cs`
- Debug/example:
  - `../Debug/DetailVisualizer.cs`
  - `../ExampleUsage/SangriaMeshExample.cs`
- Legacy cutter stack (separate path, not yet migrated to core):
  - `../../../Scripts/MeshOpps/*.cs`
  - `../../../Scripts/Helpers/TessAdapter.cs`

## Typical Node Workflow

1. Build/update editable topology in `NativeDetail`.
2. Register and write attributes/resources.
3. Call `Compile()` to produce `NativeCompiledDetail`.
4. Consume dense buffers in Burst jobs or conversion path.
5. Convert to `UnityEngine.Mesh`:
   - triangle topology -> `FillUnityMeshTriangles` (fastest)
   - mixed polygon topology -> `FillUnityMesh`.

## Performance Notes

- `Compile()` currently rebuilds a snapshot from mutable state each call.
- Best runtime path today is:
  - keep topology triangle-only,
  - stay in dense contiguous mode,
  - use `FillUnityMeshTriangles`.
- Polygon conversion path uses managed triangulation fallback and is intentionally more general but slower.

## Related Docs

- Architecture and file-level behavior: [ARCHITECTURE.md](./ARCHITECTURE.md)
- Current development review and performance roadmap: [CodeReview.md](./CodeReview.md)
- Detailed execution roadmap and benchmark targets: [ROADMAP.md](./ROADMAP.md)
