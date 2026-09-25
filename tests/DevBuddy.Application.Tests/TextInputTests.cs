using System.Text.Json;
using DevBuddy.Application.Dispatch;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The NUL rule every host applies to text before it can reach PostgreSQL (Phase 14, C4).
/// </summary>
public sealed class TextInputTests
{
    [Theory]
    [InlineData("""{"name":"plain text"}""", false)]
    [InlineData("""{"name":"a\u0000b"}""", true)]
    [InlineData("""{"scope":{"workspaceId":"w"},"items":[{"note":"fine"},{"note":"\u0000"}]}""", true)]
    [InlineData("""["one", ["two", ["three\u0000"]]]""", true)]
    [InlineData("""{"count":0,"flag":true,"missing":null}""", false)]
    [InlineData("""{"text":"\\u0000 written out as characters, which is not a NUL"}""", false)]
    public void finds_a_nul_in_any_string_however_deeply_nested(string json, bool expected)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(expected, TextInput.ContainsNul(document.RootElement));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("someone@example.test", false)]
    [InlineData("someone\0@example.test", true)]
    public void a_single_text_is_checked_the_same_way(string? text, bool expected) =>
        Assert.Equal(expected, TextInput.ContainsNul(text));

    [Fact]
    public void the_refusal_names_the_rule_and_carries_none_of_the_text() =>
        Assert.Equal("Text may not contain a NUL character (U+0000).", TextInput.NulRefusal);
}
