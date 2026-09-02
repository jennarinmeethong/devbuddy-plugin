using DevBuddy.Application.Abstractions;

namespace DevBuddy.Infrastructure.Security;

/// <summary>
/// A redactor that redacts nothing. **Phase 6 replaces this.**
/// <para>
/// The pipeline has a redaction stage from Phase 2 and needs something behind it to run at all.
/// This is that something, and it is named so nobody can mistake it for a control: it returns the
/// text it was given, unchanged, every time.
/// </para>
/// <para>
/// It is registered so the system is runnable end to end while the phases that need it are built.
/// Control SB-17 is <b>not</b> satisfied by this type, and the verification matrix says so. Do not
/// connect real project data until the Phase 6 scanner is in place.
/// </para>
/// </summary>
internal sealed class UnimplementedRedactor : IRedactor
{
    public string Redact(string text) => text;
}

/// <summary>
/// A scanner that finds nothing. **Phase 6 replaces this.**
/// <para>
/// Returning Clean is the dangerous direction, and it is deliberate only because nothing in the
/// system currently treats a Clean result as permission to release: newly stored evidence is
/// NotScanned, and nothing marks it otherwise. The moment something does, this type has to be
/// gone.
/// </para>
/// </summary>
internal sealed class UnimplementedSecretScanner : ISecretScanner
{
    public Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken) =>
        Task.FromResult(SecretScanResult.Clean);
}
