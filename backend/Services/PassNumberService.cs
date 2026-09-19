using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface IPassNumberService
{
    /// <summary>
    /// Atomically allocates the next pass code for the authenticated tenant.
    /// Prefers an active user allocation overlapping an active series; otherwise the default series.
    /// </summary>
    Task<string> AllocateNextAsync(string? userId, CancellationToken ct = default);

    Task<PassNumberSeriesDto?> GetSeriesAsync(CancellationToken ct = default);
    Task<PassNumberSeriesDto> UpsertSeriesAsync(UpsertPassNumberSeriesRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PassNumberAllocationDto>> ListAllocationsAsync(CancellationToken ct = default);
    Task<PassNumberAllocationDto> UpsertAllocationAsync(UpsertPassNumberAllocationRequest request, CancellationToken ct = default);
}

public class PassNumberService : IPassNumberService
{
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
    public static string FormatPassCode(string prefix, int? seriesYear, int number)
    {
        var p = (prefix ?? "VMS-").Trim();
        if (p.Length > 20) p = p[..20];
        var year = seriesYear ?? DateTime.Now.Year;
        return $"{p}{year}-{number:D6}";
    }

    public async Task<string> AllocateNextAsync(string? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        await using var conn = await AdoSql.OpenAsync(ConnectionString, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var allocated = await TryAllocateFromUserAsync(conn, tx, tenantId, userId.Trim(), ct);
                if (allocated is not null)
                {
                    await tx.CommitAsync(ct);
                    return allocated;
                }
            }

            var fromSeries = await AllocateFromDefaultSeriesAsync(conn, tx, tenantId, ct);
            await tx.CommitAsync(ct);
            return fromSeries;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<string?> TryAllocateFromUserAsync(
        SqlConnection conn,
        SqlTransaction tx,
        Guid tenantId,
        string userId,
        CancellationToken ct)
    {
        // Active allocation whose range still has capacity, overlapping an active series.
        const string findSql = """
            SELECT TOP (1)
                a.Id, a.CurrentNumber, a.EndNumber, s.Prefix, s.Year
            FROM PassNumberAllocations a
            INNER JOIN PassNumberSeries s ON s.Id = a.SeriesId AND s.TenantId = a.TenantId
            WHERE a.TenantId = @TenantId
              AND a.UserId = @UserId
              AND a.IsActive = 1
              AND s.IsActive = 1
              AND a.CurrentNumber < a.EndNumber
              AND a.StartNumber <= a.EndNumber
              AND a.StartNumber >= s.StartNumber
              AND a.EndNumber <= s.EndNumber
            ORDER BY a.CreatedAt
            """;

        Guid allocationId;
        string prefix;
        int? year;
        await using (var reader = await AdoSql.ExecuteReaderAsync(
                         conn, tx, findSql, ct,
                         AdoSql.GuidParam("@TenantId", tenantId),
                         AdoSql.NVarChar("@UserId", userId, 450)))
        {
            if (!await reader.ReadAsync(ct))
                return null;
            allocationId = reader.GetGuid(0);
            prefix = reader.GetString(3);
            year = reader.IsDBNull(4) ? null : reader.GetInt32(4);
        }

        const string bumpSql = """
            UPDATE PassNumberAllocations
            SET CurrentNumber = CurrentNumber + 1, UpdatedAt = SYSUTCDATETIME()
            OUTPUT INSERTED.CurrentNumber
            WHERE Id = @Id AND TenantId = @TenantId AND IsActive = 1 AND CurrentNumber < EndNumber
            """;

        var result = await AdoSql.ExecuteScalarAsync(
            conn, tx, bumpSql, ct,
            AdoSql.GuidParam("@Id", allocationId),
            AdoSql.GuidParam("@TenantId", tenantId));

        if (result is null || result is DBNull)
            return null;

        var number = Convert.ToInt32(result);
        return FormatPassCode(prefix, year, number);
    }

    private static async Task<string> AllocateFromDefaultSeriesAsync(
        SqlConnection conn,
        SqlTransaction tx,
        Guid tenantId,
        CancellationToken ct)
    {
        const string findSql = """
            SELECT TOP (1) Id, Prefix, Year
            FROM PassNumberSeries
            WHERE TenantId = @TenantId AND IsActive = 1 AND CurrentNumber < EndNumber
            ORDER BY CASE WHEN SiteId IS NULL THEN 0 ELSE 1 END, CreatedAt
            """;

        Guid seriesId;
        string prefix;
        int? year;
        await using (var reader = await AdoSql.ExecuteReaderAsync(
                         conn, tx, findSql, ct,
                         AdoSql.GuidParam("@TenantId", tenantId)))
        {
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException(
                    "No active pass number series with remaining capacity. Configure Pass Settings.");
            seriesId = reader.GetGuid(0);
            prefix = reader.GetString(1);
            year = reader.IsDBNull(2) ? null : reader.GetInt32(2);
        }

        const string bumpSql = """
            UPDATE PassNumberSeries
            SET CurrentNumber = CurrentNumber + 1, UpdatedAt = SYSUTCDATETIME()
            OUTPUT INSERTED.CurrentNumber
            WHERE Id = @Id AND TenantId = @TenantId AND IsActive = 1 AND CurrentNumber < EndNumber
            """;

        var result = await AdoSql.ExecuteScalarAsync(
            conn, tx, bumpSql, ct,
            AdoSql.GuidParam("@Id", seriesId),
            AdoSql.GuidParam("@TenantId", tenantId));

        if (result is null || result is DBNull)
            throw new InvalidOperationException("Pass number series is exhausted.");

        var number = Convert.ToInt32(result);
        return FormatPassCode(prefix, year, number);
    }

    public async Task<PassNumberSeriesDto?> GetSeriesAsync(CancellationToken ct = default)
    {
        RequireTenantId();
        var series = await _db.PassNumberSeries.AsNoTracking()
            .OrderBy(s => s.SiteId == null ? 0 : 1)
            .ThenBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return series is null ? null : ToSeriesDto(series);
    }

    public async Task<PassNumberSeriesDto> UpsertSeriesAsync(UpsertPassNumberSeriesRequest request, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        ValidateSeriesRequest(request);

        var series = await _db.PassNumberSeries
            .OrderBy(s => s.SiteId == null ? 0 : 1)
            .ThenBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (series is null)
        {
            series = new PassNumberSeries
            {
                TenantId = tenantId,
                SiteId = request.SiteId,
                Prefix = request.Prefix.Trim(),
                Year = request.Year,
                StartNumber = request.StartNumber,
                EndNumber = request.EndNumber,
                CurrentNumber = request.CurrentNumber ?? (request.StartNumber - 1),
                IsActive = request.IsActive
            };
            _db.PassNumberSeries.Add(series);
        }
        else
        {
            series.SiteId = request.SiteId;
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

    public async Task<IReadOnlyList<PassNumberAllocationDto>> ListAllocationsAsync(CancellationToken ct = default)
    {
        RequireTenantId();
        var rows = await _db.PassNumberAllocations.AsNoTracking()
            .Include(a => a.User)
            .OrderByDescending(a => a.IsActive)
            .ThenBy(a => a.User != null ? a.User.FullName : a.UserId)
            .ToListAsync(ct);
        return rows.Select(ToAllocationDto).ToList();
    }

    public async Task<PassNumberAllocationDto> UpsertAllocationAsync(
        UpsertPassNumberAllocationRequest request,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new InvalidOperationException("User is required for a pass allocation.");
        if (request.StartNumber > request.EndNumber)
            throw new InvalidOperationException("Start number must be less than or equal to end number.");

        var series = request.SeriesId.HasValue
            ? await _db.PassNumberSeries.FirstOrDefaultAsync(s => s.Id == request.SeriesId.Value, ct)
            : await _db.PassNumberSeries.OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (series is null)
            throw new InvalidOperationException("Configure a pass number series before creating allocations.");

        if (request.StartNumber < series.StartNumber || request.EndNumber > series.EndNumber)
            throw new InvalidOperationException("Allocation range must sit within the series range.");

        var userExists = await _db.Users.AnyAsync(u => u.Id == request.UserId, ct);
        if (!userExists)
            throw new InvalidOperationException("User not found.");

        PassNumberAllocation allocation;
        if (request.Id.HasValue)
        {
            allocation = await _db.PassNumberAllocations.FirstOrDefaultAsync(a => a.Id == request.Id.Value, ct)
                ?? throw new InvalidOperationException("Allocation not found.");
            allocation.UserId = request.UserId.Trim();
            allocation.SeriesId = series.Id;
            allocation.StartNumber = request.StartNumber;
            allocation.EndNumber = request.EndNumber;
            if (request.CurrentNumber.HasValue)
                allocation.CurrentNumber = request.CurrentNumber.Value;
            allocation.IsActive = request.IsActive;
            allocation.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            allocation = new PassNumberAllocation
            {
                TenantId = tenantId,
                SeriesId = series.Id,
                UserId = request.UserId.Trim(),
                StartNumber = request.StartNumber,
                EndNumber = request.EndNumber,
                CurrentNumber = request.CurrentNumber ?? (request.StartNumber - 1),
                IsActive = request.IsActive
            };
            _db.PassNumberAllocations.Add(allocation);
        }

        await _db.SaveChangesAsync(ct);
        await _db.Entry(allocation).Reference(a => a.User).LoadAsync(ct);
        return ToAllocationDto(allocation);
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
        SiteId = s.SiteId,
        Prefix = s.Prefix,
        Year = s.Year,
        StartNumber = s.StartNumber,
        EndNumber = s.EndNumber,
        CurrentNumber = s.CurrentNumber,
        IsActive = s.IsActive,
        FormatExample = FormatPassCode(s.Prefix, s.Year, Math.Max(s.CurrentNumber + 1, s.StartNumber))
    };

    private static PassNumberAllocationDto ToAllocationDto(PassNumberAllocation a) => new()
    {
        Id = a.Id,
        SeriesId = a.SeriesId,
        UserId = a.UserId,
        UserName = a.User?.UserName,
        FullName = a.User?.FullName,
        StartNumber = a.StartNumber,
        EndNumber = a.EndNumber,
        CurrentNumber = a.CurrentNumber,
        IsActive = a.IsActive
    };
}
