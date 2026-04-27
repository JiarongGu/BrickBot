using System.Reflection;
using BrickBot.Modules.Core.Exceptions;

namespace BrickBot.Modules.Script.Services;

public sealed class ScriptTypingsProvider : IScriptTypingsProvider
{
    private const string ResourceName = "BrickBot.Modules.Script.Resources.brickbot.d.ts";
    private readonly Lazy<string> _content;

    public ScriptTypingsProvider()
    {
        _content = new Lazy<string>(LoadEmbedded);
    }

    public string GetGlobalTypings() => _content.Value;

    private static string LoadEmbedded()
    {
        var assembly = typeof(ScriptTypingsProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new OperationException("SCRIPT_TYPINGS_MISSING",
                new() { ["resource"] = ResourceName },
                $"Embedded resource not found: {ResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
