**Overview**
`SangriaInput` is a custom input module for Sangria built on the new Unity Input System. It normalizes mouse, pen, and touch into a unified pointer stream, builds gestures (press/drag/2-finger drag), and routes them to `InteractionObjectBase` via its own `InteractionEvent` system.

**Dependencies**
- Unity Input System (`UnityEngine.InputSystem`).
- Unity UI/EventSystem (`UnityEngine.EventSystems`, `GraphicRaycaster`, `PanelRaycaster`).
- TextMeshPro (used by `Prefabs/SimpleTextDebugger.prefab`).
- External Sangria base types: `SingletonMonoBehaviour<T>`, `DisposableMonoBehaviour`, `DisposeAction`, `FuncComparer` (defined outside this folder; the module compiles thanks to references in `SangriaInput.asmdef`).

**Folder Layout**
- `SangriaInput.asmdef` — module assembly definition.
- `SangriaInputActions.inputactions` — Input System asset with the `GestureActions` map.
- `SangriaInputActions.cs` — Input System auto-generated file (do not edit manually).
- `InputSystem.inputsettings.asset` — Input System settings.
- `Scripts/` — module code.
- `Prefabs/` — ready-to-use prefabs (including `InputManager`).
- `TestZone/` — test scene and sample scripts.

**Quick Start**
1. Add `Prefabs/InputManager.prefab` to the scene or attach `InputManager` to any GameObject.
2. Ensure the scene has an `EventSystem` and the new Input System is enabled.
3. Register cameras that should be traced:
```csharp
using Sangria.Input;
using UnityEngine;

public class InputBootstrap : MonoBehaviour
{
    private void Awake()
    {
        InputManager.Instance.RegisterCamera(Camera.main);
        InputManager.Instance.CameraTracer.LayerSettings.SetLayers("Default");
    }
}
```
4. Add a component derived from `InteractionObjectBase` and subscribe to events:
```csharp
using Sangria.Input;
using UnityEngine;

public class MyInteractable : MonoBehaviour
{
    private InteractionObjectBase m_Obj;

    private void Awake()
    {
        m_Obj = GetComponent<InteractionObjectBase>();
        m_Obj.SubscribePointerPressEvent(OnPress);
        m_Obj.SubscribePointerDragEvent(OnDragStart, OnDrag, OnDragEnd);
    }

    private void OnPress(object sender, PointerInteractionEventArgs args)
    {
        // click/tap handling
    }

    private void OnDragStart(object sender, PointerDragInteractionEventArgs args) { }
    private void OnDrag(object sender, PointerDragInteractionEventArgs args) { }
    private void OnDragEnd(object sender, PointerDragInteractionEventArgs args) { }
}
```
5. Tune `DragOrPressTriggerDistance` in `InputManager` (default is 10 in the prefab).

**Core Concepts**
- `InputManager` — the single entry point, listens to `SangriaInputActions`, generates pointer/mouse events, controls the active device, and creates gesture analyzers.
- `CameraTracer` — performs 3D raycasts and checks whether the pointer is over UI.
- `InteractionObjectBase` — base subscription object. Routes events to subscribers and forwards them to the camera’s `InteractionObjectBase` when needed.
- `IGestureAnalyzer` — gesture analyzer interface; current implementation is `SimpleGestureAnalyzer`.
- `InteractionEvent` + `InteractionEventArgs` — custom event system with `Handle()` support.
- `InteractionIgnorer` — marker component that removes an object from the interaction hierarchy.

**Input Flow**
1. `InputManager` receives events from `SangriaInputActions`.
2. For pointer events, it creates/finds a gesture analyzer for the object under the pointer (`CameraTracer`).
3. `SimpleGestureAnalyzer` tracks pointer state and raises `PointerInteractionEvents` and `MouseInteractionEvents`.
4. `InteractionObjectBase.RunEvent` invokes subscribers and optionally forwards the event to the camera object.

**Pointer vs Mouse**
- Pointer events are unified for mouse, pen, and touch (see `PointerInputComposite`).
- Mouse events are separate and include left/right/middle variants.
- Mixed devices are not allowed: while mouse/pen is active, touch is ignored and vice versa (`InputManager.CanBeHandled`).

**Events**
Pointer events (`PointerInteractionEvents`):
- `PointerPressEvent`
- `PointerMoveEvent`
- `PointerDragStartEvent`
- `PointerDragPerformEvent`
- `PointerDragEndEvent`
- `TwoPointersDragStartEvent`
- `TwoPointersDragPerformEvent`
- `TwoPointersDragEndEvent`
- `PointerGrabEvent`
- `PointerReleaseEvent`

Mouse events (`MouseInteractionEvents`):
- `MouseGrabEvent`, `MouseReleaseEvent`, `MousePressEvent`
- `MouseDragStartEvent`, `MouseDragPerformEvent`, `MouseDragEndEvent`
- `MouseLeft*`, `MouseRight*`, `MouseMiddle*` variants of all the above.

**Key Classes**
- `InputManager`
- Properties: `DragOrPressTriggerDistance`, `CameraTracer`, `PressedButtonsCount`.
- Methods: `RegisterCamera`, `SubscribeGlobalMouseDown/Up`, `SubscribeGlobalScrollDelta`, `GetActualObjectForPointerEventArgs`.
- `m_UpdateCausesGestureUpdates`: if enabled, each `Update()` forces analyzers to update (useful for sync logic but costs CPU).

- `CameraTracer`
- `TryTraceInteractionObject` — finds the nearest `InteractionObjectBase` via 3D colliders.
- `IsOverUI` — checks if UI is blocking the pointer (filters interactions over UI).
- `LayerSettings` — layer mask for raycasts.

- `SimpleGestureAnalyzer`
- One pointer: press/drag decided by `DragOrPressTriggerDistance`.
- Two pointers: `TwoPointersDrag*` (pinch/scale etc.).
- Mouse: separate `Mouse*` events.

- `InteractionObjectHelpers`
- Extension methods for subscriptions and `InteractionIgnorer` helpers.
- Simplifies common patterns like `SubscribePointerDragEvent` or `SubscribeLeftMouseGrabEvent`.

**Input Actions Asset**
The `GestureActions` map contains:
- `Pointer` (PassThrough) — `PointerInput` composite for mouse, pen, and touch.
- `MouseLeft`, `MouseRight`, `MouseMiddle` — mouse buttons.
- `MousePosition` — mouse position.
- `MouseScroll` — mouse scroll.

`Pointer` is bound to:
- Mouse (left click + position + pointerId).
- Pen (tip + position + tilt + radius + pressure + twist).
- Touch 0..9 (press + position + radius + pressure + touchId).

**Prefabs**
- `Prefabs/InputManager.prefab` — ready-to-use `InputManager` with `DragOrPressTriggerDistance = 10`.
- `Prefabs/SimpleTextDebugger.prefab` — UI debugger with TMP and a custom script.
- `Prefabs/TestPrefabs/` — test materials/prefabs.

**TestZone**
- `TestZone/SampleScene.unity` — demo scene.
- `TestZone/InputTest.cs` — sample global subscriptions and `ignoreHandled` toggle.
- `TestZone/TestObject.cs` — example drag/press/grab/scale logic.
- `TestZone/PointerVisualizer.prefab` — UI pointer visualization.

**Notes and Quirks**
- UI blocks pointer events: `CameraTracer.IsOverUI` filters interactions over UI.
- Mixed device input is disabled by design (see `CanBeHandled`).
- `TwoPointersDragInteractionEventArgs` stores secondary positions in reversed order in its constructor (`SecondaryPointerPrevPosition`/`SecondaryPointerPosition`). If gesture math looks off, check this spot.

**Where to Extend**
- Create your own `InteractionObjectBase` and return a custom analyzer in `CreateAnalyzer`.
- Add new `InteractionEvent` keys and subscription helpers in `InteractionObjectHelpers` style.
- Modify `SangriaInputActions.inputactions` for new input types, but remember `SangriaInputActions.cs` is regenerated.
