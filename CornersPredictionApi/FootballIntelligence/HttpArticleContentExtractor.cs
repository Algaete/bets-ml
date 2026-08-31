using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CornersPrediction.Application.FootballIntelligence;
using HtmlAgilityPack;

namespace CornersPredictionApi.FootballIntelligence;

public sealed partial class HttpArticleContentExtractor : IArticleContentExtractor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpArticleContentExtractor> _logger;

    public HttpArticleContentExtractor(
        HttpClient httpClient,
        ILogger<HttpArticleContentExtractor> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ExtractedArticle?> ExtractAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https")
            || await IsUnsafeHostAsync(uri.Host, cancellationToken))
            return null;
        try
        {
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return null;
            if (response.Content.Headers.ContentLength is > 2_000_000)
                return null;
            await response.Content.LoadIntoBufferAsync(2_000_000);
            cancellationToken.ThrowIfCancellationRequested();
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var document = new HtmlDocument();
            document.LoadHtml(html);
            var removableNodes = document.DocumentNode.SelectNodes(
                "//script|//style|//noscript|//nav|//footer|//header|//aside|//form|//svg|//iframe");
            if (removableNodes is not null)
            {
                foreach (var node in removableNodes)
                    node.Remove();
            }

            var canonicalText = document.DocumentNode.SelectSingleNode("//link[@rel='canonical']")
                ?.GetAttributeValue("href", null);
            Uri? canonical = null;
            if (!string.IsNullOrWhiteSpace(canonicalText))
                Uri.TryCreate(uri, WebUtility.HtmlDecode(canonicalText), out canonical);
            var title = Meta(document, "property", "og:title")
                ?? Meta(document, "name", "twitter:title")
                ?? WebUtility.HtmlDecode(document.DocumentNode.SelectSingleNode("//title")?.InnerText ?? string.Empty);
            var author = Meta(document, "name", "author");
            var published = ParseDate(
                Meta(document, "property", "article:published_time")
                ?? Meta(document, "name", "date")
                ?? Meta(document, "itemprop", "datePublished"));
            var updated = ParseDate(
                Meta(document, "property", "article:modified_time")
                ?? Meta(document, "itemprop", "dateModified"));
            var language = document.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("lang", null);
            var articleNode = document.DocumentNode.SelectSingleNode("//article")
                ?? document.DocumentNode.SelectSingleNode("//main")
                ?? document.DocumentNode.SelectSingleNode("//body");
            var text = NormalizeText(articleNode?.InnerText ?? string.Empty);
            if (text.Length < 120)
                return null;

            return new ExtractedArticle(
                uri,
                canonical,
                uri.Host.ToLowerInvariant(),
                NormalizeInline(title),
                NormalizeNullable(author),
                published,
                updated,
                NormalizeNullable(language),
                text,
                FootballIntelligenceHash.Sha256(text),
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Article extraction timed out for {Url}", uri);
            return null;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Article extraction failed for {Url}", uri);
            return null;
        }
    }

    private static string? Meta(HtmlDocument document, string attribute, string value) =>
        document.DocumentNode.SelectSingleNode($"//meta[translate(@{attribute}, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='{value.ToLowerInvariant()}']")
            ?.GetAttributeValue("content", null);

    private static DateTime? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed.UtcDateTime
            : null;

    private static string NormalizeText(string text)
    {
        var decoded = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
        var lines = decoded.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeInline)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static string NormalizeInline(string value) => Whitespace().Replace(value, " ").Trim();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeInline(value);

    private static async Task<bool> IsUnsafeHostAsync(string host, CancellationToken cancellationToken)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var addresses = IPAddress.TryParse(host, out var parsed)
                ? [parsed]
                : await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Length == 0 || addresses.Any(IsPrivateAddress);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Any))
            return true;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] is 0 or 10 or 127
            || bytes[0] == 169 && bytes[1] == 254
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168;
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex Whitespace();
}
