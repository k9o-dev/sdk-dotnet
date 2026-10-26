using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using K9o;
using Xunit;

namespace K9o.Tests;

/// <summary>client.Storage buckets and objects against a stub transport.</summary>
public class StorageTests
{
    private const string Root = "https://acme.api.k9o.io/@member-portal/storage/buckets";

    private sealed class Stub : HttpMessageHandler
    {
        public int Calls;
        public string Method = "", Url = "";
        public string? Body;
        public byte[]? RawBody;
        public string? ContentType;
        public int Status = 200;
        public string Response = "{\"data\":null,\"meta\":{}}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Method = request.Method.Method;
            Url = request.RequestUri!.AbsoluteUri;
            ContentType = request.Content?.Headers.ContentType?.ToString();
            RawBody = request.Content != null ? await request.Content.ReadAsByteArrayAsync(ct) : null;
            Body = RawBody != null ? Encoding.UTF8.GetString(RawBody) : null;
            return new HttpResponseMessage((HttpStatusCode)Status)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (K9oClient c, Stub s) Mk(string? slug = "member-portal")
    {
        var s = new Stub();
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://acme.api.k9o.io", ProjectSlug = slug, HttpClient = new HttpClient(s) });
        return (c, s);
    }

    [Fact]
    public async Task BucketCrudRoutes()
    {
        var (c, s) = Mk();
        s.Response = "{\"data\":[],\"meta\":{}}";
        await c.Storage.ListBucketsAsync();
        Assert.Equal(("GET", Root), (s.Method, s.Url));

        s.Response = "{\"data\":{\"name\":\"a\"},\"meta\":{}}";
        await c.Storage.GetBucketAsync("a/b");
        Assert.Equal(("GET", Root + "/a%2Fb"), (s.Method, s.Url));

        await c.Storage.CreateBucketAsync("avatars", new CreateBucketOptions
        {
            Public = false, FileSizeLimit = 10_485_760, AllowedMimeTypes = new() { "image/png" }, Versioning = true,
        });
        Assert.Equal(("POST", Root), (s.Method, s.Url));
        Assert.Equal("{\"name\":\"avatars\",\"public\":false,\"fileSizeLimit\":10485760,\"allowedMimeTypes\":[\"image/png\"],\"versioning\":true}", s.Body);
        Assert.StartsWith("application/json", s.ContentType);

        await c.Storage.UpdateBucketAsync("avatars", new Dictionary<string, object?> { ["fileSizeLimit"] = null, ["deleteAfterDays"] = 30 });
        Assert.Equal(("PATCH", Root + "/avatars"), (s.Method, s.Url));
        Assert.Equal("{\"fileSizeLimit\":null,\"deleteAfterDays\":30}", s.Body);
    }

    [Fact]
    public async Task DeleteBucketSendsEmptyOnlyWhenTrue()
    {
        var (c, s) = Mk();
        await c.Storage.DeleteBucketAsync("avatars");
        Assert.Equal(("DELETE", Root + "/avatars"), (s.Method, s.Url));
        await c.Storage.DeleteBucketAsync("avatars", empty: true);
        Assert.Equal(Root + "/avatars?empty=true", s.Url);
        await c.Storage.DeleteBucketAsync("avatars", empty: false);
        Assert.Equal(Root + "/avatars", s.Url);
    }

    [Fact]
    public async Task UploadIsRealMultipartWithAllParts()
    {
        var (c, s) = Mk();
        s.Response = "{\"data\":{\"path\":\"user/a.jpg\",\"size\":3,\"lastModified\":null},\"meta\":{}}";
        await c.Storage.From("avatars").UploadAsync("user/a.jpg", new byte[] { 1, 2, 3 },
            new UploadOptions { ContentType = "image/jpeg", Upsert = true, Metadata = new() { ["userId"] = "u1" } });
        Assert.Equal(("POST", Root + "/avatars/objects"), (s.Method, s.Url));
        Assert.StartsWith("multipart/form-data; boundary=", s.ContentType);
        Assert.DoesNotContain("json", s.ContentType);
        var boundary = s.ContentType!.Substring(s.ContentType.IndexOf("boundary=") + 9).Trim('"');
        var text = Encoding.Latin1.GetString(s.RawBody!);
        Assert.Contains("--" + boundary, text);
        Assert.Contains("name=file; filename=a.jpg", text);
        Assert.Contains("Content-Type: image/jpeg", text);
        Assert.Contains("\r\n\r\n\u0001\u0002\u0003\r\n--" + boundary, text);
        Assert.Contains("name=path\r\n\r\nuser/a.jpg\r\n", text);
        Assert.Contains("name=contentType\r\n\r\nimage/jpeg\r\n", text);
        Assert.Contains("name=upsert\r\n\r\ntrue\r\n", text);
        Assert.Contains("name=metadata\r\n\r\n{\"userId\":\"u1\"}\r\n", text);
        Assert.EndsWith("--" + boundary + "--\r\n", text);
    }

    [Fact]
    public async Task UploadStringDefaultsUpsertFalseAndIsUtf8()
    {
        var (c, s) = Mk();
        await c.Storage.From("docs").UploadAsync("a.txt", "héllo");
        var text = Encoding.UTF8.GetString(s.RawBody!);
        Assert.Contains("name=upsert\r\n\r\nfalse\r\n", text);
        Assert.DoesNotContain("name=contentType", text);
        Assert.Contains("\r\n\r\nhéllo\r\n--", text);

        await c.Storage.From("docs").UploadAsync("b.bin", new System.IO.MemoryStream(new byte[] { 9, 8 }));
        Assert.Contains("\r\n\r\n\u0009\u0008\r\n--", Encoding.Latin1.GetString(s.RawBody!));
        Assert.StartsWith("multipart/form-data", s.ContentType);
    }

    [Fact]
    public async Task CreateSignedUrlBodyAndBounds()
    {
        var (c, s) = Mk();
        s.Response = "{\"data\":{\"signedUrl\":\"https://x\",\"expiresIn\":300},\"meta\":{}}";
        var env = await c.Storage.From("avatars").CreateSignedUrlAsync("a.jpg", new SignedUrlOptions { ExpiresIn = 300, Download = true });
        Assert.Equal(("POST", Root + "/avatars/objects/sign"), (s.Method, s.Url));
        Assert.Equal("{\"path\":\"a.jpg\",\"expiresIn\":300,\"download\":true}", s.Body);
        Assert.Equal("https://x", env.Data!.Url);

        await c.Storage.From("avatars").CreateSignedUrlAsync("a.jpg", new SignedUrlOptions { ExpiresIn = 1 });
        await c.Storage.From("avatars").CreateSignedUrlAsync("a.jpg", new SignedUrlOptions { ExpiresIn = 604_800 });
        Assert.Equal("{\"path\":\"a.jpg\",\"expiresIn\":604800}", s.Body);

        var before = s.Calls;
        foreach (var bad in new long[] { 0, -1, 604_801 })
        {
            var e = await Assert.ThrowsAsync<KError>(() =>
                c.Storage.From("avatars").CreateSignedUrlAsync("a.jpg", new SignedUrlOptions { ExpiresIn = bad }));
            Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
        }
        Assert.Equal(before, s.Calls);
    }

    [Fact]
    public async Task ListAndRemoveQueryEncoding()
    {
        var (c, s) = Mk();
        s.Response = "{\"data\":[],\"meta\":{}}";
        await c.Storage.From("avatars").ListAsync();
        Assert.Equal(Root + "/avatars/objects", s.Url);
        await c.Storage.From("avatars").ListAsync("user/ a&b");
        Assert.Equal(Root + "/avatars/objects?prefix=user%2f+a%26b", s.Url);

        s.Response = "{\"data\":{\"bucket\":\"avatars\",\"path\":\"p\",\"deleted\":true},\"meta\":{}}";
        await c.Storage.From("avatars").RemoveAsync("a b&c.jpg");
        Assert.Equal(("DELETE", Root + "/avatars/objects?path=a+b%26c.jpg"), (s.Method, s.Url));
        // ".." and a leading "/" are not rewritten by the SDK, only encoded
        await c.Storage.From("avatars").RemoveAsync("/../x");
        Assert.Equal(Root + "/avatars/objects?path=%2f..%2fx", s.Url);
    }

    [Fact]
    public async Task EmptyNamesRefusedBeforeAnyRequest()
    {
        var (c, s) = Mk();
        var calls = new Func<Task>[]
        {
            () => c.Storage.From("avatars").UploadAsync("", "x"),
            () => c.Storage.From("avatars").RemoveAsync(""),
            () => c.Storage.From("avatars").CreateSignedUrlAsync("", new SignedUrlOptions { ExpiresIn = 60 }),
            () => c.Storage.GetBucketAsync(""),
        };
        foreach (var call in calls)
            Assert.Equal("K9O_INVALID_FIELD_VALUE", (await Assert.ThrowsAsync<KError>(call)).Code);
        Assert.Equal(0, s.Calls);
    }

    [Fact]
    public async Task DotSegmentBucketNamesRefusedBeforeAnyRequest()
    {
        var (c, s) = Mk();
        foreach (var name in new[] { ".", ".." })
        {
            var calls = new Func<Task>[]
            {
                () => c.Storage.GetBucketAsync(name),
                () => c.Storage.UpdateBucketAsync(name, new Dictionary<string, object?> { ["deleteAfterDays"] = 1 }),
                () => c.Storage.DeleteBucketAsync(name),
                () => c.Storage.From(name).UploadAsync("p", "x"),
                () => c.Storage.From(name).UploadAsync("p", new byte[] { 1 }),
                () => c.Storage.From(name).CreateSignedUrlAsync("p", new SignedUrlOptions { ExpiresIn = 60 }),
                () => c.Storage.From(name).ListAsync(),
                () => c.Storage.From(name).RemoveAsync("p"),
            };
            foreach (var call in calls)
            {
                var e = await Assert.ThrowsAsync<KError>(call);
                Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
                Assert.Equal("bucket", e.Context["field"]);
                Assert.Equal("dot_segment", e.Context["reason"]);
            }
        }
        Assert.Equal(0, s.Calls);

        // Positive control: an ordinary name still goes out.
        await c.Storage.From("avatars").ListAsync();
        Assert.True(s.Calls > 0);
        Assert.Equal(Root + "/avatars/objects", s.Url);
    }

    [Fact]
    public async Task WhyDotSegmentIsRefused_UriRewritesTheUrlBeforeSending()
    {
        // Documents the reason for the check: percent-encoding does not protect a `..` segment, and the URL layer
        // collapses it, so WITHOUT the check a bucket named `..` would hit a different route.
        Assert.Equal("..", Uri.EscapeDataString(".."));
        var s = new Stub();
        using var http = new HttpClient(s);
        await http.GetAsync(Root + "/" + Uri.EscapeDataString("..") + "/objects");
        Assert.Equal("https://acme.api.k9o.io/@member-portal/storage/objects", s.Url);
        Assert.NotEqual(Root + "/../objects", s.Url);
    }

    [Fact]
    public async Task MissingProjectSlugRefused()
    {
        var (c, s) = Mk(slug: null);
        var calls = new Func<Task>[]
        {
            () => c.Storage.ListBucketsAsync(),
            () => c.Storage.From("a").ListAsync(),
            () => c.Storage.From("a").UploadAsync("p", "x"),
            () => c.Storage.CreateBucketAsync("abc"),
        };
        foreach (var call in calls)
            Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", (await Assert.ThrowsAsync<KError>(call)).Code);
        Assert.Equal(0, s.Calls);
    }

    [Fact]
    public async Task GatewayErrorsPassThroughWithoutObjectBytes()
    {
        var (c, s) = Mk();
        s.Status = 409;
        s.Response = "{\"code\":\"bucket_not_empty\",\"message\":\"bucket has objects\",\"context\":{},\"recommendation\":null}";
        var e = await Assert.ThrowsAsync<KError>(() => c.Storage.DeleteBucketAsync("avatars"));
        Assert.Equal(("bucket_not_empty", 409), (e.Code, e.Status));

        s.Response = "{\"code\":\"K9O_INVALID_BUCKET_NAME\",\"message\":\"bad name\",\"context\":{},\"recommendation\":null}";
        s.Status = 400;
        e = await Assert.ThrowsAsync<KError>(() => c.Storage.CreateBucketAsync("A"));
        Assert.Equal("K9O_INVALID_BUCKET_NAME", e.Code);

        s.Status = 409;
        s.Response = "{\"code\":\"K9O_ALREADY_EXISTS\",\"message\":\"exists\",\"context\":{},\"recommendation\":null}";
        e = await Assert.ThrowsAsync<KError>(() => c.Storage.From("avatars").UploadAsync("a.jpg", "TOP-SECRET-BYTES"));
        Assert.Equal("K9O_ALREADY_EXISTS", e.Code);
        Assert.DoesNotContain("TOP-SECRET-BYTES", e.ToString());
        Assert.DoesNotContain("TOP-SECRET-BYTES", e.Message);
    }
}
