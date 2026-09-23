using System.Net;
using System.Net.Sockets;
using DevBuddy.Infrastructure.Scanning;

namespace DevBuddy.Infrastructure.Embeddings;

/// <summary>
/// Holds <see cref="EmbeddingProviderKind.SelfHosted"/> to what it claims: an endpoint on a
/// private address, in the deployment or on the network around it (2026-09-23).
/// <para>
/// The mode is what tells the gateway and <c>embedding-check</c> whether project text leaves, so a
/// cloud machine configured as self-hosted would send text out while every report said it had
/// not. "Private" is <see cref="UrlGuard.IsPrivate"/>'s definition, so the installation has one
/// answer to that question and not two. It catches a public address, which is the mistake a
/// setting can make; it cannot see a VPN route to a machine elsewhere, and says so in
/// <c>docs/operations/deployment.md</c>.
/// </para>
/// </summary>
internal static class SelfHostedEndpoint
{
    /// <summary>The endpoint's host when it is an address literal on a public address, otherwise null.</summary>
    public static IPAddress? PublicLiteral(Uri endpoint) =>
        IPAddress.TryParse(endpoint.DnsSafeHost, out IPAddress? literal) && !UrlGuard.IsPrivate(literal)
            ? literal
            : null;

    /// <summary>
    /// Null when every address <paramref name="endpoint"/> resolves to is private; otherwise why the
    /// call is refused. Every address, not the first, for the reason <see cref="UrlGuard"/> gives.
    /// </summary>
    public static async Task<string?> RefusalAsync(
        Uri endpoint, IHostResolver resolver, CancellationToken cancellationToken)
    {
        string host = endpoint.DnsSafeHost;
        IReadOnlyList<IPAddress> addresses;

        try
        {
            addresses = IPAddress.TryParse(host, out IPAddress? literal)
                ? [literal]
                : await resolver.ResolveAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return $"The self-hosted embedding endpoint's host {host} could not be resolved.";
        }

        if (addresses.Count == 0)
        {
            return $"The self-hosted embedding endpoint's host {host} resolved to no addresses.";
        }

        IPAddress? exposed = addresses.FirstOrDefault(address => !UrlGuard.IsPrivate(address));
        return exposed is null ? null : Refusal(host, exposed);
    }

    public static string Refusal(string host, IPAddress address) =>
        $"The self-hosted embedding endpoint {host} is on the public address {address}. Text sent "
        + "there leaves this installation, so configure it as Embedding:Provider=HostedApi, with "
        + "its host on OutboundAccess:AllowedHosts and an acceptance in info.md.";
}
