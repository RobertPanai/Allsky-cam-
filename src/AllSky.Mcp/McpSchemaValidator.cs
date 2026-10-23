using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllSky.Mcp;

/// <summary>
/// Validazione degli argomenti dei tool contro il loro <c>inputSchema</c>. Copre solo il sottoinsieme usato dai tool di
/// questo server: <c>type</c> (object, string, integer, number, boolean), <c>properties</c>, <c>required</c>,
/// <c>additionalProperties: false</c>, <c>enum</c>, <c>minimum</c>, <c>maximum</c>.
/// </summary>
public static class McpSchemaValidator
{
    /// <summary>
    /// Restituisce null se gli argomenti rispettano lo schema, altrimenti un messaggio in italiano per il client.
    /// </summary>
    /// <param name="schema">Schema del tool (oggetto JSON).</param>
    /// <param name="arguments">Argomenti ricevuti, o null se assenti (equivalgono a un oggetto vuoto).</param>
    public static string? Validate(JsonNode schema, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (arguments is { } args && args.ValueKind != JsonValueKind.Object)
        {
            return "Gli argomenti devono essere un oggetto JSON.";
        }

        JsonObject schemaObject = schema.AsObject();
        JsonObject properties = schemaObject["properties"]?.AsObject() ?? new JsonObject();
        bool closed = schemaObject["additionalProperties"] is JsonValue v && v.TryGetValue(out bool b) && !b;

        if (schemaObject["required"] is JsonArray required)
        {
            foreach (JsonNode? name in required)
            {
                string key = name?.GetValue<string>() ?? string.Empty;
                if (arguments is not { } present || !present.TryGetProperty(key, out _))
                {
                    return $"Manca l'argomento '{key}'.";
                }
            }
        }

        if (arguments is not { } given)
        {
            return null;
        }

        foreach (JsonProperty prop in given.EnumerateObject())
        {
            if (!properties.TryGetPropertyValue(prop.Name, out JsonNode? rule) || rule is null)
            {
                if (closed)
                {
                    return $"Argomento sconosciuto '{prop.Name}'.";
                }

                continue;
            }

            string? error = CheckProperty(prop.Name, prop.Value, rule.AsObject());
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    private static string? CheckProperty(string name, JsonElement value, JsonObject rule)
    {
        string type = rule["type"]?.GetValue<string>() ?? "any";
        bool typeOk = type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => true,
        };
        if (!typeOk)
        {
            string text = type switch
            {
                "string" => "una stringa",
                "integer" => "un numero intero",
                "number" => "un numero",
                "boolean" => "vero o falso",
                _ => "valido",
            };
            return $"L'argomento '{name}' deve essere {text}.";
        }

        if (rule["enum"] is JsonArray allowed)
        {
            string current = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
            bool found = allowed.Any(a => a?.GetValue<string>() == current);
            if (!found)
            {
                string list = string.Join(", ", allowed.Select(a => a?.GetValue<string>() ?? string.Empty));
                return $"L'argomento '{name}' deve essere uno di: {list}.";
            }
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            if (rule["minimum"] is JsonValue min && number < min.GetValue<long>())
            {
                return $"L'argomento '{name}' deve essere almeno {min.GetValue<long>()}.";
            }

            if (rule["maximum"] is JsonValue max && number > max.GetValue<long>())
            {
                return $"L'argomento '{name}' deve essere al massimo {max.GetValue<long>()}.";
            }
        }

        return null;
    }
}
