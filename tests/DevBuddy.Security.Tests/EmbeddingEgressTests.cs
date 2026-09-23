using System.Net;
using System.Text;
using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Workers;
using DevBuddy.Application.Workers.Jobs;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure;
using DevBuddy.Infrastructure.Embeddings;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace DevBuddy.Security.Tests;

/// <summary>
/// The embedding egress path, which ADR-0012 says is a third way out of the trust boundary and
/// must be verified on its own rather than read as covered by SB-17 and SB-18.
/// <para>
/// Everything that ships is real here: PostgreSQL with pgvector, the migrations, the pipeline, the
/// authorization service, the per-project AI access policy, the secret scanner and the personal-data
/// redactor, the machine token service, the embedding gateway, the vector index, and
/// <see cref="HttpEmbeddingProvider"/> serialising a real request. What is not real is the far side
/// of the wire: the provider's HTTP handler is replaced by one that records every text it was sent.
/// That is the one substitution that lets a test say what <b>left</b>, which is the question.
/// </para>
/// <para>
/// Each assertion is about the recorder, not about a report. A job that reported "skipped" and sent
/// the text anyway would pass a test that read its report; it cannot pass these.
/// </para>
/// </summary>
[Collection(EmbeddingEgressCollection.Name)]
public sealed class EmbeddingEgressTests(EmbeddingEgressFixture fixture)
{
    private readonly EmbeddingEgressFixture _fixture = fixture;

    /// <summary>
    /// The single most important property of the sweep. <c>list_projects</c> on the AI channel omits
    /// a project whose owner never enabled access, so nothing in it is read, sent, or indexed.
    /// </summary>
    [Fact]
    public async Task a_project_nobody_opened_to_ai_is_never_sent_to_the_provider()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        await ground.PublishAsync(ground.World.Alpha, ground.AlphaItem, "Alpha decision", "Why alpha chose this.");
        await ground.PublishAsync(ground.World.Beta, ground.BetaItem, "Beta decision", "Why beta chose that.");

        WorkerRunReport report = await ground.SweepAsync();

        Assert.False(report.Refused);
        Assert.Contains(_fixture.Recorder.Texts, text => text.Contains("Why alpha chose this.", StringComparison.Ordinal));
        Assert.DoesNotContain(_fixture.Recorder.Texts, text => text.Contains("beta", StringComparison.OrdinalIgnoreCase));

        Assert.Single(await ground.IndexedAsync(ground.World.Alpha));
        Assert.Empty(await ground.IndexedAsync(ground.World.Beta));
    }

    /// <summary>
    /// SB-17 on the way out. Whether the pipeline redacts the credential from the read or the gateway
    /// refuses the text, the property is the same and it is the only one asserted: the credential is
    /// not in anything the provider received, and the sweep carried on with the next record.
    /// </summary>
    [Fact]
    public async Task a_credential_in_a_published_record_never_reaches_the_provider()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        await ground.PublishAsync(
            ground.World.Alpha, ground.AlphaItem, "Deployment notes", "Connect with AKIAIOSFODNN7EXAMPLE and then run the migration.");

        await ground.PublishAsync(
            ground.World.Alpha, ground.AlphaItem, "Retry policy", "Retries stop after three attempts.");

        WorkerRunReport report = await ground.SweepAsync();

        Assert.False(report.Refused);
        Assert.DoesNotContain(_fixture.Recorder.Texts, text => text.Contains("AKIAIOSFODNN7EXAMPLE", StringComparison.Ordinal));
        Assert.Contains(_fixture.Recorder.Texts, text => text.Contains("three attempts", StringComparison.Ordinal));
    }

    /// <summary>
    /// SB-18 on the way out. The project is open to AI with no approved bounded scope, so personal
    /// data is redacted from what the AI channel reads — and the embedding sweep reads on that
    /// channel, so what it sends is the redacted text.
    /// </summary>
    [Fact]
    public async Task personal_data_is_redacted_before_the_text_leaves()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        await ground.PublishAsync(
            ground.World.Alpha, ground.AlphaItem, "Legacy escalation", "Customer date of birth: 1990-04-12, recorded for verification.");

        await ground.SweepAsync();

        string sent = Assert.Single(_fixture.Recorder.Texts);
        Assert.DoesNotContain("1990-04-12", sent, StringComparison.Ordinal);
        Assert.Contains("date of birth", sent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A draft is not knowledge yet. Indexing one would let a semantic search surface something
    /// nobody approved, so it is not sent at all.
    /// </summary>
    [Fact]
    public async Task a_draft_is_never_sent_to_the_provider()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        await ground.DraftAsync(ground.World.Alpha, ground.AlphaItem, "Unapproved idea", "Nobody has reviewed this yet.");

        WorkerRunReport report = await ground.SweepAsync();

        Assert.False(report.Refused);
        Assert.Empty(_fixture.Recorder.Texts);
        Assert.Empty(await ground.IndexedAsync(ground.World.Alpha));
    }

    /// <summary>
    /// An archived record keeps its published revision. The first pass after archiving takes its
    /// row out of the index, sends nothing, and semantic search stops offering it — found on the
    /// owner's installation on 2026-09-17, where an archived test record kept coming back.
    /// </summary>
    [Fact]
    public async Task an_archived_record_leaves_the_index_and_is_not_sent_again()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        KnowledgeRecord record = await ground.PublishAsync(
            ground.World.Alpha, ground.AlphaItem, "Retired retry policy", "Retries used to stop after five attempts.");

        await ground.SweepAsync();
        Assert.Single(await ground.IndexedAsync(ground.World.Alpha));

        DispatchResult before = await ground.SearchAsync(ground.World.Alpha, "retry policy");
        Assert.Equal(1, before.Payload!.Value.GetProperty("hits").GetArrayLength());

        await ground.ArchiveAsync(ground.World.Alpha, record.Id);
        _fixture.Recorder.Clear();

        WorkerRunReport report = await ground.SweepAsync();

        Assert.False(report.Refused);
        Assert.Empty(_fixture.Recorder.Texts);
        Assert.Empty(await ground.IndexedAsync(ground.World.Alpha));

        DispatchResult after = await ground.SearchAsync(ground.World.Alpha, "retry policy");
        Assert.True(after.IsSuccess);
        Assert.Equal(0, after.Payload!.Value.GetProperty("hits").GetArrayLength());
    }

    /// <summary>
    /// The other caller that sends text out: a semantic search query. A credential pasted into the
    /// search box is refused before anything is sent.
    /// </summary>
    [Fact]
    public async Task a_search_query_carrying_a_credential_is_not_sent()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        DispatchResult result = await ground.SearchAsync(
            ground.World.Alpha, "how do I use ghp_1234567890abcdefghijklmnopqrstuvwxyzAB");

        Assert.False(result.IsSuccess);
        Assert.Empty(_fixture.Recorder.Texts);
    }

    /// <summary>
    /// And a clean query is sent — the control above is a refusal of the credential, not of search.
    /// Without this, a gateway that sent nothing ever would pass every test in this file.
    /// </summary>
    [Fact]
    public async Task a_clean_search_query_is_sent_once()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        await ground.SearchAsync(ground.World.Alpha, "retry policy");

        Assert.Equal(["retry policy"], _fixture.Recorder.Texts);
    }

    /// <summary>
    /// The hosted mode sends project text to somebody else's system, and `info.md` requires the
    /// vendor's host on the outbound allow-list as the deliberate statement that it may. Without the
    /// entry the installation refuses to compose at all, so the omission is found at start-up rather
    /// than by sending.
    /// </summary>
    [Fact]
    public void a_hosted_provider_whose_host_is_not_allowed_refuses_to_start()
    {
        ServiceCollection services = new();

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            services.AddDevBuddyInfrastructure(
                "Host=unused;Database=unused",
                configureEmbedding: embedding =>
                {
                    embedding.Provider = EmbeddingProviderKind.HostedApi;
                    embedding.Endpoint = "https://embeddings.vendor.example/v1";
                    embedding.Model = "vendor-model";
                    embedding.Dimensions = 3;
                    embedding.ApiKey = "not-a-real-key";
                }));

        Assert.Contains("embedding provider", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IEmbeddingProvider));
    }

    /// <summary>One workspace, a worker account with a real token, and a work item per project.</summary>
    private sealed class Ground
    {
        private readonly EmbeddingEgressFixture _fixture;

        private Ground(EmbeddingEgressFixture fixture, World world, UserId author, string token, WorkItem alpha, WorkItem beta)
        {
            _fixture = fixture;
            World = world;
            Author = author;
            Token = token;
            AlphaItem = alpha;
            BetaItem = beta;
        }

        public World World { get; }

        public UserId Author { get; }

        public string Token { get; }

        public WorkItem AlphaItem { get; }

        public WorkItem BetaItem { get; }

        /// <summary>
        /// The worker is a Viewer, workspace-wide: it can read, and nothing else. That is the
        /// membership the embedding sweep needs, and the recorder is cleared so each test reads only
        /// its own traffic.
        /// </summary>
        public static async Task<Ground> CreateAsync(EmbeddingEgressFixture fixture)
        {
            fixture.Recorder.Clear();

            World world = await fixture.CreateWorldAsync();
            UserId author = await fixture.CreateUserAsync($"author-{Guid.NewGuid():N}@example.com");
            UserId worker = await fixture.CreateUserAsync($"embedding-worker-{Guid.NewGuid():N}@example.com");

            await fixture.GrantAsync(world.Workspace, worker, Role.Viewer);
            string token = await fixture.IssueMachineTokenAsync(worker, world.Workspace, "embedding sweep");

            WorkItem alpha = await fixture.SeedWorkItemAsync(world.Alpha, "EMB-A", author);
            WorkItem beta = await fixture.SeedWorkItemAsync(world.Beta, "EMB-B", author);

            return new Ground(fixture, world, author, token, alpha, beta);
        }

        public Task<KnowledgeRecord> PublishAsync(ProjectScope scope, WorkItem item, string title, string body) =>
            _fixture.SeedPublishedRecordAsync(scope, item.Id, title, body, Author);

        public async Task DraftAsync(ProjectScope scope, WorkItem item, string title, string body)
        {
            KnowledgeRecord draft = KnowledgeRecord.CreateDraft(
                KnowledgeRecordId.New(), scope, item.Id, RecordKind.Decision, title, body,
                frontMatter: null,
                new Provenance(ProvenanceSourceKind.HumanAuthored, "seed", "seed", World.Now),
                World.Now, Author);

            using Session session = _fixture.OpenSession(scope.WorkspaceId);
            await session.Resolve<IKnowledgeRepository>().AddRecordAsync(draft, CancellationToken.None);
        }

        public async Task ArchiveAsync(ProjectScope scope, KnowledgeRecordId recordId)
        {
            using Session session = _fixture.OpenSession(scope.WorkspaceId);
            IKnowledgeRepository repository = session.Resolve<IKnowledgeRepository>();

            KnowledgeRecord record = (await repository.FindRecordAsync(recordId, scope, CancellationToken.None))!;
            record.Archive(World.Now.AddMinutes(30));
            await repository.UpdateRecordAsync(record, CancellationToken.None);
        }

        /// <summary>
        /// One pass of the real job, as the owner of the real token, the way the console's
        /// <c>worker</c> command runs it: resolve, build the caller from the job's own declaration,
        /// run in a scope whose tenant is the token's workspace.
        /// </summary>
        public async Task<WorkerRunReport> SweepAsync()
        {
            using Session session = _fixture.OpenSession(World.Workspace);

            RecordEmbeddingSweepJob job = new(
                session.Resolve<OperationDispatcher>(),
                session.Resolve<EmbeddingGateway>(),
                session.Resolve<IEmbeddingIndex>());

            WorkerCaller caller = await CallerAsync(session, job.ModelUse);
            return await job.RunAsync(caller, new WorkerBudget(100), CancellationToken.None);
        }

        public async Task<DispatchResult> SearchAsync(ProjectScope scope, string query)
        {
            using Session session = _fixture.OpenSession(World.Workspace);

            WorkerCaller caller = await CallerAsync(session, WorkerModelUse.SendsContentToAModel);

            return await session.Resolve<OperationDispatcher>().InvokeAsync(
                "search_similar_records",
                JsonSerializer.SerializeToElement(
                    new
                    {
                        scope = new { workspaceId = scope.WorkspaceId.Value, projectId = scope.ProjectId.Value },
                        queryText = query,
                        maxResults = 5,
                    },
                    JsonConventions.Options),
                caller.Context,
                CancellationToken.None);
        }

        public async Task<IReadOnlySet<string>> IndexedAsync(ProjectScope scope)
        {
            using Session session = _fixture.OpenSession(scope.WorkspaceId);

            return await session.Resolve<IEmbeddingIndex>().IndexedContentHashesAsync(
                scope, EmbeddingEgressFixture.Model, CancellationToken.None);
        }

        private async Task<WorkerCaller> CallerAsync(Session session, WorkerModelUse modelUse)
        {
            MachineTokenIdentity? identity = await session.Resolve<IMachineTokenService>()
                .ResolveAsync(Token, CancellationToken.None);

            return WorkerCaller.FromMachineToken(identity, "embedding-egress-test", modelUse)
                ?? throw new InvalidOperationException("The worker token did not resolve.");
        }
    }
}

/// <summary>
/// The real system over a pgvector-capable PostgreSQL, with a self-hosted embedding provider whose
/// far end is <see cref="Recorder"/>.
/// </summary>
public sealed class EmbeddingEgressFixture : SecurityFixture
{
    public const string Model = "egress-test-model";

    public EmbeddingEgressFixture()
        : this(new EmbeddingRecorder())
    {
    }

    private EmbeddingEgressFixture(EmbeddingRecorder recorder)
        : base(
            "pgvector/pgvector:pg17",
            embedding =>
            {
                embedding.Provider = EmbeddingProviderKind.SelfHosted;
                embedding.Endpoint = "http://embeddings.test.invalid/v1";
                embedding.Model = Model;
                embedding.Dimensions = 3;
                embedding.BatchSize = 8;
            },
            services =>
            {
                // The operations and the dispatcher, which is how a worker reaches a use case.
                services.AddDevBuddyOperations();

                // The far side of the wire. Everything up to the handler is the adapter that ships.
                services.ConfigureHttpClientDefaults(client =>
                    client.ConfigurePrimaryHttpMessageHandler(() => new RecordingEmbeddingHandler(recorder)));

                // The stand-in model server's name answers with a private address, as a model in
                // the stack would; the adapter refuses a self-hosted endpoint that resolves to a
                // public one before it sends anything.
                services.AddSingleton<IHostResolver>(new PrivateModelServerResolver());
            })
    {
        Recorder = recorder;
    }

    /// <summary>Every text the provider was sent.</summary>
    public EmbeddingRecorder Recorder { get; }
}

/// <summary>What reached the provider, in the order it arrived.</summary>
public sealed class EmbeddingRecorder
{
    private readonly List<string> _texts = [];

    public IReadOnlyList<string> Texts
    {
        get
        {
            lock (_texts)
            {
                return [.. _texts];
            }
        }
    }

    public void Clear()
    {
        lock (_texts)
        {
            _texts.Clear();
        }
    }

    internal void Record(IEnumerable<string> texts)
    {
        lock (_texts)
        {
            _texts.AddRange(texts);
        }
    }
}

/// <summary>
/// Reads the request the real adapter sent, records its inputs, and answers in the OpenAI-compatible
/// shape with one three-number vector per input.
/// </summary>
/// <summary>Resolves every name to a private address, which is what the stack's model server has.</summary>
internal sealed class PrivateModelServerResolver : IHostResolver
{
    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("172.18.0.9")]);
}

internal sealed class RecordingEmbeddingHandler(EmbeddingRecorder recorder) : HttpMessageHandler
{
    private static readonly float[] Vector = [1f, 0f, 0f];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = await request.Content!.ReadAsStringAsync(cancellationToken);

        using JsonDocument document = JsonDocument.Parse(body);

        string[] inputs =
        [
            .. document.RootElement
                .GetProperty("input")
                .EnumerateArray()
                .Select(input => input.GetString() ?? string.Empty),
        ];

        recorder.Record(inputs);

        string answer = JsonSerializer.Serialize(new
        {
            data = inputs.Select((_, index) => new { index, embedding = Vector }),
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(answer, Encoding.UTF8, "application/json"),
        };
    }
}

[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection definitions are named after the collection they define.")]
public sealed class EmbeddingEgressCollection : ICollectionFixture<EmbeddingEgressFixture>
{
    public const string Name = "embedding-egress";
}
