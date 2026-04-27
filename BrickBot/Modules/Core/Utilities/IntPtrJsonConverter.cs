using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrickBot.Modules.Core.Utilities;

/// <summary>
/// Serializes <see cref="IntPtr"/> / <c>nint</c> as a JSON number (Int64).
/// HWND and similar Win32 handles fit comfortably in JavaScript's 53-bit safe-integer range.
/// Reads accept either a number or a numeric string for forward-compat.
/// </summary>
public sealed class IntPtrJsonConverter : JsonConverter<IntPtr>
{
    public override IntPtr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return new IntPtr(reader.GetInt64());
            case JsonTokenType.String:
                var s = reader.GetString();
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                {
                    return new IntPtr(v);
                }
                return IntPtr.Zero;
            case JsonTokenType.Null:
                return IntPtr.Zero;
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for IntPtr");
        }
    }

    public override void Write(Utf8JsonWriter writer, IntPtr value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToInt64());
    }
}
