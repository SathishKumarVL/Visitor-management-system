using System.Text.Json;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

/// <summary>Extra fields configured per purpose and the answers saved on a visit.</summary>
public static class PurposeFields
{
    public const int MaxFieldsPerPurpose = 5;
    public const int MaxLabelLength = 100;
    public const int MaxValueLength = 500;

    public static List<PurposeField> ReadDefinitions(string? json) => Read<PurposeField>(json);

    public static List<PurposeFieldValue> ReadValues(string? json) => Read<PurposeFieldValue>(json);

    public static string? Write<T>(IReadOnlyCollection<T> items) =>
        items.Count == 0 ? null : JsonSerializer.Serialize(items);

    public static bool IsLookup(PurposeField field) =>
        field.Type == PurposeFieldTypes.Lookup && field.LookupSourceId is Guid id && id != Guid.Empty;

    public static List<PurposeFieldDto> ToDtos(string? json) =>
        ReadDefinitions(json).Select(f => new PurposeFieldDto
        {
            Id = f.Id,
            Label = f.Label,
            IsRequired = f.IsRequired,
            Type = IsLookup(f) ? PurposeFieldTypes.Lookup : PurposeFieldTypes.Text,
            LookupSourceId = IsLookup(f) ? f.LookupSourceId : null,
        }).ToList();

    public static List<PurposeFieldValueDto> ToValueDtos(string? json) =>
        ReadValues(json).Select(v => new PurposeFieldValueDto
        {
            Purpose = v.Purpose,
            Label = v.Label,
            Value = v.Value,
            Verified = v.Verified,
            Source = v.Source,
            RecordId = v.RecordId,
            Details = v.Details?.Select(d => new LookupDetailDto { Label = d.Label, Value = d.Value }).ToList(),
            MatchAgainst = v.MatchAgainst,
            Matched = v.Matched,
            NewPerson = v.NewPerson,
        }).ToList();

    /// <summary>"Invoice number: 123; Student: Arjun K (not verified)" for reports.</summary>
    public static string Describe(string? json, string separator = "; ") =>
        string.Join(separator, ReadValues(json).Select(v => $"{v.Label}: {v.Value}{Qualifier(v)}"));

    private static string Qualifier(PurposeFieldValue v)
    {
        var notes = new List<string>();
        if (v.Verified == false) notes.Add("not verified");
        else if (v.Matched == false) notes.Add($"{v.MatchAgainst} not on record");
        if (v.NewPerson == true) notes.Add("first visit by this person");
        return notes.Count == 0 ? string.Empty : $" ({string.Join(", ", notes)})";
    }

    /// <summary>Validates the edited list; fields keep their id so a visit form open during the edit still matches.</summary>
    public static List<PurposeField> Normalize(IEnumerable<PurposeFieldDto>? input)
    {
        var result = new List<PurposeField>();
        foreach (var field in input ?? [])
        {
            var label = field.Label?.Trim() ?? string.Empty;
            if (label.Length == 0) continue;
            if (label.Length > MaxLabelLength)
                throw new InvalidOperationException($"Field name \"{label[..20]}…\" must be at most {MaxLabelLength} characters.");
            if (result.Any(f => string.Equals(f.Label, label, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Field \"{label}\" is listed twice.");
            var id = field.Id is Guid g && g != Guid.Empty && result.All(f => f.Id != g) ? g : Guid.NewGuid();
            var isLookup = string.Equals(field.Type?.Trim(), PurposeFieldTypes.Lookup, StringComparison.OrdinalIgnoreCase);
            if (isLookup && (field.LookupSourceId is not Guid sourceId || sourceId == Guid.Empty))
                throw new InvalidOperationException($"Choose which lookup source \"{label}\" searches.");
            result.Add(new PurposeField
            {
                Id = id,
                Label = label,
                IsRequired = field.IsRequired,
                Type = isLookup ? PurposeFieldTypes.Lookup : PurposeFieldTypes.Text,
                LookupSourceId = isLookup ? field.LookupSourceId : null,
            });
        }
        if (result.Count > MaxFieldsPerPurpose)
            throw new InvalidOperationException($"A purpose can have at most {MaxFieldsPerPurpose} extra fields.");
        return result;
    }

    /// <summary>
    /// Answers for the selected purposes only; a required field left blank stops the registration.
    /// A lookup answer with a token is trusted only as far as the token says; without one it is saved as typed, not verified.
    /// </summary>
    public static List<PurposeFieldValue> BuildValues(
        IEnumerable<VisitPurpose> purposes,
        IEnumerable<PurposeFieldValueRequest>? answers,
        Func<string, LookupSelection?>? readToken = null,
        string? visitorPhone = null,
        string? visitorEmail = null)
    {
        var byField = new Dictionary<Guid, PurposeFieldValueRequest>();
        foreach (var a in answers ?? [])
        {
            if (a.FieldId == Guid.Empty) continue;
            a.Value = a.Value?.Trim();
            a.LookupToken = string.IsNullOrWhiteSpace(a.LookupToken) ? null : a.LookupToken.Trim();
            if (!string.IsNullOrEmpty(a.Value) || a.LookupToken is not null) byField[a.FieldId] = a;
        }

        var result = new List<PurposeFieldValue>();
        foreach (var purpose in purposes.OrderBy(p => p.SortOrder).ThenBy(p => p.Name))
        {
            foreach (var field in ReadDefinitions(purpose.FieldsJson))
            {
                if (!byField.TryGetValue(field.Id, out var answer))
                {
                    if (field.IsRequired)
                        throw new InvalidOperationException($"{field.Label} is required for {purpose.Name}.");
                    continue;
                }

                var lookup = IsLookup(field);
                if (lookup && answer.LookupToken is not null)
                {
                    result.Add(FromSelection(purpose, field, answer.LookupToken, readToken, visitorPhone, visitorEmail));
                    continue;
                }

                var value = answer.Value ?? string.Empty;
                if (value.Length == 0)
                {
                    if (field.IsRequired)
                        throw new InvalidOperationException($"{field.Label} is required for {purpose.Name}.");
                    continue;
                }
                if (value.Length > MaxValueLength)
                    throw new InvalidOperationException($"{field.Label} must be at most {MaxValueLength} characters.");
                result.Add(new PurposeFieldValue
                {
                    FieldId = field.Id,
                    Purpose = purpose.Name,
                    Label = field.Label,
                    Value = value,
                    Verified = lookup ? false : null,
                });
            }
        }
        return result;
    }

    private static PurposeFieldValue FromSelection(
        VisitPurpose purpose, PurposeField field, string token,
        Func<string, LookupSelection?>? readToken, string? visitorPhone, string? visitorEmail)
    {
        var selection = readToken?.Invoke(token);
        if (selection is null || selection.SourceId != field.LookupSourceId)
            throw new InvalidOperationException($"The {field.Label} picked from the list has expired. Search and pick it again, or type it in.");

        bool? matched = null;
        if (selection.MatchAgainst is LookupMatchTargets.Mobile or LookupMatchTargets.Email)
        {
            var visitorValue = selection.MatchAgainst == LookupMatchTargets.Mobile ? visitorPhone : visitorEmail;
            matched = LookupMapping.Matches(selection.MatchValues, visitorValue, selection.MatchAgainst);
            if (matched == false && selection.BlockOnMismatch)
                throw new InvalidOperationException(
                    $"The visitor's {selection.MatchAgainst} is not on the record for {selection.Display}, so this visit cannot be registered.");
        }

        return new PurposeFieldValue
        {
            FieldId = field.Id,
            Purpose = purpose.Name,
            Label = field.Label,
            Value = LookupMapping.Truncate(selection.Display, MaxValueLength),
            Verified = true,
            Source = selection.SourceName,
            RecordId = selection.RecordId,
            Query = selection.Query,
            Details = selection.Details.Count == 0 ? null : selection.Details,
            MatchAgainst = matched.HasValue ? selection.MatchAgainst : null,
            Matched = matched,
        };
    }

    private static List<T> Read<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
