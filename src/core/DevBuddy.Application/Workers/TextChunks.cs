namespace DevBuddy.Application.Workers;

/// <summary>
/// Splits a record's text into overlapping pieces a model can embed whole (Phase 13, D9).
/// <para>
/// A cut lands on a paragraph break where one falls in the second half of the window, then on a
/// line break, then on a space, and only then mid-word. Thai is written without spaces between
/// words, so a Thai paragraph with no line break is cut by length. Consecutive chunks overlap by a
/// tenth of the window, so a sentence cut in two is still whole in one of them.
/// </para>
/// </summary>
public static class TextChunks
{
    public static IReadOnlyList<string> Split(string text, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 2);

        if (text.Length <= maxCharacters)
        {
            return [text];
        }

        int overlap = maxCharacters / 10;
        List<string> chunks = [];
        int start = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + maxCharacters, text.Length);

            if (end < text.Length)
            {
                end = CutPoint(text, start, end);
            }

            chunks.Add(text[start..end]);

            if (end >= text.Length)
            {
                break;
            }

            // Always forward, however the overlap and the cut point fall.
            start = Math.Max(end - overlap, start + 1);
        }

        return chunks;
    }

    private static int CutPoint(string text, int start, int end)
    {
        int earliest = start + ((end - start) / 2);

        foreach (string separator in (string[])["\n\n", "\n", " "])
        {
            int found = text.LastIndexOf(separator, end - 1, end - earliest, StringComparison.Ordinal);

            if (found > earliest)
            {
                return found + separator.Length;
            }
        }

        return end;
    }
}
