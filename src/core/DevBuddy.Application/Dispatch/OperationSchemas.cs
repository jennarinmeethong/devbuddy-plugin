using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Dispatch;

/// <summary>
/// JSON schemas for what an operation takes and returns, generated from the records themselves.
/// <para>
/// Generated rather than hand-written, so a schema cannot describe a shape the code does not
/// accept. Every host that needs to describe an operation uses this one implementation: the MCP
/// tool list, the HTTP operation listing, and the generated web client. Three descriptions of the
/// same argument record would eventually disagree, and the one that disagreed would be whichever
/// nobody was looking at.
/// </para>
/// </summary>
public static class OperationSchemas
{
    private static readonly JsonSchemaExporterOptions Exporting = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = Describe,
    };

    /// <summary>
    /// The schema for one type.
    /// <para>
    /// A type the exporter cannot express falls back to a permissive object rather than throwing.
    /// A loose schema is worse than a precise one and far better than a missing operation, and
    /// the pipeline validates what actually arrives regardless of what any schema promised.
    /// </para>
    /// </summary>
    public static JsonElement For(Type type)
    {
        try
        {
            JsonNode schema = JsonConventions.Options.GetJsonSchemaAsNode(type, Exporting);
            Repair(schema, JsonConventions.Options.GetTypeInfo(type));

            return JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString());
        }
        catch (NotSupportedException)
        {
            return JsonSerializer.Deserialize<JsonElement>("""{"type":"object"}""");
        }
    }

    /// <summary>
    /// Puts back the type information the exporter drops from optional properties.
    /// <para>
    /// An optional parameter — one with a default in the constructor — takes a path through the
    /// exporter that skips the node transform entirely, so a property whose type needs that
    /// transform comes out as <c>{"default": null}</c> and nothing else. It is not a hypothetical:
    /// it silently untyped every optional identifier and every optional list of enumerations,
    /// which between them are most of the filters in the read operations.
    /// </para>
    /// <para>
    /// The repair regenerates the schema for that property's declared type on its own and merges
    /// what the exporter did manage to say back into it. Regenerating rather than patching by hand
    /// means it stays right when the property type changes.
    /// </para>
    /// </summary>
    private static void Repair(JsonNode? node, JsonTypeInfo info)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (JsonPropertyInfo property in info.Properties)
            {
                if (properties[property.Name] is not JsonObject child)
                {
                    continue;
                }

                if (Describes(child))
                {
                    Repair(child, JsonConventions.Options.GetTypeInfo(property.PropertyType));
                    continue;
                }

                if (Regenerate(property.PropertyType) is not JsonObject replacement)
                {
                    continue;
                }

                // Everything the exporter did say about the property — a default, a description —
                // is kept. Only the missing type information is filled in.
                foreach (KeyValuePair<string, JsonNode?> carried in child.ToArray())
                {
                    child.Remove(carried.Key);
                    replacement[carried.Key] = carried.Value;
                }

                properties[property.Name] = replacement;
            }
        }

        if (schema["items"] is JsonObject items && info.ElementType is { } element)
        {
            Repair(items, JsonConventions.Options.GetTypeInfo(element));
        }
    }

    /// <summary>Whether a node says anything at all about what shape it is.</summary>
    private static bool Describes(JsonObject node) =>
        node.ContainsKey("type")
        || node.ContainsKey("enum")
        || node.ContainsKey("$ref")
        || node.ContainsKey("anyOf");

    private static JsonObject? Regenerate(Type type)
    {
        try
        {
            JsonNode schema = JsonConventions.Options.GetJsonSchemaAsNode(type, Exporting);

            // A type that needs definitions of its own cannot be spliced in alone. Leaving the
            // node untyped is honest; splicing half of it would not be.
            return schema is JsonObject node && !node.ContainsKey("$defs") ? node : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fills in the shapes the exporter cannot see.
    /// <para>
    /// A type behind a custom converter is opaque to schema generation: the exporter knows the
    /// converter exists and nothing about what it writes, so it emits "any value". These two are
    /// the ones that matter, because between them they appear in nearly every operation, and "any
    /// value" for a project scope would make a generated client typeless exactly where it counts.
    /// </para>
    /// </summary>
    private static JsonNode Describe(JsonSchemaExporterContext context, JsonNode schema)
    {
        Type type = context.TypeInfo.Type;

        // An optional identifier arrives as Nullable<T>, and the exporter is just as blind to it.
        Type? optional = Nullable.GetUnderlyingType(type);

        if (optional is not null && JsonConventions.IsGuidIdentifier(optional))
        {
            return new JsonObject
            {
                ["type"] = new JsonArray("string", "null"),
                ["format"] = "uuid",
            };
        }

        if (JsonConventions.IsGuidIdentifier(type))
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
            };
        }

        if (type == typeof(ProjectScope))
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["workspaceId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
                    ["projectId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
                },
                ["required"] = new JsonArray("workspaceId", "projectId"),
            };
        }

        return schema;
    }
}
