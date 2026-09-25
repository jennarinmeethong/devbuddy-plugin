using System.Text.Json;

namespace DevBuddy.Application.Dispatch;

/// <summary>
/// The one rule about text every host applies before anything reaches storage: no NUL character.
/// <para>
/// PostgreSQL cannot store U+0000 in text, and refuses the whole statement when a parameter holds
/// one. Nothing refused it earlier, so a NUL anywhere in a caller's text became an unhandled
/// exception and a 500 — ZAP's first API scan found it on account recovery (Phase 14, C4). No
/// legitimate text here needs the character, so it is refused as a malformed request, before a
/// query runs, with the same answer whatever else the request says.
/// </para>
/// <para>
/// Checked here rather than by a JSON converter for <see cref="string"/>. A custom converter makes
/// the schema exporter describe every string field as "anything", which would strip the types
/// from the MCP tool schemas and the generated client.
/// </para>
/// </summary>
public static class TextInput
{
    /// <summary>What a caller is told. It names the rule, never the text that broke it.</summary>
    public const string NulRefusal = "Text may not contain a NUL character (U+0000).";

    public static bool ContainsNul(string? text) =>
        text is not null && text.Contains('\0', StringComparison.Ordinal);

    /// <summary>Every string value anywhere in the arguments, however deeply nested.</summary>
    public static bool ContainsNul(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => ContainsNul(element.GetString()),
        JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsNul(item)),
        JsonValueKind.Object => element.EnumerateObject().Any(property => ContainsNul(property.Value)),
        _ => false,
    };
}
