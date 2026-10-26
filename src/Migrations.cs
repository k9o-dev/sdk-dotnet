using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>Migration lifecycle namespace — client.Migrations.</summary>
public sealed class Migrations
{
    private readonly K9oClient _client;
    internal Migrations(K9oClient client) => _client = client;

    private string RequireProjectSlug()
    {
        if (string.IsNullOrEmpty(_client.Config.ProjectSlug))
            throw new KError("K9O_MISSING_PROJECT_CONTEXT", "migrations operations require ProjectSlug bound on the client");
        return _client.Config.ProjectSlug!;
    }

    public async Task<Envelope<Dictionary<string, object?>>> ApplyAsync(List<Migration> migrations, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "migrations/apply"), new Dictionary<string, object?> { ["migrations"] = migrations }, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<List<AppliedMigration>>> ListAsync(CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<List<AppliedMigration>>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "migrations"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<AppliedMigration>> GetAsync(string version, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<AppliedMigration>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, $"migrations/{HttpUtility.UrlEncode(version)}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> QueueAsync(List<Migration> migrations, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "migrations/queue"), new Dictionary<string, object?> { ["migrations"] = migrations }, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<List<PendingMigration>>> ListPendingAsync(CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<List<PendingMigration>>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "migrations/pending"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<AppliedMigration>> ApplyPendingAsync(string pendingId, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<AppliedMigration>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"migrations/pending/{HttpUtility.UrlEncode(pendingId)}/apply"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> RejectPendingAsync(string pendingId, string? reason = null, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        object? body = reason != null ? new Dictionary<string, object?> { ["reason"] = reason } : null;
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"migrations/pending/{HttpUtility.UrlEncode(pendingId)}/reject"), body, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> DeletePendingAsync(string pendingId, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Delete,
            UrlGrammar.ProjectPath(slug, $"migrations/pending/{HttpUtility.UrlEncode(pendingId)}"), null, null, ct).ConfigureAwait(false);
    }
}

// ---------------------------------------------------------------------------
// STRUCTURED DECLARATIVE DDL MODEL.
//
// Control-plane shapes are TYPED per tier-1: every schema change is a typed,
// key-tagged Operation, so schema drift is impossible at the SDK layer. Raw SQL
// is the separate runtime client.Query() surface, which cannot alter schema.
//
// Wire JSON is matched exactly: snake_case keys, optional fields omitted when
// default. Optional bool/string fields are nullable so the WhenWritingNull
// ignore-condition drops them (mirrors Go's `omitempty`). The `encrypted` union
// (true | { store, searchable? }) is rendered by ColumnEncryptionConverter.
// ---------------------------------------------------------------------------

/// <summary>
/// Foreign-key reference on a column — compiles to a REFERENCES clause.
/// OnDelete/OnUpdate accept "CASCADE" | "RESTRICT" | "SET NULL" | "NO ACTION".
/// </summary>
public sealed class ColumnReferences
{
    [JsonPropertyName("collection")] public required string Collection { get; init; }
    [JsonPropertyName("on")] public required string On { get; init; }
    [JsonPropertyName("on_delete")] public string? OnDelete { get; init; }
    [JsonPropertyName("on_update")] public string? OnUpdate { get; init; }
}

/// <summary>
/// At-rest field encryption — the object form of the wire union
/// <c>true | { store, searchable? }</c>. <see cref="ColumnSpec.Encrypted"/> is
/// the union itself; this type is the inhabited object arm. <c>Searchable</c>
/// is NESTED here (D-mode searchable-encryption index), NOT a sibling attribute
/// of the column. <c>Store</c> is "inline" | "vault".
/// </summary>
[JsonConverter(typeof(ColumnEncryptionConverter))]
public sealed class ColumnEncryption
{
    /// <summary>"inline" | "vault". Required in the object form.</summary>
    public required string Store { get; init; }
    /// <summary>D-mode searchable-encryption index (nested, not a sibling).</summary>
    public bool Searchable { get; init; }

    /// <summary>
    /// The bare <c>true</c> shorthand: an inline-stored encrypted column with no
    /// extra options. Serializes to the JSON literal <c>true</c>.
    /// </summary>
    public static ColumnEncryption Inline { get; } = new ColumnEncryption { Store = InlineShorthandSentinel };

    internal const string InlineShorthandSentinel = "\0inline-shorthand";
}

/// <summary>
/// Serializes <see cref="ColumnEncryption"/> as the wire union
/// <c>true | { store, searchable? }</c>: <see cref="ColumnEncryption.Inline"/>
/// collapses to the JSON literal <c>true</c>; otherwise the object form is
/// emitted with <c>store</c> required and <c>searchable</c> omitted when false.
/// </summary>
public sealed class ColumnEncryptionConverter : JsonConverter<ColumnEncryption>
{
    public override ColumnEncryption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)
            return ColumnEncryption.Inline;
        if (reader.TokenType == JsonTokenType.False)
            throw new JsonException("encrypted: bare boolean must be `true`, never `false`");
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("encrypted: expected `true` or an object { store, searchable? }");

        string? store = null;
        bool searchable = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var prop = reader.GetString();
            reader.Read();
            switch (prop)
            {
                case "store": store = reader.GetString(); break;
                case "searchable": searchable = reader.GetBoolean(); break;
                default: reader.Skip(); break;
            }
        }
        if (store is null) throw new JsonException("encrypted: object form requires `store`");
        return new ColumnEncryption { Store = store, Searchable = searchable };
    }

    public override void Write(Utf8JsonWriter writer, ColumnEncryption value, JsonSerializerOptions options)
    {
        if (ReferenceEquals(value, ColumnEncryption.Inline) || value.Store == ColumnEncryption.InlineShorthandSentinel)
        {
            writer.WriteBooleanValue(true);
            return;
        }
        writer.WriteStartObject();
        writer.WriteString("store", value.Store);
        if (value.Searchable) writer.WriteBoolean("searchable", true);
        writer.WriteEndObject();
    }
}

/// <summary>
/// A column definition. <c>Type</c> is the column-type vocabulary
/// (parser-owned; kept as a string rather than a fabricated enum). Optional
/// flags are nullable so they are omitted from the wire when unset (mirrors
/// Go's <c>omitempty</c>).
/// </summary>
public sealed class ColumnSpec
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("primary_key")] public bool? PrimaryKey { get; init; }
    [JsonPropertyName("not_null")] public bool? NotNull { get; init; }
    [JsonPropertyName("unique")] public bool? Unique { get; init; }
    [JsonPropertyName("default")] public string? Default { get; init; }
    [JsonPropertyName("references")] public ColumnReferences? References { get; init; }
    /// <summary>
    /// At-rest field encryption — the union <c>true | { store, searchable? }</c>.
    /// Use <see cref="ColumnEncryption.Inline"/> for the bare-<c>true</c> form.
    /// </summary>
    [JsonPropertyName("encrypted")] public ColumnEncryption? Encrypted { get; init; }
    /// <summary>C-mode deterministic blind index for equality lookup.</summary>
    [JsonPropertyName("blind_indexed")] public bool? BlindIndexed { get; init; }
}

/// <summary>An index definition. <c>Name</c> defaults to <c>&lt;table&gt;_&lt;cols&gt;_idx</c>.</summary>
public sealed class IndexSpec
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("columns")] public required List<string> Columns { get; init; }
    [JsonPropertyName("unique")] public bool? Unique { get; init; }
}

/// <summary>Payload of a <c>create_table</c> operation.</summary>
public sealed class CreateTableOp
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("columns")] public required List<ColumnSpec> Columns { get; init; }
    [JsonPropertyName("indexes")] public List<IndexSpec>? Indexes { get; init; }
}

/// <summary>Payload of an <c>add_column</c> operation.</summary>
public sealed class AddColumnOp
{
    [JsonPropertyName("table")] public required string Table { get; init; }
    [JsonPropertyName("column")] public required ColumnSpec Column { get; init; }
}

/// <summary>Payload of a <c>drop_column</c> operation.</summary>
public sealed class DropColumnOp
{
    [JsonPropertyName("table")] public required string Table { get; init; }
    [JsonPropertyName("column")] public required string Column { get; init; }
}

/// <summary>
/// Payload of an <c>alter_table</c> operation — the BATCHED column mutation: one
/// op carries optional <c>add_columns[]</c> + <c>drop_columns[]</c> (NOT separate
/// add/drop ops). <c>AddColumns</c> entries are full <see cref="ColumnSpec"/>
/// (same shape as create_table columns, incl the field-protection flags);
/// <c>DropColumns</c> is a list of column names. Live in the gateway runner.
/// </summary>
public sealed class AlterTableOp
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("add_columns")] public List<ColumnSpec>? AddColumns { get; init; }
    [JsonPropertyName("drop_columns")] public List<string>? DropColumns { get; init; }
}

/// <summary>Payload of a <c>create_index</c> operation.</summary>
public sealed class CreateIndexOp
{
    [JsonPropertyName("table")] public required string Table { get; init; }
    [JsonPropertyName("index")] public required IndexSpec Index { get; init; }
}

/// <summary>Payload of a <c>drop_index</c> operation.</summary>
public sealed class DropIndexOp
{
    [JsonPropertyName("name")] public required string Name { get; init; }
}

/// <summary>
/// A single declarative DDL operation — KEY-TAGGED (exactly one field set), the
/// shape the gateway applies. The v2 set is these 7 — no alter_column,
/// rename_table, or constraint ops at v2. alter_table is the BATCHED column
/// mutation (live in the gateway runner). rename_column
/// remains a pending future op and is
/// added in one bump when it lands. C# has no closed sum type, so the union is
/// modelled as a record of WhenWritingNull-omitted fields; set exactly one via
/// the static factory helpers. The closed-union property is enforced at wire +
/// server validation (the server is fail-closed source of truth).
/// </summary>
public sealed class Operation
{
    [JsonPropertyName("create_table")] public CreateTableOp? CreateTable { get; init; }
    [JsonPropertyName("drop_table")] public string? DropTable { get; init; }
    [JsonPropertyName("add_column")] public AddColumnOp? AddColumn { get; init; }
    [JsonPropertyName("drop_column")] public DropColumnOp? DropColumn { get; init; }
    [JsonPropertyName("alter_table")] public AlterTableOp? AlterTable { get; init; }
    [JsonPropertyName("create_index")] public CreateIndexOp? CreateIndex { get; init; }
    [JsonPropertyName("drop_index")] public DropIndexOp? DropIndex { get; init; }

    public static Operation OfCreateTable(CreateTableOp op) => new() { CreateTable = op };
    public static Operation OfDropTable(string table) => new() { DropTable = table };
    public static Operation OfAddColumn(AddColumnOp op) => new() { AddColumn = op };
    public static Operation OfDropColumn(DropColumnOp op) => new() { DropColumn = op };
    public static Operation OfAlterTable(AlterTableOp op) => new() { AlterTable = op };
    public static Operation OfCreateIndex(CreateIndexOp op) => new() { CreateIndex = op };
    public static Operation OfDropIndex(string name) => new() { DropIndex = new DropIndexOp { Name = name } };
}

/// <summary>
/// A declarative authorization/relationship rule (compiles to RLS USING + WITH
/// CHECK). The rule grammar is the policy-expression DSL; left as
/// an open value to mirror the spec's open shape.
/// </summary>
public sealed class ActionRule
{
    [JsonPropertyName("rule")] public object? Rule { get; init; }
}

/// <summary>
/// A declared relationship for embedding/joins.
/// <c>Type</c> accepts "inner" | "left".
/// </summary>
public sealed class RelationshipSpec
{
    [JsonPropertyName("collection")] public required string Collection { get; init; }
    [JsonPropertyName("on")] public required string On { get; init; }
    [JsonPropertyName("as")] public required string As { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
}

/// <summary>The per-action RLS rules on a collection.</summary>
public sealed class CollectionActions
{
    [JsonPropertyName("select")] public ActionRule? Select { get; init; }
    [JsonPropertyName("insert")] public ActionRule? Insert { get; init; }
    [JsonPropertyName("update")] public ActionRule? Update { get; init; }
    [JsonPropertyName("delete")] public ActionRule? Delete { get; init; }
}

/// <summary>Per-collection policy block (RLS + relationships).</summary>
public sealed class CollectionPolicy
{
    [JsonPropertyName("references")] public List<RelationshipSpec>? References { get; init; }
    [JsonPropertyName("actions")] public CollectionActions? Actions { get; init; }
}

/// <summary>The per-action rules on a storage bucket.</summary>
public sealed class StorageBucketActions
{
    [JsonPropertyName("read")] public ActionRule? Read { get; init; }
    [JsonPropertyName("upload")] public ActionRule? Upload { get; init; }
    [JsonPropertyName("delete")] public ActionRule? Delete { get; init; }
    [JsonPropertyName("list")] public ActionRule? List { get; init; }
}

/// <summary>Per-bucket storage policy block.</summary>
public sealed class StorageBucketPolicy
{
    [JsonPropertyName("public")] public bool? Public { get; init; }
    [JsonPropertyName("actions")] public StorageBucketActions? Actions { get; init; }
}

/// <summary>The storage section of a <see cref="MigrationPolicy"/>.</summary>
public sealed class MigrationStorage
{
    [JsonPropertyName("buckets")] public Dictionary<string, StorageBucketPolicy>? Buckets { get; init; }
}

/// <summary>The optional policy section authored alongside a migration.</summary>
public sealed class MigrationPolicy
{
    [JsonPropertyName("collections")] public Dictionary<string, CollectionPolicy>? Collections { get; init; }
    [JsonPropertyName("storage")] public MigrationStorage? Storage { get; init; }
}

/// <summary>
/// A migration — a versioned, checksummed unit of structured DDL.
/// <c>Up</c> applies the change; <c>Down</c> reverses it. Both are operation
/// arrays.
/// </summary>
public sealed class Migration
{
    [JsonPropertyName("version")] public required int Version { get; init; }
    [JsonPropertyName("slug")] public required string Slug { get; init; }
    [JsonPropertyName("up")] public required List<Operation> Up { get; init; }
    [JsonPropertyName("down")] public required List<Operation> Down { get; init; }
    [JsonPropertyName("policy")] public MigrationPolicy? Policy { get; init; }
}

/// <summary>Applied-migration record returned by <c>List</c> + <c>Get</c>.</summary>
public sealed class AppliedMigration
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("checksum")] public string Checksum { get; init; } = "";
    [JsonPropertyName("appliedAt")] public string AppliedAt { get; init; } = "";
}

/// <summary>Pending migration record returned by <c>ListPending</c>.</summary>
public sealed class PendingMigration
{
    [JsonPropertyName("pendingId")] public string PendingId { get; init; } = "";
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("checksum")] public string Checksum { get; init; } = "";
    [JsonPropertyName("submittedAt")] public string SubmittedAt { get; init; } = "";
    [JsonPropertyName("submittedBy")] public string SubmittedBy { get; init; } = "";
}
