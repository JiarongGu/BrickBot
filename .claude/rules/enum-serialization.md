# Enum Serialization Rule (CRITICAL)

**IpcHandler will use `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`** (mirroring BrickBot).

This means ALL C# enums serialize to **camelCase strings** when sent to the frontend.

## Rule

When creating TypeScript types for C# enums:
1. **Always use camelCase** — `'running'` not `'Running'`
2. **Add the NOTE comment** at the top of the type:
   ```typescript
   // NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase)
   ```
3. **All comparisons must use camelCase** — `state.status === 'running'` not `'Running'`

## Examples (BrickBot domain)

| C# Enum Value | JSON Output | Frontend Type |
|---|---|---|
| `RunnerStatus.Idle` | `"idle"` | `'idle'` |
| `RunnerStatus.Running` | `"running"` | `'running'` |
| `CaptureMode.WinRT` | `"winRT"` | `'winRT'` |
| `CaptureMode.BitBlt` | `"bitBlt"` | `'bitBlt'` |

## Where to verify (once IpcHandler exists)

The serializer config will live in `Modules/Core/WebView/IpcHandler.cs`:
```csharp
_jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
```
