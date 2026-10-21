using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

/// <summary>Reads records out of an arbitrary JSON response using the dot paths configured on a lookup source.</summary>
public static class LookupMapping
{
    public const int MaxDisplayLength = PurposeFields.MaxValueLength;
    public const int MaxDetailLength = 300;
    public const int MaxMatchValues = 20;
    public const int MaxMatchValueLength = 200;

    private static readonly Regex Placeholder = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// "data.students" walks objects by property name (case-insensitive). Arrays met on the way are flattened,
    /// so "guardians.mobile" collects every guardian's mobile; a number picks one item ("guardians.0.mobile").
    /// </summary>
    public static List<JsonElement> Select(JsonElement root, string? path)
    {
        var current = new List<JsonElement> { root };
        if (string.IsNullOrWhiteSpace(path)) return current;
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segment = raw.EndsWith("[]", StringComparison.Ordinal) ? raw[..^2] : raw;
            if (segment.Length == 0) continue;
            current = current.SelectMany(e => Step(e, segment)).ToList();
        }
        return current;
    }

    public static List<JsonElement> ExtractRecords(JsonElement root, string? resultsPath)
    {
        var records = new List<JsonElement>();
        foreach (var e in Select(root, resultsPath))
        {
            if (e.ValueKind == JsonValueKind.Array)
                records.AddRange(e.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object));
            else if (e.ValueKind == JsonValueKind.Object)
                records.Add(e);
        }
        return records;
    }

    public static List<string> Texts(JsonElement record, string? path)
    {
        var result = new List<string>();
        foreach (var e in Select(record, path)) Collect(e, result);
        return result;
    }

    public static string? FirstText(JsonElement record, string? path) => Texts(record, path).FirstOrDefault();

    /// <summary>"{name} - Class {class}" with each placeholder replaced by the record's value(s).</summary>
    public static string Render(string template, JsonElement record)
    {
        var text = Placeholder.Replace(template ?? string.Empty, m => string.Join(", ", Texts(record, m.Groups[1].Value)));
        return Truncate(Spaces.Replace(text, " ").Trim(), MaxDisplayLength);
    }

    public static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    public static bool Matches(IEnumerable<string> candidates, string? visitorValue, string against)
    {
        var visitor = Normalize(visitorValue, against);
        if (visitor is null) return false;
        return candidates
            .SelectMany(c => against == LookupMatchTargets.Mobile ? c.Split([',', ';', '/', '|'], StringSplitOptions.RemoveEmptyEntries) : [c])
            .Any(c => Normalize(c, against) == visitor);
    }

    /// <summary>Mobiles compare on their last 10 digits so "+91 98765 43210" equals "9876543210".</summary>
    public static string? Normalize(string? value, string against)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (against == LookupMatchTargets.Mobile)
        {
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length < 6) return null;
            return digits.Length > 10 ? digits[^10..] : digits;
        }
        return value.Trim().ToLowerInvariant();
    }

    private static IEnumerable<JsonElement> Step(JsonElement e, string segment)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            if (int.TryParse(segment, out var index))
            {
                if (index >= 0 && index < e.GetArrayLength()) yield return e[index];
                yield break;
            }
            foreach (var item in e.EnumerateArray())
                foreach (var r in Step(item, segment))
                    yield return r;
            yield break;
        }
        if (e.ValueKind != JsonValueKind.Object) yield break;
        if (e.TryGetProperty(segment, out var exact))
        {
            yield return exact;
            yield break;
        }
        foreach (var p in e.EnumerateObject())
        {
            if (string.Equals(p.Name, segment, StringComparison.OrdinalIgnoreCase))
            {
                yield return p.Value;
                yield break;
            }
        }
    }

    private static void Collect(JsonElement e, List<string> into)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) Collect(item, into);
                break;
            case JsonValueKind.String:
                var s = e.GetString();
                if (!string.IsNullOrWhiteSpace(s)) into.Add(s.Trim());
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                into.Add(e.GetRawText());
                break;
        }
    }
}

/// <summary>
/// Fills <c>{query}</c> (the typed text) and <c>{field:Label}</c> (another extra field of the same purpose)
/// into a URL or JSON body. Values are escaped for where they go, so typed text cannot change the request's shape.
/// </summary>
public static class LookupTemplates
{
    private static readonly Regex Token = new(@"\{(query|field:[^{}""]+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex QueryToken = new(@"\{query\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool HasQuery(string? template) => template is not null && QueryToken.IsMatch(template);

    public static List<string> FieldNames(params string?[] templates) =>
        templates
            .Where(t => t is not null)
            .SelectMany(t => Token.Matches(t!).Select(m => m.Groups[1].Value))
            .Where(v => v.StartsWith("field:", StringComparison.OrdinalIgnoreCase))
            .Select(v => v[6..].Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string Fill(string template, string query, IReadOnlyDictionary<string, string>? context, Func<string, string> encode) =>
        Token.Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            if (name.Equals("query", StringComparison.OrdinalIgnoreCase)) return encode(query ?? string.Empty);
            var label = name[6..].Trim();
            var value = context?.FirstOrDefault(kv => string.Equals(kv.Key.Trim(), label, StringComparison.OrdinalIgnoreCase)).Value;
            return encode(value ?? string.Empty);
        });

    /// <summary>Escapes for use inside a JSON string literal; non-English letters stay readable.</summary>
    public static string JsonString(string value) =>
        System.Text.Json.JsonEncodedText.Encode(value, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();
}

/// <summary>Stops a configured lookup URL from reaching the server itself or the internal network.</summary>
public static class OutboundUrlGuard
{
    public const string QueryPlaceholder = "{query}";

    public static Uri BuildSearchUri(string template, string query, bool allowHttp, bool allowPrivate,
        IReadOnlyDictionary<string, string>? context = null, bool requireQuery = true)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new InvalidOperationException("The search URL is required.");
        if (requireQuery && !LookupTemplates.HasQuery(template))
            throw new InvalidOperationException($"The search URL must contain {QueryPlaceholder} where the typed text goes.");
        var url = LookupTemplates.Fill(template.Trim(), query, context, Uri.EscapeDataString);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("The search URL is not a valid web address.");
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp))
            throw new InvalidOperationException("The search URL must start with https://.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Put credentials in the API key, not in the search URL.");
        if (!allowPrivate && IsBlockedHost(uri.DnsSafeHost))
            throw new InvalidOperationException("The search URL points to a local or private network address, which is not allowed.");
        return uri;
    }

    public static bool IsBlockedHost(string host)
    {
        if (IPAddress.TryParse(host, out var ip)) return IsBlocked(ip);
        var h = host.TrimEnd('.').ToLowerInvariant();
        return h == "localhost" || h.EndsWith(".localhost") || h.EndsWith(".local") || h.EndsWith(".internal") || !h.Contains('.');
    }

    public static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19));
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None)) return true;
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;
            // NAT64 (64:ff9b::/96) carries an IPv4 address in the last four bytes.
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4..12].All(x => x == 0))
                return IsBlocked(new IPAddress(b[12..]));
            return false;
        }
        return true;
    }

    /// <summary>Checks the address actually connected to, so a DNS name cannot switch to an internal IP after validation.</summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> ConnectCallback(bool allowPrivate) =>
        async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
            var allowed = allowPrivate ? addresses : addresses.Where(a => !IsBlocked(a)).ToArray();
            if (allowed.Length == 0)
                throw new HttpRequestException($"{host} resolves to a local or private network address, which lookups may not call.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
}

/// <summary>What the server found for a picked record, carried in an encrypted token until the visit is saved.</summary>
public sealed class LookupSelection
{
    public Guid SourceId { get; set; }
    public Guid TenantId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public List<PurposeFieldDetail> Details { get; set; } = new();
    public List<string> MatchValues { get; set; } = new();
    public string? MatchAgainst { get; set; }
    public bool BlockOnMismatch { get; set; }
    public long ExpiresAtUnix { get; set; }
}

/// <summary>
/// AES-GCM so staff browsers can neither read the record's match values (guardian phones) nor alter what gets saved.
/// </summary>
public static class LookupTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Info = Encoding.UTF8.GetBytes("tiaano-vms-lookup-selection-v1");

    public static string Protect(LookupSelection selection, string secret)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(selection);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(DeriveKey(secret), TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);
        var all = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(all, 0);
        tag.CopyTo(all, NonceSize);
        cipher.CopyTo(all, NonceSize + TagSize);
        return Convert.ToBase64String(all).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Null when the token was altered, made with another key, or has expired.</summary>
    public static LookupSelection? Unprotect(string? token, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var b64 = token.Trim().Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var all = Convert.FromBase64String(b64);
            if (all.Length <= NonceSize + TagSize) return null;
            var plain = new byte[all.Length - NonceSize - TagSize];
            using (var aes = new AesGcm(DeriveKey(secret), TagSize))
                aes.Decrypt(all.AsSpan(0, NonceSize), all.AsSpan(NonceSize + TagSize), all.AsSpan(NonceSize, TagSize), plain);
            var selection = JsonSerializer.Deserialize<LookupSelection>(plain);
            if (selection is null || selection.ExpiresAtUnix < now.ToUnixTimeSeconds()) return null;
            return selection;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static byte[] DeriveKey(string secret) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, salt: null, info: Info);
}
