using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace K9o;

/// <summary>URL grammar helpers.</summary>
public static class UrlGrammar
{
    private static readonly Regex SlugRegex = new(@"^[a-z][a-z0-9-]{1,62}$", RegexOptions.Compiled);

    /// <summary>The convenience default when the caller passes no baseUrl. A default value only: never used to parse a host.</summary>
    public const string DefaultBaseUrl = "https://api.k9o.io";

    private static readonly Regex ApiLabel = new(@"^(api|api-staging)(?:-([a-z]{2}[0-9]{2}))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The structure of a gateway API host, read from the host itself. <see cref="OrgSlug"/> is the first label of an
    /// org host (null for a platform host); <see cref="Family"/> is <c>api</c> or <c>api-staging</c>;
    /// <see cref="Region"/> is the 4-character region code of a regional host (<c>api-de01</c>), else null;
    /// <see cref="Apex"/> is everything after the gateway label, never matched against a list.
    /// </summary>
    public sealed record ApiHost(string? OrgSlug, string Family, string? Region, string Apex);

    /// <summary>
    /// Read the org slug and region out of a gateway host without knowing its apex. The gateway label is <c>api</c> or
    /// <c>api-staging</c>, optionally followed by <c>-&lt;rgn&gt;</c>; the label before it is the org slug and whatever
    /// follows it is the apex. Returns null for any other shape (localhost, an IP, a single-label host, a host with no
    /// gateway label), which callers use exactly as given.
    /// </summary>
    public static ApiHost? ParseApiHost(string? host)
    {
        if (host == null) return null;
        var labels = host.ToLowerInvariant().Split('.');
        foreach (var l in labels) if (l.Length == 0) return null;
        if (labels.Length >= 3 && SlugRegex.IsMatch(labels[0]) && !labels[0].EndsWith("-") && ApiLabel.IsMatch(labels[1]))
            return MakeApiHost(labels[1], labels[0], labels[2..]);
        if (labels.Length >= 2 && ApiLabel.IsMatch(labels[0]))
            return MakeApiHost(labels[0], null, labels[1..]);
        return null;
    }

    private static ApiHost? MakeApiHost(string gatewayLabel, string? orgSlug, string[] rest)
    {
        var m = ApiLabel.Match(gatewayLabel);
        if (!m.Success || rest.Length == 0) return null;
        return new ApiHost(orgSlug, m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null, string.Join(".", rest));
    }

    /// <summary>
    /// Validate + normalise a baseUrl
    /// Returns the normalised form on success; throws <see cref="KError"/> with
    /// <c>K9O_INVALID_BASE_URL</c> on violation.
    /// </summary>
    public static string ValidateBaseUrl(string baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new KError(
                "K9O_INVALID_BASE_URL",
                $"baseUrl \"{baseUrl}\" is not a valid URL",
                new Dictionary<string, object?> { ["baseUrl"] = baseUrl },
                "Pass scheme + host, for example \"https://api.example.com\".",
                null, null
            );
        }
        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            throw new KError(
                "K9O_INVALID_BASE_URL",
                $"baseUrl \"{baseUrl}\" uses unsupported scheme \"{uri.Scheme}\"; expected http or https",
                new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["scheme"] = uri.Scheme },
                null, null, null
            );
        }
        if (string.IsNullOrEmpty(uri.Host))
        {
            throw new KError(
                "K9O_INVALID_BASE_URL",
                $"baseUrl \"{baseUrl}\" missing host",
                new Dictionary<string, object?> { ["baseUrl"] = baseUrl },
                null, null, null
            );
        }
        if (uri.AbsolutePath != "" && uri.AbsolutePath != "/")
        {
            throw new KError(
                "K9O_INVALID_BASE_URL",
                $"baseUrl \"{baseUrl}\" must not contain a path segment; got \"{uri.AbsolutePath}\"",
                new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["path"] = uri.AbsolutePath },
                null, null, null
            );
        }
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new KError(
                "K9O_INVALID_BASE_URL",
                $"baseUrl \"{baseUrl}\" must not contain query or fragment",
                new Dictionary<string, object?> { ["baseUrl"] = baseUrl },
                null, null, null
            );
        }

        return uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    /// <summary>
    /// Resolve the effective baseUrl. <paramref name="baseUrl"/> is OPTIONAL: omitted, it defaults to
    /// <see cref="DefaultBaseUrl"/> (the public cloud, a convenience only). When the (validated) host is a platform
    /// host (<c>api.&lt;apex&gt;</c> or <c>api-staging.&lt;apex&gt;</c>, any apex) and <paramref name="orgSlug"/> is
    /// supplied, the canonical org host <c>&lt;orgSlug&gt;.&lt;host&gt;</c> is composed, scheme and port preserved.
    /// Any other host (an org host already, an internal or air-gapped host, localhost) passes through unchanged;
    /// <paramref name="orgSlug"/> is then only sent as a header.
    /// </summary>
    public static string ResolveBaseUrl(string? baseUrl, string? orgSlug)
    {
        var validated = ValidateBaseUrl(string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl);
        var uri = new Uri(validated, UriKind.Absolute);
        var host = ParseApiHost(uri.Host);
        if (host != null && host.OrgSlug == null && host.Region == null && !string.IsNullOrEmpty(orgSlug))
        {
            ValidateSlug(orgSlug, "org");
            return $"{uri.Scheme}://{orgSlug}.{uri.Authority}";
        }
        return validated;
    }

    /// <summary>Validate org/project slug</summary>
    public static void ValidateSlug(string slug, string kind)
    {
        if (slug == null || !SlugRegex.IsMatch(slug) || slug.EndsWith("-"))
        {
            throw new KError(
                "K9O_INVALID_FIELD_VALUE",
                $"{kind}Slug \"{slug}\" does not match required format ^[a-z][a-z0-9-]{{1,62}}$ (must not end with hyphen)",
                new Dictionary<string, object?> { ["slug"] = slug ?? "", ["kind"] = kind },
                null, null, null
            );
        }
    }

    /// <summary>Member-plane path: <c>/@&lt;slug&gt;/&lt;sub-resource&gt;</c>.</summary>
    public static string ProjectPath(string projectSlug, string subResource)
    {
        var cleaned = subResource.StartsWith("/") ? subResource[1..] : subResource;
        return $"/@{projectSlug}/{cleaned}";
    }

    /// <summary>Org-plane path: <c>/&lt;resource&gt;</c>.</summary>
    public static string OrgPath(string resource) =>
        "/" + (resource.StartsWith("/") ? resource[1..] : resource);

    /// <summary>Platform-plane path: <c>/&lt;resource&gt;</c>.</summary>
    public static string PlatformPath(string resource) =>
        "/" + (resource.StartsWith("/") ? resource[1..] : resource);
}
