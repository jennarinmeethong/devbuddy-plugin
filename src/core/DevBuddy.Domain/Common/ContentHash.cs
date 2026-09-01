using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DevBuddy.Domain.Common;

/// <summary>
/// A SHA-256 hash of canonical content, held as uppercase hexadecimal.
/// Approval binds to one of these, which is what makes control SB-23 checkable: a reviewer
/// approves a hash, and publication compares hashes rather than trusting a revision number.
/// </summary>
public readonly record struct ContentHash
{
    private const int HexLength = 64;

    private ContentHash(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Computes the hash of the canonical text form of a revision. Callers pass the exact
    /// text that a reviewer would read, so that what is approved is what was displayed.
    /// </summary>
    public static ContentHash FromContent(string canonicalContent)
    {
        Guard.NotNull(canonicalContent, nameof(canonicalContent));

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalContent));
        return new ContentHash(Convert.ToHexString(digest));
    }

    public static ContentHash Parse(string value)
    {
        Guard.NotBlank(value, nameof(value));

        string normalised = value.ToUpperInvariant();
        if (normalised.Length != HexLength)
        {
            throw new DomainValidationException(
                $"A content hash must be {HexLength} hexadecimal characters but was {normalised.Length}.");
        }

        foreach (char character in normalised)
        {
            if (!Uri.IsHexDigit(character))
            {
                throw new DomainValidationException("A content hash must contain hexadecimal characters only.");
            }
        }

        return new ContentHash(normalised);
    }

    public override string ToString() => Value ?? string.Empty;

    public string ToShortString() =>
        string.IsNullOrEmpty(Value)
            ? string.Empty
            : Value[..Math.Min(12, Value.Length)].ToString(CultureInfo.InvariantCulture);
}
