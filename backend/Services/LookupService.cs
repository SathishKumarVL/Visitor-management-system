using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface ILookupService
{
    Task<IReadOnlyList<LookupSourceDto>> ListAsync();
    Task<IReadOnlyList<LookupSourceOptionDto>> OptionsAsync();
    Task<LookupSourceDto> UpsertAsync(Guid? id, LookupSourceUpsertRequest request, string? user);
    Task DeleteAsync(Guid id);
    Task<LookupTestResultDto> TestAsync(LookupTestRequest request, CancellationToken ct);
    Task<IReadOnlyList<LookupMatchDto>> SearchAsync(Guid sourceId, string? query, IReadOnlyDictionary<string, string>? context, CancellationToken ct);
    /// <summary>Searches again with a previous visit's text and details; null when that record is no longer found.</summary>
    Task<LookupMatchDto?> RecheckAsync(Guid sourceId, LookupRecheckRequest request, CancellationToken ct);
    LookupCheckResultDto Check(LookupCheckRequest request);
    LookupSelection? ReadSelection(string token);
    Task<HashSet<Guid>> ExistingSourceIdsAsync(IEnumerable<Guid> ids);
}

public sealed class LookupService : ILookupService
{
    public const string HttpClientName = "lookup";
    public const int MaxResults = 20;
    public const int MaxSavedFields = 8;
    public const int MaxQueryLength = 100;
    private const int MaxContextEntries = 10;
    private const int MaxContextValueLength = 200;
    private const int MaxResponseBytes = 1024 * 1024;
    private const int RawSampleChars = 4000;
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private static readonly Regex HeaderName = new(@"^[A-Za-z0-9!#$%&'*+.^_`|~-]+$", RegexOptions.Compiled);
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Upgrade", "Expect", "TE", "Trailer", "Proxy-Authorization", "Cookie",
    };

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LookupService> _logger;

    public LookupService(ApplicationDbContext db, ITenantContext tenant, IHttpClientFactory http, IMemoryCache cache,
        IConfiguration config, IWebHostEnvironment env, ILogger<LookupService> logger)
    {
        _db = db;
        _tenant = tenant;
        _http = http;
        _cache = cache;
        _config = config;
        _env = env;
        _logger = logger;
    }

    /// <summary>Plain http and private addresses are for local testing only; production keeps both off.</summary>
    private bool AllowHttp => _config.GetValue<bool>("Lookups:AllowHttp");
    private bool AllowPrivate => _config.GetValue<bool>("Lookups:AllowPrivateNetworks");

    private string Secret => SecretConfiguration.GetRequiredEncryptionKey(_config, _env);

    private Guid TenantId => _tenant.TenantId is Guid id && id != Guid.Empty
        ? id
        : throw new UnauthorizedAccessException("Tenant context is required.");

    public async Task<IReadOnlyList<LookupSourceDto>> ListAsync()
    {
        var sources = await _db.LookupSources.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        return sources.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<LookupSourceOptionDto>> OptionsAsync() =>
        await _db.LookupSources.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new LookupSourceOptionDto { Id = s.Id, Name = s.Name, MinChars = s.MinChars, IsActive = s.IsActive })
            .ToListAsync();

    public async Task<HashSet<Guid>> ExistingSourceIdsAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return (await _db.LookupSources.AsNoTracking().Where(s => list.Contains(s.Id)).Select(s => s.Id).ToListAsync()).ToHashSet();
    }

    public async Task<LookupSourceDto> UpsertAsync(Guid? id, LookupSourceUpsertRequest request, string? user)
    {
        LookupSource entity;
        if (id.HasValue)
        {
            entity = await _db.LookupSources.FirstOrDefaultAsync(s => s.Id == id.Value)
                     ?? throw new InvalidOperationException("Lookup source not found.");
        }
        else
        {
            entity = new LookupSource { TenantId = TenantId, CreatedBy = user };
            _db.LookupSources.Add(entity);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) throw new InvalidOperationException("Name is required.");
        if (await _db.LookupSources.AnyAsync(s => s.Id != entity.Id && s.Name == name))
            throw new InvalidOperationException($"A lookup source named \"{name}\" already exists.");

        Apply(entity, request);
        entity.Name = name;

        if (request.ClearApiKey)
            entity.ApiKeyEncrypted = null;
        else if (!string.IsNullOrWhiteSpace(request.ApiKey))
            entity.ApiKeyEncrypted = SensitiveDataHelper.Encrypt(ValidateHeaderValue(request.ApiKey), Secret);
        if (entity.ApiKeyEncrypted is not null && entity.ApiKeyHeader is null)
            throw new InvalidOperationException("Enter the header name the API key is sent in (for example X-Api-Key).");

        if (id.HasValue)
        {
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }
        await _db.SaveChangesAsync();
        return Map(entity);
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _db.LookupSources.FirstOrDefaultAsync(s => s.Id == id)
                     ?? throw new InvalidOperationException("Lookup source not found.");
        var key = id.ToString();
        var usedBy = await _db.VisitPurposes.AsNoTracking()
            .Where(p => p.FieldsJson != null && p.FieldsJson.Contains(key))
            .Select(p => p.Name)
            .ToListAsync();
        if (usedBy.Count > 0)
            throw new InvalidOperationException(
                $"This source is used by the purpose(s) {string.Join(", ", usedBy)}. Change those fields first, or turn the source off instead.");
        _db.LookupSources.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<LookupTestResultDto> TestAsync(LookupTestRequest request, CancellationToken ct)
    {
        var source = new LookupSource { TenantId = TenantId, Name = request.Source.Name?.Trim() ?? "Test" };
        string? apiKey;
        try
        {
            Apply(source, request.Source);
            apiKey = string.IsNullOrWhiteSpace(request.Source.ApiKey) ? null : ValidateHeaderValue(request.Source.ApiKey);
            if (apiKey is null && !request.Source.ClearApiKey && request.Id is Guid savedId)
            {
                var saved = await _db.LookupSources.AsNoTracking().Where(s => s.Id == savedId).Select(s => s.ApiKeyEncrypted).FirstOrDefaultAsync();
                apiKey = SensitiveDataHelper.TryDecrypt(saved, Secret);
            }
        }
        catch (InvalidOperationException ex)
        {
            return new LookupTestResultDto { Ok = false, Error = ex.Message };
        }

        var query = request.Query?.Trim() ?? string.Empty;
        if (query.Length == 0) return new LookupTestResultDto { Ok = false, Error = "Type something to search for." };

        var result = new LookupTestResultDto();
        try
        {
            var fetched = await FetchAsync(source, apiKey, query, CleanContext(request.Context), ct);
            result.StatusCode = fetched.StatusCode;
            result.RawSample = fetched.Body.Length > RawSampleChars ? fetched.Body[..RawSampleChars] + " …" : fetched.Body;
            if (!fetched.Success)
            {
                result.Error = $"The API answered with HTTP {fetched.StatusCode}.";
                return result;
            }
            using var doc = Parse(fetched.Body);
            var records = LookupMapping.ExtractRecords(doc.RootElement, source.ResultsPath);
            result.RecordCount = records.Count;
            result.Matches = records.Take(MaxResults).Select(r => ToMatch(source, r, query, includeMatchValues: true)).ToList();
            result.Ok = true;
            if (records.Count == 0)
                result.Error = "The API answered, but no records were found at the results path. Compare the path with the raw response below.";
            else if (result.Matches.All(m => string.IsNullOrEmpty(m.RecordId)))
                result.Error = "Records were found, but the ID property is empty for all of them. Check the ID path.";
        }
        catch (LookupUnavailableException ex)
        {
            result.Ok = false;
            result.Error = ex.Message;
        }
        return result;
    }

    public async Task<IReadOnlyList<LookupMatchDto>> SearchAsync(Guid sourceId, string? query,
        IReadOnlyDictionary<string, string>? context, CancellationToken ct)
    {
        var source = await _db.LookupSources.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sourceId && s.IsActive)
                     ?? throw new InvalidOperationException("This lookup is turned off. Type the value in instead.");
        var q = (query ?? string.Empty).Trim();
        if (q.Length > MaxQueryLength) q = q[..MaxQueryLength];
        if (q.Length < Math.Max(1, source.MinChars)) return [];

        var values = CleanContext(context);
        var used = LookupTemplates.FieldNames(source.SearchUrl, source.HttpMethod == "POST" ? source.BodyTemplate : null);
        var contextKey = string.Join("|", used.Select(n =>
            $"{n.ToLowerInvariant()}={values.FirstOrDefault(kv => string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase)).Value?.ToLowerInvariant()}"));
        var cacheKey = $"lookup:{source.TenantId}:{source.Id}:{(source.UpdatedAt ?? source.CreatedAt).Ticks}:{q.ToLowerInvariant()}:{contextKey}";
        if (_cache.TryGetValue(cacheKey, out List<LookupMatchDto>? cached) && cached is not null) return cached;

        var apiKey = SensitiveDataHelper.TryDecrypt(source.ApiKeyEncrypted, Secret);
        LookupFetch fetched;
        try
        {
            fetched = await FetchAsync(source, apiKey, q, values, ct);
        }
        catch (LookupUnavailableException ex)
        {
            throw new InvalidOperationException($"{source.Name} is not reachable right now ({ex.Message}) Type the value in instead.");
        }
        if (!fetched.Success)
        {
            _logger.LogWarning("Lookup {Source} answered HTTP {Status}", source.Name, fetched.StatusCode);
            throw new InvalidOperationException($"{source.Name} answered with an error (HTTP {fetched.StatusCode}). Type the value in instead.");
        }

        List<LookupMatchDto> matches;
        try
        {
            using var doc = Parse(fetched.Body);
            matches = LookupMapping.ExtractRecords(doc.RootElement, source.ResultsPath)
                .Take(MaxResults)
                .Select(r => ToMatch(source, r, q, includeMatchValues: false))
                .Where(m => m.RecordId.Length > 0)
                .ToList();
        }
        catch (LookupUnavailableException ex)
        {
            throw new InvalidOperationException($"{ex.Message} Type the value in instead.");
        }
        _cache.Set(cacheKey, matches, CacheFor);
        return matches;
    }

    public async Task<LookupMatchDto?> RecheckAsync(Guid sourceId, LookupRecheckRequest request, CancellationToken ct)
    {
        var recordId = request.RecordId?.Trim() ?? string.Empty;
        if (recordId.Length == 0) return null;
        var matches = await SearchAsync(sourceId, request.Query, request.Context, ct);
        return matches.FirstOrDefault(m => string.Equals(m.RecordId, recordId, StringComparison.Ordinal));
    }

    public LookupCheckResultDto Check(LookupCheckRequest request)
    {
        var selection = ReadSelection(request.Token)
                        ?? throw new InvalidOperationException("This pick has expired. Search and pick it again.");
        if (selection.MatchAgainst is not (LookupMatchTargets.Mobile or LookupMatchTargets.Email))
            return new LookupCheckResultDto();
        var visitorValue = selection.MatchAgainst == LookupMatchTargets.Mobile ? request.Telephone : request.Email;
        return new LookupCheckResultDto
        {
            MatchAgainst = selection.MatchAgainst,
            BlockOnMismatch = selection.BlockOnMismatch,
            Matched = LookupMapping.Normalize(visitorValue, selection.MatchAgainst) is null
                ? null
                : LookupMapping.Matches(selection.MatchValues, visitorValue, selection.MatchAgainst),
        };
    }

    public LookupSelection? ReadSelection(string token)
    {
        var selection = LookupTokens.Unprotect(token, Secret, DateTimeOffset.UtcNow);
        return selection is not null && selection.TenantId == TenantId ? selection : null;
    }

    private LookupMatchDto ToMatch(LookupSource source, JsonElement record, string query, bool includeMatchValues)
    {
        var recordId = LookupMapping.Truncate(LookupMapping.FirstText(record, source.IdPath) ?? string.Empty, 200);
        var display = LookupMapping.Render(source.DisplayTemplate, record);
        if (display.Length == 0) display = recordId;
        var details = ReadSavedFields(source.SavedFieldsJson)
            .Select(f => new PurposeFieldDetail
            {
                Label = f.Label,
                Value = LookupMapping.Truncate(string.Join(", ", LookupMapping.Texts(record, f.Path)), LookupMapping.MaxDetailLength),
            })
            .Where(d => d.Value.Length > 0)
            .ToList();
        var hasCheck = source.MatchAgainst is not null && !string.IsNullOrWhiteSpace(source.MatchPath);
        var matchValues = hasCheck
            ? LookupMapping.Texts(record, source.MatchPath)
                .Select(v => LookupMapping.Truncate(v, LookupMapping.MaxMatchValueLength))
                .Distinct()
                .Take(LookupMapping.MaxMatchValues)
                .ToList()
            : new List<string>();

        var token = recordId.Length == 0 || includeMatchValues
            ? string.Empty
            : LookupTokens.Protect(new LookupSelection
            {
                SourceId = source.Id,
                TenantId = source.TenantId,
                SourceName = source.Name,
                RecordId = recordId,
                Display = display,
                Query = query,
                Details = details,
                MatchValues = matchValues,
                MatchAgainst = hasCheck ? source.MatchAgainst : null,
                BlockOnMismatch = hasCheck && source.BlockOnMismatch,
                ExpiresAtUnix = DateTimeOffset.UtcNow.Add(LookupTokens.Lifetime).ToUnixTimeSeconds(),
            }, Secret);

        return new LookupMatchDto
        {
            Token = token,
            RecordId = recordId,
            Display = display,
            Details = details.Select(d => new LookupDetailDto { Label = d.Label, Value = d.Value }).ToList(),
            HasMatchCheck = hasCheck,
            MatchValues = includeMatchValues && hasCheck ? matchValues : null,
        };
    }

    private sealed record LookupFetch(bool Success, int StatusCode, string Body);

    private async Task<LookupFetch> FetchAsync(LookupSource source, string? apiKey, string query,
        IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var post = source.HttpMethod == "POST";
        Uri uri;
        try
        {
            uri = OutboundUrlGuard.BuildSearchUri(source.SearchUrl, query, AllowHttp, AllowPrivate, context,
                requireQuery: !post || !LookupTemplates.HasQuery(source.BodyTemplate));
        }
        catch (InvalidOperationException ex)
        {
            throw new LookupUnavailableException(ex.Message);
        }

        using var request = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (post)
        {
            var body = string.IsNullOrWhiteSpace(source.BodyTemplate)
                ? "{}"
                : LookupTemplates.Fill(source.BodyTemplate, query, context, LookupTemplates.JsonString);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(source.ApiKeyHeader))
            request.Headers.TryAddWithoutValidation(source.ApiKeyHeader, apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(source.TimeoutSeconds, 2, 15)));
        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                throw new LookupUnavailableException("The response is larger than 1 MB.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes)
                    throw new LookupUnavailableException("The response is larger than 1 MB.");
                buffer.Write(chunk, 0, read);
            }
            return new LookupFetch(response.IsSuccessStatusCode, (int)response.StatusCode, Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LookupUnavailableException($"No answer within {Math.Clamp(source.TimeoutSeconds, 2, 15)} seconds.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Lookup {Source} request failed", source.Name);
            throw new LookupUnavailableException(ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static JsonDocument Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            throw new LookupUnavailableException("The API did not return JSON.");
        }
    }

    /// <summary>Copies and validates everything except the name and API key.</summary>
    private void Apply(LookupSource entity, LookupSourceUpsertRequest request)
    {
        var method = string.IsNullOrWhiteSpace(request.HttpMethod) ? "GET" : request.HttpMethod.Trim().ToUpperInvariant();
        if (method is not ("GET" or "POST"))
            throw new InvalidOperationException("Choose GET or POST.");
        var body = method == "POST" && !string.IsNullOrWhiteSpace(request.BodyTemplate) ? request.BodyTemplate.Trim() : null;
        var url = request.SearchUrl?.Trim() ?? string.Empty;
        if (method == "POST" && !LookupTemplates.HasQuery(url) && !LookupTemplates.HasQuery(body))
            throw new InvalidOperationException($"Put {OutboundUrlGuard.QueryPlaceholder} in the request body (or the URL) where the typed text goes.");
        OutboundUrlGuard.BuildSearchUri(url, "test", AllowHttp, AllowPrivate, requireQuery: method == "GET");
        if (body is not null)
        {
            try
            {
                using var _ = JsonDocument.Parse(LookupTemplates.Fill(body, "test", null, LookupTemplates.JsonString));
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(
                    "The request body is not valid JSON. Put placeholders inside quotes, e.g. {\"fullName\": \"{query}\"}.");
            }
        }
        entity.SearchUrl = url;
        entity.HttpMethod = method;
        entity.BodyTemplate = body;

        var header = string.IsNullOrWhiteSpace(request.ApiKeyHeader) ? null : request.ApiKeyHeader.Trim();
        if (header is not null && (!HeaderName.IsMatch(header) || ReservedHeaders.Contains(header)))
            throw new InvalidOperationException($"\"{header}\" cannot be used as the API key header name.");
        entity.ApiKeyHeader = header;

        entity.ResultsPath = string.IsNullOrWhiteSpace(request.ResultsPath) ? null : request.ResultsPath.Trim();
        entity.IdPath = request.IdPath?.Trim() ?? string.Empty;
        if (entity.IdPath.Length == 0) throw new InvalidOperationException("ID property is required.");
        entity.DisplayTemplate = request.DisplayTemplate?.Trim() ?? string.Empty;
        if (entity.DisplayTemplate.Length == 0) throw new InvalidOperationException("Display text is required.");

        var saved = (request.SavedFields ?? [])
            .Select(f => new LookupSavedField { Path = f.Path?.Trim() ?? "", Label = f.Label?.Trim() ?? "" })
            .Where(f => f.Path.Length > 0)
            .Select(f => { if (f.Label.Length == 0) f.Label = f.Path; return f; })
            .ToList();
        if (saved.Count > MaxSavedFields)
            throw new InvalidOperationException($"Save at most {MaxSavedFields} properties per record.");
        entity.SavedFieldsJson = saved.Count == 0 ? null : JsonSerializer.Serialize(saved);

        var against = request.MatchAgainst?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(against) || against == "none") against = null;
        if (against is not null && against != LookupMatchTargets.Mobile && against != LookupMatchTargets.Email)
            throw new InvalidOperationException("The check can compare the visitor's mobile or email.");
        var matchPath = string.IsNullOrWhiteSpace(request.MatchPath) ? null : request.MatchPath.Trim();
        if (against is not null && matchPath is null)
            throw new InvalidOperationException("Enter the property to compare the visitor's details with.");
        entity.MatchAgainst = against;
        entity.MatchPath = against is null ? null : matchPath;
        entity.BlockOnMismatch = against is not null && request.BlockOnMismatch;

        entity.MinChars = Math.Clamp(request.MinChars, 1, 10);
        entity.TimeoutSeconds = Math.Clamp(request.TimeoutSeconds, 2, 15);
        entity.IsActive = request.IsActive;
    }

    private static Dictionary<string, string> CleanContext(IReadOnlyDictionary<string, string>? context)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in context ?? new Dictionary<string, string>())
        {
            if (result.Count >= MaxContextEntries) break;
            var k = key?.Trim();
            if (string.IsNullOrEmpty(k) || k.Length > PurposeFields.MaxLabelLength) continue;
            var v = (value ?? string.Empty).Trim();
            result[k] = v.Length > MaxContextValueLength ? v[..MaxContextValueLength] : v;
        }
        return result;
    }

    private static string ValidateHeaderValue(string value)
    {
        var v = value.Trim();
        if (v.Any(c => c == '\r' || c == '\n'))
            throw new InvalidOperationException("The API key cannot contain line breaks.");
        return v;
    }

    private static List<LookupSavedField> ReadSavedFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<LookupSavedField>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static LookupSourceDto Map(LookupSource s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        SearchUrl = s.SearchUrl,
        HttpMethod = s.HttpMethod,
        BodyTemplate = s.BodyTemplate,
        ApiKeyHeader = s.ApiKeyHeader,
        HasApiKey = !string.IsNullOrEmpty(s.ApiKeyEncrypted),
        ResultsPath = s.ResultsPath,
        IdPath = s.IdPath,
        DisplayTemplate = s.DisplayTemplate,
        SavedFields = ReadSavedFields(s.SavedFieldsJson).Select(f => new LookupSavedFieldDto { Path = f.Path, Label = f.Label }).ToList(),
        MatchPath = s.MatchPath,
        MatchAgainst = s.MatchAgainst,
        BlockOnMismatch = s.BlockOnMismatch,
        MinChars = s.MinChars,
        TimeoutSeconds = s.TimeoutSeconds,
        IsActive = s.IsActive,
        UpdatedAt = VisitorService.AsUtc(s.UpdatedAt ?? s.CreatedAt),
    };
}

public sealed class LookupUnavailableException : Exception
{
    public LookupUnavailableException(string message) : base(message) { }
}
