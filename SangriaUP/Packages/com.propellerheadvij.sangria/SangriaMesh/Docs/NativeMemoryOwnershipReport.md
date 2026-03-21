# NativeDetail / NativeCompiledDetail Memory Ownership Report

## Scope

This report covers:

- `Scripts/Core/NativeDetail.cs`
- `Scripts/Core/NativeDetail.*.cs`
- `Scripts/Core/NativeCompiledDetail.cs`
- Consumers in `Scripts/Generators/SangriaMeshUnityMeshExtensions.cs`

## Executive Summary

The current design has a strong architectural split (editable mutable mesh -> compiled packed snapshot), which is good and aligned with data-oriented Unity workflows.

The main correctness gap is ownership semantics of disposable native memory inside mutable `struct` containers. Today both `NativeDetail` and `NativeCompiledDetail` can be copied by value, and `NativeCompiledDetail` also exposes public mutable fields. This combination allows accidental double-dispose and use-after-free patterns.

## Current Ownership Model

- `NativeDetail` is an editable owner of topology, attributes, resources, and adjacency caches.
- `NativeCompiledDetail` is a compiled packed owner used for fast read access and mesh emission.
- `NativeDetail.Compile(...)` allocates and returns a new compiled owner.

This high-level model is sound. The ownership boundaries inside public API are not strict enough.

## Key Risks

### 1) Value-copy ownership aliasing on disposable structs

Both owners are plain mutable `struct`s. Any assignment or by-value parameter creates aliasing over the same underlying native allocations.

Examples:

- `var copy = compiled;` then `compiled.Dispose(); copy.Dispose();` (double-dispose risk)
- Passing owners by value to helper/extension methods

### 2) Public mutable fields in `NativeCompiledDetail`

`NativeCompiledDetail` exposes raw containers as public fields:

- `VertexToPointDense`
- `PrimitiveOffsetsDense`
- `PrimitiveVerticesDense`
- `PointAttributes`, `VertexAttributes`, `PrimitiveAttributes`, `Resources`

This allows external code to:

- Dispose nested containers independently (breaking owner invariants)
- Reassign fields (breaking internal consistency)

### 3) Incomplete disposed-guarding in `NativeDetail`

`NativeCompiledDetail` guards access with `ThrowIfDisposed()`.
`NativeDetail` tracks `m_IsDisposed` but public methods do not consistently guard it before operating on native members.

### 4) Read-only API still copies owners by value

Several extension methods accept `NativeDetail` / `NativeCompiledDetail` by value for read scenarios (visualization, mesh conversion). This increases accidental owner copies in user code.

## How Other Libraries Usually Model This

### Unity.Collections pattern

- Native containers are disposable value types, but their internal state is encapsulated.
- Public API usually exposes methods/properties instead of raw mutable ownership fields.
- Safety checks (in checks-enabled builds) catch many misuse patterns, but API still tries to reduce misuse surface.

### Unity.Entities pattern (Blob-like runtime snapshots)

- Immutable runtime snapshots are preferred for hot-path reads.
- Ownership and reading are often split conceptually: one entity owns storage, many systems consume read-only views.

### .NET memory-owner pattern (`IMemoryOwner<T>`)

- One explicit owner object controls disposal.
- Consumers receive views (`Memory<T>`, `ReadOnlyMemory<T>`) without ownership transfer.

## Recommended Changes (Idiomatic, Incremental)

## Phase 1: Low-Risk Hardening (recommended first)

1. Make `NativeCompiledDetail` fields private.
2. Expose read-only data via properties/methods:
   - `NativeArray<int>.ReadOnly VertexToPoint`
   - `NativeArray<int>.ReadOnly PrimitiveOffsets`
   - `NativeArray<int>.ReadOnly PrimitiveVertices`
   - Keep typed accessor methods for attributes/resources.
3. Add `ThrowIfDisposed()` checks to all public `NativeDetail` entry points.
4. Change read-only extension methods to `in NativeCompiledDetail` and `ref NativeDetail` (or `in` once `Compile` is made `readonly`-safe) to avoid owner copies.
5. Add XML docs: "Owner type. Do not copy by value. Pass by `ref`/`in`."

Expected effect: major reduction of accidental ownership violations with moderate API churn.

## Phase 2: Explicit Owner/View Split

Introduce a non-disposable read-only view type:

- `readonly struct NativeCompiledDetailView`
- Holds `ReadOnly` topology slices + typed accessors
- No `Dispose()`

Owner API:

```csharp
public NativeCompiledDetailView AsView();
```

Consumer APIs (`FillUnityMesh`, debug drawing, analysis tools) should accept `in NativeCompiledDetailView`.

Expected effect: disposal authority is centralized; read-only consumers cannot accidentally dispose.

## Phase 3: Strong Ownership Semantics (breaking change, best correctness)

Promote owners to reference types:

- `sealed class NativeDetail : IDisposable`
- `sealed class NativeCompiledDetail : IDisposable`

Jobs still consume blittable views/handles, not the managed owner.

Expected effect: eliminates value-copy aliasing class of bugs entirely.

## Suggested API Direction (Practical)

Minimal hardening shape:

```csharp
public struct NativeCompiledDetail : IDisposable
{
    private NativeArray<int> _vertexToPointDense;
    // ...

    public NativeArray<int>.ReadOnly VertexToPointDense => _vertexToPointDense.AsReadOnly();
}
```

Consumer shape:

```csharp
public static void FillUnityMesh(this in NativeCompiledDetailView compiled, Mesh mesh)
```

## Tests To Add

1. Copy-dispose misuse test for each owner type (documents unsupported behavior or validates new guard).
2. Ensure nested data cannot be individually disposed by external code after encapsulation.
3. `NativeDetail` throws `ObjectDisposedException` consistently after disposal.
4. Extension methods do not create owner copies (signature-level regression tests where possible).

## Implementation Priority

Recommended order:

1. Phase 1 immediately (best cost/benefit).
2. Phase 2 for stable long-term API.
3. Phase 3 only if you can accept a breaking major-version migration.

## Final Assessment

Your current two-stage architecture is idiomatic for Unity/Burst.
What is not yet idiomatic enough is ownership exposure: public mutable owner fields + disposable struct copies.
Harden ownership boundaries first; then, if needed, migrate to owner/view or class-owner architecture for maximal safety.

