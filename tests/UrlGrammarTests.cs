using K9o;
using Xunit;

namespace K9o.Tests;

public class UrlGrammarTests
{
    [Theory]
    [InlineData("https://api.k9o.io", "https://api.k9o.io")]
    [InlineData("https://acme.api.k9o.io", "https://acme.api.k9o.io")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("https://api.k9o.io/", "https://api.k9o.io")]
    public void ValidateBaseUrl_Accepts(string input, string expected)
    {
        Assert.Equal(expected, UrlGrammar.ValidateBaseUrl(input));
    }

    [Theory]
    [InlineData("https://api.k9o.io/v2")]
    [InlineData("wss://api.k9o.io")]
    [InlineData("api.k9o.io")]
    [InlineData("https://api.k9o.io?x=1")]
    public void ValidateBaseUrl_Rejects(string input)
    {
        var e = Assert.Throws<KError>(() => UrlGrammar.ValidateBaseUrl(input));
        Assert.Equal("K9O_INVALID_BASE_URL", e.Code);
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("member-portal")]
    [InlineData("a1")]
    [InlineData("a-1-z")]
    public void ValidateSlug_Accepts(string slug)
    {
        UrlGrammar.ValidateSlug(slug, "test"); // throws on failure
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("1acme")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("a")]
    [InlineData("")]
    public void ValidateSlug_Rejects(string slug)
    {
        var e = Assert.Throws<KError>(() => UrlGrammar.ValidateSlug(slug, "test"));
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
    }

    [Fact]
    public void ResolveBaseUrl_DefaultsToApex()
    {
        Assert.Equal("https://api.k9o.io", UrlGrammar.ResolveBaseUrl(null, null));
        Assert.Equal("https://api.k9o.io", UrlGrammar.ResolveBaseUrl(null, ""));
    }

    [Theory]
    [InlineData(null, "acme", "https://acme.api.k9o.io")]
    [InlineData("https://api.k9o.io", "acme", "https://acme.api.k9o.io")]
    [InlineData("https://api.k9o.io/", "member-portal", "https://member-portal.api.k9o.io")]
    [InlineData("http://api.k9o.io", "acme", "http://acme.api.k9o.io")] // scheme preserved
    [InlineData("https://api-staging.k9o.io", "acme", "https://acme.api-staging.k9o.io")] // staging apex derives (cross-pool fix)
    public void ResolveBaseUrl_DerivesSubdomainFromApexPlusOrgSlug(string? baseUrl, string orgSlug, string expected)
    {
        Assert.Equal(expected, UrlGrammar.ResolveBaseUrl(baseUrl, orgSlug));
    }

    [Theory]
    [InlineData("https://acme.api.k9o.io", "acme", "https://acme.api.k9o.io")]   // explicit subdomain unchanged
    [InlineData("https://acme.api.k9o.io", null, "https://acme.api.k9o.io")]     // explicit subdomain, no orgSlug
    [InlineData("https://platform.acme.internal", "acme", "https://platform.acme.internal")] // self-hosted/Sovereign unchanged
    [InlineData("http://localhost:8080", "acme", "http://localhost:8080")]       // dev host unchanged
    [InlineData("https://api.k9o.io", null, "https://api.k9o.io")]               // apex, no orgSlug → unchanged
    [InlineData("https://acme.api-staging.k9o.io", "acme", "https://acme.api-staging.k9o.io")] // explicit staging subdomain unchanged
    [InlineData("https://api-staging.k9o.io", null, "https://api-staging.k9o.io")]             // staging apex, no orgSlug → unchanged
    public void ResolveBaseUrl_PassesThroughEverythingElse(string baseUrl, string? orgSlug, string expected)
    {
        Assert.Equal(expected, UrlGrammar.ResolveBaseUrl(baseUrl, orgSlug));
    }

    [Fact]
    public void ResolveBaseUrl_KeepsBaseUrlValidation()
    {
        var e = Assert.Throws<KError>(() => UrlGrammar.ResolveBaseUrl("https://api.k9o.io/v2", "acme"));
        Assert.Equal("K9O_INVALID_BASE_URL", e.Code);
    }

    [Fact]
    public void ResolveBaseUrl_ValidatesOrgSlugBeforeDeriving()
    {
        var e = Assert.Throws<KError>(() => UrlGrammar.ResolveBaseUrl("https://api.k9o.io", "Bad-Slug"));
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
    }

    [Fact]
    public void ResolveBaseUrl_ValidatesOrgSlugBeforeDerivingOnStagingApex()
    {
        var e = Assert.Throws<KError>(() => UrlGrammar.ResolveBaseUrl("https://api-staging.k9o.io", "Bad_Slug"));
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
    }

    [Fact]
    public void ProjectPath_StripsLeadingSlash()
    {
        Assert.Equal("/@member-portal/data/posts",
            UrlGrammar.ProjectPath("member-portal", "data/posts"));
        Assert.Equal("/@acme/project", UrlGrammar.ProjectPath("acme", "/project"));
    }

    [Fact]
    public void OrgPath_PrefixesSlash()
    {
        Assert.Equal("/projects", UrlGrammar.OrgPath("projects"));
        Assert.Equal("/projects", UrlGrammar.OrgPath("/projects"));
    }
}
