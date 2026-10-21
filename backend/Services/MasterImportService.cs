using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;

namespace Tiaano.Vms.Api.Services;

public interface IMasterImportService
{
    bool IsSupported(string kind);
    Task<byte[]> BuildTemplateAsync(string kind);
    Task<MasterImportResultDto> ImportAsync(string kind, Stream excel, string? user);
    Task<IntegrationEmployeeBatchResult> ImportEmployeesAsync(IReadOnlyList<IntegrationEmployeeInput> employees, string? user);
}

/// <summary>
/// Excel template download + bulk upload for master tables. Rows go through the same
/// upsert methods as the UI forms, so validation rules stay in one place.
/// Existing records are matched (by code, then name) and updated instead of duplicated.
/// </summary>
public class MasterImportService : IMasterImportService
{
    private const string DataSheet = "Data";
    private const string ListsSheet = "Lists";
    private const int TemplateRows = 1000;
    private const int MaxImportRows = 5000;

    private enum ColumnType { Text, Number, YesNo, Location, Department, Designation }

    private sealed record Column(string Key, string Header, ColumnType Type, bool Required, string Help, params string[] Aliases);

    private static readonly Dictionary<string, (string Title, Column[] Columns)> Definitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["departments"] = ("Departments", new[]
            {
                new Column("name", "Name", ColumnType.Text, true, "Department name."),
                new Column("code", "Code", ColumnType.Text, false, "Optional short code. Used to match existing departments on re-upload."),
                new Column("intercom", "Intercom", ColumnType.Text, false, "Optional intercom / extension."),
                new Column("location", "Location", ColumnType.Location, false, "Location name or code. Leave blank for all locations."),
                new Column("sort", "Sort Order", ColumnType.Number, false, "Whole number; lower shows first."),
                new Column("active", "Active", ColumnType.YesNo, false, "Yes or No. Default Yes."),
            }),
            ["employees"] = ("Employees", new[]
            {
                new Column("name", "Full Name", ColumnType.Text, true, "Employee full name.", "name", "employeename"),
                new Column("code", "Employee Code", ColumnType.Text, false, "Optional employee code. Used to match existing employees on re-upload.", "code"),
                new Column("designation", "Designation", ColumnType.Designation, false, "Optional job title. Pick from the Lists sheet; a new title is added to Masters → Designations."),
                new Column("department", "Department", ColumnType.Department, true, "Department name or code (must already exist)."),
                new Column("location", "Location", ColumnType.Location, false, "Location name or code. Leave blank for all locations."),
                new Column("phone", "Phone", ColumnType.Text, false, "Optional 10-digit mobile number.", "mobile"),
                new Column("email", "Email", ColumnType.Text, false, "Optional email address."),
                new Column("intercom", "Intercom", ColumnType.Text, false, "Optional intercom / extension."),
                new Column("active", "Active", ColumnType.YesNo, false, "Yes or No. Default Yes."),
            }),
            ["purposes"] = ("Purposes", new[]
            {
                new Column("name", "Name", ColumnType.Text, true, "Purpose of visit."),
                new Column("code", "Code", ColumnType.Text, false, "Optional short code. Used to match existing purposes on re-upload."),
                new Column("location", "Location", ColumnType.Location, true, "Location name or code (required)."),
                new Column("sort", "Sort Order", ColumnType.Number, false, "Whole number; lower shows first."),
                new Column("active", "Active", ColumnType.YesNo, false, "Yes or No. Default Yes."),
            }),
            ["locations"] = ("Locations", new[]
            {
                new Column("name", "Name", ColumnType.Text, true, "Location name."),
                new Column("code", "Code", ColumnType.Text, false, "Optional short code. Used to match existing locations on re-upload."),
                new Column("sort", "Sort Order", ColumnType.Number, false, "Whole number; lower shows first."),
                new Column("active", "Active", ColumnType.YesNo, false, "Yes or No. Default Yes."),
                new Column("plant", "Requires Plant Number", ColumnType.YesNo, false, "Yes or No. Default No."),
                new Column("other", "Requires Other Text", ColumnType.YesNo, false, "Yes or No. Default No."),
            }),
            ["designations"] = ("Designations", new[]
            {
                new Column("name", "Name", ColumnType.Text, true, "Designation (job title), e.g. Manager. Up to 100 characters.", "designation"),
                new Column("code", "Code", ColumnType.Text, false, "Optional short code. Used to match existing designations on re-upload."),
                new Column("sort", "Sort Order", ColumnType.Number, false, "Whole number; lower shows first."),
                new Column("active", "Active", ColumnType.YesNo, false, "Yes or No. Default Yes."),
            }),
            ["meeting-halls"] = ("Meeting Halls", new[]
            {
                new Column("code", "Code", ColumnType.Text, true, "Unique hall code. Used to match existing halls on re-upload."),
                new Column("name", "Name", ColumnType.Text, true, "Meeting hall name."),
                new Column("capacity", "Seating Capacity", ColumnType.Number, true, "Whole number of seats (at least 1).", "capacity", "seats"),
                new Column("intercom", "Intercom No", ColumnType.Text, false, "Optional intercom / extension.", "intercom"),
                new Column("location", "Location", ColumnType.Location, true, "Location name or code (required)."),
                new Column("active", "Active", ColumnType.YesNo, false, "Yes or No. Default Yes.", "isactive"),
            }),
            ["feedback-questions"] = ("Feedback Questions", new[]
            {
                new Column("prompt", "Question", ColumnType.Text, true, "Feedback question shown to the visitor.", "prompt"),
                new Column("location", "Location", ColumnType.Location, true, "Location name or code (required)."),
                new Column("required", "Required", ColumnType.YesNo, false, "Yes or No. Default Yes."),
                new Column("sort", "Sort Order", ColumnType.Number, false, "Whole number; lower shows first."),
                new Column("active", "Active", ColumnType.YesNo, false, "Yes or No. Default Yes."),
            }),
        };

    private readonly ApplicationDbContext _db;
    private readonly IMasterDataService _masters;
    private readonly IFeedbackService _feedback;

    public MasterImportService(ApplicationDbContext db, IMasterDataService masters, IFeedbackService feedback)
    {
        _db = db;
        _masters = masters;
        _feedback = feedback;
    }

    public bool IsSupported(string kind) => Definitions.ContainsKey(kind);

    public async Task<byte[]> BuildTemplateAsync(string kind)
    {
        var (title, columns) = Definition(kind);

        var locationNames = await _db.Locations.AsNoTracking()
            .Where(l => l.IsActive).OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .Select(l => l.Name).ToListAsync();
        var departmentNames = columns.Any(c => c.Type == ColumnType.Department)
            ? (await _db.Departments.AsNoTracking().Where(d => d.IsActive)
                .OrderBy(d => d.Name).Select(d => d.Name).ToListAsync())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        var designationNames = columns.Any(c => c.Type == ColumnType.Designation)
            ? await _db.Designations.AsNoTracking().Where(d => d.IsActive)
                .OrderBy(d => d.SortOrder).ThenBy(d => d.Name).Select(d => d.Name).ToListAsync()
            : new List<string>();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(DataSheet);
        var lists = wb.Worksheets.Add(ListsSheet);
        var help = wb.Worksheets.Add("Instructions");

        lists.Cell(1, 1).Value = "Locations";
        for (var i = 0; i < locationNames.Count; i++) lists.Cell(i + 2, 1).Value = locationNames[i];
        lists.Cell(1, 2).Value = "Departments";
        for (var i = 0; i < departmentNames.Count; i++) lists.Cell(i + 2, 2).Value = departmentNames[i];
        lists.Cell(1, 3).Value = "Yes / No";
        lists.Cell(2, 3).Value = "Yes";
        lists.Cell(3, 3).Value = "No";
        lists.Cell(1, 4).Value = "Designations";
        for (var i = 0; i < designationNames.Count; i++) lists.Cell(i + 2, 4).Value = designationNames[i];
        lists.Row(1).Style.Font.Bold = true;
        lists.Columns(1, 4).AdjustToContents();

        for (var i = 0; i < columns.Length; i++)
        {
            var col = columns[i];
            var cell = ws.Cell(1, i + 1);
            cell.Value = col.Required ? $"{col.Header} *" : col.Header;
            cell.CreateComment().AddText(col.Help);

            var body = ws.Range(2, i + 1, TemplateRows + 1, i + 1);
            switch (col.Type)
            {
                case ColumnType.Text:
                    body.Style.NumberFormat.Format = "@";
                    break;
                case ColumnType.Number:
                    body.CreateDataValidation().WholeNumber.Between(-100000, 100000);
                    break;
                case ColumnType.YesNo:
                    body.CreateDataValidation().List(lists.Range(2, 3, 3, 3), true);
                    break;
                case ColumnType.Location when locationNames.Count > 0:
                    AddSoftListValidation(body, lists.Range(2, 1, locationNames.Count + 1, 1));
                    break;
                case ColumnType.Department when departmentNames.Count > 0:
                    AddSoftListValidation(body, lists.Range(2, 2, departmentNames.Count + 1, 2));
                    break;
                case ColumnType.Designation:
                    body.Style.NumberFormat.Format = "@";
                    if (designationNames.Count > 0)
                        AddSoftListValidation(body, lists.Range(2, 4, designationNames.Count + 1, 4),
                            "Value is not in the Lists sheet. A new designation will be added to the master.");
                    break;
            }
            ws.Column(i + 1).Width = Math.Max(16, col.Header.Length + 6);
        }

        var header = ws.Range(1, 1, 1, columns.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
        ws.SheetView.FreezeRows(1);

        help.Cell(1, 1).Value = $"{title} upload template";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(1, 1).Style.Font.FontSize = 14;
        help.Cell(2, 1).Value = "Fill one record per row in the 'Data' sheet, starting at row 2. Do not rename or reorder the header row.";
        help.Cell(3, 1).Value = "Columns marked * are required. Rows matching an existing record (same code, or same name) update that record instead of adding a duplicate.";
        help.Cell(4, 1).Value = "The 'Lists' sheet shows the locations, departments and designations you can pick from.";
        help.Cell(6, 1).Value = "Column";
        help.Cell(6, 2).Value = "Required";
        help.Cell(6, 3).Value = "Description";
        help.Range(6, 1, 6, 3).Style.Font.Bold = true;
        for (var i = 0; i < columns.Length; i++)
        {
            help.Cell(7 + i, 1).Value = columns[i].Header;
            help.Cell(7 + i, 2).Value = columns[i].Required ? "Yes" : "No";
            help.Cell(7 + i, 3).Value = columns[i].Help;
        }
        help.Column(1).Width = 26;
        help.Column(2).Width = 10;
        help.Column(3).Width = 80;

        ws.SetTabActive();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Dropdown that still accepts codes typed by hand (warning instead of hard stop).</summary>
    private static void AddSoftListValidation(IXLRange body, IXLRange source,
        string message = "Value is not in the Lists sheet. Codes are also accepted.")
    {
        var dv = body.CreateDataValidation();
        dv.List(source, true);
        dv.ErrorStyle = XLErrorStyle.Warning;
        dv.ErrorTitle = "Not in list";
        dv.ErrorMessage = message;
    }

    public async Task<MasterImportResultDto> ImportAsync(string kind, Stream excel, string? user)
    {
        var (_, columns) = Definition(kind);
        var result = new MasterImportResultDto();

        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(excel);
        }
        catch
        {
            throw new InvalidOperationException("Could not read the file. Upload the .xlsx template downloaded from this page.");
        }

        using (wb)
        {
            var ws = wb.Worksheets.TryGetWorksheet(DataSheet, out var data) ? data : wb.Worksheet(1);
            var headerMap = MapHeaders(ws, columns);
            var missing = columns.Where(c => c.Required && !headerMap.ContainsKey(c.Key)).Select(c => c.Header).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Missing required column(s): {string.Join(", ", missing)}. Use the downloaded template.");

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            if (lastRow - 1 > MaxImportRows)
                throw new InvalidOperationException($"Too many rows. Upload at most {MaxImportRows} rows at a time.");

            var locations = await LoadLocationsAsync();

            for (var r = 2; r <= lastRow; r++)
            {
                var values = headerMap.ToDictionary(
                    kv => kv.Key,
                    kv => ws.Cell(r, kv.Value).GetFormattedString().Trim());
                if (values.Values.All(string.IsNullOrWhiteSpace))
                    continue;

                result.TotalRows++;
                try
                {
                    var row = new RowValues(values);
                    foreach (var col in columns.Where(c => c.Required))
                        if (string.IsNullOrWhiteSpace(row.Text(col.Key)))
                            throw new InvalidOperationException($"'{col.Header}' is required.");

                    var created = kind.ToLowerInvariant() switch
                    {
                        "departments" => await ImportDepartmentAsync(row, locations, user),
                        "employees" => await ImportEmployeeAsync(row, locations, user),
                        "purposes" => await ImportPurposeAsync(row, locations, user),
                        "locations" => await ImportLocationAsync(row, user),
                        "designations" => await ImportDesignationAsync(row, user),
                        "feedback-questions" => await ImportFeedbackQuestionAsync(row, locations, user),
                        "meeting-halls" => await ImportMeetingHallAsync(row, locations, user),
                        _ => throw new InvalidOperationException("Unsupported import type."),
                    };
                    if (kind.Equals("locations", StringComparison.OrdinalIgnoreCase))
                        locations = await LoadLocationsAsync();

                    if (created) result.Created++;
                    else result.Updated++;
                }
                catch (Exception ex)
                {
                    _db.ChangeTracker.Clear();
                    result.Failed++;
                    result.Errors.Add(new MasterImportRowError { Row = r, Message = ex.Message });
                }
            }
        }

        return result;
    }

    /// <summary>Employee feed from another application; same matching and validation as the Excel upload.</summary>
    public async Task<IntegrationEmployeeBatchResult> ImportEmployeesAsync(
        IReadOnlyList<IntegrationEmployeeInput> employees, string? user)
    {
        if (employees.Count > MaxImportRows)
            throw new InvalidOperationException($"Too many employees. Send at most {MaxImportRows} at a time.");

        var result = new IntegrationEmployeeBatchResult { Total = employees.Count };
        var locations = await LoadLocationsAsync();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < employees.Count; i++)
        {
            var input = employees[i];
            try
            {
                if (input is null)
                    throw new InvalidOperationException("Employee entry is empty.");
                if (string.IsNullOrWhiteSpace(input.Code))
                    throw new InvalidOperationException("'code' is required.");
                if (string.IsNullOrWhiteSpace(input.FullName))
                    throw new InvalidOperationException("'fullName' is required.");
                if (string.IsNullOrWhiteSpace(input.Department))
                    throw new InvalidOperationException("'department' is required.");
                if (!seenCodes.Add(input.Code.Trim()))
                    throw new InvalidOperationException($"Employee code '{input.Code.Trim()}' appears more than once in this request.");

                var row = new RowValues(new Dictionary<string, string>
                {
                    ["code"] = input.Code,
                    ["name"] = input.FullName,
                    ["department"] = input.Department,
                    ["location"] = input.Location ?? "",
                    ["designation"] = input.Designation ?? "",
                    ["phone"] = input.Phone ?? "",
                    ["email"] = input.Email ?? "",
                    ["intercom"] = input.Intercom ?? "",
                    ["active"] = input.Active is bool active ? (active ? "yes" : "no") : "",
                });

                if (await ImportEmployeeAsync(row, locations, user)) result.Created++;
                else result.Updated++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UnauthorizedAccessException or DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                result.Failed++;
                result.Errors.Add(new IntegrationEmployeeError
                {
                    Index = i,
                    Code = input?.Code?.Trim(),
                    Message = ex is DbUpdateException ? "Could not save this employee." : ex.Message,
                });
            }
        }

        return result;
    }

    private async Task<bool> ImportDepartmentAsync(RowValues row, List<LocationRef> locations, string? user)
    {
        var name = row.Text("name")!;
        var code = row.Text("code");
        var locationId = ResolveLocation(row.Text("location"), locations, required: false);

        var existing = code is not null
            ? await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Code == code)
            : await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Name == name && d.LocationId == locationId);

        await _masters.UpsertDepartmentAsync(existing?.Id, new MasterUpsertRequest
        {
            Name = name,
            Code = code ?? existing?.Code,
            Intercom = row.Text("intercom") ?? existing?.Intercom,
            LocationId = row.Has("location") ? locationId : existing?.LocationId,
            SortOrder = row.Int("sort") ?? existing?.SortOrder ?? 0,
            IsActive = row.Bool("active") ?? existing?.IsActive ?? true,
        }, user);
        return existing is null;
    }

    private async Task<bool> ImportEmployeeAsync(RowValues row, List<LocationRef> locations, string? user)
    {
        var name = row.Text("name")!;
        var code = row.Text("code");
        var email = row.Text("email");
        var locationId = ResolveLocation(row.Text("location"), locations, required: false);
        var departmentId = await ResolveDepartmentAsync(row.Text("department")!, locationId);

        var existing = code is not null
            ? await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == code)
            : email is not null
                ? await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Email == email)
                : await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.FullName == name && e.DepartmentId == departmentId);

        await _masters.UpsertEmployeeAsync(existing?.Id, new CreateEmployeeRequest
        {
            FullName = name,
            Code = code ?? existing?.Code,
            Designation = row.Text("designation") ?? existing?.Designation,
            DepartmentId = departmentId,
            LocationId = row.Has("location") ? locationId : existing?.LocationId,
            Phone = row.Text("phone") ?? existing?.Phone,
            Email = email ?? existing?.Email,
            Intercom = row.Text("intercom") ?? existing?.Intercom,
            UserId = existing?.UserId,
            IsActive = row.Bool("active") ?? existing?.IsActive ?? true,
        }, user);
        return existing is null;
    }

    private async Task<bool> ImportPurposeAsync(RowValues row, List<LocationRef> locations, string? user)
    {
        var name = row.Text("name")!;
        var code = row.Text("code");
        var locationId = ResolveLocation(row.Text("location"), locations, required: true);

        var existing = code is not null
            ? await _db.VisitPurposes.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code)
            : await _db.VisitPurposes.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name && p.LocationId == locationId);

        await _masters.UpsertPurposeAsync(existing?.Id, new MasterUpsertRequest
        {
            Name = name,
            Code = code ?? existing?.Code,
            LocationId = locationId,
            SortOrder = row.Int("sort") ?? existing?.SortOrder ?? 0,
            IsActive = row.Bool("active") ?? existing?.IsActive ?? true,
        }, user);
        return existing is null;
    }

    private async Task<bool> ImportLocationAsync(RowValues row, string? user)
    {
        var name = row.Text("name")!;
        var code = row.Text("code");

        var existing = code is not null
            ? await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code)
            : await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Name == name);

        await _masters.UpsertLocationAsync(existing?.Id, new MasterUpsertRequest
        {
            Name = name,
            Code = code ?? existing?.Code,
            SortOrder = row.Int("sort") ?? existing?.SortOrder ?? 0,
            IsActive = row.Bool("active") ?? existing?.IsActive ?? true,
            RequiresPlantNumber = row.Bool("plant") ?? existing?.RequiresPlantNumber ?? false,
            RequiresOtherText = row.Bool("other") ?? existing?.RequiresOtherText ?? false,
        }, user);
        return existing is null;
    }

    private async Task<bool> ImportDesignationAsync(RowValues row, string? user)
    {
        var name = row.Text("name")!;
        var code = row.Text("code");

        var existing = (code is not null
                ? await _db.Designations.AsNoTracking().FirstOrDefaultAsync(d => d.Code == code)
                : null)
            ?? await _db.Designations.AsNoTracking().FirstOrDefaultAsync(d => d.Name == name);

        await _masters.UpsertDesignationAsync(existing?.Id, new MasterUpsertRequest
        {
            Name = name,
            Code = code ?? existing?.Code,
            SortOrder = row.Int("sort") ?? existing?.SortOrder ?? 0,
            IsActive = row.Bool("active") ?? existing?.IsActive ?? true,
        }, user);
        return existing is null;
    }

    private async Task<bool> ImportFeedbackQuestionAsync(RowValues row, List<LocationRef> locations, string? user)
    {
        var prompt = row.Text("prompt")!;
        var locationId = ResolveLocation(row.Text("location"), locations, required: true);

        var existing = await _db.FeedbackQuestions.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Prompt == prompt && q.LocationId == locationId);

        await _feedback.UpsertQuestionAsync(existing?.Id, new FeedbackQuestionUpsertRequest
        {
            Prompt = prompt,
            LocationId = locationId,
            IsRequired = row.Bool("required") ?? existing?.IsRequired ?? true,
            SortOrder = row.Int("sort") ?? existing?.SortOrder ?? 0,
            IsActive = row.Bool("active") ?? existing?.IsActive ?? true,
        }, user);
        return existing is null;
    }

    private async Task<bool> ImportMeetingHallAsync(RowValues row, List<LocationRef> locations, string? user)
    {
        var code = row.Text("code")!;
        var existing = await _db.MeetingHalls.AsNoTracking().FirstOrDefaultAsync(h => h.Code == code);

        await _masters.UpsertMeetingHallAsync(existing?.Id, new MeetingHallUpsertRequest
        {
            Code = code,
            Name = row.Text("name")!,
            SeatingCapacity = row.Int("capacity") ?? 0,
            IntercomNo = row.Text("intercom") ?? existing?.IntercomNo,
            LocationId = ResolveLocation(row.Text("location"), locations, required: true),
            IsActive = row.Bool("active") ?? existing?.IsActive ?? true,
        }, user);
        return existing is null;
    }

    private sealed record LocationRef(Guid Id, string Name, string? Code);

    private async Task<List<LocationRef>> LoadLocationsAsync() =>
        await _db.Locations.AsNoTracking()
            .Select(l => new LocationRef(l.Id, l.Name, l.Code))
            .ToListAsync();

    private static Guid? ResolveLocation(string? value, List<LocationRef> locations, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new InvalidOperationException("'Location' is required.");
            return null;
        }
        var match = locations.FirstOrDefault(l => string.Equals(l.Name, value, StringComparison.OrdinalIgnoreCase))
                    ?? locations.FirstOrDefault(l => string.Equals(l.Code, value, StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? throw new InvalidOperationException($"Location '{value}' was not found.");
    }

    private async Task<Guid> ResolveDepartmentAsync(string value, Guid? locationId)
    {
        var candidates = await _db.Departments.AsNoTracking()
            .Where(d => d.Name == value || d.Code == value)
            .Select(d => new { d.Id, d.LocationId, d.IsActive })
            .ToListAsync();
        if (candidates.Count == 0)
            throw new InvalidOperationException($"Department '{value}' was not found.");

        var best = candidates
            .OrderByDescending(d => locationId.HasValue && d.LocationId == locationId)
            .ThenByDescending(d => d.LocationId == null)
            .ThenByDescending(d => d.IsActive)
            .First();
        return best.Id;
    }

    private static (string Title, Column[] Columns) Definition(string kind) =>
        Definitions.TryGetValue(kind, out var def)
            ? def
            : throw new InvalidOperationException($"Import is not supported for '{kind}'.");

    private static Dictionary<string, int> MapHeaders(IXLWorksheet ws, Column[] columns)
    {
        var map = new Dictionary<string, int>();
        var lastCol = ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var c = 1; c <= lastCol; c++)
        {
            var header = Normalize(ws.Cell(1, c).GetString());
            if (header.Length == 0) continue;
            var col = columns.FirstOrDefault(x =>
                Normalize(x.Header) == header || x.Aliases.Any(a => Normalize(a) == header));
            if (col is not null && !map.ContainsKey(col.Key))
                map[col.Key] = c;
        }
        return map;
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed class RowValues
    {
        private readonly Dictionary<string, string> _values;
        public RowValues(Dictionary<string, string> values) => _values = values;

        public bool Has(string key) => !string.IsNullOrWhiteSpace(Text(key));

        public string? Text(string key) =>
            _values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        public int? Int(string key)
        {
            var v = Text(key);
            if (v is null) return null;
            return int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)
                ? n
                : throw new InvalidOperationException($"'{v}' is not a whole number.");
        }

        public bool? Bool(string key)
        {
            var v = Text(key)?.ToLowerInvariant();
            return v switch
            {
                null => null,
                "yes" or "y" or "true" or "1" or "active" => true,
                "no" or "n" or "false" or "0" or "inactive" => false,
                _ => throw new InvalidOperationException($"'{Text(key)}' must be Yes or No."),
            };
        }
    }
}
