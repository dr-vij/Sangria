# Legacy Candidates (Core)

This list tracks modern Core functions that currently look like fallback or low-value legacy surfaces.  
Nothing here was removed automatically; this is a review shortlist for future cleanup.

## 1. Adjacency-dirty scan fallback chain (`NativeDetail.Utility.cs`)

Functions:
- `CollectPointVerticesByScan`
- `CollectIncidentPrimitivesByScan`
- `HasIncidentPrimitivesByScan`
- `CanRemovePointWhenAdjacencyDirty`
- `RemovePointWhenAdjacencyDirty`
- `RemoveVertexWhenAdjacencyDirty`
- `RemovePrimitiveWhenAdjacencyDirty`
- `RemoveVertexFromPrimitiveAllOccurrencesWhenAdjacencyDirty`

Why it looks legacy / low-value:
- It duplicates behavior that already exists in the adjacency-map path.
- It uses full scans and extra branching, which increases maintenance cost.
- It is only used as a fallback when adjacency is marked dirty.

Suggested direction:
- If adjacency invalidation/rebuild rules are stable enough, consolidate on a single adjacency-updated path and remove scan fallback code.
