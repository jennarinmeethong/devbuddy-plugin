using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Application.UseCases.Administration;

// Making things: projects, work items, accounts, and the lists an administration screen needs to
// show what already exists.
//
// These arrived in Phase 8 rather than Phase 2 because nothing before the UI needed them, and it
// is worth being clear about what their absence meant: until now the only thing in the system
// that could create anything was the one-time bootstrap, so an installation had exactly one
// person in it and no way to gain a second.
//
// Every one of them is denied to AI. info.md permits AI to search, get, analyse, draft, and hand
// over. Making a project or an account is none of those.

public sealed record CreateProjectRequest(WorkspaceId WorkspaceId, string Name)
    : WorkspaceRequest(WorkspaceId), IScannableRequest
{
    public override string ResourceReference => Name;

    public IEnumerable<string> ContentForScanning
    {
        get { yield return Name; }
    }

    public override IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(Name) ? ["A project needs a name."] : [];
}

public sealed record ProjectCreatedResponse(ProjectId ProjectId, string Name) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectId"] = ProjectId.Value.ToString(),
        };
}

/// <summary>
/// Creates a project inside a workspace the caller administers.
/// <para>
/// AI access for the new project is not written here, and that is the point: absence is the deny.
/// A policy row appears only when somebody turns access on, so a project created and forgotten is
/// closed rather than open (SB-08).
/// </para>
/// </summary>
public sealed class CreateProjectUseCase(IProjectDirectory directory, IClock clock)
    : UseCase<CreateProjectRequest, ProjectCreatedResponse>
{
    private readonly IProjectDirectory _directory = Guard.NotNull(directory, nameof(directory));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.CreateProject;

    protected internal override async Task<ProjectCreatedResponse> HandleAsync(
        CreateProjectRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        var project = new Project(ProjectId.New(), request.WorkspaceId, request.Name, _clock.UtcNow);
        await _directory.AddProjectAsync(project, cancellationToken);

        return new ProjectCreatedResponse(project.Id, project.Name);
    }
}

public sealed record CreateWorkItemRequest(
    ProjectScope Scope,
    string Key,
    WorkItemType Type,
    string Title,
    string Goal,
    string? InScope = null,
    string? Exclusions = null) : ProjectRequest(Scope), IScannableRequest
{
    public override string ResourceReference => Key;

    /// <summary>
    /// Everything that would be written down. A work item is prose a person typed, and prose a
    /// person typed is where a connection string ends up (SB-17).
    /// </summary>
    public IEnumerable<string> ContentForScanning
    {
        get
        {
            yield return Key;
            yield return Title;
            yield return Goal;

            if (InScope is not null)
            {
                yield return InScope;
            }

            if (Exclusions is not null)
            {
                yield return Exclusions;
            }
        }
    }

    public override IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(Key))
        {
            errors.Add("A work item needs a key, the identifier a team says out loud.");
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            errors.Add("A work item needs a title.");
        }

        if (string.IsNullOrWhiteSpace(Goal))
        {
            errors.Add("A work item needs a goal. What it is for is the part nobody writes down.");
        }

        if (!Enum.IsDefined(Type))
        {
            errors.Add("A work item needs a type: Develop, Enhance, FixBug, ChangeRequest, or CodeReview.");
        }

        return errors;
    }
}

public sealed record WorkItemCreatedResponse(WorkItemId WorkItemId, string Key) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workItemId"] = WorkItemId.Value.ToString(),
            ["key"] = Key,
        };
}

/// <summary>
/// Registers a piece of work. Knowledge records hang off one of these, so this is what has to
/// exist before anything can be written down about it.
/// </summary>
public sealed class CreateWorkItemUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<CreateWorkItemRequest, WorkItemCreatedResponse>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.CreateWorkItem;

    protected internal override async Task<WorkItemCreatedResponse> HandleAsync(
        CreateWorkItemRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        var item = new WorkItem(
            WorkItemId.New(),
            request.Scope,
            request.Key,
            request.Type,
            request.Title,
            request.Goal,
            _clock.UtcNow,
            caller.UserId);

        if (request.InScope is not null || request.Exclusions is not null)
        {
            item.SetScope(request.InScope, request.Exclusions);
        }

        await _repository.AddWorkItemAsync(item, cancellationToken);
        return new WorkItemCreatedResponse(item.Id, item.Key);
    }
}

public sealed record ListWorkItemsRequest(ProjectScope Scope) : ProjectRequest(Scope)
{
    public override string ResourceReference => "work-items";
}

public sealed record ListWorkItemsResponse(IReadOnlyList<WorkItemSummary> WorkItems)
    : IRedactableResponse<ListWorkItemsResponse>
{
    public ListWorkItemsResponse Redact(IRedactor redactor) =>
        new([.. WorkItems.Select(item => item.Redact(redactor))]);
}

public sealed record WorkItemSummary(
    WorkItemId WorkItemId,
    string Key,
    WorkItemType Type,
    string Title,
    DateTimeOffset CreatedAt) : IRedactableResponse<WorkItemSummary>
{
    public WorkItemSummary Redact(IRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        return this with { Title = redactor.Redact(Title) };
    }
}

public sealed class ListWorkItemsUseCase(IKnowledgeRepository repository)
    : UseCase<ListWorkItemsRequest, ListWorkItemsResponse>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ListWorkItems;

    protected internal override async Task<ListWorkItemsResponse> HandleAsync(
        ListWorkItemsRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkItem> items =
            await _repository.ListWorkItemsAsync(request.Scope, cancellationToken);

        return new ListWorkItemsResponse(
        [
            .. items.Select(item => new WorkItemSummary(
                item.Id, item.Key, item.Type, item.Title, item.CreatedAt))
        ]);
    }
}

public sealed record ListRecordsRequest(
    ProjectScope Scope, IReadOnlyList<RecordStatus>? Statuses = null) : ProjectRequest(Scope)
{
    public override string ResourceReference => "records";
}

public sealed record ListRecordsResponse(IReadOnlyList<RecordSummary> Records)
    : IRedactableResponse<ListRecordsResponse>
{
    public ListRecordsResponse Redact(IRedactor redactor) =>
        new([.. Records.Select(record => record.Redact(redactor))]);
}

public sealed record RecordSummary(
    KnowledgeRecordId RecordId,
    WorkItemId WorkItemId,
    RecordKind Kind,
    RecordStatus Status,
    string Title,
    int CurrentRevisionNumber,
    int? PublishedRevisionNumber,
    DateTimeOffset UpdatedAt) : IRedactableResponse<RecordSummary>
{
    public RecordSummary Redact(IRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        return this with { Title = redactor.Redact(Title) };
    }
}

/// <summary>
/// The review queue, and the project listing behind it.
/// <para>
/// Separate from <c>search_knowledge</c> because a reviewer asking "what is waiting for me" has
/// no query text to give, and a search that invented one would return whatever happened to match
/// it rather than everything that is waiting.
/// </para>
/// </summary>
public sealed class ListRecordsUseCase(IKnowledgeRepository repository)
    : UseCase<ListRecordsRequest, ListRecordsResponse>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ListRecords;

    protected internal override async Task<ListRecordsResponse> HandleAsync(
        ListRecordsRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<KnowledgeRecord> records =
            await _repository.ListRecordsAsync(request.Scope, request.Statuses, cancellationToken);

        return new ListRecordsResponse(
        [
            .. records.Select(record => new RecordSummary(
                record.Id,
                record.WorkItemId,
                record.Kind,
                record.Status,
                record.CurrentRevision.Title,
                record.CurrentRevision.Number,
                record.PublishedRevisionNumber,
                record.LastUpdatedAt))
        ]);
    }
}

public sealed record ListMembershipsRequest(WorkspaceId WorkspaceId) : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => "memberships";
}

public sealed record ListMembershipsResponse(IReadOnlyList<MembershipSummary> Memberships);

public sealed record MembershipSummary(
    MembershipId MembershipId,
    UserId UserId,
    Role Role,
    ProjectId? ScopedToProject,
    bool IsActive,
    DateTimeOffset GrantedAt);

/// <summary>Who holds what in this workspace, for the membership administration screen.</summary>
public sealed class ListMembershipsUseCase(IAccessDirectory directory)
    : UseCase<ListMembershipsRequest, ListMembershipsResponse>
{
    private readonly IAccessDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ListMemberships;

    protected internal override async Task<ListMembershipsResponse> HandleAsync(
        ListMembershipsRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<Membership> memberships =
            await _directory.ListMembershipsForWorkspaceAsync(request.WorkspaceId, cancellationToken);

        return new ListMembershipsResponse(
        [
            .. memberships.Select(membership => new MembershipSummary(
                membership.Id,
                membership.UserId,
                membership.Role,
                membership.ProjectId,
                membership.IsActive,
                membership.GrantedAt))
        ]);
    }
}

public sealed record CreateUserAccountRequest(
    WorkspaceId WorkspaceId,
    string Email,
    string DisplayName,
    Role Role,
    ProjectId? ScopedToProject = null) : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => Email;

    public override IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        // Deliberately shallow. An address is valid if the person who owns it can receive at it,
        // and no pattern here knows that; a stricter rule would reject real addresses to feel
        // thorough.
        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@', StringComparison.Ordinal))
        {
            errors.Add("An account needs an email address.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            errors.Add("An account needs a display name.");
        }

        if (!Enum.IsDefined(Role))
        {
            errors.Add("A membership needs a role: Viewer, Contributor, Reviewer, or Administrator.");
        }

        return errors;
    }
}

/// <summary>
/// The new account, and the one-time token its owner uses to choose a password.
/// <para>
/// The token is in the response because there is no mail transport yet, so an administrator has
/// to carry it. That is stated rather than hidden, and it goes away when a transport lands. It is
/// single-use and short-lived, which is what makes carrying it survivable.
/// </para>
/// </summary>
public sealed record UserAccountCreatedResponse(
    UserId UserId,
    MembershipId MembershipId,
    string SetupToken,
    DateTimeOffset SetupTokenExpiresAt) : IAuditableResult
{
    /// <summary>The token is never audited. An audit row that carried it would be a credential at rest.</summary>
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["userId"] = UserId.Value.ToString(),
            ["membershipId"] = MembershipId.Value.ToString(),
        };
}

/// <summary>
/// Creates an account and grants it membership of this workspace, in one operation.
/// <para>
/// One operation rather than two because two would leave an account belonging to no workspace
/// whenever the second failed: reachable by sign-in, visible to nobody, and impossible to clean up
/// through any operation that exists.
/// </para>
/// </summary>
public sealed class CreateUserAccountUseCase(
    ICredentialManager credentials, IAccessDirectory directory, IClock clock)
    : UseCase<CreateUserAccountRequest, UserAccountCreatedResponse>
{
    private readonly ICredentialManager _credentials = Guard.NotNull(credentials, nameof(credentials));
    private readonly IAccessDirectory _directory = Guard.NotNull(directory, nameof(directory));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.CreateUserAccount;

    protected internal override async Task<UserAccountCreatedResponse> HandleAsync(
        CreateUserAccountRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        AccountCreation? created = await _credentials.CreateAccountAsync(
            request.Email, request.DisplayName, cancellationToken);

        if (created is null)
        {
            // Told plainly, because the caller is an administrator of this workspace acting on an
            // address they typed. This is not the sign-in surface, where the same answer would be
            // account enumeration; refusing to say would just leave them guessing why nothing
            // happened.
            throw new InvalidTransitionException("An account already exists for that address.");
        }

        Membership membership = request.ScopedToProject is { } projectId
            ? Membership.ForProject(
                MembershipId.New(),
                created.UserId,
                new ProjectScope(request.WorkspaceId, projectId),
                request.Role,
                _clock.UtcNow,
                caller.UserId)
            : Membership.ForWorkspace(
                MembershipId.New(),
                created.UserId,
                request.WorkspaceId,
                request.Role,
                _clock.UtcNow,
                caller.UserId);

        await _directory.AddMembershipAsync(membership, cancellationToken);

        return new UserAccountCreatedResponse(
            created.UserId, membership.Id, created.SetupToken, created.ExpiresAt);
    }
}
