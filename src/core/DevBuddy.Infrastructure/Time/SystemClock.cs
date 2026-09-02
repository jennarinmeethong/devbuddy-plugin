using DevBuddy.Application.Abstractions;

namespace DevBuddy.Infrastructure.Time;

/// <summary>
/// The real clock. Always UTC, because the domain rejects any other offset and an approval
/// timestamp in local time would be ambiguous the moment a second person in another country
/// reads it.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
