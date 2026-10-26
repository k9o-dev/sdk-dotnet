using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using K9o;
using Xunit;

namespace K9o.Tests;

/// <summary>QueryAsync, SearchAsync, TextSearch and VectorSearch: wire shape, shared vectors, validation.</summary>
public class QueryTests
{
    private const string Rows = "{\"data\":[{\"id\":\"a\",\"title\":\"x\"}],\"meta\":{\"rawQuery\":{\"intent\":\"select\",\"rowCount\":1}}}";

    private sealed class Rec : HttpMessageHandler
    {
        public int Calls;
        public string? Method;
        public string? Target;
        public string Body = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Method = request.Method.Method;
            Target = request.RequestUri!.PathAndQuery;
            Body = request.Content != null ? await request.Content.ReadAsStringAsync(ct) : "";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Rows, Encoding.UTF8, "application/json") };
        }
    }

    private static (K9oClient c, Rec rec) Make()
    {
        var rec = new Rec();
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "http://localhost:6000", ProjectSlug = "portal", HttpClient = new HttpClient(rec) });
        return (c, rec);
    }

    private static Dictionary<string, object?> Named(params (string, object?)[] kv) => kv.ToDictionary(p => p.Item1, p => p.Item2);

    private static Dictionary<string, string> QueryParams(string target)
    {
        var i = target.IndexOf('?');
        var q = new Dictionary<string, string>();
        if (i < 0) return q;
        foreach (var kv in target[(i + 1)..].Split('&'))
        {
            var p = kv.Split('=', 2);
            q[System.Web.HttpUtility.UrlDecode(p[0])] = System.Web.HttpUtility.UrlDecode(p.Length > 1 ? p[1] : "");
        }
        return q;
    }

    private static void AssertInvalid(Action a, string contains)
    {
        var e = Assert.Throws<KError>(a);
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
        Assert.Contains(contains, e.Message);
    }

    // ---- shared vectors ----

    [Fact]
    public void Vector1()
    {
        var t = Query.TranslateNamedParams("SELECT * FROM users WHERE data->>'role' = :role", Named(("role", "admin")));
        Assert.Equal("SELECT * FROM users WHERE data->>'role' = $1", t.Sql);
        Assert.Equal(new object?[] { "admin" }, t.Params);
    }

    [Fact]
    public void Vector2()
    {
        var t = Query.TranslateNamedParams("SELECT :a, :b, :a", Named(("a", 1), ("b", 2)));
        Assert.Equal("SELECT $1, $2, $1", t.Sql);
        Assert.Equal(new object?[] { 1, 2 }, t.Params);
    }

    [Fact]
    public void Vector3()
    {
        var t = Query.TranslateNamedParams("SELECT ':not_a_param', \":also_not\", x::int FROM t WHERE y = :y", Named(("y", true)));
        Assert.Equal("SELECT ':not_a_param', \":also_not\", x::int FROM t WHERE y = $1", t.Sql);
        Assert.Equal(new object?[] { true }, t.Params);
    }

    [Fact]
    public void Vector4()
    {
        var t = Query.TranslateNamedParams("SELECT 'it''s :no' , :v -- :comment\nFROM t", Named(("v", null)));
        Assert.Equal("SELECT 'it''s :no' , $1 -- :comment\nFROM t", t.Sql);
        Assert.Equal(new object?[] { null }, t.Params);
    }

    [Fact]
    public void Vector5()
    {
        var t = Query.TranslateNamedParams("SELECT /* :c /* :d */ :e */ :f", Named(("f", 1)));
        Assert.Equal("SELECT /* :c /* :d */ :e */ $1", t.Sql);
        Assert.Equal(new object?[] { 1 }, t.Params);
    }

    [Fact]
    public void Vector6()
    {
        var t = Query.TranslateNamedParams("SELECT $$ :x $$, $q$ :y $q$, :z", Named(("z", "s")));
        Assert.Equal("SELECT $$ :x $$, $q$ :y $q$, $1", t.Sql);
        Assert.Equal(new object?[] { "s" }, t.Params);
    }

    [Fact]
    public void Vector7()
    {
        var t = Query.TranslateNamedParams("SELECT arr[1:2], a:b, :n", Named(("n", 5)));
        Assert.Equal("SELECT arr[1:2], a:b, $1", t.Sql);
        Assert.Equal(new object?[] { 5 }, t.Params);
    }

    [Fact]
    public async Task Vector8MissingValueFailsBeforeAnyRequest()
    {
        var (c, rec) = Make();
        var e = await Assert.ThrowsAsync<KError>(() => c.QueryAsync("SELECT :missing", new Dictionary<string, object?>()));
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
        Assert.Contains(":missing", e.Message);
        Assert.Equal(0, rec.Calls);
    }

    [Fact]
    public async Task Vector9NonScalarValueFailsBeforeAnyRequest()
    {
        var (c, rec) = Make();
        var e = await Assert.ThrowsAsync<KError>(() => c.QueryAsync("SELECT :a", Named(("a", new Dictionary<string, object?>()))));
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
        Assert.Contains("serialise", e.Message);
        await Assert.ThrowsAsync<KError>(() => c.QueryAsync("SELECT $1", new List<object?> { new[] { 1 } }));
        await Assert.ThrowsAsync<KError>(() => c.QueryAsync("SELECT $1", new List<object?> { DateTime.UtcNow }));
        Assert.Equal(0, rec.Calls);
    }

    [Fact]
    public async Task Vector10ListParamsAreSentUnchanged()
    {
        var (c, rec) = Make();
        await c.QueryAsync("SELECT $1", new List<object?> { "x" });
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"sql\":\"SELECT $1\",\"params\":[\"x\"]}"), JsonNode.Parse(rec.Body)), rec.Body);
    }

    [Fact]
    public async Task Vector11NoParamsOmitsTheKey()
    {
        var (c, rec) = Make();
        await c.QueryAsync("SELECT 1");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"sql\":\"SELECT 1\"}"), JsonNode.Parse(rec.Body)), rec.Body);
        await c.QueryAsync("SELECT 1", new List<object?>());
        Assert.False(JsonNode.Parse(rec.Body)!.AsObject().ContainsKey("params"));
    }

    [Fact]
    public void ExtraKeysAreIgnoredAndEscapeStringsSkipped()
    {
        var t = Query.TranslateNamedParams("SELECT E'a\\' :x', :y", Named(("y", 1), ("unused", 2)));
        Assert.Equal("SELECT E'a\\' :x', $1", t.Sql);
        Assert.Equal(new object?[] { 1 }, t.Params);
    }

    // ---- wire ----

    [Fact]
    public async Task QueryPostsNamedParamsAsPositionalAndParsesRows()
    {
        var (c, rec) = Make();
        var r = await c.QueryAsync("SELECT id FROM t WHERE a = :a AND b = :b", Named(("a", "x"), ("b", null)));
        Assert.Equal("POST", rec.Method);
        Assert.Equal("/@portal/query", rec.Target);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("{\"sql\":\"SELECT id FROM t WHERE a = $1 AND b = $2\",\"params\":[\"x\",null]}"), JsonNode.Parse(rec.Body)), rec.Body);
        Assert.Equal("a", r.Data![0]["id"].GetString());
    }

    [Fact]
    public async Task QueryRequiresBoundProject()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "http://localhost:6000", HttpClient = new HttpClient(new Rec()) });
        var e = await Assert.ThrowsAsync<KError>(() => c.QueryAsync("SELECT 1"));
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", e.Code);
    }

    // ---- textSearch / vectorSearch ----

    [Fact]
    public async Task TextSearchDefaultsSendOnlyTheQuery()
    {
        var (c, rec) = Make();
        await c.From("docs").Select().TextSearch("body", "red & green").ExecuteAsync<object>();
        Assert.Equal("GET", rec.Method);
        Assert.Contains("textSearch=body%3ared+%26+green", rec.Target);
        Assert.Equal(new Dictionary<string, string> { ["textSearch"] = "body:red & green" }, QueryParams(rec.Target!));
    }

    [Fact]
    public async Task TextSearchSendsLanguageAndTypeWhenNotDefault()
    {
        var (c, rec) = Make();
        await c.From("docs").Select().Eq("kind", "a").Limit(5).TextSearch("body", "x", "german", "websearch").ExecuteAsync<object>();
        var q = QueryParams(rec.Target!);
        Assert.Equal("body:x", q["textSearch"]);
        Assert.Equal("german", q["textSearchLanguage"]);
        Assert.Equal("websearch", q["textSearchType"]);
        Assert.Equal("eq.a", q["kind"]);
        Assert.Equal("5", q["limit"]);
        await c.From("docs").Select().TextSearch("body", "x", "english", "plain").ExecuteAsync<object>();
        Assert.Equal(new[] { "textSearch" }, QueryParams(rec.Target!).Keys.ToArray());
    }

    [Fact]
    public async Task VectorSearchWire()
    {
        var (c, rec) = Make();
        await c.From("docs").Select().VectorSearch("embedding", new[] { 0.5, 1, -2 }).ExecuteAsync<object>();
        Assert.Equal(new Dictionary<string, string> { ["vectorSearch"] = "embedding:[0.5,1,-2]" }, QueryParams(rec.Target!));
        await c.From("docs").Select().VectorSearch("embedding", new[] { 1.0, 2.0 }, "dot", 25).ExecuteAsync<object>();
        var q = QueryParams(rec.Target!);
        Assert.Equal("embedding:[1,2]", q["vectorSearch"]);
        Assert.Equal("dot", q["vectorMetric"]);
        Assert.Equal("25", q["vectorLimit"]);
    }

    [Fact]
    public async Task SearchValidationFailsBeforeAnyRequest()
    {
        var (c, rec) = Make();
        var s = c.From("docs").Select();
        AssertInvalid(() => s.TextSearch("bad col", "x"), "column");
        AssertInvalid(() => s.TextSearch("9col", "x"), "column");
        AssertInvalid(() => s.TextSearch(new string('a', 64), "x"), "column");
        AssertInvalid(() => s.TextSearch("body", ""), "non-empty");
        AssertInvalid(() => s.TextSearch("body", "x", "en glish"), "language");
        AssertInvalid(() => s.TextSearch("body", "x", "english", "fuzzy"), "type");
        AssertInvalid(() => s.VectorSearch("e;", new[] { 1.0 }), "column");
        AssertInvalid(() => s.VectorSearch("e", Array.Empty<double>()), "non-empty");
        AssertInvalid(() => s.VectorSearch("e", new[] { double.NaN }), "non-empty");
        AssertInvalid(() => s.VectorSearch("e", new[] { 1.0 }, "l2"), "metric");
        AssertInvalid(() => s.VectorSearch("e", new[] { 1.0 }, "cosine", 0), "limit");
        AssertInvalid(() => s.VectorSearch("e", new[] { 1.0 }, "cosine", 1001), "limit");
        await Task.CompletedTask;
        Assert.Equal(0, rec.Calls);
    }

    // ---- root search ----

    [Fact]
    public async Task SearchIsTextSearchOnTheCollectionRoute()
    {
        var (c, rec) = Make();
        var r = await c.SearchAsync("docs", new SearchOptions { Column = "body", Q = "hello", Language = "french", Type = "phrase", Limit = 3, Cursor = "cur_1" });
        Assert.Equal("GET", rec.Method);
        Assert.StartsWith("/@portal/data/docs?", rec.Target);
        var q = QueryParams(rec.Target!);
        Assert.Equal("body:hello", q["textSearch"]);
        Assert.Equal("french", q["textSearchLanguage"]);
        Assert.Equal("phrase", q["textSearchType"]);
        Assert.Equal("3", q["limit"]);
        Assert.Equal("cur_1", q["cursor"]);
        Assert.Equal("a", r.Data![0]["id"].GetString());
    }

    [Fact]
    public async Task SearchRequiresColumnAndQuery()
    {
        var (c, rec) = Make();
        await Assert.ThrowsAsync<KError>(() => c.SearchAsync("docs", new SearchOptions { Q = "x" }));
        await Assert.ThrowsAsync<KError>(() => c.SearchAsync("docs", new SearchOptions { Column = "body" }));
        Assert.Equal(0, rec.Calls);
    }
}
