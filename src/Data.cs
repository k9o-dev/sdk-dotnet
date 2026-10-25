using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace K9o.Data;

/// <summary>
/// Data-plane DSL.
///
/// <code>
/// var response = await k9o.From("posts").Select().Limit(10).ExecuteAsync&lt;Post&gt;();
/// foreach (var row in response.Data!) Console.WriteLine(row);
/// </code>
/// </summary>
public sealed class FromBuilder
{
    private static readonly Regex CollectionRegex = new(@"^[a-z][a-z0-9_-]{0,62}$", RegexOptions.Compiled);

    private readonly K9oClient _client;
    private readonly string _collection;

    public FromBuilder(K9oClient client, string collection)
    {
        _client = client;
        _collection = collection;
    }

    public SelectBuilder Select() => new(_client, _collection);

    public async Task<Envelope<T>> InsertAsync<T>(object record, CancellationToken ct = default)
    {
        var path = CollectionPath(_client, _collection);
        return await HttpTransport.RequestAsync<T>(_client.Config, HttpMethod.Post, path, record, null, ct).ConfigureAwait(false);
    }

    public UpdateBuilder<TPatch> Update<TPatch>(TPatch patch) where TPatch : class => new(_client, _collection, patch);

    public DeleteBuilder Delete() => new(_client, _collection);

    internal static string CollectionPath(K9oClient client, string collection)
    {
        if (string.IsNullOrEmpty(client.Config.ProjectSlug))
        {
            throw new KError(
                "K9O_MISSING_PROJECT_CONTEXT",
                "data-plane operations require ProjectSlug to be bound on the client",
                new Dictionary<string, object?> { ["collection"] = collection },
                "Pass ProjectSlug in K9oOptions when calling K9oClient.Create.",
                null, null
            );
        }
        if (!CollectionRegex.IsMatch(collection))
        {
            throw new KError(
                "K9O_INVALID_FIELD_VALUE",
                $"collection name \"{collection}\" does not match required format",
                new Dictionary<string, object?> { ["collection"] = collection },
                null, null, null
            );
        }
        return UrlGrammar.ProjectPath(client.Config.ProjectSlug!, $"data/{collection}");
    }
}

public abstract class BaseBuilder<TSelf> where TSelf : BaseBuilder<TSelf>
{
    protected readonly List<(string col, string op, string val)> Filters = new();
    protected int? LimitVal;
    protected string? CursorVal;
    protected string? CountMode;

    protected abstract TSelf Self();

    /// <summary>A filter value as the gateway reads it: booleans are <c>true</c> / <c>false</c> (<c>bool.ToString()</c> gives "True").</summary>
    private static string Wire(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null",
    };

    public TSelf Eq(string column, object value) { Filters.Add((column, "eq", Wire(value))); return Self(); }
    public TSelf Neq(string column, object value) { Filters.Add((column, "neq", Wire(value))); return Self(); }
    public TSelf Gt(string column, object value) { Filters.Add((column, "gt", Wire(value))); return Self(); }
    public TSelf Gte(string column, object value) { Filters.Add((column, "gte", Wire(value))); return Self(); }
    public TSelf Lt(string column, object value) { Filters.Add((column, "lt", Wire(value))); return Self(); }
    public TSelf Lte(string column, object value) { Filters.Add((column, "lte", Wire(value))); return Self(); }
    public TSelf Like(string column, string pattern) { Filters.Add((column, "like", pattern)); return Self(); }
    public TSelf ILike(string column, string pattern) { Filters.Add((column, "ilike", pattern)); return Self(); }
    public TSelf Is(string column, string value) { Filters.Add((column, "is", value)); return Self(); }
    public TSelf In(string column, IEnumerable<object> values) { Filters.Add((column, "in", $"({string.Join(",", values.Select(Wire))})")); return Self(); }
    public TSelf NotIn(string column, IEnumerable<object> values) { Filters.Add((column, "notIn", $"({string.Join(",", values.Select(Wire))})")); return Self(); }
    public TSelf Filter(string column, string op, object value) { Filters.Add((column, op, Wire(value))); return Self(); }

    public TSelf Limit(int n) { LimitVal = n; return Self(); }
    public TSelf Cursor(string opaque) { CursorVal = opaque; return Self(); }
    public TSelf Count(string mode) { CountMode = mode; return Self(); }

    protected Dictionary<string, string> ToQuery()
    {
        var q = new Dictionary<string, string>();
        foreach (var (col, op, val) in Filters) q[col] = val.StartsWith("(") ? $"{op}.{val}" : $"{op}.{val}";
        if (LimitVal.HasValue) q["limit"] = LimitVal.Value.ToString();
        if (CursorVal != null) q["cursor"] = CursorVal;
        if (CountMode != null) q["count"] = CountMode;
        return q;
    }
}

public sealed class SelectBuilder : BaseBuilder<SelectBuilder>
{
    private readonly K9oClient _client;
    private readonly string _collection;

    public SelectBuilder(K9oClient client, string collection)
    {
        _client = client;
        _collection = collection;
    }

    protected override SelectBuilder Self() => this;

    public async Task<Envelope<List<T>>> ExecuteAsync<T>(CancellationToken ct = default)
    {
        var path = FromBuilder.CollectionPath(_client, _collection);
        return await HttpTransport.RequestAsync<List<T>>(_client.Config, HttpMethod.Get, path, null, ToQuery(), ct).ConfigureAwait(false);
    }
}

public sealed class UpdateBuilder<TPatch> : BaseBuilder<UpdateBuilder<TPatch>> where TPatch : class
{
    private readonly K9oClient _client;
    private readonly string _collection;
    private readonly TPatch _patch;

    public UpdateBuilder(K9oClient client, string collection, TPatch patch)
    {
        _client = client;
        _collection = collection;
        _patch = patch;
    }

    protected override UpdateBuilder<TPatch> Self() => this;

    public async Task<Envelope<T>> ExecuteAsync<T>(CancellationToken ct = default)
    {
        var idFilter = Filters.FirstOrDefault(f => f.col == "id" && f.op == "eq");
        if (idFilter.col == null)
        {
            throw new KError(
                "K9O_INVALID_FIELD_VALUE",
                "Update() requires .Eq(\"id\", ...) filter; changing many rows in one call is not supported",
                new Dictionary<string, object?> { ["collection"] = _collection },
                null, null, null
            );
        }
        var path = $"{FromBuilder.CollectionPath(_client, _collection)}/{idFilter.val}";
        return await HttpTransport.RequestAsync<T>(_client.Config, HttpMethod.Patch, path, _patch, null, ct).ConfigureAwait(false);
    }
}

public sealed class DeleteBuilder : BaseBuilder<DeleteBuilder>
{
    private readonly K9oClient _client;
    private readonly string _collection;

    public DeleteBuilder(K9oClient client, string collection)
    {
        _client = client;
        _collection = collection;
    }

    protected override DeleteBuilder Self() => this;

    public async Task<Envelope<T>> ExecuteAsync<T>(CancellationToken ct = default)
    {
        var idFilter = Filters.FirstOrDefault(f => f.col == "id" && f.op == "eq");
        if (idFilter.col == null)
        {
            throw new KError(
                "K9O_INVALID_FIELD_VALUE",
                "Delete() requires .Eq(\"id\", ...) filter; deleting many rows in one call is not supported",
                new Dictionary<string, object?> { ["collection"] = _collection },
                null, null, null
            );
        }
        var path = $"{FromBuilder.CollectionPath(_client, _collection)}/{idFilter.val}";
        return await HttpTransport.RequestAsync<T>(_client.Config, HttpMethod.Delete, path, null, null, ct).ConfigureAwait(false);
    }
}
