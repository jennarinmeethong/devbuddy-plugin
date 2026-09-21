using DevBuddy.Application.Workers;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The splitter behind chunked embeddings (Phase 13, D9). What matters is that no part of a long
/// record is left out, that no chunk exceeds the window, and that a cut prefers a boundary.
/// </summary>
public sealed class TextChunksTests
{
    [Fact]
    public void a_short_text_is_one_chunk()
    {
        Assert.Equal(["Short."], TextChunks.Split("Short.", 3000));
    }

    [Fact]
    public void a_long_text_is_covered_end_to_end_in_chunks_no_longer_than_the_window()
    {
        string text = string.Join("\n\n", Enumerable.Range(1, 400).Select(i => $"Paragraph {i} says something about the importer."));

        IReadOnlyList<string> chunks = TextChunks.Split(text, 500);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 500, $"{chunk.Length} characters"));
        Assert.StartsWith(chunks[0], text, StringComparison.Ordinal);
        Assert.EndsWith(chunks[^1], text, StringComparison.Ordinal);

        // Every paragraph is whole in at least one chunk, because cuts land on paragraph breaks.
        for (int i = 1; i <= 400; i++)
        {
            string paragraph = $"Paragraph {i} says something about the importer.";
            Assert.Contains(chunks, chunk => chunk.Contains(paragraph, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void text_with_no_spaces_is_cut_by_length_and_nothing_is_lost()
    {
        // Thai is written without spaces between words.
        // Numbered so no stretch of it repeats, which lets each chunk be found at exactly one place.
        string text = string.Concat(Enumerable.Range(0, 400).Select(i => $"การนำเข้าขั้นตอน{i:D4}"));

        IReadOnlyList<string> chunks = TextChunks.Split(text, 1000);

        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 1000));

        // Consecutive chunks overlap, so joining each chunk's new part rebuilds the text exactly.
        int covered = 0;
        foreach (string chunk in chunks)
        {
            int at = text.IndexOf(chunk, Math.Max(0, covered - 1000), StringComparison.Ordinal);
            Assert.True(at >= 0 && at <= covered, "a chunk must start inside what was already covered");
            covered = at + chunk.Length;
        }

        Assert.Equal(text.Length, covered);
    }
}
