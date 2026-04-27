# Testing Guide

> After every bug fix or new feature, write tests.

## Stack

- **xunit.v3** — runner
- **FluentAssertions** — `result.Should().Be(...)`
- **Moq** — interface mocks
- **Microsoft.Extensions.TimeProvider.Testing** — virtual time for capture/runner timing tests

## File layout

```
BrickBot.Tests/
├── Modules/
│   ├── Capture/
│   │   └── CaptureServiceTests.cs
│   ├── Vision/
│   ├── Script/
│   └── ...
└── Infrastructure/
    └── ApplicationHostTests.cs
```

Mirrors the source folder structure.

## Naming

`<ClassUnderTest>Tests.cs` containing methods named `MethodName_Scenario_Expected`.

```csharp
public class TemplateMatcherTests
{
    [Fact]
    public async Task Find_WhenTemplateMissing_ReturnsNull()
    {
        // Arrange
        var matcher = new TemplateMatcher(...);

        // Act
        var result = await matcher.FindAsync(haystack, missingTemplate);

        // Assert
        result.Should().BeNull();
    }
}
```

## Priorities (test these first)

1. **Pure logic** — vision matching thresholds, coordinate translations, script step interpretation.
2. **Service business rules** — Runner state machine (idle → running → paused → stopped), Profile validation.
3. **IPC round-trip** — facade routes the right service method, payload extraction, response shape.
4. **JS sandbox** — that denied APIs throw, that allowed APIs work, that cancellation interrupts a long script.

## What NOT to test

- WinRT `GraphicsCaptureSession` interactions (no headless test surface — verify manually).
- Win32 `SendInput` calls (verify manually with a test target window).
- WebView2 message passing (covered by manual smoke testing).

## Running tests

```sh
dotnet test BrickBot.slnx
```
