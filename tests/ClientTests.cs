using System.Threading.Tasks;
using K9o;
using Xunit;

namespace K9o.Tests;

public class ClientTests
{
    [Fact]
    public void CreateConstructsWithMinimalOpts()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io" });
        Assert.Equal("https://api.k9o.io", c.BaseUrl);
        Assert.NotNull(c.Projects);
        Assert.NotNull(c.Gate);
    }

    [Fact]
    public void CreateDefaultsToApexWhenBaseUrlOmitted()
    {
        var c = K9oClient.Create(new K9oOptions { ApiKey = "k" });
        Assert.Equal("https://api.k9o.io", c.BaseUrl);
    }

    [Fact]
    public void CreateDerivesOrgSubdomainFromApexPlusOrgSlug()
    {
        var omitted = K9oClient.Create(new K9oOptions { OrgSlug = "acme" });
        Assert.Equal("https://acme.api.k9o.io", omitted.BaseUrl);

        var explicitApex = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io", OrgSlug = "acme" });
        Assert.Equal("https://acme.api.k9o.io", explicitApex.BaseUrl);
    }

    [Fact]
    public void CreatePassesThroughExplicitSubdomainAndSelfHosted()
    {
        var sub = K9oClient.Create(new K9oOptions { BaseUrl = "https://acme.api.k9o.io", OrgSlug = "acme" });
        Assert.Equal("https://acme.api.k9o.io", sub.BaseUrl);

        var selfHosted = K9oClient.Create(new K9oOptions { BaseUrl = "https://platform.acme.internal", OrgSlug = "acme" });
        Assert.Equal("https://platform.acme.internal", selfHosted.BaseUrl);

        var dev = K9oClient.Create(new K9oOptions { BaseUrl = "http://localhost:8080", OrgSlug = "acme" });
        Assert.Equal("http://localhost:8080", dev.BaseUrl);
    }

    [Fact]
    public void CreateNormalisesTrailingSlash()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io/" });
        Assert.Equal("https://api.k9o.io", c.BaseUrl);
    }

    [Fact]
    public void CreateRejectsPathBearingBaseUrl()
    {
        var e = Assert.Throws<KError>(() => K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io/v2" }));
        Assert.Equal("K9O_INVALID_BASE_URL", e.Code);
    }

    [Fact]
    public async Task DataPlaneSelectRequiresProjectSlug()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io" });
        var e = await Assert.ThrowsAsync<KError>(() => c.From("posts").Select().ExecuteAsync<object>());
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", e.Code);
    }

    [Fact]
    public async Task DataPlaneUpdateRequiresIdFilter()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io", ProjectSlug = "member-portal" });
        var patch = new { title = "x" };
        var e = await Assert.ThrowsAsync<KError>(() => c.From("posts").Update(patch).ExecuteAsync<object>());
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
    }

    [Fact]
    public async Task ProjectsCreateValidatesSlug()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io", OrgSlug = "acme" });
        var e = await Assert.ThrowsAsync<KError>(() => c.Projects.CreateAsync(
            new CreateProjectInput { Slug = "Bad-Slug", Name = "x" }));
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
    }

    [Fact]
    public async Task GateRequiresProjectSlug()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io" });
        var e = await Assert.ThrowsAsync<KError>(() => c.Gate.WhoamiAsync());
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", e.Code);
    }

    [Fact]
    public async Task CollectionNameValidation()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io", ProjectSlug = "member-portal" });
        var e = await Assert.ThrowsAsync<KError>(() => c.From("Bad-Name").Select().ExecuteAsync<object>());
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
    }
}
