using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LicenseHub.Application.Licensing;

public sealed class TermWriteJsonConverter : JsonConverter<TermWrite>
{
    public override TermWrite Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
            throw new JsonException("Vigencia inválida.");
        var name = kind.GetString();
        var allowed = name switch { "Duration" => new[] { "kind", "duration" }, "Until" => ["kind", "until"], "Lifetime" => ["kind"], _ => throw new JsonException("Vigencia inválida.") };
        var properties = value.EnumerateObject().Select(x => x.Name).ToArray();
        if (properties.Length != allowed.Length || properties.Distinct().Count() != allowed.Length || properties.Except(allowed).Any())
            throw new JsonException("Alternativas de vigencia exclusivas.");
        if (name == "Lifetime") return new(name);
        if (name == "Duration") return new(name, value.GetProperty("duration").Deserialize<DurationWrite>(options) ?? throw new JsonException("Duración requerida."));
        var until = value.GetProperty("until");
        if (until.ValueKind != JsonValueKind.String || !until.GetString()!.EndsWith('Z') || !until.TryGetDateTimeOffset(out var date))
            throw new JsonException("Fecha UTC requerida.");
        return new(name!, null, date);
    }

    public override void Write(Utf8JsonWriter writer, TermWrite value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        // Include contradictory fields from internal callers so distinct invalid requests cannot hash identically.
        if (value.Duration is not null) { writer.WritePropertyName("duration"); JsonSerializer.Serialize(writer, value.Duration, options); }
        if (value.Until is not null) writer.WriteString("until", value.Until.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }
}
