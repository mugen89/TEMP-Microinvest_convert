using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

public class VatCorrectionTests
{
    private static readonly DocumentProcessor Processor = new(ConverterOptions.Default);

    private static (OperationBatch Batch, OperationDocument Doc, VatGroup Group) Purchase(decimal @base, decimal vat)
    {
        var batch = new TestSheet(Layout.Purchases).Purchase(1d, "1", @base, vat).Process();
        return (batch, batch.Documents[0], Assert.Single(batch.Documents[0].VatGroups));
    }

    [Fact]
    public void Accepting_as_2_keeps_the_amounts()
    {
        var (batch, doc, group) = Purchase(150.55m, 30.01m);
        Assert.True(VatCorrections.SetDeal(group, 2));
        Processor.Process(batch, doc);
        Assert.Equal(("150.55;30.01", "2;2"), (doc.Amounts(), doc.Deals()));
        Assert.False(doc.HasWarnings);
    }

    [Theory]
    [InlineData(150.55, 30.01)]
    [InlineData(480.00, 90.00)]
    [InlineData(0, 150.00)]
    [InlineData(-100, -13)]
    public void Correcting_the_vat_keeps_the_gross_and_gives_a_proper_rate(double @base, double vat)
    {
        var (batch, doc, group) = Purchase((decimal)@base, (decimal)vat);
        decimal gross = doc.TotalGross;
        Assert.True(VatCorrections.CorrectVat(group));
        Processor.Process(batch, doc);
        Assert.Equal(gross, doc.TotalGross);
        Assert.Equal("2;2", doc.Deals());
        Assert.False(doc.Has(IssueCode.VatUnresolved));
    }

    [Fact]
    public void Nothing_can_be_corrected_in_a_zero_document()
    {
        var (_, _, group) = Purchase(0m, 0m);
        Assert.False(VatCorrections.CorrectVat(group));
        Assert.False(VatCorrections.Split(group));
    }

    [Fact]
    public void Splitting_gives_2_for_the_taxed_part_and_3_for_the_rest()
    {
        var (batch, doc, group) = Purchase(150.55m, 30.01m);
        Assert.True(VatCorrections.Split(group));
        Processor.Process(batch, doc);
        Assert.Equal(("150.05;30.01;0.5", "2;2;3"), (doc.Amounts(), doc.Deals()));
        Assert.Equal(180.56m, doc.TotalGross);
        Assert.False(doc.HasWarnings);
    }

    [Fact]
    public void Too_much_vat_cannot_be_split()
    {
        var (_, _, group) = Purchase(100m, 30m);
        Assert.False(VatCorrections.Split(group));
    }

    [Fact]
    public void A_sale_can_be_accepted_as_it_is()
    {
        var batch = new TestSheet(Layout.Sales).Sale(1d, "1", 100, 15, ("AB", 100d), ("AC", 15d)).Process();
        var doc = batch.Documents[0];
        Assert.True(doc.HasWarnings);
        Assert.True(VatCorrections.AcceptAsIs(doc.VatGroups[0]));
        Processor.Process(batch, doc);
        Assert.False(doc.HasWarnings);
        Assert.Equal(("100;15", "7;7"), (doc.Amounts(), doc.Deals()));
    }

    [Fact]
    public void A_sale_can_be_corrected()
    {
        var batch = new TestSheet(Layout.Sales).Sale(1d, "1", 100, 15, ("AB", 100d), ("AC", 15d)).Process();
        var doc = batch.Documents[0];
        Assert.True(VatCorrections.CorrectVat(doc.VatGroups[0]));
        Processor.Process(batch, doc);
        Assert.Equal("95.83;19.17", doc.Amounts());
        Assert.False(doc.HasWarnings);
    }

    [Fact]
    public void Only_the_lines_are_put_back_by_RevertLines()
    {
        var (batch, doc, group) = Purchase(480.00m, 90.00m);
        doc.TrySet(DocField.Reason, "ново основание", out _);
        VatCorrections.Split(group);
        doc.RevertLines();
        Processor.Process(batch, doc);
        Assert.Equal(("480;90", "ново основание"), (doc.Amounts(), doc.Reason));
    }
}
