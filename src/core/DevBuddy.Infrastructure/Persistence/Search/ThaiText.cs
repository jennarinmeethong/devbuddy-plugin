using System.Text;

namespace DevBuddy.Infrastructure.Persistence.Search;

/// <summary>
/// What <see cref="PostgresSearchIndex"/> needs to search Thai by substring: whether a query holds
/// Thai, its terms, a <c>LIKE</c> pattern that matches each term literally, and a rank.
/// </summary>
internal static class ThaiText
{
    /// <summary>
    /// The escape character the patterns use. Without one, a <c>%</c> or <c>_</c> a person typed
    /// would be a wildcard.
    /// </summary>
    public const string Escape = "\\";

    /// <summary>
    /// More terms than this are ignored. Each one adds a condition to the query, and a person
    /// searching does not type more.
    /// </summary>
    public const int MaxTerms = 8;

    /// <summary>True when the text holds a character of the Thai block, U+0E00 to U+0E7F.</summary>
    public static bool IsPresentIn(string text) =>
        text.AsSpan().ContainsAnyInRange('฀', '๿');

    public static string[] Terms(string query) =>
        [.. query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)];

    public static string ContainsPattern(string term)
    {
        var pattern = new StringBuilder(term.Length + 2).Append('%');

        foreach (char character in term)
        {
            if (character is '\\' or '%' or '_')
            {
                pattern.Append('\\');
            }

            pattern.Append(character);
        }

        return pattern.Append('%').ToString();
    }

    /// <summary>
    /// A term found in the title scores two, one found only in the body scores one, divided by
    /// the most the terms could score. Every hit matched each term somewhere, so it is above zero.
    /// </summary>
    public static float Rank(IReadOnlyList<string> terms, string title, string body)
    {
        if (terms.Count == 0)
        {
            return 0f;
        }

        int score = 0;

        foreach (string term in terms)
        {
            if (title.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (body.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
        }

        return score / (2f * terms.Count);
    }
}
