using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public static class DesignationMaster
{
    /// <summary>Adds a designation typed on an employee (or imported from Excel) to the master if it is new.</summary>
    public static async Task EnsureAsync(ApplicationDbContext db, Guid tenantId, string? designation, string? user)
    {
        var name = designation?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100) return;
        if (await db.Designations.AnyAsync(d => d.Name == name)) return;
        if (db.Designations.Local.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        db.Designations.Add(new Designation { TenantId = tenantId, Name = name, CreatedBy = user });
    }
}
