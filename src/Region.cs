using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("K9o.Core.Tests")]

namespace K9o;

/// <summary>
/// Regional host resolution.
///
/// The region-free org host <c>&lt;org&gt;.api[-staging].&lt;rest&gt;</c> redirects GET/HEAD only and refuses a request
/// carrying a body, Authorization or cookie. The SDK learns the region with ONE unauthenticated
/// <c>HEAD</c> to the configured host that does not follow redirects, takes the 308
/// <c>Location</c>, caches the regional origin and sends every request there.
///
/// Nothing here knows an apex: the host comes from the configured base URL and is read with
/// <see cref="UrlGrammar.ParseApiHost"/>. Held here: only a region-free org host is probed; the probe carries no Authorization/cookie/default header;
/// the Location is trusted only if https, no userinfo, no port and host exactly the same org on the same apex
/// family with a 4-character region; any other outcome keeps the configured base URL; a definite answer is cached,
/// a network failure is not.
/// </summary>
internal static class Region
{
    /// <summary>Probe transport used when the caller supplied no HttpClient: never follows redirects, no cookies.</summary>
    internal static HttpClientHandler CreateProbeHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    };

    internal static readonly HttpClient NoRedirectClient = new(CreateProbeHandler());

    /// <summary>The regional origin a Location may name for <paramref name="host"/>, or null. Pure.</summary>
    internal static string? OriginFromLocation(string host, string location, string baseUrl)
    {
        var from = UrlGrammar.ParseApiHost(host);
        if (from == null || from.OrgSlug == null || from.Region != null) return null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) return null;
        if (!Uri.TryCreate(baseUri, location, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps || u.UserInfo.Length != 0 || !u.IsDefaultPort) return null;
        var to = UrlGrammar.ParseApiHost(u.IdnHost);
        if (to == null || to.Region == null) return null;
        if (to.OrgSlug != from.OrgSlug || to.Family != from.Family || to.Apex != from.Apex) return null;
        return $"https://{u.IdnHost}";
    }

    /// <summary>The origin requests go to: the cached regional origin once learned, else the configured base URL.</summary>
    internal static async Task<string> ResolveAsync(HttpClientConfig config, CancellationToken ct)
    {
        Task<string> task;
        lock (config.RegionLock)
        {
            config.RegionTask ??= ProbeAsync(config);
            task = config.RegionTask;
        }
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string> ProbeAsync(HttpClientConfig config)
    {
        // Yield so the caller has stored this task in RegionTask before a synchronous failure can clear it.
        await Task.Yield();
        var (origin, cacheable) = await ProbeOnceAsync(config).ConfigureAwait(false);
        if (!cacheable)
        {
            lock (config.RegionLock) config.RegionTask = null;
        }
        return origin;
    }

    private static async Task<(string, bool)> ProbeOnceAsync(HttpClientConfig config)
    {
        var configured = config.BaseUrl;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)) return (configured, true);
        var host = uri.IdnHost;
        var parsed = UrlGrammar.ParseApiHost(host);
        if (parsed == null || parsed.OrgSlug == null || parsed.Region != null) return (configured, true);

        var client = config.ProbeClient;
        // A caller-supplied HttpClient may carry its own default Authorization / Cookie; the probe must not,
        // and a request cannot remove client defaults. Do not probe through such a client.
        var defaults = client.DefaultRequestHeaders;
        if (defaults.Authorization != null || defaults.Contains("Cookie") || defaults.Contains("Proxy-Authorization"))
            return (configured, true);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var req = new HttpRequestMessage(HttpMethod.Head, configured.TrimEnd('/') + "/");
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            if ((int)res.StatusCode == 308 && res.Headers.Location != null)
            {
                var origin = OriginFromLocation(host, res.Headers.Location.OriginalString, configured);
                if (origin != null) return (origin, true);
            }
            return (configured, true);
        }
        catch (Exception)
        {
            return (configured, false);
        }
    }
}
