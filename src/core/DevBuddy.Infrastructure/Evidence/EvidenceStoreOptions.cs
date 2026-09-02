using System.Globalization;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Infrastructure.Evidence;

/// <summary>Which evidence store to use. MinIO is the default (ADR-0004).</summary>
public enum EvidenceStoreProvider
{
    /// <summary>S3-compatible object storage. MinIO in the shipped Compose stack.</summary>
    ObjectStorage = 1,

    /// <summary>Local filesystem. For tests and single-host installations only.</summary>
    FileSystem = 2,
}

/// <summary>
/// How evidence is stored. Defaults are the self-hosted MinIO stack; nothing here reaches a
/// managed service unless an operator points it at one.
/// </summary>
public sealed class EvidenceStoreOptions
{
    public const string SectionName = "Evidence";

    public EvidenceStoreProvider Provider { get; set; } = EvidenceStoreProvider.ObjectStorage;

    /// <summary>The S3-compatible endpoint. For MinIO in Compose, the service address.</summary>
    public string ServiceUrl { get; set; } = "http://minio:9000";

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Bucket names are derived as prefix plus workspace identifier, so one workspace can never
    /// address another bucket by construction rather than by a check somebody has to remember.
    /// </summary>
    public string BucketPrefix { get; set; } = "devbuddy";

    /// <summary>Root directory for the filesystem provider.</summary>
    public string RootPath { get; set; } = "./.data/evidence";

    /// <summary>
    /// Refuse anything larger. An upload limit is an availability control (SB-21) before it is a
    /// storage-cost one: hashing requires buffering, and unbounded buffering is a denial of
    /// service anyone with an account can perform.
    /// </summary>
    public long MaxObjectBytes { get; set; } = 100L * 1024 * 1024;

    public bool UseServerSideEncryption { get; set; } = true;

    /// <summary>One bucket per workspace. Lowercase, because S3 bucket names must be.</summary>
    public string BucketFor(WorkspaceId workspaceId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{BucketPrefix}-{workspaceId.Value}").ToLowerInvariant();

    /// <summary>
    /// Content-addressed, under a project prefix. The two-character fan-out keeps directory
    /// listings usable on the filesystem provider; the hash makes a substituted artefact
    /// detectable.
    /// </summary>
    public static string KeyFor(ProjectScope scope, ContentHash contentHash)
    {
        string hash = contentHash.Value.ToLowerInvariant();
        return $"{scope.ProjectId.Value}/{hash[..2]}/{hash}";
    }
}
