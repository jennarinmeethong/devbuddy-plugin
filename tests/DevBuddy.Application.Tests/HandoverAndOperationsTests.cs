using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Application.UseCases.Handover;
using DevBuddy.Application.UseCases.Safety;
using DevBuddy.Application.UseCases.Sources;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Handover, analysis, source synchronisation, the safety scanners, and administration.
/// </summary>
public sealed class HandoverAndOperationsTests
{
    [Fact]
    public async Task a_handover_is_built_from_published_records_only()
    {
        var harness = new Harness();
        harness.Ports.RecordsForWorkItem.Add(PublishedRecord("Published finding."));
        harness.Ports.RecordsForWorkItem.Add(TestData.NewDraft("Unreviewed draft finding."));

        HandoverResponse handover = await harness.SucceedAsync(
            new GenerateHandoverUseCase(harness.Ports, harness.Ports),
            new WorkItemScopedRequest(TestData.Scope, TestData.WorkItem));

        string body = string.Join(" ", handover.Sections.Select(section => section.Content));

        // Handing the next owner statements nobody reviewed is the opposite of what a handover
        // is for, so the draft appears as an open question instead of as content.
        Assert.Contains("Published finding.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Unreviewed draft finding.", body, StringComparison.Ordinal);
        Assert.Contains(handover.OpenQuestions, question => question.Contains("Draft", StringComparison.Ordinal));
    }

    [Fact]
    public async Task missing_exclusions_are_reported_as_an_open_question()
    {
        var harness = new Harness();

        OpenQuestionsResponse response = await harness.SucceedAsync(
            new FindOpenQuestionsUseCase(harness.Ports),
            new WorkItemScopedRequest(TestData.Scope, TestData.WorkItem));

        // The most expensive question a later owner asks is what the work deliberately left out.
        Assert.Contains(response.Questions, question =>
            question.Contains("out of scope", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task missing_evidence_covers_both_uncited_records_and_unscanned_artefacts()
    {
        var harness = new Harness();
        harness.Ports.RecordsForWorkItem.Add(TestData.NewDraft());
        harness.Ports.Evidence.Add(new EvidenceObject(
            EvidenceObjectId.New(), TestData.Scope, ContentHash.FromContent("log"), "text/plain",
            10, "key", TestData.Now, TestData.Author));

        MissingEvidenceResponse response = await harness.SucceedAsync(
            new FindMissingEvidenceUseCase(harness.Ports, harness.Ports),
            new WorkItemScopedRequest(TestData.Scope, TestData.WorkItem));

        Assert.Contains(response.Gaps, gap => gap.Contains("cites no evidence", StringComparison.Ordinal));
        Assert.Contains(response.Gaps, gap => gap.Contains("NotScanned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task analysis_output_is_redacted_before_it_reaches_the_caller()
    {
        var harness = new Harness();

        AnalysisResponse response = await harness.SucceedAsync(
            new AnalyzeCodeUseCase(harness.Ports),
            new AnalysisRequest(TestData.Scope, TestData.Repository));

        Assert.DoesNotContain("SECRET", response.Report.Summary, StringComparison.Ordinal);
        Assert.All(response.Report.Observations, observation =>
            Assert.DoesNotContain("SECRET", observation.Detail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task each_analysis_use_case_runs_its_own_kind()
    {
        var harness = new Harness();
        var request = new AnalysisRequest(TestData.Scope, TestData.Repository);

        Assert.Equal(AnalysisKind.Project,
            (await harness.SucceedAsync(new AnalyzeProjectUseCase(harness.Ports), request)).Report.Kind);
        Assert.Equal(AnalysisKind.Documents,
            (await harness.SucceedAsync(new AnalyzeDocumentsUseCase(harness.Ports), request)).Report.Kind);
        Assert.Equal(AnalysisKind.Architecture,
            (await harness.SucceedAsync(new AnalyzeArchitectureUseCase(harness.Ports), request)).Report.Kind);
        Assert.Equal(AnalysisKind.GitHistory,
            (await harness.SucceedAsync(new AnalyzeGitHistoryUseCase(harness.Ports), request)).Report.Kind);
        Assert.Equal(AnalysisKind.WorkItems,
            (await harness.SucceedAsync(new AnalyzeWorkItemsUseCase(harness.Ports), request)).Report.Kind);
        Assert.Equal(AnalysisKind.TestEvidence,
            (await harness.SucceedAsync(new AnalyzeTestEvidenceUseCase(harness.Ports), request)).Report.Kind);
    }

    [Fact]
    public async Task change_impact_reports_the_paths_a_commit_touched()
    {
        var harness = new Harness();

        ChangeImpactResponse response = await harness.SucceedAsync(
            new AnalyzeChangeImpactUseCase(harness.Ports, harness.Ports),
            new AnalyzeChangeImpactRequest(TestData.Scope, TestData.Repository, "abc123"));

        Assert.Equal("abc123", response.CommitOrRange);
        Assert.Contains("src/importer.cs", response.ChangedPaths);
        Assert.All(response.Impact, observation =>
            Assert.DoesNotContain("SECRET", observation.Detail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task change_impact_requires_a_commit_or_range()
    {
        var harness = new Harness();

        UseCaseResult<ChangeImpactResponse> result = await harness.RunAsync(
            new AnalyzeChangeImpactUseCase(harness.Ports, harness.Ports),
            new AnalyzeChangeImpactRequest(TestData.Scope, TestData.Repository, "  "));

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task sync_sources_records_the_snapshot_it_imported()
    {
        var harness = new Harness();

        SyncSourcesResponse response = await harness.SucceedAsync(
            new SyncSourcesUseCase(harness.Ports),
            new SyncSourcesRequest(TestData.Scope, TestData.Repository));

        Assert.Equal("abc123", response.CommitId);
        Assert.Equal(1, response.LinkCount);
    }

    [Fact]
    public async Task the_quality_sweeps_report_under_their_own_operation_names()
    {
        var harness = new Harness();
        var request = new QualitySweepRequest(TestData.Scope);

        Assert.Equal("validate_provenance",
            (await harness.SucceedAsync(new ValidateProvenanceUseCase(harness.Ports), request)).Sweep);
        Assert.Equal("detect_duplicates",
            (await harness.SucceedAsync(new DetectDuplicatesUseCase(harness.Ports), request)).Sweep);

        QualitySweepResponse staleness = await harness.SucceedAsync(
            new DetectStalenessUseCase(harness.Ports, harness.Ports),
            new DetectStalenessRequest(TestData.Scope, TimeSpan.FromDays(180)));

        Assert.Equal("detect_staleness", staleness.Sweep);
    }

    [Fact]
    public async Task staleness_needs_a_positive_window()
    {
        var harness = new Harness();

        UseCaseResult<QualitySweepResponse> result = await harness.RunAsync(
            new DetectStalenessUseCase(harness.Ports, harness.Ports),
            new DetectStalenessRequest(TestData.Scope, TimeSpan.Zero));

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task reindex_reports_how_many_documents_it_rebuilt()
    {
        var harness = new Harness();

        ReindexResponse response = await harness.SucceedAsync(
            new ReindexUseCase(harness.Ports), new QualitySweepRequest(TestData.Scope));

        Assert.Equal(7, response.DocumentsIndexed);
    }

    [Fact]
    public async Task secret_detection_reports_where_and_which_rule_but_never_the_value()
    {
        var harness = new Harness();

        SecretScanResponse response = await harness.SucceedAsync(
            new DetectSecretsUseCase(harness.Ports),
            new ScanContentRequest(TestData.Scope, "token=SECRET"));

        Assert.True(response.HasFindings);
        SecretFinding finding = Assert.Single(response.Findings);
        Assert.Equal("literal", finding.RuleName);

        // A finding that quoted the secret would put it into every log that touched the result.
        Assert.DoesNotContain("SECRET", finding.RuleName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task redaction_returns_the_content_with_matches_replaced()
    {
        var harness = new Harness();

        RedactionResponse response = await harness.SucceedAsync(
            new RedactSensitiveDataUseCase(harness.Ports, harness.Ports),
            new ScanContentRequest(TestData.Scope, "token=SECRET"));

        Assert.Equal("token=[REDACTED]", response.RedactedContent);
        Assert.Equal(1, response.FindingCount);
    }

    [Fact]
    public async Task an_export_carries_an_expiry_from_the_moment_it_is_created()
    {
        var harness = new Harness();

        ExportManifest manifest = await harness.SucceedAsync(
            new ExportProjectUseCase(harness.Ports), new ExportProjectRequest(TestData.Scope));

        // An export is a data copy, and the retention schedule applies to copies (ADR-0009).
        Assert.True(manifest.ExpiresAt > manifest.CreatedAt);
        Assert.Equal(TestData.Now.AddDays(30), manifest.ExpiresAt);
    }

    [Fact]
    public async Task backup_restore_and_health_run_as_administrative_operations()
    {
        var harness = new Harness();
        var request = new AdministrativeRequest(TestData.Workspace);

        BackupManifest backup = await harness.SucceedAsync(new BackupSystemUseCase(harness.Ports), request);
        Assert.Equal("backup-1", backup.Reference);

        RestoreOutcome restore = await harness.SucceedAsync(
            new RestoreSystemUseCase(harness.Ports),
            new RestoreSystemRequest(TestData.Workspace, backup.Reference));
        Assert.True(restore.Succeeded);

        HealthReport health = await harness.SucceedAsync(new CheckSystemHealthUseCase(harness.Ports), request);
        Assert.True(health.IsHealthy);
        Assert.All(health.Components, component =>
            Assert.DoesNotContain("password", component.Detail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task a_restore_needs_a_backup_reference()
    {
        var harness = new Harness();

        UseCaseResult<RestoreOutcome> result = await harness.RunAsync(
            new RestoreSystemUseCase(harness.Ports),
            new RestoreSystemRequest(TestData.Workspace, "  "));

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task a_project_grant_does_not_widen_to_the_workspace()
    {
        var harness = new Harness();

        await harness.SucceedAsync(
            new GrantMembershipUseCase(harness.Ports, harness.Ports),
            new GrantMembershipRequest(
                TestData.Workspace, TestData.Reviewer, Role.Contributor, TestData.ProjectAlpha));

        Membership membership = Assert.IsType<Membership>(harness.Ports.Membership);
        Assert.Equal(TestData.ProjectAlpha, membership.ProjectId);
        Assert.True(membership.Covers(TestData.Scope));
        Assert.False(membership.Covers(new Domain.Tenancy.ProjectScope(TestData.Workspace, ProjectId.New())));
    }

    [Fact]
    public async Task revoking_a_membership_from_another_workspace_reports_not_found()
    {
        var harness = new Harness();
        harness.Ports.Membership = Membership.ForWorkspace(
            MembershipId.New(), TestData.Reviewer, WorkspaceId.New(), Role.Viewer,
            TestData.Now, TestData.Author);

        UseCaseResult<MembershipResponse> result = await harness.RunAsync(
            new RevokeMembershipUseCase(harness.Ports, harness.Ports),
            new RevokeMembershipRequest(TestData.Workspace, harness.Ports.Membership.Id));

        // Not found, not forbidden. Confirming that a membership exists somewhere the caller
        // cannot see would itself be a disclosure.
        Assert.Equal(ExecutionOutcome.NotFound, result.Outcome);
        Assert.True(harness.Ports.Membership.IsActive);
    }

    [Fact]
    public async Task ai_access_is_off_until_a_named_person_turns_it_on()
    {
        var harness = new Harness();

        Assert.False(harness.Ports.Policy.IsEnabled);

        AiAccessResponse enabled = await harness.SucceedAsync(
            new EnableProjectAiAccessUseCase(harness.Ports, harness.Ports),
            new EnableProjectAiAccessRequest(TestData.Scope, "Sanitised issue exports only."));

        Assert.True(enabled.IsEnabled);
        Assert.Equal(TestData.Author, enabled.EnabledBy);
        Assert.Equal(TestData.Now, enabled.EnabledAt);
        Assert.Equal("Sanitised issue exports only.", harness.Ports.Policy.BoundedDataScope);

        AiAccessResponse disabled = await harness.SucceedAsync(
            new DisableProjectAiAccessUseCase(harness.Ports),
            new DisableProjectAiAccessRequest(TestData.Scope));

        Assert.False(disabled.IsEnabled);
        Assert.Null(harness.Ports.Policy.BoundedDataScope);
    }

    [Fact]
    public async Task an_audit_window_must_end_after_it_starts()
    {
        var harness = new Harness();

        UseCaseResult<AuditHistoryResponse> result = await harness.RunAsync(
            new ReadAuditHistoryUseCase(harness.Ports),
            new ReadAuditHistoryRequest(TestData.Scope, TestData.Now, TestData.Now.AddDays(-1)));

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
    }

    private static KnowledgeRecord PublishedRecord(string body)
    {
        KnowledgeRecord record = TestData.NewDraft(body);
        record.SubmitForApproval(TestData.Now);
        record.Approve(TestData.Reviewer, record.CurrentRevision.ContentHash, TestData.Now);
        record.Publish(TestData.Now);
        return record;
    }
}
