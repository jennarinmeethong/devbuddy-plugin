using System.Reflection;
using DevBuddy.Application;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Application.UseCases.Handover;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Application.UseCases.Safety;
using DevBuddy.Application.UseCases.Sources;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Every use case, wired to fake ports, with a request that is valid enough to reach execution.
/// <para>
/// This exists so the authorization enforcement test can drive all of them rather than a
/// hand-picked few. A test asserts that this registry covers every use case in the assembly, so
/// adding one without adding it here fails the build. That is the forcing function: a new use
/// case cannot slip past the pipeline test by being forgotten.
/// </para>
/// </summary>
internal sealed class UseCaseRegistry
{
    private readonly List<RegisteredUseCase> _entries = [];

    public UseCaseRegistry(FakePorts ports)
    {
        Add(new SearchKnowledgeUseCase(ports),
            new SearchKnowledgeRequest(TestData.Scope, "importer"));

        Add(new GetRecordUseCase(ports),
            new GetRecordRequest(TestData.Scope, KnowledgeRecordId.New()));

        Add(new GetWorkItemUseCase(ports),
            new GetWorkItemRequest(TestData.Scope, TestData.WorkItem));

        Add(new ListProjectsUseCase(ports, ports),
            new ListProjectsRequest(TestData.Workspace));

        Add(new ViewRecordHistoryUseCase(ports),
            new ViewRecordHistoryRequest(TestData.Scope, KnowledgeRecordId.New()));

        Add(new CompareSnapshotsUseCase(ports),
            new CompareSnapshotsRequest(TestData.Scope, TestData.Repository, "v1", "v2"));

        Add(new DownloadEvidenceUseCase(ports),
            new DownloadEvidenceRequest(TestData.Scope, EvidenceObjectId.New()));

        var analysisRequest = new AnalysisRequest(TestData.Scope, TestData.Repository);
        Add(new AnalyzeProjectUseCase(ports), analysisRequest);
        Add(new AnalyzeCodeUseCase(ports), analysisRequest);
        Add(new AnalyzeDocumentsUseCase(ports), analysisRequest);
        Add(new AnalyzeArchitectureUseCase(ports), analysisRequest);
        Add(new AnalyzeGitHistoryUseCase(ports), analysisRequest);
        Add(new AnalyzeWorkItemsUseCase(ports), analysisRequest);
        Add(new AnalyzeTestEvidenceUseCase(ports), analysisRequest);

        Add(new AnalyzeChangeImpactUseCase(ports, ports),
            new AnalyzeChangeImpactRequest(TestData.Scope, TestData.Repository, "abc123"));

        var workItemRequest = new WorkItemScopedRequest(TestData.Scope, TestData.WorkItem);
        Add(new GenerateHandoverUseCase(ports, ports), workItemRequest);
        Add(new FindOpenQuestionsUseCase(ports), workItemRequest);
        Add(new FindMissingEvidenceUseCase(ports, ports), workItemRequest);

        Add(new CreateDraftUseCase(ports, ports),
            new CreateDraftRequest(
                TestData.Scope, TestData.WorkItem, RecordKind.Decision,
                "Title", "Body", TestData.Provenance));

        Add(new ReviseDraftUseCase(ports, ports),
            new ReviseDraftRequest(
                TestData.Scope, KnowledgeRecordId.New(), "Title", "Body", TestData.Provenance));

        var recordAction = new RecordActionRequest(TestData.Scope, KnowledgeRecordId.New());
        Add(new SubmitForApprovalUseCase(ports, ports), recordAction);
        Add(new PublishRecordUseCase(ports, ports), recordAction);
        Add(new ArchiveRecordUseCase(ports, ports), recordAction);

        Add(new ApproveRecordUseCase(ports, ports),
            new ApproveRecordRequest(
                TestData.Scope, KnowledgeRecordId.New(), ContentHash.FromContent("x").Value));

        Add(new RequestCorrectionUseCase(ports, ports),
            new RequestCorrectionRequest(TestData.Scope, KnowledgeRecordId.New(), "Missing rollback."));

        Add(new SyncSourcesUseCase(ports),
            new SyncSourcesRequest(TestData.Scope, TestData.Repository));

        var sweep = new QualitySweepRequest(TestData.Scope);
        Add(new ValidateProvenanceUseCase(ports), sweep);
        Add(new DetectDuplicatesUseCase(ports), sweep);
        Add(new ReindexUseCase(ports), sweep);

        Add(new DetectStalenessUseCase(ports, ports),
            new DetectStalenessRequest(TestData.Scope, TimeSpan.FromDays(180)));

        var scanRequest = new ScanContentRequest(TestData.Scope, "token=SECRET");
        Add(new DetectSecretsUseCase(ports), scanRequest);
        Add(new RedactSensitiveDataUseCase(ports, ports), scanRequest);

        Add(new ExportProjectUseCase(ports), new ExportProjectRequest(TestData.Scope));

        var administrative = new AdministrativeRequest(TestData.Workspace);
        Add(new BackupSystemUseCase(ports), administrative);
        Add(new CheckSystemHealthUseCase(ports), administrative);

        Add(new RestoreSystemUseCase(ports),
            new RestoreSystemRequest(TestData.Workspace, "backup-1"));

        Add(new GrantMembershipUseCase(ports, ports),
            new GrantMembershipRequest(TestData.Workspace, TestData.Reviewer, Role.Contributor));

        Add(new RevokeMembershipUseCase(ports, ports),
            new RevokeMembershipRequest(TestData.Workspace, MembershipId.New()));

        Add(new EnableProjectAiAccessUseCase(ports, ports),
            new EnableProjectAiAccessRequest(TestData.Scope));

        Add(new DisableProjectAiAccessUseCase(ports),
            new DisableProjectAiAccessRequest(TestData.Scope));

        Add(new ReadAuditHistoryUseCase(ports),
            new ReadAuditHistoryRequest(
                TestData.Scope, TestData.Now.AddDays(-1), TestData.Now));
    }

    public IReadOnlyList<RegisteredUseCase> Entries => _entries;

    /// <summary>Every concrete use case compiled into the application assembly.</summary>
    public static IReadOnlyList<Type> DiscoverUseCaseTypes() =>
    [
        .. typeof(UseCaseCatalog).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && IsUseCase(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
    ];

    private static bool IsUseCase(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(UseCase<,>))
            {
                return true;
            }
        }

        return false;
    }

    private void Add<TRequest, TResponse>(UseCase<TRequest, TResponse> useCase, TRequest request)
        where TRequest : IUseCaseRequest =>
        _entries.Add(new RegisteredUseCase(
            useCase.GetType(),
            useCase.Descriptor,
            async (executor, caller, cancellationToken) =>
            {
                UseCaseResult<TResponse> result =
                    await executor.ExecuteAsync(useCase, request, caller, cancellationToken);

                return new ExecutionSummary(result.Outcome, result.Reason, result.Value);
            }));
}

/// <summary>One registered use case and a closure that runs it through the pipeline.</summary>
internal sealed record RegisteredUseCase(
    Type UseCaseType,
    UseCaseDescriptor Descriptor,
    Func<UseCaseExecutor, CallerContext, CancellationToken, Task<ExecutionSummary>> Run);

/// <summary>A pipeline result flattened so the test can inspect it without knowing its type.</summary>
internal sealed record ExecutionSummary(ExecutionOutcome Outcome, string Reason, object? Value);
