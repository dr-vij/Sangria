**IMPORTANT: FOR SUBSCRIPTIONS TO WORK, YOU NEED THE EXTENSION WITH DisposeAction (FROM SangriaUnityExtensions PACKAGE)!**

# Sangria Source Generators Documentation

## Overview
This solution contains Roslyn source generators that remove repetitive Unity boilerplate (subscriptions, shader IDs, and layer info). It is organized into three projects:
- `SangriaAttributes`: attribute definitions and the `Visibility` enum used to drive generation.
- `SangriaGenerators`: the Roslyn generators themselves.
- `SourceGeneratorExamples`: sample inputs that demonstrate the generated output.

## SangriaAttributes
Namespace: `Sangria.SourceGeneratorAttributes`

**Visibility**
Enum that controls accessor visibility for generated members.

**PropertySubscription** (field attribute)
Adds a property, change callbacks, an event, and a disposable subscription method for the field.
- Parameters: `getterVisibility = Visibility.Public`, `setterVisibility = Visibility.Public`, `params Type[] outputInterfaces`.
- Named properties: `GetterVisibility`, `SetterVisibility`, `OutputInterfaces`.

**ShaderPropertiesProviderAttribute** (class attribute)
Marks a static partial class to generate shader IDs and shader/material caches.

**ShaderNameAttribute** (const string field attribute)
Marks a shader name string constant.

**ShaderPropertyAttribute** (const string field attribute)
Marks a shader property name string constant.

**ExportLayerInfo** (field attribute)
Marks a layer name string field for layer and mask generation.

## SangriaGenerators
Namespace: `Sangria.SourceGenerators`

**FieldSubscriptionsGenerator**
Targets fields with `[PropertySubscription]`.
- Generates a property (name derived from field name by removing leading lowercase/underscore prefix), a private event, and two partial callbacks: `partial void Before{Property}Change(ref T newValue);` and `partial void On{Property}Change(T newValue);`.
- Generates `IDisposable SubscribeTo{Property}(EventHandler<T> handler, bool initialCall = true)`.
- If `OutputInterfaces`/constructor `typeof(...)` arguments are provided, it generates a `public partial interface` with matching public members and makes the class implement it.
- Output filename pattern: `{ClassName}Gen{N}.cs`.
- Note: generated code references `DisposeAction` in the `Sangria` namespace.

**LayersInfoGenerator**
Targets fields with `[ExportLayerInfo]`.
- Expects a string field named like `m_TestLayerName`.
- Generates `int TestLayer` and `int TestLayerMask` properties that call `InitOnce()`.
- Generates `string TestLayerName => m_TestLayerName`.
- Generates backing fields `m_TestLayer` and `m_TestLayerMask`.
- Generates `InitOnce()` that fills fields using `LayerMask.NameToLayer` and `LayerMask.GetMask`.
- Output filename pattern: `{ClassName}Gen.cs`.

**ShaderProviderGenerator**
Targets classes with `[ShaderPropertiesProvider]`.
- Class must be `static partial`, otherwise generation throws.
- For each `[ShaderProperty] const string` field: generates `{ConstToCamelCase(name)}PropertyId` and assigns `Shader.PropertyToID`.
- For each `[ShaderName] const string` field: generates `{ConstToCamelCase(name)}ShaderId`, loads shader from `Resources`, and creates a `Material`.
- Adds two dictionaries and extension accessors: `GetShader(this int shader)` and `GetMaterialByShader(this int shader)`.
- Output filename pattern: `{ClassName}Generated.cs`.
- Note on naming: `ConstToCamelCase` uppercases the first letter and lowercases the rest of each underscore-separated part. Example: `MAIN_TEX` -> `MainTex`.

## Examples (Input -> Generated)
The snippets below are trimmed to the relevant members but reflect the actual generator output.

### PropertySubscriptionExample1
Input (`SourceGeneratorExamples/PropertySubscriptionExample1.cs`):
```csharp
public partial class PropertySubscriptionExample1
{
    [PropertySubscription] private bool m_TestField;
}
```
Generated (relevant members):
```csharp
partial void BeforeTestFieldChange(ref bool newValue);
partial void OnTestFieldChange(bool newValue);
private event EventHandler<bool> TestFieldChanged;

public bool TestField
{
    get { return m_TestField; }
    set
    {
        BeforeTestFieldChange(ref value);
        if (!EqualityComparer<bool>.Default.Equals(m_TestField, value))
        {
            m_TestField = value;
            OnTestFieldChange(value);
            TestFieldChanged?.Invoke(this, value);
        }
    }
}

public IDisposable SubscribeToTestField(EventHandler<bool> handler, bool initialCall = true)
{
    TestFieldChanged += handler;
    if (initialCall) handler?.Invoke(this, m_TestField);
    return new DisposeAction(() => TestFieldChanged -= handler);
}
```

### PropertySubscriptionExample2 (visibility)
Input (`SourceGeneratorExamples/PropertySubscriptionExample2.cs`):
```csharp
public partial class PropertySubscriptionExample2
{
    [PropertySubscription(Visibility.Public, Visibility.Private)] protected bool m_TestVariable;
}
```
Generated (relevant members):
```csharp
public bool TestVariable
{
    get { return m_TestVariable; }
    private set
    {
        BeforeTestVariableChange(ref value);
        if (!EqualityComparer<bool>.Default.Equals(m_TestVariable, value))
        {
            m_TestVariable = value;
            OnTestVariableChange(value);
            TestVariableChanged?.Invoke(this, value);
        }
    }
}
```

### EventSubscriptionExample (interface export)
Input (`SourceGeneratorExamples/EventSubscriptionExample.cs`):
```csharp
public partial interface IEventInterface { }

public partial class EventSubscriptionExample
{
    [PropertySubscription(typeof(IEventInterface))]
    private bool m_TestFieldEvent;
}
```
Generated interface:
```csharp
public partial interface IEventInterface
{
    bool TestFieldEvent { get; set; }
    IDisposable SubscribeToTestFieldEvent(EventHandler<bool> handler, bool initialCall = true);
}
```
Generated class members (relevant):
```csharp
public bool TestFieldEvent { get; set; }
public IDisposable SubscribeToTestFieldEvent(EventHandler<bool> handler, bool initialCall = true) { ... }
```

### MultipleInterfaceExportExample (multiple interfaces)
Input (`SourceGeneratorExamples/MultipleInterfaceExportExample.cs`):
```csharp
[PropertySubscription(Visibility.Public, Visibility.Internal, typeof(ITestInterface))]
private bool m_TestVisibility2;

[PropertySubscription(OutputInterfaces = new[] { typeof(ITestInterface) })]
private bool m_TestFieldDisposable;

[PropertySubscription(typeof(ITestInterface), typeof(ITestInterface2))]
private bool m_TestField2;

[PropertySubscription(typeof(ITestInterface3))]
private bool m_TestField3;
```
Generated interface members (relevant):
```csharp
public partial interface ITestInterface
{
    bool TestVisibility2 { get; }
    bool TestFieldDisposable { get; set; }
    bool TestField2 { get; set; }
    IDisposable SubscribeToTestVisibility2(EventHandler<bool> handler, bool initialCall = true);
    IDisposable SubscribeToTestFieldDisposable(EventHandler<bool> handler, bool initialCall = true);
    IDisposable SubscribeToTestField2(EventHandler<bool> handler, bool initialCall = true);
}

public partial interface ITestInterface2
{
    bool TestField2 { get; set; }
    IDisposable SubscribeToTestField2(EventHandler<bool> handler, bool initialCall = true);
}

public partial interface ITestInterface3
{
    bool TestField3 { get; set; }
    IDisposable SubscribeToTestField3(EventHandler<bool> handler, bool initialCall = true);
}
```

### LayersFieldExample
Input (`SourceGeneratorExamples/LayersFieldExample.cs`):
```csharp
public partial class LayersFieldExample
{
    [ExportLayerInfo] private string m_TestLayerName;
}
```
Generated (relevant members):
```csharp
private int m_TestLayer;
private int m_TestLayerMask;
private bool m_IsInitialized;

public int TestLayer
{
    get { InitOnce(); return m_TestLayer; }
}

public int TestLayerMask
{
    get { InitOnce(); return m_TestLayerMask; }
}

public string TestLayerName => m_TestLayerName;

private void InitOnce()
{
    if (m_IsInitialized) return;
    m_TestLayer = LayerMask.NameToLayer(m_TestLayerName);
    m_TestLayerMask = LayerMask.GetMask(m_TestLayerName);
    m_IsInitialized = true;
}
```

### ShaderProviderExample
Input (`SourceGeneratorExamples/ShaderProviderExample.cs`):
```csharp
[ShaderPropertiesProvider]
public static partial class ShaderProviderExample
{
    [ShaderName] private const string ShaderName = "ShaderName";
    [ShaderProperty] private const string ShaderProperty = "ShaderPropertyName";
}
```
Generated (relevant members):
```csharp
private static readonly Dictionary<int, Shader> Shaders = new();
private static readonly Dictionary<int, Material> Materials = new();

public static int ShadernameShaderId { get; private set; }
public static int ShaderpropertyPropertyId { get; private set; }

public static Shader GetShader(this int shader) => Shaders[shader];
public static Material GetMaterialByShader(this int shader) => Materials[shader];

static ShaderProviderExample()
{
    ShaderpropertyPropertyId = Shader.PropertyToID("ShaderPropertyName");
    ShadernameShaderId = Shader.PropertyToID("ShaderName");
    Shaders[ShadernameShaderId] = Resources.Load<Shader>("ShaderName");
    Materials[ShadernameShaderId] = new Material(Shaders[ShadernameShaderId]);
}
```
