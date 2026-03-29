# SangriaMesh — Debug Visualization

## Overview

`DetailVisualizer` provides Gizmo-based visualization tools for inspecting `NativeDetail` geometry in the Unity Scene view. All methods are extension methods on `NativeDetail`.

**Source file**: `DetailVisualizer.cs`

## Visualization Methods

### DrawPointGizmos

Draws wire cubes at each alive point position.

```csharp
detail.DrawPointGizmos(pointSize: 0.03f, pointColor: Color.yellow);
```

| Parameter | Description |
|-----------|-------------|
| `pointSize` | Size of the wire cube at each point |
| `pointColor` | Color of the wire cubes |

### DrawPrimitiveLines

Draws wireframe edges for all alive primitives.

```csharp
detail.DrawPrimitiveLines(lineColor: Color.cyan);
```

Draws lines between consecutive vertices of each primitive, forming a closed polygon outline.

### DrawVertexNormalsGizmos

Draws normal vectors as lines from vertex positions.

```csharp
detail.DrawVertexNormalsGizmos(normalLength: 0.08f, normalColor: Color.green);
```

Uses vertex-domain normals if available, falls back to point-domain normals.

### DrawPointNumbers

Draws point index labels in the Scene view (Editor only).

```csharp
detail.DrawPointNumbers(textColor: Color.white, offset: 0.02f);
```

| Parameter | Description |
|-----------|-------------|
| `textColor` | Color of the label text |
| `offset` | Offset from point position to label position |

Requires `UNITY_EDITOR` — uses `Handles.Label` for rendering.

### DrawPrimitiveNumbers

Draws primitive index labels at the centroid of each primitive (Editor only).

```csharp
detail.DrawPrimitiveNumbers(textColor: Color.cyan, offset: 0.02f);
```

Computes the centroid as the average position of all vertices in the primitive.

## Usage in MonoBehaviour

Typically used in `OnDrawGizmos` or `OnDrawGizmosSelected`:

```csharp
private void OnDrawGizmos()
{
    var savedMatrix = Gizmos.matrix;
    Gizmos.matrix = transform.localToWorldMatrix;

    try
    {
        if (m_DrawWireframe)
            m_Detail.DrawPrimitiveLines(Color.cyan);

        if (m_DrawPoints)
            m_Detail.DrawPointGizmos(0.03f, Color.yellow);

        if (m_DrawNormals)
            m_Detail.DrawVertexNormalsGizmos(0.08f, Color.green);

        if (m_DrawPointNumbers)
            m_Detail.DrawPointNumbers(Color.white, 0.02f);

        if (m_DrawPrimitiveNumbers)
            m_Detail.DrawPrimitiveNumbers(Color.cyan, 0.02f);
    }
    finally
    {
        Gizmos.matrix = savedMatrix;
    }
}
```

## Notes

- All methods gracefully handle missing attributes (e.g., if `Position` is not available, drawing is skipped)
- `DrawPointNumbers` and `DrawPrimitiveNumbers` are only available in the Editor (`#if UNITY_EDITOR`)
- Set `Gizmos.matrix` to `transform.localToWorldMatrix` to draw in local space
- All methods allocate temporary `NativeList` buffers for alive element enumeration (disposed immediately)
