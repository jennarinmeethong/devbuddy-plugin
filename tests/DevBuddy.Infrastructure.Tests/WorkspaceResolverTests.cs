using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Mapping;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// Single-workspace mode: the simplest local experience info.md asks for, without giving up a
/// single project boundary.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WorkspaceResolverTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task simple_mode_resolves_the_only_workspace()
    {
        string connection = await _fixture.CreateIsolatedDatabaseAsync("devbuddy_single_workspace");
        await using DevBuddyDbContext context = PostgresFixture.CreateContextFor(connection, null);

        var workspace = WorkspaceId.New();
        context.Workspaces.Add(RowMappers.ToRow(
            new Workspace(workspace, "Only", UserId.New(), Seed.Now)));

        await context.SaveChangesAsync(Ct);

        WorkspaceId? resolved = await Resolver(context, simpleMode: true).ResolveAsync(null, Ct);

        Assert.Equal(workspace, resolved);
    }

    [Fact]
    public async Task simple_mode_refuses_to_guess_once_a_second_workspace_exists()
    {
        string connection = await _fixture.CreateIsolatedDatabaseAsync("devbuddy_two_workspaces");
        await using DevBuddyDbContext context = PostgresFixture.CreateContextFor(connection, null);

        context.Workspaces.Add(RowMappers.ToRow(
            new Workspace(WorkspaceId.New(), "First", UserId.New(), Seed.Now)));

        context.Workspaces.Add(RowMappers.ToRow(
            new Workspace(WorkspaceId.New(), "Second", UserId.New(), Seed.Now)));

        await context.SaveChangesAsync(Ct);

        // A system that silently picks one would eventually pick the wrong one.
        Assert.Null(await Resolver(context, simpleMode: true).ResolveAsync(null, Ct));
    }

    [Fact]
    public async Task an_explicit_workspace_is_passed_through_in_either_mode()
    {
        string connection = await _fixture.CreateIsolatedDatabaseAsync("devbuddy_explicit_workspace");
        await using DevBuddyDbContext context = PostgresFixture.CreateContextFor(connection, null);

        var requested = WorkspaceId.New();

        // Resolving is not authorising. The authorization service still checks membership
        // against whatever comes back here (SB-11).
        Assert.Equal(requested, await Resolver(context, simpleMode: true).ResolveAsync(requested, Ct));
        Assert.Equal(requested, await Resolver(context, simpleMode: false).ResolveAsync(requested, Ct));
    }

    [Fact]
    public async Task without_simple_mode_the_caller_has_to_name_a_workspace()
    {
        string connection = await _fixture.CreateIsolatedDatabaseAsync("devbuddy_no_simple_mode");
        await using DevBuddyDbContext context = PostgresFixture.CreateContextFor(connection, null);

        context.Workspaces.Add(RowMappers.ToRow(
            new Workspace(WorkspaceId.New(), "Only", UserId.New(), Seed.Now)));

        await context.SaveChangesAsync(Ct);

        Assert.Null(await Resolver(context, simpleMode: false).ResolveAsync(null, Ct));
    }

    private static WorkspaceResolver Resolver(DevBuddyDbContext context, bool simpleMode) =>
        new(context, Options.Create(new IdentitySettings { SingleWorkspaceMode = simpleMode }));
}
