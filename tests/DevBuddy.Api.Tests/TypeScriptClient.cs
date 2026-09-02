using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DevBuddy.Api.Tests;

/// <summary>
/// Turns the operation manifest the API serves into the TypeScript the web client is built on.
/// <para>
/// Generated rather than hand-written, and generated from what the server actually says rather
/// than from the C# types directly: the manifest is the contract, so anything the manifest cannot
/// express is something the client should not be promising either.
/// </para>
/// <para>
/// It lives in the test project on purpose. Generating the client needs a running application,
/// because the schemas come from the wire conventions and the dispatcher rather than from a static
/// table, and the integration tests are the one place that already has one. The generated file is
/// committed, and the test that produced it fails when the two diverge, which is what makes drift
/// a build failure rather than a surprise in a browser.
/// </para>
/// </summary>
internal static class TypeScriptClient
{
    /// <summary>Names TypeScript cannot use as a bare identifier get quoted instead.</summary>
    private static readonly char[] Safe =
        [.. "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_$"];

    public static string Render(JsonElement manifest)
    {
        var text = new StringBuilder();

        text.AppendLine("// Generated from GET /operations. Do not edit by hand.");
        text.AppendLine("//");
        text.AppendLine("// Regenerate with:");
        text.AppendLine("//   DEVBUDDY_WRITE_CLIENT=1 dotnet test tests/DevBuddy.Api.Tests -c Release \\");
        text.AppendLine("//     --filter FullyQualifiedName~GeneratedClientTests");
        text.AppendLine("//");
        text.AppendLine("// The test that writes this file also compares it. A server whose operations or");
        text.AppendLine("// argument shapes changed without the client being regenerated fails that test.");
        text.AppendLine();

        List<JsonElement> operations = [.. manifest.EnumerateArray()];

        RenderOperationNames(text, operations);
        RenderPermissions(text, operations);
        RenderShapes(text, operations);
        RenderMetadata(text, operations);

        return text.ToString();
    }

    private static void RenderOperationNames(StringBuilder text, List<JsonElement> operations)
    {
        text.AppendLine("/** Every operation this deployment can perform. */");
        text.AppendLine("export type OperationName =");

        foreach (JsonElement operation in operations)
        {
            text.Append("  | \"").Append(Name(operation)).AppendLine("\"");
        }

        text.AppendLine("  ;");
        text.AppendLine();
    }

    private static void RenderPermissions(StringBuilder text, List<JsonElement> operations)
    {
        string[] permissions =
        [
            .. operations
                .Select(operation => operation.GetProperty("permission").GetString()!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        ];

        text.AppendLine("/** Permission names, as /me reports the ones a caller holds. */");
        text.AppendLine("export type PermissionName =");

        foreach (string permission in permissions)
        {
            text.Append("  | \"").Append(permission).AppendLine("\"");
        }

        text.AppendLine("  ;");
        text.AppendLine();
    }

    /// <summary>
    /// One argument type and one result type per operation, named after it.
    /// <para>
    /// Emitted inline rather than as shared named types even where two operations happen to take
    /// the same record. Sharing would be tidier and would also mean an operation silently
    /// changing shape when an unrelated one was edited.
    /// </para>
    /// </summary>
    private static void RenderShapes(StringBuilder text, List<JsonElement> operations)
    {
        foreach (JsonElement operation in operations)
        {
            string name = Name(operation);
            string type = TypeNameFor(name);

            text.Append("export type ").Append(type).Append("Arguments = ")
                .Append(Emit(operation.GetProperty("argumentsSchema"), operation.GetProperty("argumentsSchema"), 0))
                .AppendLine(";");
            text.AppendLine();

            text.Append("export type ").Append(type).Append("Result = ")
                .Append(Emit(operation.GetProperty("resultSchema"), operation.GetProperty("resultSchema"), 0))
                .AppendLine(";");
            text.AppendLine();
        }

        text.AppendLine("/** Argument and result types, keyed by operation name. */");
        text.AppendLine("export interface Operations {");

        foreach (JsonElement operation in operations)
        {
            string name = Name(operation);
            string type = TypeNameFor(name);

            text.Append("  \"").Append(name).Append("\": { arguments: ").Append(type)
                .Append("Arguments; result: ").Append(type).AppendLine("Result };");
        }

        text.AppendLine("}");
        text.AppendLine();
    }

    private static void RenderMetadata(StringBuilder text, List<JsonElement> operations)
    {
        text.AppendLine("/** What each operation needs, and whether the AI surface may reach it. */");
        text.AppendLine(
            "export const OPERATIONS: Record<OperationName, "
            + "{ permission: PermissionName; availableToAi: boolean }> = {");

        foreach (JsonElement operation in operations)
        {
            text.Append("  \"").Append(Name(operation)).Append("\": { permission: \"")
                .Append(operation.GetProperty("permission").GetString())
                .Append("\", availableToAi: ")
                .Append(operation.GetProperty("availableToAi").GetBoolean() ? "true" : "false")
                .AppendLine(" },");
        }

        text.AppendLine("};");
    }

    private static string Name(JsonElement operation) => operation.GetProperty("name").GetString()!;

    /// <summary>snake_case on the wire, PascalCase in TypeScript.</summary>
    private static string TypeNameFor(string operation) =>
        string.Concat(operation.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>
    /// One JSON schema node as a TypeScript type.
    /// <para>
    /// Deliberately narrow. It covers the shapes the exporter actually produces for these records
    /// — objects, arrays, enumerations, references into <c>$defs</c>, and the two-branch unions it
    /// uses for nullables — and emits <c>unknown</c> for anything else. <c>unknown</c> is a
    /// failure a compiler points at; a wrong guess is one nobody sees.
    /// </para>
    /// </summary>
    private static string Emit(JsonElement node, JsonElement root, int depth)
    {
        // A schema of `true` means any value. The exporter emits it for types behind a custom
        // converter, which here means the identifier structs: strings on the wire.
        if (node.ValueKind is JsonValueKind.True)
        {
            return "unknown";
        }

        if (node.ValueKind is JsonValueKind.False)
        {
            return "never";
        }

        if (node.ValueKind is not JsonValueKind.Object)
        {
            return "unknown";
        }

        if (depth > 12)
        {
            // A record that refers to itself. Stopping is better than not stopping.
            return "unknown";
        }

        if (node.TryGetProperty("$ref", out JsonElement reference))
        {
            return Emit(Resolve(reference.GetString()!, root), root, depth + 1);
        }

        if (node.TryGetProperty("enum", out JsonElement enumeration))
        {
            string[] members =
            [
                .. enumeration.EnumerateArray()
                    .Select(member => member.ValueKind == JsonValueKind.String
                        ? $"\"{member.GetString()}\""
                        : member.ToString())
            ];

            return members.Length == 0 ? "never" : string.Join(" | ", members);
        }

        if (node.TryGetProperty("anyOf", out JsonElement anyOf))
        {
            string[] branches =
            [
                .. anyOf.EnumerateArray().Select(branch => Emit(branch, root, depth + 1)).Distinct(StringComparer.Ordinal)
            ];

            return branches.Length == 0 ? "unknown" : string.Join(" | ", branches);
        }

        if (!node.TryGetProperty("type", out JsonElement kind))
        {
            return "unknown";
        }

        // The exporter writes a nullable as ["object", "null"] rather than as a union.
        if (kind.ValueKind == JsonValueKind.Array)
        {
            string[] alternatives =
            [
                .. kind.EnumerateArray()
                    .Select(entry => entry.GetString()!)
                    .Select(entry => string.Equals(entry, "null", StringComparison.Ordinal)
                        ? "null"
                        : EmitScalar(entry, node, root, depth))
            ];

            return string.Join(" | ", alternatives.Distinct(StringComparer.Ordinal));
        }

        return EmitScalar(kind.GetString()!, node, root, depth);
    }

    private static string EmitScalar(string kind, JsonElement node, JsonElement root, int depth) => kind switch
    {
        "string" => "string",
        "integer" or "number" => "number",
        "boolean" => "boolean",
        "null" => "null",
        "array" => node.TryGetProperty("items", out JsonElement items)
            ? $"Array<{Emit(items, root, depth + 1)}>"
            : "unknown[]",
        "object" => EmitObject(node, root, depth),
        _ => "unknown",
    };

    private static string EmitObject(JsonElement node, JsonElement root, int depth)
    {
        if (!node.TryGetProperty("properties", out JsonElement properties))
        {
            // A dictionary, or an object the exporter could say nothing about.
            return node.TryGetProperty("additionalProperties", out JsonElement additional)
                ? $"Record<string, {Emit(additional, root, depth + 1)}>"
                : "Record<string, unknown>";
        }

        HashSet<string> required = node.TryGetProperty("required", out JsonElement names)
            ? [.. names.EnumerateArray().Select(entry => entry.GetString()!)]
            : [];

        var indent = new string(' ', (depth + 1) * 2);
        var closing = new string(' ', depth * 2);
        var text = new StringBuilder("{\n");

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            text.Append(indent)
                .Append(Identifier(property.Name))
                .Append(required.Contains(property.Name) ? ": " : "?: ")
                .Append(Emit(property.Value, root, depth + 1))
                .Append(";\n");
        }

        text.Append(closing).Append('}');
        return text.ToString();
    }

    private static string Identifier(string name) =>
        name.Length > 0 && !char.IsAsciiDigit(name[0]) && name.All(Safe.Contains)
            ? name
            : string.Create(CultureInfo.InvariantCulture, $"\"{name}\"");

    /// <summary>Follows a local <c>#/$defs/Name</c> reference. Anything else is not resolvable here.</summary>
    private static JsonElement Resolve(string reference, JsonElement root)
    {
        const string Prefix = "#/$defs/";

        if (!reference.StartsWith(Prefix, StringComparison.Ordinal)
            || !root.TryGetProperty("$defs", out JsonElement definitions)
            || !definitions.TryGetProperty(reference[Prefix.Length..], out JsonElement target))
        {
            return JsonSerializer.Deserialize<JsonElement>("true");
        }

        return target;
    }
}
