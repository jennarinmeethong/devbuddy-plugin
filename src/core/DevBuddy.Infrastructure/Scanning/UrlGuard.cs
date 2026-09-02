using System.Net;
using System.Net.Sockets;
using DevBuddy.Domain.Common;

namespace DevBuddy.Infrastructure.Scanning;

/// <summary>Resolves a host name to addresses. Injectable so the guard can be tested.</summary>
public interface IHostResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class DnsHostResolver : IHostResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host, CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>Where outbound requests are allowed to go.</summary>
public sealed class OutboundAccessOptions
{
    public const string SectionName = "OutboundAccess";

    /// <summary>
    /// Hosts analysis and synchronisation may reach. Empty means nothing is reachable, which is
    /// the correct default for a system whose analysers read attacker-controlled content.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>
    /// Set only for a deployment that genuinely needs to reach an internal service, and then only
    /// with a host allow-list that names it. Off by default because the internal network is what
    /// an SSRF is usually reaching for.
    /// </summary>
    public bool AllowPrivateAddresses { get; set; }
}

/// <summary>
/// Decides whether an outbound request may be made.
/// <para>
/// Control SB-06 and part of SB-03. Two independent gates, and both have to pass: the host must be
/// on the allow-list, and every address it resolves to must be public. The allow-list alone is not
/// enough, because a name on it can be pointed at 127.0.0.1 by whoever controls the DNS record.
/// </para>
/// <para>
/// The address check is re-run for every request rather than cached. A name that resolved to a
/// public address a minute ago is not the same promise as a name that resolves to one now, which
/// is the whole shape of a DNS rebinding attack.
/// </para>
/// </summary>
public sealed class UrlGuard(OutboundAccessOptions options, IHostResolver resolver)
{
    private readonly OutboundAccessOptions _options = Guard.NotNull(options, nameof(options));
    private readonly IHostResolver _resolver = Guard.NotNull(resolver, nameof(resolver));

    public async Task<UrlDecision> InspectAsync(string? url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return UrlDecision.Refuse("The value is not an absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            // file:, gopher:, and their relatives are how a URL fetcher becomes a file reader.
            return UrlDecision.Refuse($"The scheme {uri.Scheme} is not permitted.");
        }

        if (!IsAllowedHost(uri.Host))
        {
            return UrlDecision.Refuse($"The host {uri.Host} is not on the allow-list.");
        }

        IReadOnlyList<IPAddress> addresses;

        try
        {
            addresses = IPAddress.TryParse(uri.Host, out IPAddress? literal)
                ? [literal]
                : await _resolver.ResolveAsync(uri.Host, cancellationToken);
        }
        catch (SocketException)
        {
            return UrlDecision.Refuse($"The host {uri.Host} could not be resolved.");
        }

        if (addresses.Count == 0)
        {
            return UrlDecision.Refuse($"The host {uri.Host} resolved to no addresses.");
        }

        if (!_options.AllowPrivateAddresses)
        {
            // Every address, not just the first. A name that resolves to one public address and
            // one loopback address is a rebinding attack with the work already done.
            foreach (IPAddress address in addresses)
            {
                if (IsPrivate(address))
                {
                    return UrlDecision.Refuse(
                        $"The host {uri.Host} resolves to a non-public address.");
                }
            }
        }

        return UrlDecision.Allow(uri);
    }

    /// <summary>
    /// Exact host match or a single-label subdomain of an allow-listed suffix written as
    /// <c>.example.com</c>. Deliberately not a wildcard: <c>*.com</c> is not an allow-list.
    /// </summary>
    private bool IsAllowedHost(string host)
    {
        foreach (string allowed in _options.AllowedHosts)
        {
            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (allowed.StartsWith('.')
                && host.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)
                && host.Length > allowed.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Loopback, link-local (including the cloud metadata address), private ranges, unique local
    /// addresses, and anything that is not IPv4 or IPv6.
    /// </summary>
    internal static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPrivate(address.MapToIPv4());
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] octets = address.GetAddressBytes();

            return octets[0] switch
            {
                0 => true,
                10 => true,
                127 => true,
                169 when octets[1] == 254 => true,
                172 when octets[1] >= 16 && octets[1] <= 31 => true,
                192 when octets[1] == 168 => true,
                100 when octets[1] >= 64 && octets[1] <= 127 => true,
                >= 224 => true,
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast
                || address.Equals(IPAddress.IPv6Any);
        }

        return true;
    }
}

/// <summary>Whether a request may be made, and why not when it may not.</summary>
public sealed record UrlDecision(bool IsAllowed, Uri? Target, string Reason)
{
    public static UrlDecision Allow(Uri target) => new(true, target, "Permitted.");

    public static UrlDecision Refuse(string reason) => new(false, null, reason);
}
