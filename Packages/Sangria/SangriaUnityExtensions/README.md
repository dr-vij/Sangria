**Overview**
This folder contains general Unity helper classes and utilities. All types live in the `Sangria` namespace.

**Types**
- `MathUtilities`: vector, bounds, and renderer bounds helpers.
- `CameraSolver`: calculates a camera position that fits a set of world points in the camera frustum.
- `FuncComparer<T>`: build an `IComparer<T>` from a lambda.
- `Singleton<T>`: lazy, non-thread-safe singleton for plain classes with `new()`.
- `SingletonMonoBehaviour<T>`: scene singleton lookup for MonoBehaviour instances.
- `TextDebugger`: on-screen logger that writes into a `TextMeshProUGUI` field.

**MathUtilities**
- `IsProjectionGreater(original, compared, direction)`: compares point projections along a direction vector.
- `GetAllRendererBounds(GameObject)`: bounds of all renderers in a hierarchy.
- `GetCombinedBounds(Renderer[])`: combine multiple renderer bounds.
- `GetBoundsCorners(Bounds)`: returns the 8 corners of a bounds.

**CameraSolver**
`CalculatePositionToFitPoints` computes a camera position that keeps points inside the frustum. Gaps are expressed as percentages of screen width/height.

```csharp
var bounds = Sangria.MathUtilities.GetAllRendererBounds(target);
var points = bounds.GetBoundsCorners();

if (Sangria.CameraSolver.CalculatePositionToFitPoints(
        cam,
        points,
        out var camPos,
        leftGap: 0.05f,
        rightGap: 0.05f,
        topGap: 0.10f,
        bottomGap: 0.10f))
{
    cam.transform.position = camPos;
    cam.transform.LookAt(bounds.center);
}
```

**FuncComparer**

```csharp
IComparer<MyType> comparer = Sangria.FuncComparer<MyType>.Create(
    (a, b) => a.Score.CompareTo(b.Score));
```

**Singleton<T>**
`Singleton<T>.Instance` constructs `T` with `new()` the first time it is accessed.

```csharp
public sealed class GameConfig : Sangria.Singleton<GameConfig>
{
    public int MaxLives = 3;
}

var cfg = Sangria.Singleton<GameConfig>.Instance;
```

**SingletonMonoBehaviour<T>**
Searches the scene for exactly one instance of `T`. Logs an error if none or more than one exist.

```csharp
public sealed class GameManager : Sangria.SingletonMonoBehaviour<GameManager> { }

var mgr = Sangria.SingletonMonoBehaviour<GameManager>.Instance;
```

**TextDebugger**
Attach to a GameObject with a `TextMeshProUGUI` reference assigned in the inspector.

```csharp
Sangria.TextDebugger.Instance.Log("hello");
Sangria.TextDebugger.Instance.LogWarning("warn");
Sangria.TextDebugger.Instance.LogError("error");
Sangria.TextDebugger.Instance.Clear();
```
