using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Evidence;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;

namespace DevBuddy.Security.Tests;

/// <summary>
/// The write side of the evidence store, against a real database and a real object store.
/// <para>
/// It had none until now. <c>download_evidence</c> existed, backup and restore carried the bytes,
/// retention swept them and the isolation tests covered them — but no operation, endpoint or
/// screen ever called <c>StoreAsync</c>, so a real installation could never have had anything to
/// download. Every test that touched evidence seeded it directly in test code, which is exactly
/// why nobody noticed.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class EvidenceCaptureTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task an_artefact_can_be_captured_listed_and_read_back()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId contributor = await _fixture.CreateUserAsync($"evidence-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, contributor, Role.Contributor);

        using Session session = _fixture.OpenSession(world.Workspace);

        byte[] content = "the build log, with nothing sensitive in it"u8.ToArray();

        UseCaseResult<CaptureEvidenceResponse> captured = await session.RunAsync(
            new CaptureEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new CaptureEvidenceRequest(world.Alpha, "text/plain", "Build log for the release", content),
            World.Human(contributor));

        Assert.True(captured.IsSuccess, $"Expected success but got {captured.Outcome}: {captured.Reason}");

        // Clean rather than NotScanned, which is what makes it readable at all: the download
        // refuses anything unscanned, so a capture that skipped this would write bytes nobody
        // could ever get back.
        Assert.Equal(RedactionState.Clean, captured.Value!.RedactionState);
        Assert.Equal(content.LongLength, captured.Value.SizeBytes);

        UseCaseResult<ListEvidenceResponse> listed = await session.RunAsync(
            new ListEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new ListEvidenceRequest(world.Alpha),
            World.Human(contributor));

        EvidenceSummary summary = Assert.Single(
            listed.Value!.Evidence, item => item.EvidenceId == captured.Value.EvidenceId);

        Assert.True(summary.IsReleasable);
        Assert.Equal(contributor, summary.CapturedBy);

        UseCaseResult<EvidenceDownloadResponse> downloaded = await session.RunAsync(
            new DownloadEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new DownloadEvidenceRequest(world.Alpha, captured.Value.EvidenceId),
            World.Human(contributor));

        Assert.True(downloaded.IsSuccess, $"Expected success but got {downloaded.Outcome}: {downloaded.Reason}");

        using var read = new MemoryStream();
        await downloaded.Value!.Content.CopyToAsync(read, CancellationToken.None);

        // The bytes, not merely a row saying bytes exist. That distinction is the one the restore
        // drill's own checklist names as the failure people miss.
        Assert.Equal(content, read.ToArray());
    }

    [Fact]
    public async Task a_file_carrying_a_credential_is_refused_and_nothing_is_stored()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId contributor = await _fixture.CreateUserAsync($"evidence-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, contributor, Role.Contributor);

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<CaptureEvidenceResponse> result = await session.RunAsync(
            new CaptureEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new CaptureEvidenceRequest(
                world.Alpha,
                "text/plain",
                "A log somebody did not read first",
                "deploy finished AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE"u8.ToArray()),
            World.Human(contributor));

        // Blocked, the same answer a draft carrying a credential gets (SB-17). Evidence is the
        // material most likely to carry one by accident, which is the whole reason this path runs
        // through the pipeline rather than writing to the store directly.
        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);

        // And nothing retained, rather than stored and then deleted: the scan happens before the
        // store is touched at all.
        IReadOnlyList<EvidenceObject> stored = await session
            .Resolve<IEvidenceStore>()
            .ListForScopeAsync(world.Alpha, CancellationToken.None);

        Assert.Empty(stored);
    }

    [Fact]
    public async Task evidence_captured_in_one_project_is_not_reachable_from_another()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId contributor = await _fixture.CreateUserAsync($"evidence-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, contributor, Role.Contributor);

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<CaptureEvidenceResponse> captured = await session.RunAsync(
            new CaptureEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new CaptureEvidenceRequest(world.Alpha, "text/plain", "A log in Alpha", "alpha only"u8.ToArray()),
            World.Human(contributor));

        Assert.True(captured.IsSuccess);

        // The same identifier, asked for through the other project scope. SB-12 covered this for
        // evidence a test had seeded; it now covers evidence a person actually put there.
        UseCaseResult<EvidenceDownloadResponse> crossProject = await session.RunAsync(
            new DownloadEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new DownloadEvidenceRequest(world.Beta, captured.Value!.EvidenceId),
            World.Human(contributor));

        Assert.NotEqual(ExecutionOutcome.Succeeded, crossProject.Outcome);

        UseCaseResult<ListEvidenceResponse> listedInBeta = await session.RunAsync(
            new ListEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new ListEvidenceRequest(world.Beta),
            World.Human(contributor));

        Assert.DoesNotContain(
            listedInBeta.Value!.Evidence, item => item.EvidenceId == captured.Value.EvidenceId);
    }

    [Fact]
    public async Task a_viewer_cannot_attach_anything()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId viewer = await _fixture.CreateUserAsync($"evidence-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, viewer, Role.Viewer);

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<CaptureEvidenceResponse> result = await session.RunAsync(
            new CaptureEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new CaptureEvidenceRequest(world.Alpha, "text/plain", "Not mine to add", "x"u8.ToArray()),
            World.Human(viewer));

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task the_ai_channel_cannot_attach_anything_even_where_ai_access_is_on()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId contributor = await _fixture.CreateUserAsync($"evidence-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, contributor, Role.Contributor);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<CaptureEvidenceResponse> result = await session.RunAsync(
            new CaptureEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new CaptureEvidenceRequest(world.Alpha, "text/plain", "From an assistant", "x"u8.ToArray()),
            World.Ai(contributor));

        // Human-only, and it stays human-only with the project opted in. A channel that could push
        // bytes into the evidence store would be a way around SB-17 rather than a use of it.
        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
    }
}
