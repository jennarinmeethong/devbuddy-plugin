using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// A machine token is bound to one person <em>and</em> one workspace, and this is where that is
/// proved against the real pipeline, the real authorization service, and real PostgreSQL.
/// <para>
/// The hole it closes was ours. A token used to carry only its owner, so one credential sitting
/// in a global plugin configuration reached every workspace that person belonged to, from
/// whichever checkout the assistant happened to be launched in. Nothing on the server could see
/// that this was unintended: each call named a workspace its owner was genuinely a member of, and
/// each call was answered correctly. The fix is not a better membership check — the membership
/// was real — it is a ceiling on the credential itself.
/// </para>
/// <para>
/// Nothing here is faked. The tokens are minted by the service that ships, resolved by it, and
/// the caller is assembled exactly as the MCP host assembles one, so a refusal in this file is a
/// refusal the deployed system would give.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class MachineTokenScopeTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task a_token_reaches_the_workspace_it_was_minted_in()
    {
        Reachable alpha = await ReachableWorkspaceAsync("scope-same");

        string token = await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "laptop");
        CallerContext caller = await CallerAsync(token);

        Assert.True((await ReadAsync(alpha, caller)).IsSuccess);
    }

    /// <summary>
    /// The scenario the whole change exists for: one person, two workspaces, a live grant and an
    /// enabled AI policy in both, and a token that still only works in one of them.
    /// </summary>
    [Fact]
    public async Task a_token_from_one_workspace_is_refused_in_another_the_same_person_belongs_to()
    {
        UserId user = await _fixture.CreateUserAsync($"two-workspaces-{Guid.NewGuid():N}@example.com");

        Reachable alpha = await ReachableWorkspaceAsync("scope-a", user);
        Reachable beta = await ReachableWorkspaceAsync("scope-b", user);

        string alphaToken = await _fixture.IssueMachineTokenAsync(user, alpha.Workspace, "laptop");
        CallerContext caller = await CallerAsync(alphaToken);

        // The membership is real, the AI policy is on, and the record is readable to this person
        // in a browser. The credential is what says no.
        UseCaseResult<KnowledgeRecordView> refused = await ReadAsync(beta, caller);

        Assert.Equal(ExecutionOutcome.Denied, refused.Outcome);
        Assert.Contains("credential", refused.Reason, StringComparison.OrdinalIgnoreCase);

        // And the same token still works where it belongs, so this is a boundary rather than a
        // token that simply stopped working.
        Assert.True((await ReadAsync(alpha, caller)).IsSuccess);
    }

    [Fact]
    public async Task the_refusal_runs_in_both_directions()
    {
        UserId user = await _fixture.CreateUserAsync($"both-ways-{Guid.NewGuid():N}@example.com");

        Reachable alpha = await ReachableWorkspaceAsync("both-a", user);
        Reachable beta = await ReachableWorkspaceAsync("both-b", user);

        CallerContext fromBeta = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(user, beta.Workspace, "desktop"));

        UseCaseResult<KnowledgeRecordView> refused = await ReadAsync(alpha, fromBeta);

        Assert.Equal(ExecutionOutcome.Denied, refused.Outcome);
        Assert.True((await ReadAsync(beta, fromBeta)).IsSuccess);
    }

    [Fact]
    public async Task one_person_holds_a_working_token_for_each_workspace_at_once()
    {
        UserId user = await _fixture.CreateUserAsync($"one-each-{Guid.NewGuid():N}@example.com");

        Reachable alpha = await ReachableWorkspaceAsync("each-a", user);
        Reachable beta = await ReachableWorkspaceAsync("each-b", user);

        CallerContext forAlpha = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(user, alpha.Workspace, "laptop, Acme"));

        CallerContext forBeta = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(user, beta.Workspace, "laptop, Globex"));

        // Two credentials, two workspaces, no overlap. Which assistant holds which is not a
        // question the server asks or could answer.
        Assert.True((await ReadAsync(alpha, forAlpha)).IsSuccess);
        Assert.True((await ReadAsync(beta, forBeta)).IsSuccess);
        Assert.Equal(ExecutionOutcome.Denied, (await ReadAsync(beta, forAlpha)).Outcome);
        Assert.Equal(ExecutionOutcome.Denied, (await ReadAsync(alpha, forBeta)).Outcome);
    }

    /// <summary>
    /// Several in one workspace — a laptop, a desktop, a build agent — because separate tokens are
    /// how somebody revokes one machine without disturbing the others.
    /// </summary>
    [Fact]
    public async Task several_tokens_in_one_workspace_all_work()
    {
        Reachable alpha = await ReachableWorkspaceAsync("several");

        CallerContext laptop = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "laptop"));

        CallerContext desktop = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "desktop"));

        CallerContext agent = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "build agent"));

        Assert.True((await ReadAsync(alpha, laptop)).IsSuccess);
        Assert.True((await ReadAsync(alpha, desktop)).IsSuccess);
        Assert.True((await ReadAsync(alpha, agent)).IsSuccess);
    }

    [Fact]
    public async Task a_listing_shows_this_workspace_and_never_another()
    {
        UserId user = await _fixture.CreateUserAsync($"listing-{Guid.NewGuid():N}@example.com");

        Reachable alpha = await ReachableWorkspaceAsync("list-a", user);
        Reachable beta = await ReachableWorkspaceAsync("list-b", user);

        await _fixture.IssueMachineTokenAsync(user, alpha.Workspace, "here");
        await _fixture.IssueMachineTokenAsync(user, beta.Workspace, "elsewhere");

        IReadOnlyList<MachineTokenSummary> listed =
            await _fixture.ListMachineTokensAsync(user, alpha.Workspace);

        Assert.Equal(["here"], listed.Select(token => token.Name));
    }

    [Fact]
    public async Task revoking_a_token_that_belongs_to_another_workspace_finds_nothing()
    {
        UserId user = await _fixture.CreateUserAsync($"revoke-other-{Guid.NewGuid():N}@example.com");

        Reachable alpha = await ReachableWorkspaceAsync("revoke-a", user);
        Reachable beta = await ReachableWorkspaceAsync("revoke-b", user);

        string betaToken = await _fixture.IssueMachineTokenAsync(user, beta.Workspace, "elsewhere");
        MachineTokenId betaId = (await _fixture.ListMachineTokensAsync(user, beta.Workspace))
            .Single(token => token.Name == "elsewhere").Id;

        // Not found, which is the same answer a token nobody ever issued gets. A caller must not
        // be able to learn that a credential exists somewhere they cannot reach.
        Assert.False(await _fixture.RevokeMachineTokenAsync(user, alpha.Workspace, betaId));

        // And it is untouched: the refusal is a refusal, not a quiet partial revocation.
        Assert.NotNull(await _fixture.CallerForTokenAsync(betaToken));
    }

    [Fact]
    public async Task revoking_one_token_leaves_the_others_alone()
    {
        Reachable alpha = await ReachableWorkspaceAsync("revoke-one");

        string lost = await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "lost laptop");
        string kept = await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "desktop");

        MachineTokenId lostId = (await _fixture.ListMachineTokensAsync(alpha.User, alpha.Workspace))
            .Single(token => token.Name == "lost laptop").Id;

        Assert.True(await _fixture.RevokeMachineTokenAsync(alpha.User, alpha.Workspace, lostId));

        Assert.Null(await _fixture.CallerForTokenAsync(lost));
        Assert.True((await ReadAsync(alpha, await CallerAsync(kept))).IsSuccess);
    }

    /// <summary>
    /// The upgrade case. A token issued before this change carries no workspace, and no workspace
    /// can be chosen for it after the fact without handing it authority somebody would have had to
    /// grant. It is refused, and it is listed as needing replacement so its owner can see why.
    /// </summary>
    [Fact]
    public async Task a_token_from_before_scoping_is_refused_and_listed_as_needing_replacement()
    {
        Reachable alpha = await ReachableWorkspaceAsync("legacy");

        string legacy = await _fixture.PlantLegacyUnscopedTokenAsync(alpha.User, "from the old release");

        Assert.Null(await _fixture.CallerForTokenAsync(legacy));

        MachineTokenSummary listed = (await _fixture.ListMachineTokensAsync(alpha.User, alpha.Workspace))
            .Single(token => token.Name == "from the old release");

        Assert.True(listed.NeedsReplacement);

        // Not active, whatever its expiry says. Reporting it as live would be reporting a lie:
        // the resolver refuses it on every call.
        Assert.False(listed.IsActive);
    }

    [Fact]
    public async Task a_missing_an_invented_an_expired_and_a_revoked_token_are_all_refused()
    {
        Reachable alpha = await ReachableWorkspaceAsync("bad-tokens");

        Assert.Null(await _fixture.CallerForTokenAsync(string.Empty));
        Assert.Null(await _fixture.CallerForTokenAsync("not-a-token-anybody-issued"));

        string expired = await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "old");
        MachineTokenId expiredId = (await _fixture.ListMachineTokensAsync(alpha.User, alpha.Workspace))
            .Single(token => token.Name == "old").Id;
        await _fixture.ExpireMachineTokenAsync(expiredId);
        Assert.Null(await _fixture.CallerForTokenAsync(expired));

        string revoked = await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "returned");
        MachineTokenId revokedId = (await _fixture.ListMachineTokensAsync(alpha.User, alpha.Workspace))
            .Single(token => token.Name == "returned").Id;
        await _fixture.RevokeMachineTokenAsync(alpha.User, alpha.Workspace, revokedId);
        Assert.Null(await _fixture.CallerForTokenAsync(revoked));
    }

    /// <summary>
    /// The checks that were already there still run, and still run after the credential check.
    /// A correctly scoped token buys nothing when the account behind it is switched off or the
    /// grant behind it is withdrawn.
    /// </summary>
    [Fact]
    public async Task a_revoked_membership_stops_a_correctly_scoped_token_on_the_next_call()
    {
        Reachable alpha = await ReachableWorkspaceAsync("revoked-grant");

        CallerContext caller = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "laptop"));

        Assert.True((await ReadAsync(alpha, caller)).IsSuccess);

        await _fixture.RevokeAsync(alpha.Workspace, alpha.Membership);

        UseCaseResult<KnowledgeRecordView> refused = await ReadAsync(alpha, caller);

        Assert.Equal(ExecutionOutcome.Denied, refused.Outcome);
        Assert.Contains("no access to this scope", refused.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task a_disabled_account_stops_a_correctly_scoped_token_on_the_next_call()
    {
        Reachable alpha = await ReachableWorkspaceAsync("disabled");

        CallerContext caller = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(alpha.User, alpha.Workspace, "laptop"));

        Assert.True((await ReadAsync(alpha, caller)).IsSuccess);

        await _fixture.DisableAccountAsync(alpha.User);

        UseCaseResult<KnowledgeRecordView> refused = await ReadAsync(alpha, caller);

        Assert.Equal(ExecutionOutcome.Denied, refused.Outcome);
        Assert.Contains("disabled", refused.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A token scoped to the right workspace is still not permission to read a project whose
    /// owner never opened it to AI. The credential ceiling is one gate among the several that
    /// were already there, not a replacement for any of them (SB-08).
    /// </summary>
    [Fact]
    public async Task a_project_with_ai_access_off_is_refused_to_a_correctly_scoped_token()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"ai-off-token-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Reviewer);

        // Alpha is opened to AI; Beta is not, and never was.
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Beta, $"CRQ-{Guid.NewGuid():N}"[..9], world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Beta, item.Id, "Beta", "Body.", world.Founder);

        CallerContext caller = await CallerAsync(
            await _fixture.IssueMachineTokenAsync(user, world.Workspace, "laptop"));

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<KnowledgeRecordView> refused = await session.RunAsync(
            new GetRecordUseCase(session.Resolve<IKnowledgeRepository>()),
            new GetRecordRequest(world.Beta, record.Id),
            caller);

        Assert.Equal(ExecutionOutcome.Denied, refused.Outcome);
        Assert.Contains("AI access is not enabled", refused.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The other half of the boundary: a signed-in person carries no credential scope at all, and
    /// nothing about machine tokens narrows what a browser session can reach. Somebody who is a
    /// member of two workspaces reads both, exactly as they did before this change.
    /// </summary>
    [Fact]
    public async Task a_signed_in_person_carries_no_credential_scope_and_reaches_both_workspaces()
    {
        UserId user = await _fixture.CreateUserAsync($"human-{Guid.NewGuid():N}@example.com");

        Reachable alpha = await ReachableWorkspaceAsync("human-a", user);
        Reachable beta = await ReachableWorkspaceAsync("human-b", user);

        CallerContext person = World.Human(user);
        Assert.Null(person.Credential);

        Assert.True((await ReadAsync(alpha, person)).IsSuccess);
        Assert.True((await ReadAsync(beta, person)).IsSuccess);
    }

    /// <summary>
    /// Minting a credential through the real pipeline leaves the credential out of the audit
    /// store (SB-19).
    /// <para>
    /// The response carries the token value, once, and the response is also what contributes the
    /// audit metadata — which is exactly the shape that puts a working credential into the one
    /// table designed to be kept for years. Asserted over every audit row and every metadata
    /// value in the workspace rather than over the one entry this call wrote, because the failure
    /// worth catching is a row somebody adds later without thinking about it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task minting_a_token_never_writes_its_value_into_the_audit_store()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"audit-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Contributor);

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<MachineTokenIssuedResponse> issued = await session.RunAsync(
            new IssueMachineTokenUseCase(session.Resolve<IMachineTokenService>()),
            new IssueMachineTokenRequest(world.Workspace, "laptop"),
            World.Human(user));

        Assert.True(issued.IsSuccess);

        string value = issued.Value!.Token;
        Assert.NotEmpty(value);

        List<AuditEventRow> entries = await session.Db.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.WorkspaceId == world.Workspace.Value)
            .ToListAsync();

        Assert.NotEmpty(entries);

        foreach (AuditEventRow entry in entries)
        {
            Assert.DoesNotContain(value, entry.ResourceReference, StringComparison.Ordinal);

            foreach (string detail in entry.Details.Values)
            {
                Assert.DoesNotContain(value, detail, StringComparison.Ordinal);
            }
        }

        // The name is what identifies it to a person, and the identifier is what a later
        // revocation refers to. Both are safe, and both being there is what makes the entry worth
        // keeping at all.
        Assert.Contains(
            entries,
            entry => entry.Details.ContainsValue(issued.Value.TokenId.Value.ToString()));
    }

    /// <summary>One workspace a given person can genuinely read a record in, on the AI channel.</summary>
    private sealed record Reachable(
        WorkspaceId Workspace,
        Domain.Tenancy.ProjectScope Project,
        KnowledgeRecordId Record,
        UserId User,
        MembershipId Membership);

    private async Task<Reachable> ReachableWorkspaceAsync(string label, UserId? existing = null)
    {
        World world = await _fixture.CreateWorldAsync();

        UserId user = existing
            ?? await _fixture.CreateUserAsync($"{label}-{Guid.NewGuid():N}@example.com");

        MembershipId membership = await _fixture.GrantAsync(world.Workspace, user, Role.Reviewer);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        WorkItem item = await _fixture.SeedWorkItemAsync(
            world.Alpha, $"CRQ-{Guid.NewGuid():N}"[..9], world.Founder);

        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, $"Record for {label}", "Body.", world.Founder);

        return new Reachable(world.Workspace, world.Alpha, record.Id, user, membership);
    }

    private async Task<CallerContext> CallerAsync(string token) =>
        await _fixture.CallerForTokenAsync(token)
        ?? throw new InvalidOperationException("The token did not resolve, and this test needs one that does.");

    private async Task<UseCaseResult<KnowledgeRecordView>> ReadAsync(
        Reachable target, CallerContext caller)
    {
        using Session session = _fixture.OpenSession(target.Workspace);

        return await session.RunAsync(
            new GetRecordUseCase(session.Resolve<IKnowledgeRepository>()),
            new GetRecordRequest(target.Project, target.Record),
            caller);
    }
}
