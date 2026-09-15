using DevBuddy.Application.Abstractions;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Which recorded knowledge a change bears on. The fake change set touches
/// <c>src/importer.cs</c>; what the working copy says about it is covered in
/// <c>ChangeImpactAnalysisTests</c>, and the whole path over real storage in Security.Tests.
/// </summary>
public sealed class ChangeImpactTests
{
    private const string Changed = "src/importer.cs";

    [Fact]
    public async Task a_published_record_citing_a_changed_path_at_a_ref_is_reported()
    {
        var harness = new Harness();
        KnowledgeRecord record = Published(Locator($"{Changed}@v0.2.0"));
        harness.Ports.Records.Add(record);

        AnalysisObservation cited = Assert.Single(await CitationsAsync(harness));

        Assert.Equal(Changed, cited.SourceLocator);
        Assert.Contains(record.Id.ToString(), cited.Detail, StringComparison.Ordinal);
        Assert.Contains("provenance", cited.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_path_named_in_front_matter_is_a_citation()
    {
        var harness = new Harness();
        KnowledgeRecord record = Published(
            Locator("handover/2026-09-01"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["paths"] = $"docs/decision.md, ./{Changed}#L12",
            });
        harness.Ports.Records.Add(record);

        AnalysisObservation cited = Assert.Single(await CitationsAsync(harness));

        Assert.Contains("front matter 'paths'", cited.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("provenance", cited.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_draft_citing_a_changed_path_is_not_reported()
    {
        var harness = new Harness();
        harness.Ports.Records.Add(Draft(Locator(Changed)));

        Assert.Empty(await CitationsAsync(harness));
    }

    [Fact]
    public async Task only_the_published_revision_decides_what_a_record_cites()
    {
        var harness = new Harness();

        // Published citing nothing, then a draft on top that cites the path: not knowledge yet.
        KnowledgeRecord startsCiting = Published(Locator("handover/2026-09-01"));
        startsCiting.AddRevision(
            "Why the import runs first", "Revised.", null, Locator(Changed), TestData.Now.AddMinutes(4), TestData.Author);

        // Published citing the path, then a draft on top that stops: still live until replaced.
        KnowledgeRecord stopsCiting = Published(Locator(Changed));
        stopsCiting.AddRevision(
            "Why the import runs first", "Revised.", null, Locator("handover/2026-09-02"), TestData.Now.AddMinutes(4), TestData.Author);

        harness.Ports.Records.Add(startsCiting);
        harness.Ports.Records.Add(stopsCiting);

        AnalysisObservation cited = Assert.Single(await CitationsAsync(harness));

        Assert.Contains(stopsCiting.Id.ToString(), cited.Detail, StringComparison.Ordinal);
        Assert.Contains("published revision 1", cited.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task an_archived_record_is_not_reported()
    {
        var harness = new Harness();
        KnowledgeRecord record = Published(Locator(Changed));
        record.Archive(TestData.Now.AddMinutes(10));
        harness.Ports.Records.Add(record);

        Assert.Empty(await CitationsAsync(harness));
    }

    [Theory]
    [InlineData("src/importer.cs.bak")]
    [InlineData("lib/src/importer.cs")]
    [InlineData("src/Importer.cs")]
    public async Task a_different_file_whose_name_resembles_the_path_is_not_a_citation(string locator)
    {
        var harness = new Harness();
        harness.Ports.Records.Add(Published(Locator(locator)));

        Assert.Empty(await CitationsAsync(harness));
    }

    [Fact]
    public async Task the_record_is_identified_and_its_content_is_not_served()
    {
        var harness = new Harness();
        harness.Ports.Records.Add(Published(Locator(Changed)));

        AnalysisObservation cited = Assert.Single(await CitationsAsync(harness));

        Assert.DoesNotContain("Why the import runs first", cited.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("normalises", cited.Detail, StringComparison.Ordinal);
    }

    private static async Task<AnalysisObservation[]> CitationsAsync(Harness harness)
    {
        ChangeImpactResponse response = await harness.SucceedAsync(
            new AnalyzeChangeImpactUseCase(harness.Ports, harness.Ports, harness.Ports),
            new AnalyzeChangeImpactRequest(TestData.Scope, TestData.Repository, "abc123"));

        return [.. response.Impact.Where(observation => observation.Subject == "knowledge-record")];
    }

    private static Provenance Locator(string sourceLocator) =>
        new(ProvenanceSourceKind.HumanAuthored, sourceLocator, "Jennarin", TestData.Now);

    private static KnowledgeRecord Draft(
        Provenance provenance, IReadOnlyDictionary<string, string>? frontMatter = null) =>
        KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), TestData.Scope, TestData.WorkItem, RecordKind.Decision,
            "Why the import runs first", "The importer normalises first.", frontMatter, provenance,
            TestData.Now, TestData.Author);

    private static KnowledgeRecord Published(
        Provenance provenance, IReadOnlyDictionary<string, string>? frontMatter = null)
    {
        KnowledgeRecord record = Draft(provenance, frontMatter);
        record.SubmitForApproval(TestData.Now.AddMinutes(1));
        record.Approve(TestData.Reviewer, record.CurrentRevision.ContentHash, TestData.Now.AddMinutes(2));
        record.Publish(TestData.Now.AddMinutes(3));
        return record;
    }
}
