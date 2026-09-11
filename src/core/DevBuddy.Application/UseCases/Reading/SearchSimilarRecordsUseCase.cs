using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.Workers;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Reading;

/// <summary>
/// Semantic search: embed the question, then ask the derived vector index which revisions are
/// nearest (ADR-0012).
/// <para>
/// The path is deliberately long and every step of it is somebody else's job. This use case
/// authorises nothing, scans nothing, and stores nothing — the pipeline authorises the scope,
/// <see cref="EmbeddingGateway"/> refuses a secret before the query text leaves the boundary, the
/// index puts the scope in its <c>where</c> clause, and the executor redacts the response. What is
/// left here is the shape of the question and the shape of the answer.
/// </para>
/// <para>
/// It returns <b>identifiers and distances, plus the title and kind of each hit</b>, and not
/// content. A caller that wants a record calls <c>get_record</c>, which is separately authorised,
/// separately audited, and separately redacted. That is more round trips and it is the right
/// number of them: a search that returned bodies would be a way to read every record in a project
/// with one permission check.
/// </para>
/// </summary>
public sealed record SearchSimilarRecordsRequest(
    ProjectScope Scope,
    string QueryText,
    int MaxResults = 10) : ProjectRequest(Scope), IScannableRequest
{
    public override string ResourceReference => "search";

    /// <summary>
    /// The query is scanned, and that is not ceremony. The text is about to be sent to an
    /// embedding provider, which on a hosted one is out of the trust boundary — so a caller who
    /// pastes a connection string into a search box is refused here, with nothing sent, rather
    /// than having it indexed by somebody else (SB-17).
    /// </summary>
    public IEnumerable<string> ContentForScanning
    {
        get { yield return QueryText; }
    }

    public override IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(QueryText))
        {
            errors.Add("A query is required.");
        }

        if (MaxResults is < 1 or > 50)
        {
            errors.Add("MaxResults must be between 1 and 50.");
        }

        return errors;
    }
}

/// <summary>
/// The hits, and — when there are none — <paramref name="Unavailable"/> saying why.
/// <para>
/// A reason rather than an empty list, because "no similar records" and "this installation has no
/// embedding provider" are different answers and a caller that cannot tell them apart will read
/// the second as the first. The three ways this comes back unavailable are: no provider
/// configured, no vector index on this database, and nothing indexed for this project yet.
/// </para>
/// </summary>
public sealed record SearchSimilarRecordsResponse(
    IReadOnlyList<SimilarRecordHit> Hits,
    string? Unavailable = null) : IRedactableResponse<SearchSimilarRecordsResponse>
{
    /// <summary>
    /// Titles are record content and are redacted like any other read. The reason is not, and
    /// must not be: it is this system's own prose about its own configuration, and running a
    /// redactor over "no embedding provider configured" would only ever make a clear refusal
    /// harder to read.
    /// </summary>
    public SearchSimilarRecordsResponse Redact(IRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);

        return new(
            [.. Hits.Select(hit => hit with { Title = redactor.Redact(hit.Title) })],
            Unavailable);
    }
}

/// <summary>
/// One hit. <paramref name="Distance"/> is cosine distance, closest first — 0 is identical.
/// Returned raw rather than as a percentage, because the conversion depends on the metric and a
/// "95% match" invites comparison between models that are not comparable.
/// </summary>
public sealed record SimilarRecordHit(
    KnowledgeRecordId RecordId,
    int RevisionNumber,
    RecordKind Kind,
    string Title,
    double Distance);

/// <inheritdoc cref="SearchSimilarRecordsRequest"/>
public sealed class SearchSimilarRecordsUseCase(
    EmbeddingGateway gateway, IEmbeddingIndex index, IKnowledgeRepository repository)
    : UseCase<SearchSimilarRecordsRequest, SearchSimilarRecordsResponse>
{
    private readonly EmbeddingGateway _gateway = Guard.NotNull(gateway, nameof(gateway));
    private readonly IEmbeddingIndex _index = Guard.NotNull(index, nameof(index));
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.SearchSimilarRecords;

    protected internal override async Task<SearchSimilarRecordsResponse> HandleAsync(
        SearchSimilarRecordsRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        // Both refusals before anything is embedded, so an installation that cannot answer this
        // never pays a provider to find that out.
        if (!_gateway.IsConfigured || _gateway.Model is not { Length: > 0 } model)
        {
            return Unavailable(
                "This installation has no embedding provider configured, so there is nothing to "
                + "compare against. Use search_knowledge, which needs none.");
        }

        if (!await _index.IsAvailableAsync(cancellationToken))
        {
            return Unavailable(
                "This database has no vector index: pgvector is not installed, so the migration "
                + "that would create it skipped itself. Use search_knowledge, which needs none.");
        }

        // No budget. An interactive search is one call per query and is bounded by the request
        // rate limiter (SB-21); the budget exists for a worker loop nobody is watching.
        EmbeddingOutcome embedded = await _gateway.EmbedAsync(
            caller, [request.QueryText], budget: null, cancellationToken);

        if (embedded.Refused)
        {
            // Including the secret-scan block. Reported as unavailable with the gateway's own
            // reason rather than thrown: the caller asked a question that cannot be answered, and
            // a refusal is an answer.
            return Unavailable(embedded.Reason ?? "The query could not be embedded.");
        }

        IReadOnlyList<SimilarRevision> nearest = await _index.FindSimilarAsync(
            request.Scope,
            model,
            embedded.Vectors[0],
            request.MaxResults,
            cancellationToken);

        if (nearest.Count == 0)
        {
            return Unavailable(
                "Nothing is indexed for this project yet, or nothing in it resembles the query. "
                + "An index is built by a worker run, not by asking.");
        }

        List<SimilarRecordHit> hits = [];

        foreach (SimilarRevision hit in nearest)
        {
            // Read back through the repository, which carries the scope, rather than storing
            // titles in the index. The index deliberately holds no text, so a title has to come
            // from the record — and a record deleted since it was indexed simply drops out here
            // instead of being reported from a stale copy.
            KnowledgeRecord? record =
                await _repository.FindRecordAsync(hit.RecordId, request.Scope, cancellationToken);

            if (record is null)
            {
                continue;
            }

            RecordRevision? revision =
                record.Revisions.FirstOrDefault(candidate => candidate.Number == hit.RevisionNumber);

            hits.Add(new SimilarRecordHit(
                record.Id,
                hit.RevisionNumber,
                record.Kind,
                revision?.Title ?? string.Empty,
                hit.Distance));
        }

        return new SearchSimilarRecordsResponse(hits);
    }

    private static SearchSimilarRecordsResponse Unavailable(string reason) => new([], reason);
}
