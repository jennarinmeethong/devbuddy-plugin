using DevBuddy.Domain.Common;

namespace DevBuddy.Infrastructure.Persistence;

/// <summary>
/// The workspace the current request belongs to, set by the host from the authenticated
/// principal.
/// <para>
/// This drives the global query filters, which are defence in depth **behind** the authorization
/// check in the application pipeline, never a replacement for it. The filters exist so that a
/// query somebody adds later, and forgets to scope, returns nothing rather than everything.
/// </para>
/// <para>
/// It fails closed. When no workspace is set, the filter matches no rows at all rather than
/// matching all of them. A background job that legitimately spans workspaces has to say so with
/// <c>IgnoreQueryFilters</c>, which is visible in review and greppable.
/// </para>
/// </summary>
public interface ITenantContext
{
    WorkspaceId? WorkspaceId { get; }
}

/// <summary>
/// A tenant context that can be set per unit of work. Hosts replace this with one that reads the
/// authenticated principal; the console and integration tests set it explicitly.
/// </summary>
public sealed class MutableTenantContext : ITenantContext
{
    public WorkspaceId? WorkspaceId { get; private set; }

    public void EnterWorkspace(WorkspaceId workspaceId) => WorkspaceId = workspaceId;

    public void Leave() => WorkspaceId = null;
}
