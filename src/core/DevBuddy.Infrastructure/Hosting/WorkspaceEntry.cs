using System.Text.Json;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;

namespace DevBuddy.Infrastructure.Hosting;

/// <summary>
/// Works out which workspace a set of operation arguments belongs to, and enters it.
/// <para>
/// Shared by every host rather than written once per transport. The workspace decides what the
/// global query filters can see, and three hosts each deciding it their own way would be three
/// chances for one of them to enter a workspace the caller did not name — the exact failure the
/// filters exist to catch.
/// </para>
/// <para>
/// Entering a workspace authorises nothing. It says which tenant's rows are even visible to the
/// query; the pipeline then checks the caller's membership of it (SB-11). A caller who names a
/// workspace they do not belong to gets an empty database and then a refusal.
/// </para>
/// </summary>
public static class WorkspaceEntry
{
    /// <summary>
    /// Enters the workspace named in the arguments, falling back to the resolver, which answers
    /// only in single-workspace mode. Returns the workspace entered, or null.
    /// <para>
    /// Nothing is entered when nothing can be resolved, and the filters then match no rows. A
    /// request that never said which workspace it meant failing closed is the right direction.
    /// </para>
    /// </summary>
    public static async Task<WorkspaceId?> EnterAsync(
        JsonElement arguments,
        MutableTenantContext tenant,
        IWorkspaceResolver workspaces,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(workspaces);

        Guid? claimed = Find(arguments);

        WorkspaceId? resolved = await workspaces.ResolveAsync(
            claimed is { } value ? new WorkspaceId(value) : null, cancellationToken);

        if (resolved is { } workspace)
        {
            tenant.EnterWorkspace(workspace);
        }

        return resolved;
    }

    /// <summary>
    /// Reads the workspace out of the arguments, accepting either a top-level identifier or one
    /// inside a scope, because both shapes appear across the operation set.
    /// </summary>
    public static Guid? Find(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (arguments.TryGetProperty("workspaceId", out JsonElement direct)
            && direct.TryGetGuid(out Guid workspace))
        {
            return workspace;
        }

        if (arguments.TryGetProperty("scope", out JsonElement scope)
            && scope.ValueKind == JsonValueKind.Object
            && scope.TryGetProperty("workspaceId", out JsonElement nested)
            && nested.TryGetGuid(out Guid scoped))
        {
            return scoped;
        }

        return null;
    }
}
