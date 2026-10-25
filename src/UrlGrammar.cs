using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace K9o;

/// <summary>URL grammar helpers.</summary>
public static class UrlGrammar
{
    private static readonly Regex SlugRegex = new(@"^[a-z][a-z0-9-]{1,62}$", RegexOptions.Compiled);

    /// <summary>
    /// Bare platform apex hosts org-subdomain derivation applies to — production
    /// and staging share the <c>&lt;orgSlug&gt;.&lt;apex&gt;</c> wire-routing scheme
    /// (confirmed live). A third apex needs the same confirmation before joining
    /// this set.
    /// </summary>
    private static readonly HashSet<string> K9oOrgDerivableApexes = new() { "api.k9o.io", "api-staging.k9o.io" };

    /// <summary>
    /// Validate + normalise a baseUrl.
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
                "Pass scheme + host like \"https://api.k9o.io\".",
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
    /// Resolve the effective baseUrl. <paramref name="baseUrl"/>
    /// is OPTIONAL — it defaults to the platform apex <c>https://api.k9o.io</c>. When the
    /// (validated) host is a bare apex in <see cref="K9oOrgDerivableApexes"/> and
    /// <paramref name="orgSlug"/> is supplied, the SDK composes the canonical org-subdomain wire
    /// root <c>https://&lt;orgSlug&gt;.&lt;apex&gt;</c> (, scheme preserved). An explicit
    /// <c>&lt;orgSlug&gt;</c> subdomain, a self-hosted / Sovereign host, or a dev host
    /// (anything not in that set) passes through unchanged — <paramref name="orgSlug"/> is
    /// intentionally NOT composed onto a non-apex host.
    ///
    /// Fixed 2026-08-30 (SDK-2): this used to silently ignore <paramref name="orgSlug"/> for
    /// <c>api-staging.k9o.io</c> too, not just genuine self-hosted/custom hosts, since only
    /// the literal production apex string was checked.
    /// </summary>
    public static string ResolveBaseUrl(string? baseUrl, string? orgSlug)
    {
        var validated = ValidateBaseUrl(baseUrl ?? "https://api.k9o.io");
        var uri = new Uri(validated, UriKind.Absolute);
        if (K9oOrgDerivableApexes.Contains(uri.Host) && !string.IsNullOrEmpty(orgSlug))
        {
            ValidateSlug(orgSlug, "org");
            return $"{uri.Scheme}://{orgSlug}.{uri.Host}";
        }
        return validated;
    }

    /// <summary>Validate org/project slug.</summary>
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
