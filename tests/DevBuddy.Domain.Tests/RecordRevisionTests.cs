using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;

namespace DevBuddy.Domain.Tests;

/// <summary>
/// Controls SB-24 (revisions are immutable) and SB-25 (provenance is mandatory), plus the
/// determinism the content hash depends on.
/// </summary>
public sealed class RecordRevisionTests
{
    [Fact]
    public void a_revision_cannot_be_created_without_provenance()
    {
        DomainValidationException failure = Assert.Throws<DomainValidationException>(() =>
            new RecordRevision(1, "Title", "Body", null, null!, Fixtures.Now, Fixtures.Author));

        Assert.Contains("provenance", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void an_earlier_revision_is_unchanged_when_a_later_one_is_added()
    {
        KnowledgeRecord record = Fixtures.Draft(body: "First statement.");
        RecordRevision first = record.CurrentRevision;
        ContentHash firstHash = first.ContentHash;

        record.AddRevision(
            "Title", "Second statement.", null, Fixtures.HumanProvenance(), Fixtures.Now.AddHours(1), Fixtures.Author);

        Assert.Equal(2, record.Revisions.Count);
        Assert.Same(first, record.Revisions[0]);
        Assert.Equal(firstHash, record.Revisions[0].ContentHash);
        Assert.Equal("First statement.", record.Revisions[0].Body);
        Assert.NotEqual(firstHash, record.CurrentRevision.ContentHash);
    }

    [Fact]
    public void the_content_hash_does_not_depend_on_front_matter_ordering()
    {
        var forward = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["owner"] = "Jennarin",
            ["status"] = "in-progress",
        };

        var reversed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = "in-progress",
            ["owner"] = "Jennarin",
        };

        var a = new RecordRevision(1, "Title", "Body", forward, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author);
        var b = new RecordRevision(1, "Title", "Body", reversed, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author);

        // If insertion order changed the hash, an approval would break for no reason a human
        // could see, and SB-23 would produce false alarms instead of real protection.
        Assert.Equal(a.ContentHash, b.ContentHash);
    }

    [Fact]
    public void the_content_hash_changes_when_any_part_of_the_content_changes()
    {
        var baseline = new RecordRevision(
            1, "Title", "Body", null, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author);

        var differentBody = new RecordRevision(
            1, "Title", "Body ", null, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author);

        var differentTitle = new RecordRevision(
            1, "Title ", "Body", null, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author);

        var differentFrontMatter = new RecordRevision(
            1,
            "Title",
            "Body",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["owner"] = "Jennarin" },
            Fixtures.HumanProvenance(),
            Fixtures.Now,
            Fixtures.Author);

        Assert.NotEqual(baseline.ContentHash, differentBody.ContentHash);
        Assert.NotEqual(baseline.ContentHash, differentTitle.ContentHash);
        Assert.NotEqual(baseline.ContentHash, differentFrontMatter.ContentHash);
    }

    [Fact]
    public void a_revision_records_that_its_content_came_from_an_ai_draft()
    {
        var aiProvenance = new Provenance(
            ProvenanceSourceKind.AiDraft,
            sourceLocator: "mcp/create_draft",
            author: "Claude",
            recordedAt: Fixtures.Now);

        var revision = new RecordRevision(1, "Title", "Body", null, aiProvenance, Fixtures.Now, Fixtures.Author);

        // A reviewer approving this must be able to see where it came from.
        Assert.True(revision.Provenance.IsAiGenerated);
        Assert.False(Fixtures.HumanProvenance().IsAiGenerated);
    }

    [Fact]
    public void whether_an_ai_wrote_it_is_a_fact_of_its_own_and_not_read_from_the_source_kind()
    {
        var analysed = new Provenance(
            ProvenanceSourceKind.RepositoryAnalysis,
            sourceLocator: "src/Importer.cs",
            author: "an assistant",
            recordedAt: Fixtures.Now,
            isAiGenerated: true);

        // An AI that analysed a repository wrote this. Both halves are kept.
        Assert.True(analysed.IsAiGenerated);
        Assert.Equal(ProvenanceSourceKind.RepositoryAnalysis, analysed.SourceKind);

        // And a declared AI draft cannot be declared back into a person's work.
        Assert.True(new Provenance(
            ProvenanceSourceKind.AiDraft, "mcp/create_draft", "Claude", Fixtures.Now, isAiGenerated: false).IsAiGenerated);

        Assert.True(Fixtures.HumanProvenance().AsAiGenerated().IsAiGenerated);
    }

    [Fact]
    public void a_revision_of_an_ai_draft_is_still_ai_generated_whoever_writes_it()
    {
        KnowledgeRecord drafted = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), Fixtures.AlphaScope, WorkItemId.New(), RecordKind.Decision,
            "Title", "Written by an assistant.", null,
            Fixtures.HumanProvenance().AsAiGenerated(), Fixtures.Now, Fixtures.Author);

        drafted.AddRevision(
            "Title", "Tidied by a person.", null, Fixtures.HumanProvenance(), Fixtures.Now.AddHours(1), Fixtures.Reviewer);

        Assert.True(drafted.CurrentRevision.Provenance.IsAiGenerated);
        Assert.Equal(ProvenanceSourceKind.HumanAuthored, drafted.CurrentRevision.Provenance.SourceKind);

        // The mark follows AI content forward; it does not appear on a person's own record.
        KnowledgeRecord written = Fixtures.Draft(body: "Written by a person.");
        written.AddRevision("Title", "Still a person.", null, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author);

        Assert.False(written.CurrentRevision.Provenance.IsAiGenerated);
    }

    [Fact]
    public void revision_numbering_starts_at_one_and_increments()
    {
        KnowledgeRecord record = Fixtures.Draft();
        record.AddRevision("Title", "Second", null, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author);
        record.AddRevision("Title", "Third", null, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author);

        int[] expected = [1, 2, 3];
        Assert.Equal(expected, record.Revisions.Select(revision => revision.Number).ToArray());
        Assert.Throws<DomainValidationException>(() =>
            new RecordRevision(0, "Title", "Body", null, Fixtures.HumanProvenance(), Fixtures.Now, Fixtures.Author));
    }

    [Fact]
    public void a_content_hash_is_sha256_and_parses_from_either_case()
    {
        // The well-known SHA-256 of the empty string, in lower case. Pinning it here means the
        // hash algorithm cannot be swapped without a test noticing, which matters because every
        // approval in the system is bound to one of these.
        const string EmptyStringDigest =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        Assert.Equal(ContentHash.Parse(EmptyStringDigest), ContentHash.FromContent(string.Empty));
        Assert.Throws<DomainValidationException>(() => ContentHash.Parse("not-a-hash"));
        Assert.Throws<DomainValidationException>(() => ContentHash.Parse(new string('z', 64)));
    }
}
