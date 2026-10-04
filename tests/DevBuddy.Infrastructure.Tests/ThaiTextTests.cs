using DevBuddy.Infrastructure.Persistence.Search;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The parts of the Thai substring search that need no database. <see cref="SearchTests"/> holds
/// the ones that do.
/// </summary>
public sealed class ThaiTextTests
{
    [Theory]
    [InlineData("ความหมาย", true)]
    [InlineData("search ค้นหา", true)]
    [InlineData("normalises identifiers", false)]
    [InlineData("", false)]
    public void thai_is_detected_by_its_unicode_block(string query, bool expected) =>
        Assert.Equal(expected, ThaiText.IsPresentIn(query));

    [Fact]
    public void a_pattern_escapes_what_like_would_read_as_a_wildcard() =>
        Assert.Equal(@"%10\%\_a\\b%", ThaiText.ContainsPattern(@"10%_a\b"));

    [Fact]
    public void terms_are_split_at_whitespace_deduplicated_and_capped()
    {
        string query = string.Join(' ', Enumerable.Range(0, 12).Select(n => $"คำ{n}")) + " คำ0";

        string[] terms = ThaiText.Terms(query);

        Assert.Equal(ThaiText.MaxTerms, terms.Length);
        Assert.Equal("คำ0", terms[0]);
        Assert.Equal(terms.Length, terms.Distinct().Count());
    }

    [Fact]
    public void a_term_in_the_title_ranks_above_one_only_in_the_body()
    {
        string[] terms = ["ติดตั้ง"];

        Assert.Equal(1f, ThaiText.Rank(terms, "การติดตั้งระบบ", "Steps."));
        Assert.Equal(0.5f, ThaiText.Rank(terms, "Body only", "ขั้นตอนการติดตั้งระบบ"));
    }
}
