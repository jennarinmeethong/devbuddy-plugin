using DevBuddy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DevBuddy.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations` build a context without a running application.
/// <para>
/// The connection string here is a placeholder used only to pick the provider and generate SQL.
/// No migration command in this repository connects to a real database, and none should: the
/// migration is applied by the application or by the CLI at deploy time.
/// </para>
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DevBuddyDbContext>
{
    public DevBuddyDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<DevBuddyDbContext> options =
            new DbContextOptionsBuilder<DevBuddyDbContext>()
                .UseNpgsql("Host=localhost;Database=devbuddy_design_time;Username=postgres")
                .Options;

        return new DevBuddyDbContext(options, new DesignTimeTenantContext());
    }

    /// <summary>No tenant at design time. Migrations do not run queries.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public WorkspaceId? WorkspaceId => null;
    }
}
