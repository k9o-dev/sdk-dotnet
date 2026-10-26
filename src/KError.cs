using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace K9o;

/// <summary>
/// KError — the closed-taxonomy exception thrown by every k9o surface
/// on non-2xx gateway responses or SDK-side validation failures.
///
/// Idiomatic .NET shape: <c>throw KError : Exception</c>.
/// Code-narrowing: callers <c>catch (KError e)</c> and switch on
/// <see cref="Code"/> per the closed taxonomy. Open code-space
/// — unknown codes pass through verbatim (gateway-curated).
/// </summary>
public class KError : Exception
{
    /// <summary>Closed-taxonomy code (e.g. <c>"K9O_TIER_CAP_HIT"</c>).</summary>
    public string Code { get; }

    /// <summary>Structured per-code payload (read-only).</summary>
    public IReadOnlyDictionary<string, object?> Context { get; }

    /// <summary>Actionable next step; may be null.</summary>
    public string? Recommendation { get; }

    /// <summary>HTTP status the gateway returned; null for SDK-side errors.</summary>
    public int? Status { get; }

    /// <summary>Gateway-emitted trace id; may be null.</summary>
    public string? RequestId { get; }

    public KError(string code, string message)
        : this(code, message, null, null, null, null)
    {
    }

    public KError(
        string code,
        string message,
        IDictionary<string, object?>? context,
        string? recommendation,
        int? status,
        string? requestId
    ) : base(message)
    {
        Code = code;
        Context = new ReadOnlyDictionary<string, object?>(
            context != null ? new Dictionary<string, object?>(context) : new Dictionary<string, object?>()
        );
        Recommendation = recommendation;
        Status = status;
        RequestId = requestId;
    }

    public KError WithRecommendation(string rec) =>
        new(Code, Message, new Dictionary<string, object?>(Context), rec, Status, RequestId);

    public override string ToString() =>
        Status.HasValue
            ? $"[{Code}] {Message} (status={Status})"
            : $"[{Code}] {Message}";
}
