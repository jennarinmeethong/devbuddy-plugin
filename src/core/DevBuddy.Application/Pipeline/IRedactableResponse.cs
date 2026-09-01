using DevBuddy.Application.Abstractions;

namespace DevBuddy.Application.Pipeline;

/// <summary>
/// A response that knows which of its own fields are free text, and can return a copy with those
/// fields redacted.
/// <para>
/// The response type does this rather than the pipeline, because only the response knows which
/// strings are prose read from an untrusted source and which are identifiers that must survive
/// intact. A pipeline that redacted every string would corrupt record identifiers; one that
/// redacted none would leak.
/// </para>
/// </summary>
/// <typeparam name="TSelf">The implementing type, so redaction returns the same shape.</typeparam>
public interface IRedactableResponse<out TSelf>
{
    TSelf Redact(IRedactor redactor);
}
