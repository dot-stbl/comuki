namespace Comuki.Modules.Artifacts.Domain.VisualArtifacts;

/// <summary>
/// Bounds on a visual-artifact payload — the per-mime size cap the
/// store rejects (HTTP 413) before it ever touches MinIO. One
/// <see cref="MaxBytes"/> per <see cref="Mime"/>; the dictionary is
/// the single source every publish path consults. Keeping the cap on
/// the entity surface (instead of inside a private helper) makes it
/// trivial to assert against in unit tests.
/// </summary>
public static class VisualArtifactLimits
{
    /// <summary>Maximum bytes for an HTML publish — 1 MiB per issue #51 slice 1.</summary>
    public const long HtmlMaxBytes = 1L * 1024 * 1024;

    /// <summary>Maximum bytes for an SVG publish — 1 MiB per issue #51 slice 1.</summary>
    public const long SvgMaxBytes = 1L * 1024 * 1024;

    /// <summary>Maximum bytes for a PNG publish — 5 MiB per issue #51 slice 1.</summary>
    public const long PngMaxBytes = 5L * 1024 * 1024;

    /// <summary>Allowed MIME types and their per-publish size cap.</summary>
    public static readonly IReadOnlyDictionary<string, long> MaxBytesByMime =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = PngMaxBytes,
            ["text/html"] = HtmlMaxBytes,
            ["image/svg+xml"] = SvgMaxBytes,
        };

    /// <summary>True when <paramref name="mime"/> is in <see cref="MaxBytesByMime"/>.</summary>
    /// <param name="mime">Content-Type header value (case-insensitive).</param>
    public static bool IsAllowedMime(string? mime)
    {
        return mime is { Length: > 0 } && MaxBytesByMime.ContainsKey(mime);
    }

    /// <summary>
    /// Returns the per-mime size cap, or <c>null</c> when the MIME type is
    /// not on the allowed list. Callers decide whether to reject the publish
    /// (HTTP 415) or simply skip the cap check.
    /// </summary>
    /// <param name="mime">Content-Type header value (case-insensitive).</param>
    public static long? MaxBytesFor(string? mime)
    {
        return mime is { Length: > 0 } && MaxBytesByMime.TryGetValue(mime, out var max) ? max : null;
    }

    /// <summary>True when the MIME type is HTML — the only kind the proxy wraps in a CSP iframe-friendly envelope.</summary>
    /// <param name="mime">Content-Type header value (case-insensitive).</param>
    public static bool IsHtml(string? mime)
    {
        return string.Equals(mime, "text/html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the MIME type is SVG.</summary>
    /// <param name="mime">Content-Type header value (case-insensitive).</param>
    public static bool IsSvg(string? mime)
    {
        return string.Equals(mime, "image/svg+xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the MIME type is a browser-rendered image (PNG/SVG).</summary>
    /// <param name="mime">Content-Type header value (case-insensitive).</param>
    public static bool IsImage(string? mime)
    {
        return IsSvg(mime) || string.Equals(mime, "image/png", StringComparison.OrdinalIgnoreCase);
    }
}
