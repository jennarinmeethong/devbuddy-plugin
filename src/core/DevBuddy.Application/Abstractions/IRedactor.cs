namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Replaces sensitive material with a marker. Applied by the pipeline to any response that
/// declares itself redactable, before the response reaches a caller.
/// </summary>
public interface IRedactor
{
    /// <summary>
    /// Returns the text with anything the scanner rules match replaced. Deliberately synchronous
    /// and total: it is called on every outbound text field, and a failure to redact must not be
    /// expressible as a partially applied result.
    /// </summary>
    string Redact(string text);
}
