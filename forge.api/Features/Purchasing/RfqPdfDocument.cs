using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;

namespace Forge.Api.Features.Purchasing;

public class RfqPdfDocument : IDocument
{
    private static readonly CultureInfo Format = CultureInfo.InvariantCulture;

    private readonly RequestForQuote _rfq;
    private readonly Vendor? _vendor;
    private readonly string? _companyName;
    private readonly string? _companyPhone;
    private readonly string? _companyEmail;
    private readonly CompanyLocation? _location;

    public RfqPdfDocument(
        RequestForQuote rfq,
        Vendor? vendor,
        string? companyName,
        string? companyPhone,
        string? companyEmail,
        CompanyLocation? location)
    {
        _rfq = rfq;
        _vendor = vendor;
        _companyName = companyName;
        _companyPhone = companyPhone;
        _companyEmail = companyEmail;
        _location = location;
    }

    public Vendor? AddressedTo => _vendor;

    public string LineDescription =>
        string.IsNullOrWhiteSpace(_rfq.Part.Description) ? _rfq.Part.Name : _rfq.Part.Description;

    public string Uom => _rfq.Part.StockUom?.Code ?? string.Empty;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Request for Quote {_rfq.RfqNumber}",
        Author = _companyName ?? string.Empty,
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(40);
            page.MarginVertical(36);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (!string.IsNullOrWhiteSpace(_companyName))
                        left.Item().Text(_companyName).Bold().FontSize(16);
                    if (_location is not null)
                    {
                        left.Item().Text(_location.Line1).FontSize(9);
                        if (!string.IsNullOrWhiteSpace(_location.Line2))
                            left.Item().Text(_location.Line2).FontSize(9);
                        left.Item().Text(CityLine(_location.City, _location.State, _location.PostalCode)).FontSize(9);
                    }
                    var phone = string.IsNullOrWhiteSpace(_companyPhone) ? _location?.Phone : _companyPhone;
                    if (!string.IsNullOrWhiteSpace(phone))
                        left.Item().Text($"Phone: {phone}").FontSize(9);
                    if (!string.IsNullOrWhiteSpace(_companyEmail))
                        left.Item().Text($"Email: {_companyEmail}").FontSize(9);
                });

                row.ConstantItem(220).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text("REQUEST FOR QUOTE").Bold().FontSize(18);
                    right.Item().AlignRight().Text($"RFQ #: {_rfq.RfqNumber}").Bold().FontSize(12);
                    var issued = _rfq.SentAt ?? _rfq.CreatedAt;
                    right.Item().AlignRight().Text($"Date: {issued.ToString("MM/dd/yyyy", Format)}");
                    if (_rfq.ResponseDeadline.HasValue)
                        right.Item().AlignRight().Text(
                            $"Response Due: {_rfq.ResponseDeadline.Value.ToString("MM/dd/yyyy", Format)}").SemiBold();
                    right.Item().AlignRight().Text($"Required By: {_rfq.RequiredDate.ToString("MM/dd/yyyy", Format)}");
                    if (!string.IsNullOrWhiteSpace(_vendor?.VendorNumber))
                        right.Item().AlignRight().Text($"Vendor #: {_vendor.VendorNumber}");
                });
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Colors.Black);

            if (_vendor is not null)
                col.Item().PaddingTop(8).Width(260).Element(ComposeVendorBlock);

            col.Item().PaddingTop(10);
        });
    }

    private void ComposeVendorBlock(IContainer container)
    {
        var vendor = _vendor!;
        container.Border(0.75f).BorderColor(Colors.Black).Padding(6).Column(block =>
        {
            block.Item().Text("TO").SemiBold().FontSize(8);
            block.Item().Text(vendor.CompanyName).Bold();
            if (!string.IsNullOrWhiteSpace(vendor.Address))
                block.Item().Text(vendor.Address).FontSize(9);
            var cityLine = CityLine(vendor.City, vendor.State, vendor.ZipCode);
            if (!string.IsNullOrWhiteSpace(cityLine))
                block.Item().Text(cityLine).FontSize(9);
            if (!string.IsNullOrWhiteSpace(vendor.Country))
                block.Item().Text(vendor.Country).FontSize(9);
            if (!string.IsNullOrWhiteSpace(vendor.ContactName))
                block.Item().PaddingTop(3).Text($"Attn: {vendor.ContactName}").FontSize(9);
            if (!string.IsNullOrWhiteSpace(vendor.Phone))
                block.Item().Text($"Phone: {vendor.Phone}").FontSize(9);
            if (!string.IsNullOrWhiteSpace(vendor.Email))
                block.Item().Text($"Email: {vendor.Email}").FontSize(9);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(18);
                    cols.ConstantColumn(90);
                    cols.RelativeColumn();
                    cols.ConstantColumn(60);
                    cols.ConstantColumn(44);
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "#");
                    HeaderCell(header.Cell(), "Part #");
                    HeaderCell(header.Cell(), "Description");
                    HeaderCell(header.Cell().AlignRight(), "Qty");
                    HeaderCell(header.Cell(), "UOM");
                });

                BodyCell(table.Cell()).Text("1");
                BodyCell(table.Cell()).Text(_rfq.Part.PartNumber);
                BodyCell(table.Cell()).Text(LineDescription);
                BodyCell(table.Cell()).AlignRight().Text(_rfq.Quantity.ToString("#,##0.####", Format));
                BodyCell(table.Cell()).Text(Uom);
            });

            if (!string.IsNullOrWhiteSpace(_rfq.Description))
                Section(col, "Description", _rfq.Description);

            if (!string.IsNullOrWhiteSpace(_rfq.SpecialInstructions))
                Section(col, "Special Instructions", _rfq.SpecialInstructions);

            col.Item().PaddingTop(16).Column(terms =>
            {
                terms.Item().Text("Response").SemiBold().FontSize(9);
                terms.Item().Text("Please quote unit price, lead time, minimum order quantity and any tooling cost.").FontSize(9);
                if (_rfq.ResponseDeadline.HasValue)
                    terms.Item().Text(
                        $"Responses are due by {_rfq.ResponseDeadline.Value.ToString("MM/dd/yyyy", Format)}.").FontSize(9);
                terms.Item().Text($"Please reference RFQ {_rfq.RfqNumber} on your quotation.").FontSize(9);
            });
        });
    }

    private static void Section(ColumnDescriptor col, string title, string body)
    {
        col.Item().PaddingTop(12).Column(section =>
        {
            section.Item().Text(title).SemiBold().FontSize(9);
            section.Item().PaddingTop(2).Text(body).FontSize(9);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(8));
            text.Span($"RFQ {_rfq.RfqNumber}  -  Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
    }

    private static void HeaderCell(IContainer cell, string label) =>
        cell.BorderBottom(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3)
            .PaddingVertical(4).PaddingHorizontal(3)
            .Text(label).SemiBold().FontSize(8);

    private static IContainer BodyCell(IContainer cell) =>
        cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(4).PaddingHorizontal(3)
            .DefaultTextStyle(x => x.FontSize(8));

    private static string CityLine(string? city, string? state, string? postalCode)
    {
        var cityState = string.Join(", ", new[] { city, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.Join(" ", new[] { cityState, postalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
