namespace DevBuddy.Application.Pipeline;

/// <summary>
/// How an execution ended. Denied and NotFound are separate, and hosts must be careful about
/// which they surface: telling a caller that a record exists but belongs to another project is
/// itself a disclosure. The pipeline never converts one into the other on its own, so that
/// decision stays visible at the boundary where it belongs.
/// </summary>
public enum ExecutionOutcome
{
    Succeeded = 1,

    /// <summary>The request was malformed. Nothing was authorised and nothing ran.</summary>
    Invalid = 2,

    /// <summary>No identity, no permission, or the AI channel reaching a denied use case.</summary>
    Denied = 3,

    /// <summary>Authorised, but the resource does not exist within the caller scope.</summary>
    NotFound = 4,

    /// <summary>Authorised and found, but a domain rule refused the operation.</summary>
    Rejected = 5,
}
