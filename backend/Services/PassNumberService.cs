using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

public interface IPassNumberService
{
    /// <summary>
    /// Allocates the lowest free pass number in the configured pool for the authenticated tenant.
    /// Numbers held by PendingApproval / Approved / Inside visits (or a short-lived allocate hold)
    /// are skipped; CheckedOut / Rejected / Cancelled visits release their number back into the pool.
    /// When <paramref name="locationId"/> is set, prefers that location's series (falls back to tenant default).
    /// </summary>
    Task<string> AllocateNextAsync(Guid? locationId = null, CancellationToken ct = default);

    /// <summary>Releases an allocate-time hold so the number can return to the free pool.</summary>
    Task ReleaseHoldAsync(string? passCode, CancellationToken ct = default);

    Task<PassNumberSeriesDto?> GetSeriesAsync(Guid? locationId = null, CancellationToken ct = default);
    Task<PassNumberSeriesDto> UpsertSeriesAsync(UpsertPassNumberSeriesRequest request, CancellationToken ct = default);
}

public class PassNumberService : IPassNumberService
{
    /// <summary>Visit statuses that still hold a physical pass badge.</summary>
    private static readonly int[] HoldingStatuses =
    [
        (int)VisitStatus.PendingApproval,
        (int)VisitStatus.Approved,
        (int)VisitStatus.Inside
    ];

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly ITenantContext _tenant;

    public PassNumberService(ApplicationDbContext db, IConfiguration config, ITenantContext tenant)
    {
        _db = db;
        _config = config;
        _tenant = tenant;
    }

    private Guid RequireTenantId() =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required to allocate a pass number.");

    private string ConnectionString =>
        _config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string DefaultConnection is missing.");

    /// <summary>
    /// Format choice: <c>{Prefix}{Year}-{number:D6}</c> (e.g. VMS-2026-000184).
    /// Year is the series Year when set; otherwise the current local calendar year.
    /// </summary>
    public static string FormatPassCode(string prefix, int? seriesYear, int number) =>
        $"{SeriesKey(prefix, seriesYear)}{number:D6}";

    /// <summary>
    /// The formatted code without its number (e.g. <c>VMS-2026-</c>). Each key is its own pool, so location
    /// series with different prefixes never block each other, while series sharing a prefix share numbers.
    /// </summary>
    public static string SeriesKey(string prefix, int? seriesYear)
    {
        var p = (prefix ?? "VMS-").Trim();
        if (p.Length > 20) p = p[..20];
        var year = seriesYear ?? AppTime.Now.Year;
        return $"{p}{year}-";
    }

    private static string? SeriesKeyOf(string passCode)
    {
        var code = passCode.Trim();
        var dash = code.LastIndexOf('-');
        return dash > 0 ? code[..(dash + 1)] : null;
    }

    /// <summary>Extracts the numeric suffix from a formatted pass code (segment after the last '-').</summary>
    public static bool TryParsePassNumber(string? passCode, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(passCode)) return false;
        var dash = passCode.LastIndexOf('-');
        var digits = dash >= 0 && dash < passCode.Length - 1
            ? passCode[(dash + 1)..]
            : passCode.Trim();
        return int.TryParse(digits, out number);
    }

    public async Task<string> AllocateNextAsync(Guid? locationId = null, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        await using var conn = await AdoSql.OpenAsync(ConnectionString, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            // Series sharing a prefix live on different rows, so row locks alone cannot serialise them.
            await AdoSql.ExecuteNonQueryAsync(
                conn, tx,
                "EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
                ct,
                AdoSql.NVarChar("@Resource", $"vms-pass:{tenantId}", 255));

            var fromSeries = await AllocateFromDefaultSeriesAsync(conn, tx, tenantId, locationId, ct);
            await tx.CommitAsync(ct);
            return fromSeries;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<string> AllocateFromDefaultSeriesAsync(
        SqlConnection conn,
        SqlTransaction tx,
        Guid tenantId,
        Guid? locationId,
        CancellationToken ct)
    {
        const string findSql = """
            SELECT TOP (1) Id, Prefix, Year, StartNumber, EndNumber
            FROM PassNumberSeries WITH (UPDLOCK, ROWLOCK)
            WHERE TenantId = @TenantId AND IsActive = 1 AND StartNumber <= EndNumber
              AND (
                    (@LocationId IS NOT NULL AND (LocationId = @LocationId OR LocationId IS NULL))
                 OR (@LocationId IS NULL AND LocationId IS NULL)
              )
            ORDER BY CASE
                WHEN @LocationId IS NOT NULL AND LocationId = @LocationId THEN 0
                ELSE 1
            END, CreatedAt
            """;

        Guid seriesId;
        string prefix;
        int? year;
        int startNumber;
        int endNumber;
        await using (var reader = await AdoSql.ExecuteReaderAsync(
                         conn, tx, findSql, ct,
                         AdoSql.GuidParam("@TenantId", tenantId),
                         AdoSql.GuidParam("@LocationId", locationId)))
        {
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException(
                    "No active pass number series configured. Configure Pass Settings.");
            seriesId = reader.GetGuid(0);
            prefix = reader.GetString(1);
            year = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            startNumber = reader.GetInt32(3);
            endNumber = reader.GetInt32(4);
        }

        var key = SeriesKey(prefix, year);
        await PurgeStaleHoldsAsync(conn, tx, tenantId, ct);
        var used = await LoadHeldPassNumbersAsync(conn, tx, tenantId, key, ct);
        var number = FindLowestFree(startNumber, endNumber, used);
        if (number is null)
            throw new InvalidOperationException(
                "All pass numbers in the pool are currently in use. Check out a visitor to free a pass, or widen the start/end range.");

        await AdoSql.ExecuteNonQueryAsync(
            conn, tx, """
                UPDATE PassNumberSeries
                SET CurrentNumber = @Number, UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id AND TenantId = @TenantId AND IsActive = 1
                """,
            ct,
            AdoSql.IntParam("@Number", number.Value),
            AdoSql.GuidParam("@Id", seriesId),
            AdoSql.GuidParam("@TenantId", tenantId));

        await InsertHoldAsync(conn, tx, tenantId, key, number.Value, ct);
        return FormatPassCode(prefix, year, number.Value);
    }

    public async Task ReleaseHoldAsync(string? passCode, CancellationToken ct = default)
    {
        if (!TryParsePassNumber(passCode, out var number))
            return;

        var tenantId = RequireTenantId();
        var key = SeriesKeyOf(passCode!);
        await using var conn = await AdoSql.OpenAsync(ConnectionString, ct);
        await AdoSql.ExecuteNonQueryAsync(
            conn, null, """
                DELETE FROM PassNumberHolds
                WHERE TenantId = @TenantId AND Number = @Number
                  AND (SeriesKey = @SeriesKey OR SeriesKey IS NULL)
                """,
            ct,
            AdoSql.GuidParam("@TenantId", tenantId),
            AdoSql.IntParam("@Number", number),
            AdoSql.NVarChar("@SeriesKey", key, 40));
    }

    private static async Task PurgeStaleHoldsAsync(
        SqlConnection conn,
        SqlTransaction tx,
        Guid tenantId,
        CancellationToken ct)
    {
        // Drop orphaned reservations if registration never completed (15 minutes).
        await AdoSql.ExecuteNonQueryAsync(
            conn, tx, """
                DELETE FROM PassNumberHolds
                WHERE TenantId = @TenantId
                  AND CreatedAt < DATEADD(minute, -15, SYSUTCDATETIME())
                """,
            ct,
            AdoSql.GuidParam("@TenantId", tenantId));
    }

    private static async Task InsertHoldAsync(
        SqlConnection conn,
        SqlTransaction tx,
        Guid tenantId,
        string seriesKey,
        int number,
        CancellationToken ct)
    {
        await AdoSql.ExecuteNonQueryAsync(
            conn, tx, """
                INSERT INTO PassNumberHolds (Id, TenantId, SeriesKey, Number, CreatedAt)
                VALUES (NEWID(), @TenantId, @SeriesKey, @Number, SYSUTCDATETIME())
                """,
            ct,
            AdoSql.GuidParam("@TenantId", tenantId),
            AdoSql.NVarChar("@SeriesKey", seriesKey, 40),
            AdoSql.IntParam("@Number", number));
    }

    private static async Task<HashSet<int>> LoadHeldPassNumbersAsync(
        SqlConnection conn,
        SqlTransaction tx,
        Guid tenantId,
        string seriesKey,
        CancellationToken ct)
    {
        // PendingApproval / Approved / Inside still hold the physical badge. Only codes from this series' pool count.
        const string visitSql = """
            SELECT PassNumber
            FROM VisitorVisits
            WHERE TenantId = @TenantId
              AND Status IN (@S1, @S2, @S3)
              AND PassNumber IS NOT NULL
              AND LEFT(LTRIM(PassNumber), LEN(@SeriesKey)) = @SeriesKey
            """;

        var used = new HashSet<int>();
        await using (var reader = await AdoSql.ExecuteReaderAsync(
                         conn, tx, visitSql, ct,
                         AdoSql.GuidParam("@TenantId", tenantId),
                         AdoSql.NVarChar("@SeriesKey", seriesKey, 40),
                         AdoSql.IntParam("@S1", HoldingStatuses[0]),
                         AdoSql.IntParam("@S2", HoldingStatuses[1]),
                         AdoSql.IntParam("@S3", HoldingStatuses[2])))
        {
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0) && TryParsePassNumber(reader.GetString(0), out var n))
                    used.Add(n);
            }
        }

        // Holds from before series keys existed have no key; treat them as blocking every pool until they expire.
        const string holdSql = """
            SELECT Number FROM PassNumberHolds
            WHERE TenantId = @TenantId AND (SeriesKey = @SeriesKey OR SeriesKey IS NULL)
            """;
        await using (var reader = await AdoSql.ExecuteReaderAsync(
                         conn, tx, holdSql, ct,
                         AdoSql.GuidParam("@TenantId", tenantId),
                         AdoSql.NVarChar("@SeriesKey", seriesKey, 40)))
        {
            while (await reader.ReadAsync(ct))
                used.Add(reader.GetInt32(0));
        }

        return used;
    }

    private static int? FindLowestFree(int startNumber, int endNumber, HashSet<int> used)
    {
        for (var n = startNumber; n <= endNumber; n++)
        {
            if (used.Contains(n)) continue;
            return n;
        }

        return null;
    }

    public async Task<PassNumberSeriesDto?> GetSeriesAsync(Guid? locationId = null, CancellationToken ct = default)
    {
        RequireTenantId();
        var series = await ResolveSeriesEntityAsync(locationId, track: false, ct);
        return series is null ? null : ToSeriesDto(series);
    }

    public async Task<PassNumberSeriesDto> UpsertSeriesAsync(UpsertPassNumberSeriesRequest request, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        ValidateSeriesRequest(request);

        if (request.LocationId is Guid locId)
        {
            var locOk = await _db.Locations.AnyAsync(l => l.Id == locId, ct);
            if (!locOk)
                throw new InvalidOperationException("Location not found.");
        }

        var series = await ResolveSeriesEntityAsync(request.LocationId, track: true, ct);

        if (series is null)
        {
            series = new PassNumberSeries
            {
                TenantId = tenantId,
                LocationId = request.LocationId,
                Prefix = request.Prefix.Trim(),
                Year = request.Year,
                StartNumber = request.StartNumber,
                EndNumber = request.EndNumber,
                // Current = last issued. Leave at Start-1 so the first allocate hands out Start.
                CurrentNumber = request.CurrentNumber ?? (request.StartNumber - 1),
                IsActive = request.IsActive
            };
            _db.PassNumberSeries.Add(series);
        }
        else
        {
            series.Prefix = request.Prefix.Trim();
            series.Year = request.Year;
            series.StartNumber = request.StartNumber;
            series.EndNumber = request.EndNumber;
            if (request.CurrentNumber.HasValue)
                series.CurrentNumber = request.CurrentNumber.Value;
            series.IsActive = request.IsActive;
            series.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return ToSeriesDto(series);
    }

    private async Task<PassNumberSeries?> ResolveSeriesEntityAsync(
        Guid? locationId,
        bool track,
        CancellationToken ct)
    {
        IQueryable<PassNumberSeries> q = track ? _db.PassNumberSeries : _db.PassNumberSeries.AsNoTracking();
        if (locationId is Guid loc)
            return await q.FirstOrDefaultAsync(s => s.LocationId == loc, ct);

        return await q.Where(s => s.LocationId == null).OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
    }

    private static void ValidateSeriesRequest(UpsertPassNumberSeriesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prefix))
            throw new InvalidOperationException("Prefix is required.");
        if (request.Prefix.Trim().Length > 20)
            throw new InvalidOperationException("Prefix must be at most 20 characters.");
        if (request.StartNumber > request.EndNumber)
            throw new InvalidOperationException("Start number must be less than or equal to end number.");
        if (request.CurrentNumber.HasValue &&
            (request.CurrentNumber.Value < request.StartNumber - 1 || request.CurrentNumber.Value > request.EndNumber))
            throw new InvalidOperationException("Current number is outside the allowed range.");
    }

    private static PassNumberSeriesDto ToSeriesDto(PassNumberSeries s) => new()
    {
        Id = s.Id,
        LocationId = s.LocationId,
        Prefix = s.Prefix,
        Year = s.Year,
        StartNumber = s.StartNumber,
        EndNumber = s.EndNumber,
        CurrentNumber = s.CurrentNumber,
        IsActive = s.IsActive,
        FormatExample = FormatPassCode(s.Prefix, s.Year, Math.Max(s.CurrentNumber + 1, s.StartNumber))
    };
}
