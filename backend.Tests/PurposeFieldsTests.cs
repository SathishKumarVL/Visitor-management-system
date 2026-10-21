using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

public class PurposeFieldsTests
{
    private static VisitPurpose Purpose(string name, params PurposeField[] fields) => new()
    {
        Name = name,
        FieldsJson = PurposeFields.Write(fields),
    };

    [Fact]
    public void Normalize_trims_drops_blank_rows_and_keeps_existing_ids()
    {
        var existing = Guid.NewGuid();
        var result = PurposeFields.Normalize(
        [
            new PurposeFieldDto { Id = existing, Label = "  Invoice number ", IsRequired = true },
            new PurposeFieldDto { Label = "   " },
            new PurposeFieldDto { Label = "Vehicle number" },
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(existing, result[0].Id);
        Assert.Equal("Invoice number", result[0].Label);
        Assert.True(result[0].IsRequired);
        Assert.NotEqual(Guid.Empty, result[1].Id);
        Assert.False(result[1].IsRequired);
    }

    [Fact]
    public void Normalize_rejects_duplicate_labels_and_too_many_fields()
    {
        Assert.Throws<InvalidOperationException>(() => PurposeFields.Normalize(
        [
            new PurposeFieldDto { Label = "Invoice" },
            new PurposeFieldDto { Label = "invoice" },
        ]));

        var tooMany = Enumerable.Range(1, PurposeFields.MaxFieldsPerPurpose + 1)
            .Select(i => new PurposeFieldDto { Label = $"Field {i}" });
        Assert.Throws<InvalidOperationException>(() => PurposeFields.Normalize(tooMany));
    }

    [Fact]
    public void BuildValues_requires_required_fields_of_selected_purposes_only()
    {
        var invoice = new PurposeField { Id = Guid.NewGuid(), Label = "Invoice number", IsRequired = true };
        var delivery = Purpose("Delivery", invoice);

        var ex = Assert.Throws<InvalidOperationException>(() => PurposeFields.BuildValues([delivery], []));
        Assert.Contains("Invoice number", ex.Message);
        Assert.Contains("Delivery", ex.Message);

        Assert.Throws<InvalidOperationException>(() => PurposeFields.BuildValues(
            [delivery],
            [new PurposeFieldValueRequest { FieldId = invoice.Id, Value = "   " }]));

        Assert.Empty(PurposeFields.BuildValues([Purpose("Meeting")], []));
    }

    [Fact]
    public void BuildValues_snapshots_purpose_and_label_and_ignores_unknown_fields()
    {
        var invoice = new PurposeField { Id = Guid.NewGuid(), Label = "Invoice number", IsRequired = true };
        var vehicle = new PurposeField { Id = Guid.NewGuid(), Label = "Vehicle number" };
        var delivery = Purpose("Delivery", invoice, vehicle);

        var values = PurposeFields.BuildValues(
            [delivery],
            [
                new PurposeFieldValueRequest { FieldId = invoice.Id, Value = " INV-42 " },
                new PurposeFieldValueRequest { FieldId = Guid.NewGuid(), Value = "from another purpose" },
            ]);

        var saved = Assert.Single(values);
        Assert.Equal("Delivery", saved.Purpose);
        Assert.Equal("Invoice number", saved.Label);
        Assert.Equal("INV-42", saved.Value);

        var json = PurposeFields.Write(values);
        Assert.Equal("Invoice number: INV-42", PurposeFields.Describe(json));
        Assert.Equal("Delivery", Assert.Single(PurposeFields.ToValueDtos(json)).Purpose);
    }

    [Fact]
    public void BuildValues_rejects_overlong_answers()
    {
        var note = new PurposeField { Id = Guid.NewGuid(), Label = "Note" };
        Assert.Throws<InvalidOperationException>(() => PurposeFields.BuildValues(
            [Purpose("Delivery", note)],
            [new PurposeFieldValueRequest { FieldId = note.Id, Value = new string('x', PurposeFields.MaxValueLength + 1) }]));
    }

    [Fact]
    public void Reading_corrupt_json_yields_no_fields()
    {
        Assert.Empty(PurposeFields.ReadDefinitions("not json"));
        Assert.Empty(PurposeFields.ReadValues(null));
        Assert.Equal("", PurposeFields.Describe("{"));
    }
}
