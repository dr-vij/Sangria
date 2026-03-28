# SangriaMesh to Unity Mesh Converter Roadmap

## 1. Material Groups (Submeshes)
Support `MaterialID` attribute on the `Primitive` domain.
- Identify `MaterialID` in `NativeCompiledDetail`.
- Group primitives by `MaterialID` before triangulation.
- Create multiple `SubMeshDescriptor`s in Unity `MeshData`.
- Assign indices correctly to submeshes.

## 2. Expanded Formats (Half, Byte, Int)
Support `VertexAttributeFormat` other than `Float32`.
- **Idea**: Dynamically select `VertexAttributeFormat` based on `TypeHash` of the attribute in Sangria.
- **Example**: If `Color` has 4 `byte`s, use `UNorm8`.
- **Complexity**: Moderate. Requires mapping `TypeHash` to Unity formats and handling alignment differences. If formats don't match, we need a "slow path" with explicit conversion.

## 3. Debug Visualization Tools (Backlog)
- Create a Unity Editor Inspector for `NativeCompiledDetail`.
- Show attribute list, domains, sizes, and raw data previews.
- Gizmo visualization for attributes on a mesh.

## 4. Stride Validation & Slow Path (TODO)
- Added validation for attribute strides.
- **TODO**: Implement a generic "slow path" that handles format conversion when the source data doesn't match the destination format (e.g., `double3` -> `float3`).
