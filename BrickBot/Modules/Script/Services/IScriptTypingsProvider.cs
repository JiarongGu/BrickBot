namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Serves BrickBot's <c>brickbot.d.ts</c> host typings to the frontend so Monaco can
/// give users autocomplete + type checking against the script host API. The .d.ts is
/// embedded as a resource at build time (see <c>Modules/Script/Resources/brickbot.d.ts</c>);
/// keep it in sync with <see cref="HostApi"/> + <see cref="StdLib"/>.
/// </summary>
public interface IScriptTypingsProvider
{
    /// <summary>Returns the global <c>brickbot.d.ts</c> contents (UTF-8 text).</summary>
    string GetGlobalTypings();
}
