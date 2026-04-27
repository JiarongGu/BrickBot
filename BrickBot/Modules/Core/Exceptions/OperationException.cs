using System.Text.Json;

namespace BrickBot.Modules.Core.Exceptions;

public class OperationException : Exception
{
    public string Code { get; }
    public Dictionary<string, string>? Parameters { get; }

    public OperationException(
        string code,
        Dictionary<string, string>? parameters = null,
        string? message = null,
        Exception? innerException = null)
        : base(message ?? code, innerException)
    {
        Code = code;
        Parameters = parameters;
    }

    public string GetStructuredMessage()
    {
        return JsonSerializer.Serialize(new { Code, Parameters });
    }
}
