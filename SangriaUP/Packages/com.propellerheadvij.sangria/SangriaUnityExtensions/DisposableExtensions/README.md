**Overview**
These utilities provide a small disposable pattern for Unity code, including base classes, helpers for safe unsubscription, and adapters for Unity events and localization.

All types live in the `Sangria` namespace.

**Types**
- `IDisposedNotifier`: exposes `event Action Disposed` for lifecycle notifications.
- `IDisposableObject`: combines `IDisposable` and `IDisposedNotifier`.
- `DisposableObject`: base class for non-MonoBehaviour disposable objects. Provides `IsDisposed`, `Disposed`, and `OnDispose()`.
- `DisposableMonoBehaviour`: MonoBehaviour that turns `Dispose()` into a safe teardown plus GameObject destruction. Override `OnAwake()` and `OnDispose()` instead of `Awake()` and `OnDestroy()`.
- `DisposeAction`: runs a provided action exactly once on dispose.
- `DisposableCombiner`: collects multiple `IDisposable` instances and disposes them together.
- `DisposeExtensions`: extension helpers for common disposal patterns.
- `UnityEventExtensions`: `UnityEvent` subscribe helpers that return `IDisposable` unsubscriptions.
- `LocalizationHelpers`: `LocalizedString` subscribe helper that returns an `IDisposable` unsubscription.

**Key Behaviors**
- `DisposableMonoBehaviour.Dispose()` calls `OnDispose()`, fires `Disposed`, then destroys the GameObject. `OnDestroy()` calls `Dispose()`.
- `DisposeAction` uses `Interlocked.Exchange` so the action is invoked at most once.
- `DisposeOnDisposed` auto-unsubscribes if the disposable is also an `IDisposedNotifier`.

**Usage**
Use `OnAwake()` and `OnDispose()` in `DisposableMonoBehaviour`.

```csharp
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;

public sealed class MyWidget : Sangria.DisposableMonoBehaviour
{
    [SerializeField] private UnityEvent m_OnClick;
    [SerializeField] private LocalizedString m_Title;
    private readonly Sangria.DisposableCombiner m_Disposables = new();

    protected override void OnAwake()
    {
        m_OnClick.Subscribe(OnClick).CombineTo(m_Disposables);
        m_Title.Subscribe(OnTitleChanged).CombineTo(m_Disposables);
    }

    protected override void OnDispose()
    {
        m_Disposables.Dispose();
    }

    private void OnClick() { }
    private void OnTitleChanged(string value) { }
}
```

Dispose when another notifier is disposed.

```csharp
IDisposable subscription = someEvent.Subscribe(Handler);
subscription.DisposeOnDisposed(owner);
```

Add and remove handlers with `IDisposedNotifier`.

```csharp
IDisposable cleanup = owner.AddActionToDisposeEvent(() => Debug.Log("Disposed"));
// Later: cleanup.Dispose();
```
