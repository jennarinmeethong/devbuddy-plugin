namespace DevBuddy.Application.Pipeline;

/// <summary>
/// A request carrying content that will be retained, and therefore has to be scanned first.
/// <para>
/// Control SB-17 has two halves. The egress half redacts what leaves the boundary; this is the
/// other one. info.md requires sensitive material to be detected and filtered <b>before it is
/// retained in records or logs</b>, and a draft is exactly that: text a person or an AI is asking
/// the system to keep.
/// </para>
/// <para>
/// The pipeline refuses rather than redacting here, and the difference matters. Redacting outbound
/// text loses nothing a reader was entitled to. Redacting inbound text would silently store
/// something other than what the author wrote, and they would never know. Refusing tells them
/// which rule matched and on which line, so they can take the credential out.
/// </para>
/// </summary>
public interface IScannableRequest
{
    /// <summary>Every free-text field that would be written down if this request succeeded.</summary>
    IEnumerable<string> ContentForScanning { get; }
}
