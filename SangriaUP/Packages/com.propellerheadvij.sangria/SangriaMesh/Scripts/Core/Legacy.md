# Legacy Candidates (Core)

This list tracks Core surfaces that may look legacy and need explicit validation before cleanup.
Resolved entries are kept as an audit trail.

## 1. Adjacency-dirty scan fallback chain (`NativeDetail.Utility.cs`)

Status:
- Resolved on 2026-03-21.
- Legacy scan fallback methods were removed.

What was changed:
1. Removed dirty-scan helper chain:
   - `CollectPointVerticesByScan`
   - `CollectIncidentPrimitivesByScan`
   - `HasIncidentPrimitivesByScan`
   - `CanRemovePointWhenAdjacencyDirty`
   - `RemovePointWhenAdjacencyDirty`
   - `RemoveVertexWhenAdjacencyDirty`
   - `RemovePrimitiveWhenAdjacencyDirty`
   - `RemoveVertexFromPrimitiveAllOccurrencesWhenAdjacencyDirty`
2. Consolidated point/vertex remove and can-remove flows to one adjacency-based path.
3. `EnsureAdjacencyUpToDate()` is now the single gate before policy checks and topology mutation when adjacency is not already prepared.

Safety constraints preserved:
- Delete-policy semantics remain enforced through adjacency maps in `CanRemovePoint`, `RemovePoint`, `CanRemoveVertex`, and `RemoveVertexInternal`.
- Dirty adjacency remains supported: operations rebuild adjacency first, then execute mutation/check logic.
- Runtime flow `PopulateUvSphere -> immediate edits` remains valid through the same public API.
