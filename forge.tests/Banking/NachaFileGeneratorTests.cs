using FluentAssertions;

using Forge.Api.Features.Banking;

namespace Forge.Tests.Banking;

/// <summary>
/// BANK-002 Phase A — pure-format proofs for the NACHA writer: fixed 94-char records, 10-record
/// blocking with '9' filler, record-type sequence (1→5→6…→8→9), entry hash (rightmost 10 of the
/// summed 8-digit receiving DFIs), credit totals in cents, prenote transaction codes, and the
/// ABA routing checksum used at account entry.
/// </summary>
public class NachaFileGeneratorTests
{
    // 061000104 (a real-format test routing: 3(0+0+1)+7(6+0+0)+1(1+0+4) = 50 → valid).
    private const string ValidRouting = "061000104";
    private const string ValidRouting2 = "071000301"; // alternate valid (3(0+0+3)+7(7+0+0)+(1+0+1)=60)

    private static NachaOrigination Origination() => new(
        ImmediateDestination: ValidRouting2,
        ImmediateDestinationName: "FRONTIER CU",
        ImmediateOrigin: "1234567890",
        ImmediateOriginName: "ARMORY PLASTICS LLC",
        CompanyName: "ARMORY PLASTICS",
        CompanyId: "1123456789",
        OriginatingDfi: "07100030",
        EntryClassCode: "CCD");

    private static NachaEntry Entry(decimal amount = 1234.56m, string routing = ValidRouting, bool savings = false)
        => new(routing, "000123456789", savings, amount, "ACH-PAY-1", "PACIFIC TOOL SUPPLY");

    private static readonly DateOnly EffectiveDate = new(2026, 6, 15);
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 12, 14, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("061000104", true)]
    [InlineData("071000301", true)]
    [InlineData("061000105", false)] // checksum off by one
    [InlineData("12345678", false)]  // 8 digits
    [InlineData("abcdefghi", false)]
    [InlineData(null, false)]
    public void RoutingChecksum(string? routing, bool expected)
        => NachaFileGenerator.IsValidRoutingNumber(routing).Should().Be(expected);

    [Fact]
    public void Generate_EveryRecordIs94Chars_AndBlockedToTen()
    {
        var file = NachaFileGenerator.Generate(
            Origination(), [Entry(), Entry(200m, ValidRouting2)], EffectiveDate, CreatedAt, isPrenote: false, batchNumber: 7);

        var lines = file.TrimEnd('\n').Split('\n');
        lines.Should().AllSatisfy(l => l.Length.Should().Be(94));
        (lines.Length % 10).Should().Be(0);
        // 2 entries + 4 control records = 6 → padded to 10 with all-9 filler.
        lines.Length.Should().Be(10);
        lines.Skip(6).Should().AllSatisfy(l => l.Should().Be(new string('9', 94)));
    }

    [Fact]
    public void Generate_RecordTypeSequence()
    {
        var file = NachaFileGenerator.Generate(
            Origination(), [Entry()], EffectiveDate, CreatedAt, isPrenote: false, batchNumber: 1);

        var lines = file.TrimEnd('\n').Split('\n');
        lines[0][0].Should().Be('1'); // file header
        lines[1][0].Should().Be('5'); // batch header
        lines[2][0].Should().Be('6'); // entry detail
        lines[3][0].Should().Be('8'); // batch control
        lines[4][0].Should().Be('9'); // file control
    }

    [Fact]
    public void Generate_EntryDetail_CarriesAmountInCents_AndCheckingCreditCode()
    {
        var file = NachaFileGenerator.Generate(
            Origination(), [Entry(1234.56m)], EffectiveDate, CreatedAt, isPrenote: false, batchNumber: 1);

        var entry = file.Split('\n')[2];
        entry.Substring(1, 2).Should().Be("22");                  // checking credit
        entry.Substring(3, 9).Should().Be(ValidRouting);          // receiving DFI + check digit
        entry.Substring(29, 10).Should().Be("0000123456");        // $1,234.56 → 123456 cents
        entry[^15..].Should().Be("071000300000001");              // trace = ODFI(8) + seq(7)
    }

    [Fact]
    public void Generate_Prenote_ZeroAmount_PrenoteCodes()
    {
        var file = NachaFileGenerator.Generate(
            Origination(),
            [Entry(500m), Entry(500m, ValidRouting, savings: true)],
            EffectiveDate, CreatedAt, isPrenote: true, batchNumber: 2);

        var lines = file.Split('\n');
        lines[2].Substring(1, 2).Should().Be("23");               // checking prenote
        lines[2].Substring(29, 10).Should().Be("0000000000");     // zero regardless of Amount
        lines[3].Substring(1, 2).Should().Be("33");               // savings prenote
        // Batch header description says PRENOTE.
        lines[1].Should().Contain("PRENOTE");
    }

    [Fact]
    public void Generate_BatchAndFileControls_TotalsAndHash()
    {
        var file = NachaFileGenerator.Generate(
            Origination(), [Entry(100m), Entry(50.25m, ValidRouting2)],
            EffectiveDate, CreatedAt, isPrenote: false, batchNumber: 3);

        // Two entries → lines: 0 file header, 1 batch header, 2–3 entries, 4 batch control, 5 file control.
        var lines = file.Split('\n');
        var batchControl = lines[4];
        var fileControl = lines[5];

        // Entry hash = sum of 8-digit DFIs: 06100010 + 07100030 = 13200040.
        batchControl.Substring(10, 10).Should().Be("0013200040");
        // Credits: 10000 + 5025 = 15025 cents; debits zero.
        batchControl.Substring(32, 12).Should().Be("000000015025");
        batchControl.Substring(20, 12).Should().Be("000000000000");

        fileControl.Substring(1, 6).Should().Be("000001");        // 1 batch
        fileControl.Substring(7, 6).Should().Be("000001");        // 6 records → 1 block
        fileControl.Substring(13, 8).Should().Be("00000002");     // 2 entries
        fileControl.Substring(21, 10).Should().Be("0013200040");
        fileControl.Substring(43, 12).Should().Be("000000015025");
    }

    [Fact]
    public void Generate_FileHeader_CarriesOriginationIdentity()
    {
        var file = NachaFileGenerator.Generate(
            Origination(), [Entry()], EffectiveDate, CreatedAt, isPrenote: false, batchNumber: 1);

        var header = file.Split('\n')[0];
        header.Substring(3, 10).Should().Be(" 071000301");        // " " + immediate destination
        header.Substring(13, 10).Should().Be("1234567890");       // immediate origin
        header.Substring(23, 6).Should().Be("260612");            // file creation date
        header.Substring(33, 1).Should().Be("A");                 // file ID modifier
        header.Substring(34, 3).Should().Be("094");
        header.Substring(37, 2).Should().Be("10");
    }

    [Fact]
    public void Generate_Balanced_AppendsOffsetDebit_ServiceClass200()
    {
        var offset = new NachaOffset(ValidRouting2, "00998877", "ARMORY PLASTICS");
        var file = NachaFileGenerator.Generate(
            Origination(), [Entry(100m), Entry(50.25m)], EffectiveDate, CreatedAt,
            isPrenote: false, batchNumber: 4, offset: offset);

        var lines = file.Split('\n');
        lines[1].Substring(1, 3).Should().Be("200");              // mixed service class
        // Offset entry: code 27 debit for the credit total (15025 cents) against our account.
        var offsetLine = lines[4];
        offsetLine.Substring(0, 3).Should().Be("627");
        offsetLine.Substring(3, 9).Should().Be(ValidRouting2);
        offsetLine.Substring(29, 10).Should().Be("0000015025");
        // Batch control: 3 entries, debits == credits (the file nets to zero).
        var batchControl = lines[5];
        batchControl.Substring(1, 3).Should().Be("200");
        batchControl.Substring(4, 6).Should().Be("000003");
        batchControl.Substring(20, 12).Should().Be("000000015025");
        batchControl.Substring(32, 12).Should().Be("000000015025");
    }

    [Fact]
    public void Generate_Prenote_IgnoresOffset_StaysCreditsOnly()
    {
        var offset = new NachaOffset(ValidRouting2, "00998877", "ARMORY PLASTICS");
        var file = NachaFileGenerator.Generate(
            Origination(), [Entry(500m)], EffectiveDate, CreatedAt,
            isPrenote: true, batchNumber: 5, offset: offset);

        var lines = file.Split('\n');
        lines[1].Substring(1, 3).Should().Be("220");
        lines.Count(l => l.StartsWith("627")).Should().Be(0);     // no offset on a zero-dollar batch
    }

    [Fact]
    public void Generate_NoEntries_Throws()
    {
        var act = () => NachaFileGenerator.Generate(
            Origination(), [], EffectiveDate, CreatedAt, isPrenote: false, batchNumber: 1);
        act.Should().Throw<InvalidOperationException>();
    }
}
