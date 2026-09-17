using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace DevBuddy.Infrastructure.Persistence;

/// <summary>
/// How every host connects to PostgreSQL.
/// </summary>
internal static class DatabaseConnection
{
    /// <summary>
    /// The connection string, with GSS encryption switched off unless an operator chose otherwise.
    /// <para>
    /// This system signs in to PostgreSQL with a password and never with Kerberos. Left at its
    /// default, the driver still probes for GSS encryption, and on the chiseled images that means
    /// trying to load <c>libgssapi_krb5.so.2</c>, which is not there. Every console run then began
    /// with "Cannot load library libgssapi_krb5.so.2" on standard error, which reads like a fault
    /// and is not one. An operator who does use GSS keeps whatever their connection string says.
    /// </para>
    /// </summary>
    public static string WithoutGssProbe(string connectionString)
    {
        DbConnectionStringBuilder given = new() { ConnectionString = connectionString };

        bool chosen = given.Keys.Cast<string>().Any(key =>
            string.Equals(key.Replace(" ", string.Empty, StringComparison.Ordinal), "GssEncryptionMode", StringComparison.OrdinalIgnoreCase));

        if (chosen)
        {
            return connectionString;
        }

        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
        }.ConnectionString;
    }

    /// <summary>
    /// The provider options every context uses.
    /// <para>
    /// Single-query loading is stated rather than left to the default. It is what EF already did,
    /// so nothing changes, but unstated it logs a warning on every query that loads a record with
    /// its revisions and approvals. Splitting would load those in separate statements, and without
    /// a transaction around them the rows could change between one statement and the next.
    /// </para>
    /// </summary>
    public static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
}
