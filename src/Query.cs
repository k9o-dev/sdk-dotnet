using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// <c>client.QueryAsync(sql, params)</c>: one parameterised, read-only SQL statement against the project's data.
///
/// Wire: <c>POST /@&lt;slug&gt;/query</c> body <c>{ sql, params? }</c> answered with <c>{ data: rows, meta }</c>.
///
/// <c>params</c> is a list (positional, bound to <c>$1, $2, ...</c>; the SQL is sent as written) or a dictionary of named
/// values: <c>:name</c> placeholders are rewritten to <c>$n</c> here and the values become the positional list. Values
/// are always bound by the database and never put into the SQL text.
///
/// The gateway allows read-only SQL only (a write is refused with <c>K9O_RAW_SQL_READ_ONLY</c>), one statement per call,
/// at most 100000 characters of SQL and 1000 parameters, and needs the project-editor role.
/// </summary>
public static class Query
{
    /// <summary>SQL with <c>:name</c> placeholders rewritten, and the values in positional order.</summary>
    public sealed record Translated(string Sql, IReadOnlyList<object?> Params);

    private static KError Invalid(string message, IDictionary<string, object?>? context = null) =>
        new("K9O_INVALID_FIELD_VALUE", message, context, null, null, null);

    private static bool IdentStart(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';
    private static bool IdentChar(char c) => IdentStart(c) || (c >= '0' && c <= '9');

    internal static object? CheckValue(object? value, string where)
    {
        switch (value)
        {
            case null:
            case string:
            case bool:
            case sbyte: case byte: case short: case ushort: case int: case uint: case long: case ulong: case decimal:
                return value;
            case double d when double.IsFinite(d):
                return value;
            case float f when float.IsFinite(f):
                return value;
        }
        throw Invalid($"{where} must be a string, number, boolean or null; serialise anything else (for example as JSON text) first");
    }

    /// <summary>
    /// Rewrite <c>:name</c> placeholders to <c>$n</c>. Quoted text, comments, dollar-quoted strings and <c>::</c> casts
    /// are copied as written.
    /// </summary>
    public static Translated TranslateNamedParams(string sql, IReadOnlyDictionary<string, object?> named)
    {
        var order = new List<string>();
        var sb = new StringBuilder(sql.Length + 16);
        var n = sql.Length;
        var i = 0;
        while (i < n)
        {
            var c = sql[i];
            var next = i + 1 < n ? sql[i + 1] : '\0';
            var eString = (c == 'E' || c == 'e') && next == '\'' && !(i > 0 && IdentChar(sql[i - 1]));
            if (c == '\'' || eString)
            {
                var j = eString ? i + 2 : i + 1;
                for (; j < n; j++)
                {
                    if (eString && sql[j] == '\\') { j++; continue; }
                    if (sql[j] == '\'')
                    {
                        if (j + 1 < n && sql[j + 1] == '\'') { j++; continue; }
                        break;
                    }
                }
                var end = Math.Min(j + 1, n);
                sb.Append(sql, i, end - i);
                i = end;
            }
            else if (c == '"')
            {
                var j = i + 1;
                for (; j < n; j++)
                {
                    if (sql[j] == '"')
                    {
                        if (j + 1 < n && sql[j + 1] == '"') { j++; continue; }
                        break;
                    }
                }
                var end = Math.Min(j + 1, n);
                sb.Append(sql, i, end - i);
                i = end;
            }
            else if (c == '-' && next == '-')
            {
                var j = sql.IndexOf('\n', i);
                if (j == -1) j = n;
                sb.Append(sql, i, j - i);
                i = j;
            }
            else if (c == '/' && next == '*')
            {
                var depth = 1;
                var j = i + 2;
                while (j < n && depth > 0)
                {
                    if (sql[j] == '/' && j + 1 < n && sql[j + 1] == '*') { depth++; j += 2; }
                    else if (sql[j] == '*' && j + 1 < n && sql[j + 1] == '/') { depth--; j += 2; }
                    else j++;
                }
                sb.Append(sql, i, j - i);
                i = j;
            }
            else if (c == '$')
            {
                var j = i + 1;
                if (j < n && IdentStart(sql[j]))
                {
                    while (j < n && IdentChar(sql[j])) j++;
                }
                if (j < n && sql[j] == '$')
                {
                    var tag = sql.Substring(i, j + 1 - i);
                    var close = sql.IndexOf(tag, j + 1, StringComparison.Ordinal);
                    var end = close == -1 ? n : close + tag.Length;
                    sb.Append(sql, i, end - i);
                    i = end;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            else if (c == ':' && next == ':')
            {
                sb.Append("::");
                i += 2;
            }
            else if (c == ':' && IdentStart(next) && !(i > 0 && IdentChar(sql[i - 1])))
            {
                var j = i + 2;
                while (j < n && IdentChar(sql[j])) j++;
                var name = sql.Substring(i + 1, j - i - 1);
                var idx = order.IndexOf(name);
                if (idx == -1)
                {
                    if (named == null || !named.ContainsKey(name))
                        throw Invalid($"no value for :{name}", new Dictionary<string, object?> { ["name"] = name });
                    order.Add(name);
                    idx = order.Count - 1;
                }
                sb.Append('$').Append(idx + 1);
                i = j;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        var values = new List<object?>(order.Count);
        foreach (var name in order) values.Add(CheckValue(named![name], $"the value for :{name}"));
        return new Translated(sb.ToString(), values);
    }

    internal static Task<Envelope<List<T>>> RunAsync<T>(
        K9oClient client, string sql, IReadOnlyList<object?>? positional, IReadOnlyDictionary<string, object?>? named, CancellationToken ct)
    {
        var slug = Studio.RequireProjectSlug(client.Config, "query");
        if (sql == null) throw Invalid("sql must be a string");
        var text = sql;
        var values = new List<object?>();
        if (positional != null)
        {
            for (var i = 0; i < positional.Count; i++) values.Add(CheckValue(positional[i], $"params[{i}]"));
        }
        else if (named != null)
        {
            var t = TranslateNamedParams(sql, named);
            text = t.Sql;
            values = new List<object?>(t.Params);
        }
        var body = new Dictionary<string, object?> { ["sql"] = text };
        if (values.Count > 0) body["params"] = values;
        return HttpTransport.RequestAsync<List<T>>(client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, "query"), body, null, ct);
    }
}

/// <summary>Arguments of <c>K9oClient.SearchAsync</c>. <see cref="Column"/> and <see cref="Q"/> are required.</summary>
public sealed class SearchOptions
{
    /// <summary>The text column to search (required).</summary>
    public string Column { get; init; } = "";
    /// <summary>The search text (required).</summary>
    public string Q { get; init; } = "";
    /// <summary>Text-search configuration; <c>english</c> when null.</summary>
    public string? Language { get; init; }
    /// <summary><c>plain</c> (default), <c>phrase</c> or <c>websearch</c>.</summary>
    public string? Type { get; init; }
    /// <summary>Page size.</summary>
    public int? Limit { get; init; }
    /// <summary>Opaque cursor from a previous response's <c>Meta.Cursor.Next</c>.</summary>
    public string? Cursor { get; init; }
}
