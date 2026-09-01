using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Access;

/// <summary>
/// An account in the product own login system. No external identity provider is involved:
/// see ADR-0005. Credentials live in the identity store, never in this aggregate.
/// </summary>
public sealed class User
{
    public User(UserId id, string email, string displayName, DateTimeOffset createdAt)
    {
        Id = id;
        Email = Guard.NotLongerThan(Guard.NotBlank(email, nameof(email)), 320, nameof(email));
        DisplayName = Guard.NotLongerThan(
            Guard.NotBlank(displayName, nameof(displayName)), 200, nameof(displayName));
        CreatedAt = Guard.Utc(createdAt, nameof(createdAt));
    }

    public UserId Id { get; }

    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsDisabled { get; private set; }

    public void Rename(string displayName) =>
        DisplayName = Guard.NotLongerThan(
            Guard.NotBlank(displayName, nameof(displayName)), 200, nameof(displayName));

    /// <summary>
    /// Disabling stops subsequent access. It does not recall data already retrieved:
    /// that is accepted limitation AL-3, and it is stated rather than implied.
    /// </summary>
    public void Disable() => IsDisabled = true;

    public void Enable() => IsDisabled = false;
}
