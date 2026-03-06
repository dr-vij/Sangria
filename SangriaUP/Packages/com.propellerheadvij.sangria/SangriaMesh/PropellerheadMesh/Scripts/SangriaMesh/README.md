# SangriaMesh

SangriaMesh is an alternative mesh kernel with a two-stage data model:

1. `NativeDetail` (editable)
2. `NativeCompiledDetail` (compiled snapshot)

It is intended to preserve authoring flexibility while providing dense runtime-friendly data for Burst jobs and GPU paths.

## Documentation

- Full architecture and design notes: [ARCHITECTURE.md](./ARCHITECTURE.md)

## Core objectives

1. Domain-based custom attributes (`point`, `vertex`, `primitive`)
2. Stable references in mutable mode (`ElementHandle`)
3. Dense compile output for fast processing
4. Extensible custom resources (`ResourceRegistry`)

## Typical workflow

1. Build or modify mesh in `NativeDetail`.
2. Resolve typed attribute handles and write attributes/resources.
3. Call `Compile()` to produce `NativeCompiledDetail`.
4. Process with dense arrays and compiled accessors.
5. Convert to `UnityEngine.Mesh` with `SangriaMeshUnityMeshExtensions` (polygon primitives use ear clipping with fan fallback, triangle-only fast path `FillUnityMeshTriangles` uses Unity MeshData API).
