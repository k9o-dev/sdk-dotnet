using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>Schema namespace: <c>client.Schema</c>.</summary>
public sealed class Schema
{
    internal Schema(K9oClient client) => Collections = new SchemaCollections(client);

    /// <summary>The project's collections.</summary>
    public SchemaCollections Collections { get; }
}

/// <summary>
/// <c>client.Schema.Collections.ListAsync(limit, cursor)</c> is <c>GET /@slug/schema/collections</c>. The list is
/// cursor-paginated, ordered by collection name: pass <c>Meta.Cursor.Next</c> of one page as <c>cursor</c> of the next,
/// until it is null.
/// </summary>
public sealed class SchemaCollections
{
    private readonly K9oClient _client;
    internal SchemaCollections(K9oClient client) => _client = client;

    /// <summary>One page of collections by name. <paramref name="limit"/> is 1 to 200 (default 50). Null arguments are not sent.</summary>
    public async Task<Envelope<List<CollectionSummary>>> ListAsync(int? limit = null, string? cursor = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_client.Config.ProjectSlug))
            throw new KError("K9O_MISSING_PROJECT_CONTEXT", "schema operations require ProjectSlug bound on the client");
        if (limit is < 1 or > 200)
            throw Approvals.Invalid("limit must be a whole number from 1 to 200", new Dictionary<string, object?> { ["limit"] = limit });
        var q = new Dictionary<string, string>();
        if (limit != null) q["limit"] = limit.Value.ToString();
        if (cursor != null) q["cursor"] = cursor;
        return await HttpTransport.RequestAsync<List<CollectionSummary>>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(_client.Config.ProjectSlug!, "schema/collections"), null, q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }
}

/// <summary>One collection (table) of the project's data.</summary>
public sealed class CollectionSummary
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    /// <summary>Live row count, excluding soft-deleted rows. Ignores row-level security.</summary>
    [JsonPropertyName("rowCount")] public long RowCount { get; init; }
    [JsonPropertyName("rowCountIsExact")] public bool RowCountIsExact { get; init; }
    [JsonPropertyName("columnCount")] public int ColumnCount { get; init; }
    [JsonPropertyName("indexCount")] public int IndexCount { get; init; }
    [JsonPropertyName("lastMigrationSlug")] public string? LastMigrationSlug { get; init; }
    [JsonPropertyName("lastMigrationAppliedAt")] public string? LastMigrationAppliedAt { get; init; }
}
