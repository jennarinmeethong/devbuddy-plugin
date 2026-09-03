using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Closing the export half of the SB-27 gap: <c>export_project</c> used to return a manifest with
/// counts and a reference naming no real file. It now writes an actual copy — records with their
/// revision history and evidence bytes — so retention has something to purge and an operator has
/// something to actually take off the system.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class ExportProjectTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task exporting_a_project_writes_its_records_and_evidence_bytes_to_disk()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"export-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-EXP1", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Exportable record", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("exportable bytes")))
        {
            await session.Resolve<IEvidenceStore>()
                .StoreAsync(world.Alpha, stream, "text/plain", world.Founder, CancellationToken.None);
        }

        Application.Pipeline.UseCaseResult<ExportManifest> result = await session.RunAsync(
            new ExportProjectUseCase(session.Resolve<IAdministrativeOperations>()),
            new ExportProjectRequest(world.Alpha),
            World.Human(administrator));

        Assert.True(result.IsSuccess, $"Expected success but got {result.Outcome}: {result.Reason}");
        ExportManifest manifest = result.Value!;

        Assert.Equal(1, manifest.RecordCount);
        Assert.Equal(1, manifest.EvidenceCount);

        string exportDirectory = Path.Combine(_fixture.ExportRoot, manifest.Reference);
        string rowsFile = Path.Combine(exportDirectory, "rows.json");

        Assert.True(Directory.Exists(exportDirectory), "The export reference names no directory on disk.");
        Assert.True(File.Exists(rowsFile), "The export wrote no rows file.");

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(rowsFile));

        JsonElement records = document.RootElement.GetProperty("KnowledgeRecords");
        Assert.Equal(1, records.GetArrayLength());
        Assert.Equal(record.Id.Value, records[0].GetProperty("Id").GetGuid());

        string evidenceDirectory = Path.Combine(exportDirectory, "evidence");
        Assert.True(Directory.Exists(evidenceDirectory), "The export wrote no evidence bytes.");
        Assert.Single(Directory.GetFiles(evidenceDirectory));
    }

    [Fact]
    public async Task exporting_a_project_does_not_include_the_one_next_to_it()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"export-iso-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator);

        WorkItem betaItem = await _fixture.SeedWorkItemAsync(world.Beta, "CRQ-EXP2", world.Founder);
        await _fixture.SeedPublishedRecordAsync(world.Beta, betaItem.Id, "Beta only", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        Application.Pipeline.UseCaseResult<ExportManifest> result = await session.RunAsync(
            new ExportProjectUseCase(session.Resolve<IAdministrativeOperations>()),
            new ExportProjectRequest(world.Alpha),
            World.Human(administrator));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.RecordCount);
    }
}
