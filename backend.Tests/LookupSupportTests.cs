using System.Net;
using System.Text.Json;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

public class LookupSupportTests
{
    private const string Secret = "unit-test-lookup-secret-0123456789";

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Paths_Walk_Objects_And_Flatten_Arrays()
    {
        var root = Json("""
            { "data": { "students": [
                { "admissionNo": 101, "name": "Arjun", "guardians": [ { "mobile": "+91 98765 43210" }, { "mobile": "9123456789" } ] },
                { "admissionNo": "102", "Name": "Meera", "guardians": [] }
            ] } }
            """);

        var records = LookupMapping.ExtractRecords(root, "data.students");
        Assert.Equal(2, records.Count);
        Assert.Equal("101", LookupMapping.FirstText(records[0], "admissionNo"));
        Assert.Equal("Meera", LookupMapping.FirstText(records[1], "name"));
        Assert.Equal(["+91 98765 43210", "9123456789"], LookupMapping.Texts(records[0], "guardians.mobile"));
        Assert.Equal(["9123456789"], LookupMapping.Texts(records[0], "guardians.1.mobile"));
        Assert.Empty(LookupMapping.Texts(records[1], "guardians.mobile"));

        Assert.Single(LookupMapping.ExtractRecords(Json("""{ "id": 1 }"""), null));
        Assert.Equal(2, LookupMapping.ExtractRecords(Json("""[ { "id": 1 }, { "id": 2 }, 3 ]"""), "").Count);
    }

    [Fact]
    public void Template_Fills_Placeholders_And_Tidies_Spaces()
    {
        var record = Json("""{ "name": "Arjun K", "class": 7, "section": "B", "tags": ["a", "b"] }""");
        Assert.Equal("Arjun K - Class 7-B", LookupMapping.Render("{name} -  Class {class}-{section}", record));
        Assert.Equal("Arjun K a, b", LookupMapping.Render("{name} {missing} {tags}", record));
    }

    [Fact]
    public void Mobile_Match_Ignores_Country_Code_And_Formatting()
    {
        Assert.True(LookupMapping.Matches(["+91 98765 43210"], "9876543210", LookupMatchTargets.Mobile));
        Assert.True(LookupMapping.Matches(["9000000001, 9876543210"], "98765-43210", LookupMatchTargets.Mobile));
        Assert.False(LookupMapping.Matches(["9876543210"], "9876543211", LookupMatchTargets.Mobile));
        Assert.False(LookupMapping.Matches(["9876543210"], null, LookupMatchTargets.Mobile));
        Assert.True(LookupMapping.Matches(["Parent@School.org"], " parent@school.org ", LookupMatchTargets.Email));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    public void Private_And_Reserved_Addresses_Are_Blocked(string ip) =>
        Assert.True(OutboundUrlGuard.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.114.10")]
    [InlineData("2606:4700:4700::1111")]
    public void Public_Addresses_Are_Allowed(string ip) =>
        Assert.False(OutboundUrlGuard.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("https://school.example.com/students", "{query}")]
    [InlineData("http://school.example.com/students?q={query}", "https://")]
    [InlineData("https://localhost/students?q={query}", "private network")]
    [InlineData("https://127.0.0.1/students?q={query}", "private network")]
    [InlineData("https://intranet/students?q={query}", "private network")]
    [InlineData("https://user:pass@school.example.com/?q={query}", "credentials")]
    public void Unsafe_Search_Urls_Are_Rejected(string url, string reason)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OutboundUrlGuard.BuildSearchUri(url, "x", allowHttp: false, allowPrivate: false));
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Search_Text_Is_Url_Encoded()
    {
        var uri = OutboundUrlGuard.BuildSearchUri("https://school.example.com/s?q={QUERY}&limit=5", "a&b=c d", false, false);
        Assert.Equal("https://school.example.com/s?q=a%26b%3Dc%20d&limit=5", uri.AbsoluteUri);
    }

    [Fact]
    public void Body_Template_Fills_Typed_Text_And_Other_Fields_As_Safe_Json()
    {
        const string template = """{ "class": "{field:Class}", "section": "{field: section }", "fullName": "{query}" }""";
        Assert.Equal(["Class", "section"], LookupTemplates.FieldNames(template, "https://x.example.com/?q={query}"));

        var context = new Dictionary<string, string> { ["class"] = "1st", ["Section"] = "A" };
        var body = LookupTemplates.Fill(template, "RA\"K\\ஷன்", context, LookupTemplates.JsonString);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("1st", doc.RootElement.GetProperty("class").GetString());
        Assert.Equal("A", doc.RootElement.GetProperty("section").GetString());
        Assert.Equal("RA\"K\\ஷன்", doc.RootElement.GetProperty("fullName").GetString());
        Assert.Contains("ஷன்", body);

        var missing = LookupTemplates.Fill(template, "x", null, LookupTemplates.JsonString);
        Assert.Equal("", JsonDocument.Parse(missing).RootElement.GetProperty("class").GetString());
    }

    [Fact]
    public void Token_Round_Trips_And_Rejects_Tampering_Other_Keys_And_Expiry()
    {
        var selection = Selection(Guid.NewGuid());
        var token = LookupTokens.Protect(selection, Secret);
        Assert.DoesNotContain("9876543210", token);

        var back = LookupTokens.Unprotect(token, Secret, DateTimeOffset.UtcNow);
        Assert.NotNull(back);
        Assert.Equal("Arjun K - 7B", back!.Display);
        Assert.Equal(["9876543210"], back.MatchValues);

        var tampered = token[..^3] + (token[^3] == 'A' ? 'B' : 'A') + token[^2..];
        Assert.Null(LookupTokens.Unprotect(tampered, Secret, DateTimeOffset.UtcNow));
        Assert.Null(LookupTokens.Unprotect(token, "another-secret-value-0123456789", DateTimeOffset.UtcNow));
        Assert.Null(LookupTokens.Unprotect(token, Secret, DateTimeOffset.UtcNow.AddHours(3)));
        Assert.Null(LookupTokens.Unprotect("not-a-token", Secret, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Lookup_Field_Saves_Verified_Pick_Or_Unverified_Typed_Value()
    {
        var sourceId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var purpose = PurposeWithLookup(sourceId, fieldId, required: true);
        var token = LookupTokens.Protect(Selection(sourceId), Secret);
        LookupSelection? Read(string t) => LookupTokens.Unprotect(t, Secret, DateTimeOffset.UtcNow);

        var picked = Assert.Single(PurposeFields.BuildValues([purpose],
            [new PurposeFieldValueRequest { FieldId = fieldId, Value = "ignored", LookupToken = token }], Read, "9876543210", null));
        Assert.True(picked.Verified);
        Assert.Equal("Arjun K - 7B", picked.Value);
        Assert.Equal("ADM-1", picked.RecordId);
        Assert.Equal("School", picked.Source);
        Assert.True(picked.Matched);
        Assert.Equal("Class", Assert.Single(picked.Details!).Label);

        var notOnList = Assert.Single(PurposeFields.BuildValues([purpose],
            [new PurposeFieldValueRequest { FieldId = fieldId, LookupToken = token }], Read, "9000000000", null));
        Assert.False(notOnList.Matched);
        Assert.Contains("(mobile not on record)", PurposeFields.Describe(PurposeFields.Write([notOnList])));

        var typed = Assert.Single(PurposeFields.BuildValues([purpose],
            [new PurposeFieldValueRequest { FieldId = fieldId, Value = "Arjun" }], Read));
        Assert.False(typed.Verified);
        Assert.Null(typed.Matched);
        Assert.Equal("Student: Arjun (not verified)", PurposeFields.Describe(PurposeFields.Write([typed])));

        var otherSource = LookupTokens.Protect(Selection(Guid.NewGuid()), Secret);
        Assert.Throws<InvalidOperationException>(() => PurposeFields.BuildValues([purpose],
            [new PurposeFieldValueRequest { FieldId = fieldId, LookupToken = otherSource }], Read));

        var required = Assert.Throws<InvalidOperationException>(() => PurposeFields.BuildValues([purpose], [], Read));
        Assert.Contains("Student is required", required.Message);
    }

    [Fact]
    public void Blocking_Source_Stops_A_Visitor_Not_On_The_Record()
    {
        var sourceId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var selection = Selection(sourceId);
        selection.BlockOnMismatch = true;
        var token = LookupTokens.Protect(selection, Secret);
        LookupSelection? Read(string t) => LookupTokens.Unprotect(t, Secret, DateTimeOffset.UtcNow);
        var purpose = PurposeWithLookup(sourceId, fieldId, required: false);

        var ex = Assert.Throws<InvalidOperationException>(() => PurposeFields.BuildValues([purpose],
            [new PurposeFieldValueRequest { FieldId = fieldId, LookupToken = token }], Read, "9000000000", null));
        Assert.Contains("not on the record", ex.Message);
        Assert.Single(PurposeFields.BuildValues([purpose],
            [new PurposeFieldValueRequest { FieldId = fieldId, LookupToken = token }], Read, "+91 98765 43210", null));
    }

    [Fact]
    public void Lookup_Field_Needs_A_Source()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PurposeFields.Normalize([new PurposeFieldDto { Label = "Student", Type = "lookup" }]));
        Assert.Contains("lookup source", ex.Message);

        var sourceId = Guid.NewGuid();
        var field = Assert.Single(PurposeFields.Normalize([new PurposeFieldDto { Label = "Student", Type = "LOOKUP", LookupSourceId = sourceId }]));
        Assert.Equal(PurposeFieldTypes.Lookup, field.Type);
        Assert.Equal(sourceId, field.LookupSourceId);

        var text = Assert.Single(PurposeFields.Normalize([new PurposeFieldDto { Label = "Note", LookupSourceId = sourceId }]));
        Assert.Equal(PurposeFieldTypes.Text, text.Type);
        Assert.Null(text.LookupSourceId);
    }

    private static LookupSelection Selection(Guid sourceId) => new()
    {
        SourceId = sourceId,
        TenantId = Guid.NewGuid(),
        SourceName = "School",
        RecordId = "ADM-1",
        Display = "Arjun K - 7B",
        Details = [new PurposeFieldDetail { Label = "Class", Value = "7B" }],
        MatchValues = ["9876543210"],
        MatchAgainst = LookupMatchTargets.Mobile,
        ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
    };

    private static VisitPurpose PurposeWithLookup(Guid sourceId, Guid fieldId, bool required) => new()
    {
        Name = "Student visit",
        FieldsJson = PurposeFields.Write(new List<PurposeField>
        {
            new() { Id = fieldId, Label = "Student", IsRequired = required, Type = PurposeFieldTypes.Lookup, LookupSourceId = sourceId },
        }),
    };
}
