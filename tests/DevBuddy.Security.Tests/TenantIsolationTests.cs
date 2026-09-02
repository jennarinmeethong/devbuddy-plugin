using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using Microsoft.Extensions.DependencyInjection;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Phase 11 scenario 1: cross-user, cross-team, and cross-project access, over every path that
/// returns data — reading a record, searching, exporting, and downloading an attachment.
/// <para>
/// Four paths rather than one because that is where isolation usually breaks. Getting a single
/// record right is easy; the leak is in the search that joins to revisions, the export that
/// forgets a predicate, or the attachment fetched by identifier alone.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class TenantIsolationTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task a_member_of_one_project_cannot_read_a_record_in_another()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId alphaOnly = await _fixture.CreateUserAsync($"alpha-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, alphaOnly, Role.Reviewer, world.AlphaId);

        WorkItem betaItem = await _fixture.SeedWorkItemAsync(world.Beta, "CRQ-B1", world.Founder);
        KnowledgeRecord betaRecord = await _fixture.SeedPublishedRecordAsync(
            world.Beta, betaItem.Id, "Beta decision", "Only Beta may read this.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<KnowledgeRecordView> denied = await session.RunAsync(
            new GetRecordUseCase(session.Resolve<IKnowledgeRepository>()),
            new GetRecordRequest(world.Beta, betaRecord.Id),
            World.Human(alphaOnly));

        // Denied at the authorization step: the caller has a grant in this workspace, just not
        // one that covers Beta. Reviewer on Alpha is not reviewer everywhere.
        Assert.Equal(ExecutionOutcome.Denied, denied.Outcome);
        Assert.Null(denied.Value);
    }

    [Fact]
    public async Task a_user_with_no_membership_reaches_nothing()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId outsider = await _fixture.CreateUserAsync($"outsider-{Guid.NewGuid():N}@example.com");

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-A1", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Alpha decision", "Internal only.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        CallerContextFor caller = new(World.Human(outsider));

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new GetRecordUseCase(session.Resolve<IKnowledgeRepository>()),
            new GetRecordRequest(world.Alpha, record.Id), caller.Value)).Outcome);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new SearchKnowledgeUseCase(session.Resolve<ISearchIndex>()),
            new SearchKnowledgeRequest(world.Alpha, "decision"), caller.Value)).Outcome);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new ExportProjectUseCase(session.Resolve<IAdministrativeOperations>()),
            new ExportProjectRequest(world.Alpha), caller.Value)).Outcome);
    }

    [Fact]
    public async Task search_never_returns_a_record_from_another_project()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId alphaOnly = await _fixture.CreateUserAsync($"searcher-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, alphaOnly, Role.Viewer, world.AlphaId);

        WorkItem alphaItem = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-A2", world.Founder);
        WorkItem betaItem = await _fixture.SeedWorkItemAsync(world.Beta, "CRQ-B2", world.Founder);

        // The same distinctive word in both projects. This is the case that catches a search
        // which scopes records but forgets the revisions it joins to.
        await _fixture.SeedPublishedRecordAsync(
            world.Alpha, alphaItem.Id, "Alpha note", "The keyword is chrysanthemum.", world.Founder);

        await _fixture.SeedPublishedRecordAsync(
            world.Beta, betaItem.Id, "Beta note", "The keyword is chrysanthemum.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new SearchKnowledgeUseCase(session.Resolve<ISearchIndex>());

        UseCaseResult<SearchKnowledgeResponse> fromAlpha = await session.RunAsync(
            useCase, new SearchKnowledgeRequest(world.Alpha, "chrysanthemum"), World.Human(alphaOnly));

        Assert.True(fromAlpha.IsSuccess);
        Assert.Equal("Alpha note", Assert.Single(fromAlpha.Value!.Hits).Title);

        UseCaseResult<SearchKnowledgeResponse> fromBeta = await session.RunAsync(
            useCase, new SearchKnowledgeRequest(world.Beta, "chrysanthemum"), World.Human(alphaOnly));

        Assert.Equal(ExecutionOutcome.Denied, fromBeta.Outcome);
    }

    [Fact]
    public async Task an_export_covers_one_project_and_is_refused_for_another()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId alphaAdmin = await _fixture.CreateUserAsync($"admin-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, alphaAdmin, Role.Administrator, world.AlphaId);

        WorkItem alphaItem = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-A3", world.Founder);
        WorkItem betaItem = await _fixture.SeedWorkItemAsync(world.Beta, "CRQ-B3", world.Founder);

        await _fixture.SeedPublishedRecordAsync(
            world.Alpha, alphaItem.Id, "Alpha one", "First.", world.Founder);

        await _fixture.SeedPublishedRecordAsync(
            world.Beta, betaItem.Id, "Beta one", "First.", world.Founder);

        await _fixture.SeedPublishedRecordAsync(
            world.Beta, betaItem.Id, "Beta two", "Second.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new ExportProjectUseCase(session.Resolve<IAdministrativeOperations>());

        UseCaseResult<ExportManifest> alphaExport = await session.RunAsync(
            useCase, new ExportProjectRequest(world.Alpha), World.Human(alphaAdmin));

        // One record, not three. An export is the easiest place for a missing predicate to turn
        // into a bulk disclosure.
        Assert.True(alphaExport.IsSuccess);
        Assert.Equal(1, alphaExport.Value!.RecordCount);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            useCase, new ExportProjectRequest(world.Beta), World.Human(alphaAdmin))).Outcome);
    }

    [Fact]
    public async Task an_attachment_cannot_be_fetched_across_a_project_boundary()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId alphaOnly = await _fixture.CreateUserAsync($"reader-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, alphaOnly, Role.Viewer, world.AlphaId);

        EvidenceObject betaEvidence = await StoreEvidenceAsync(world.Beta, world.Founder, "beta log line");

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new DownloadEvidenceUseCase(session.Resolve<IEvidenceStore>());

        // Knowing the identifier is not access. This is the path a presigned URL would have
        // bypassed entirely, which is why there is no presigned URL.
        UseCaseResult<EvidenceDownloadResponse> denied = await session.RunAsync(
            useCase, new DownloadEvidenceRequest(world.Beta, betaEvidence.Id), World.Human(alphaOnly));

        Assert.Equal(ExecutionOutcome.Denied, denied.Outcome);

        // Even claiming it belongs to a project the caller can read does not produce it.
        UseCaseResult<EvidenceDownloadResponse> mislabelled = await session.RunAsync(
            useCase, new DownloadEvidenceRequest(world.Alpha, betaEvidence.Id), World.Human(alphaOnly));

        Assert.Equal(ExecutionOutcome.NotFound, mislabelled.Outcome);
    }

    [Fact]
    public async Task unscanned_evidence_is_refused_even_to_someone_who_may_read_the_project()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reader = await _fixture.CreateUserAsync($"scanner-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, reader, Role.Reviewer, world.AlphaId);

        EvidenceObject evidence = await StoreEvidenceAsync(world.Alpha, world.Founder, "config dump");

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<EvidenceDownloadResponse> refused = await session.RunAsync(
            new DownloadEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new DownloadEvidenceRequest(world.Alpha, evidence.Id),
            World.Human(reader));

        // Permission is not the only gate. Until the scanner has looked, the safe answer is no
        // (SB-17), and "we have not checked yet" is never treated as "it is fine".
        Assert.Equal(ExecutionOutcome.Rejected, refused.Outcome);
        Assert.Contains("NotScanned", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task two_teams_in_one_workspace_do_not_see_each_other_work()
    {
        World world = await _fixture.CreateWorldAsync();

        // Teams are an organisational label; access is granted per workspace or per project. This
        // test states that plainly, because "we have teams" is easy to mistake for "teams are an
        // isolation boundary", and they are not: the project grant is.
        UserId platformEngineer = await _fixture.CreateUserAsync($"platform-{Guid.NewGuid():N}@example.com");
        UserId productEngineer = await _fixture.CreateUserAsync($"product-{Guid.NewGuid():N}@example.com");

        await _fixture.GrantAsync(world.Workspace, platformEngineer, Role.Contributor, world.AlphaId);
        await _fixture.GrantAsync(world.Workspace, productEngineer, Role.Contributor, world.BetaId);

        WorkItem alphaItem = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-A4", world.Founder);
        KnowledgeRecord alphaRecord = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, alphaItem.Id, "Platform decision", "Ours alone.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new GetRecordUseCase(session.Resolve<IKnowledgeRepository>());

        Assert.True((await session.RunAsync(
            useCase, new GetRecordRequest(world.Alpha, alphaRecord.Id),
            World.Human(platformEngineer))).IsSuccess);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            useCase, new GetRecordRequest(world.Alpha, alphaRecord.Id),
            World.Human(productEngineer))).Outcome);
    }

    [Fact]
    public async Task a_workspace_grant_covers_every_project_and_a_project_grant_does_not()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId workspaceReader = await _fixture.CreateUserAsync($"ws-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, workspaceReader, Role.Viewer);

        WorkItem alphaItem = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-A5", world.Founder);
        WorkItem betaItem = await _fixture.SeedWorkItemAsync(world.Beta, "CRQ-B5", world.Founder);

        KnowledgeRecord alpha = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, alphaItem.Id, "Alpha", "A.", world.Founder);

        KnowledgeRecord beta = await _fixture.SeedPublishedRecordAsync(
            world.Beta, betaItem.Id, "Beta", "B.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new GetRecordUseCase(session.Resolve<IKnowledgeRepository>());

        Assert.True((await session.RunAsync(
            useCase, new GetRecordRequest(world.Alpha, alpha.Id), World.Human(workspaceReader))).IsSuccess);

        Assert.True((await session.RunAsync(
            useCase, new GetRecordRequest(world.Beta, beta.Id), World.Human(workspaceReader))).IsSuccess);
    }

    private async Task<EvidenceObject> StoreEvidenceAsync(ProjectScope scope, UserId author, string content)
    {
        using Session session = _fixture.OpenSession(scope.WorkspaceId);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        return await session.Resolve<IEvidenceStore>()
            .StoreAsync(scope, stream, "text/plain", author, CancellationToken.None);
    }

    /// <summary>Names the caller in a test that uses it several times over.</summary>
    private sealed record CallerContextFor(Application.Security.CallerContext Value);
}
