# SangriaSceneView Editor Extension

## Overview
SangriaSceneView extends Unity's SceneView camera with plane-aware pivoting, zooming, and configurable clipping. It adds a dedicated editor window that hooks into `SceneView.duringSceneGui` and updates SceneView camera settings on the fly.

## Files
- `EditorMeshIntersections.cs`
  - Internal helper for editor-only mesh raycasts using Unity's internal `HandleUtility.IntersectRayMesh` (reflection-based).
  - Utility method for picking the GameObject under a 2D cursor position.
- `SceneViewSettings.cs`
  - Serializable settings: scroll sensitivity, near/far clip, and the active world plane.
  - Maintains a list of active world planes used for zoom and pivot targeting.
- `SceneCameraEditor.cs`
  - Main editor window and SceneView event handler.
  - Implements camera pivot changes, zoom behavior, rotation visuals, and clip updates.

## Opening the Window
Menu path: `Tools/UTools/SceneView Camera Extension`

## Settings
- `ScrollSensetivity` (0..1)
  - Controls zoom multiplier per mouse wheel tick.
- `NearClip` / `FarClip`
  - Applied to SceneView camera with `dynamicClip = false`.
- `SceneViewPlane`
  - Selects the world axis plane used for plane-based zoom and pivoting: `XPlane`, `YPlane`, `ZPlane`.

## Controls
- **Enable plugin / Disable plugin**
  - Toggles whether the SceneView handler is attached.
- **Alt + LMB click**
  - Moves the pivot to the nearest plane/object under the cursor without changing zoom.
- **Alt + LMB drag**
  - Rotates around the picked point and draws a yellow wire cube at the rotation pivot.
- **Mouse wheel**
  - Without `Alt`: standard zoom centered on current pivot.
  - With `Alt`: zooms toward the nearest plane/object under the cursor and draws a temporary wire cube at the zoom target.

## Plane Selection Logic (Priority)
1. Raycast against the mesh under the cursor (object plane).
2. If none, use the nearest active world plane from `SceneViewSettings`.
3. Fallback to a camera-facing plane through the current pivot.

## Persistence
`SceneViewSettings` are serialized to `EditorPrefs` as JSON on window disable and restored on enable.

## Notes
`EditorMeshIntersections` relies on Unity's internal API via reflection, which can change between Unity versions.
