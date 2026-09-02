using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Dispatch;

/// <summary>
/// How operation arguments and results are shaped on the wire.
/// <para>
/// One set of conventions for every host. The MCP tool schema, the HTTP API, and the console all
/// speak the same JSON, so a question answered in one is answered the same way in the others, and
/// a caller moving between them does not have to relearn the shape.
/// </para>
/// </summary>
public static class JsonConventions
{
    /// <summary>
    /// Identifiers travel as plain strings rather than as <c>{"value": "..."}</c>. The wrapper is
    /// a compile-time device to stop a project identifier being passed where a workspace one
    /// belongs; leaking it into every payload would make the API tedious for no benefit.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        // Enums as names. A tool schema that says "Decision" is usable; one that says 3 is not.
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new GuidIdentifierConverterFactory());
        options.Converters.Add(new ProjectScopeConverter());

        // Set explicitly rather than left to be attached on first use. Schema export needs a
        // resolver, and an options object that only acquires one when something happens to
        // serialise first is a startup order dependency waiting to bite.
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();

        options.MakeReadOnly();
        return options;
    }
}

/// <summary>
/// Reads and writes any single-field identifier struct as a string.
/// <para>
/// Written as a factory rather than eleven near-identical converters, because eleven of them would
/// drift: one gets a fix and the others do not, and the bug shows up as an identifier that only
/// round-trips on some endpoints.
/// </para>
/// </summary>
internal sealed class GuidIdentifierConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => Describe(typeToConvert) is not null;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        (ConstructorInfo constructor, PropertyInfo value) = Describe(typeToConvert)
            ?? throw new NotSupportedException($"{typeToConvert} is not a Guid identifier.");

        return (JsonConverter)Activator.CreateInstance(
            typeof(GuidIdentifierConverter<>).MakeGenericType(typeToConvert),
            constructor,
            value)!;
    }

    /// <summary>
    /// A value type with a public <c>Guid Value</c> and a constructor taking one. Deliberately
    /// narrow: anything looser would start converting types nobody meant it to.
    /// </summary>
    private static (ConstructorInfo Constructor, PropertyInfo Value)? Describe(Type type)
    {
        if (!type.IsValueType || type.IsEnum || type.IsGenericType)
        {
            return null;
        }

        PropertyInfo? value = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

        if (value?.PropertyType != typeof(Guid))
        {
            return null;
        }

        ConstructorInfo? constructor = type.GetConstructor([typeof(Guid)]);
        return constructor is null ? null : (constructor, value);
    }
}

internal sealed class GuidIdentifierConverter<T>(ConstructorInfo constructor, PropertyInfo value)
    : JsonConverter<T>
    where T : struct
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

        if (!Guid.TryParse(text, out Guid parsed))
        {
            throw new JsonException($"{text} is not a valid {typeToConvert.Name}.");
        }

        return (T)constructor.Invoke([parsed]);
    }

    public override void Write(Utf8JsonWriter writer, T identifier, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue((Guid)value.GetValue(identifier)!);
    }
}

/// <summary>
/// A scope on the wire is <c>{"workspaceId": "...", "projectId": "..."}</c>.
/// <para>
/// Both halves are required, and the constructor refuses an empty one. A scope that could arrive
/// half-filled would be a request whose tenant boundary depends on what the caller left out.
/// </para>
/// </summary>
internal sealed class ProjectScopeConverter : JsonConverter<ProjectScope>
{
    public override ProjectScope Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A scope must be an object with workspaceId and projectId.");
        }

        Guid workspace = Guid.Empty;
        Guid project = Guid.Empty;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string? name = reader.GetString();
            reader.Read();

            if (string.Equals(name, "workspaceId", StringComparison.OrdinalIgnoreCase))
            {
                workspace = reader.GetGuid();
            }
            else if (string.Equals(name, "projectId", StringComparison.OrdinalIgnoreCase))
            {
                project = reader.GetGuid();
            }
            else
            {
                reader.Skip();
            }
        }

        return new ProjectScope(new Domain.Common.WorkspaceId(workspace), new Domain.Common.ProjectId(project));
    }

    public override void Write(Utf8JsonWriter writer, ProjectScope scope, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteString("workspaceId", scope.WorkspaceId.Value);
        writer.WriteString("projectId", scope.ProjectId.Value);
        writer.WriteEndObject();
    }
}
