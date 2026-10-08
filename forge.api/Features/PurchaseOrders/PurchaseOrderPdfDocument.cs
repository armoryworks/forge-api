using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.PurchaseOrders;

public class PurchaseOrderPdfDocument : IDocument
{
    private static readonly CultureInfo Format = CultureInfo.InvariantCulture;

    private readonly PurchaseOrder _po;
    private readonly IReadOnlyDictionary<int, string> _vendorPartNumbers;
    private readonly string? _companyName;
    private readonly string? _companyPhone;
    private readonly string? _companyEmail;
    private readonly CompanyLocation? _shipTo;
    private readonly CompanyLocation? _companyLocation;

    public PurchaseOrderPdfDocument(
        PurchaseOrder po,
        IReadOnlyDictionary<int, string> vendorPartNumbers,
        string? companyName,
        string? companyPhone,
        string? companyEmail,
        CompanyLocation? shipTo,
        CompanyLocation? companyLocation = null)
    {
        _po = po;
        _vendorPartNumbers = vendorPartNumbers;
        _companyName = companyName;
        _companyPhone = companyPhone;
        _companyEmail = companyEmail;
        _shipTo = shipTo;
        _companyLocation = companyLocation ?? shipTo;
    }

    public bool IsDraft => _po.Status == PurchaseOrderStatus.Draft;

    public CompanyLocation? ShipTo => _shipTo;

    public IReadOnlyList<string> VendorAddressLines()
    {
        if (_po.VendorAddress is { } address)
            return NonBlank(address.Line1, address.Line2, CityLine(address.City, address.State, address.PostalCode), address.Country);

        var vendor = _po.Vendor;
        return NonBlank(vendor.Address, CityLine(vendor.City, vendor.State, vendor.ZipCode), vendor.Country);
    }

    public IReadOnlyList<string> VendorContactLines()
    {
        var vendor = _po.Vendor;
        var contact = _po.VendorContact;
        var name = contact is null ? vendor.ContactName : PurchaseOrderParties.ContactName(contact);
        return NonBlank(
            Labelled("Attn", name),
            Labelled("Phone", FirstNonBlank(contact?.Phone, vendor.Phone)),
            Labelled("Fax", FirstNonBlank(contact?.Fax, vendor.Fax)),
            Labelled("Email", FirstNonBlank(contact?.Email, vendor.Email)));
    }

    public static string UomFor(PurchaseOrderLine line) =>
        line.PurchaseUnit?.Label
        ?? line.Uom?.Code
        ?? line.Part?.StockUom?.Code
        ?? string.Empty;

    public string VendorPartNumberFor(PurchaseOrderLine line) =>
        line.PartId.HasValue && _vendorPartNumbers.TryGetValue(line.PartId.Value, out var number)
            ? number
            : string.Empty;

    public static string Money(decimal amount, string currency) =>
        $"{amount.ToString("#,##0.00", Format)} {currency}";

    public static string UnitPrice(decimal amount) => amount.ToString("#,##0.00##", Format);

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Purchase Order {_po.PONumber}",
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

            if (IsDraft)
            {
                page.Background().AlignCenter().AlignMiddle().Rotate(-35)
                    .Text("DRAFT").FontSize(140).Bold().FontColor(Colors.Grey.Lighten3);
            }
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
                    if (_companyLocation is not null)
                    {
                        left.Item().Text(_companyLocation.Line1).FontSize(9);
                        if (!string.IsNullOrWhiteSpace(_companyLocation.Line2))
                            left.Item().Text(_companyLocation.Line2).FontSize(9);
                        left.Item().Text(CityLine(_companyLocation.City, _companyLocation.State, _companyLocation.PostalCode)).FontSize(9);
                    }
                    var phone = string.IsNullOrWhiteSpace(_companyPhone) ? _companyLocation?.Phone : _companyPhone;
                    if (!string.IsNullOrWhiteSpace(phone))
                        left.Item().Text($"Phone: {phone}").FontSize(9);
                    if (!string.IsNullOrWhiteSpace(_companyEmail))
                        left.Item().Text($"Email: {_companyEmail}").FontSize(9);
                });

                row.ConstantItem(200).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text("PURCHASE ORDER").Bold().FontSize(20);
                    right.Item().AlignRight().Text($"PO #: {_po.PONumber}").Bold().FontSize(12);
                    var orderDate = _po.SubmittedDate ?? _po.CreatedAt;
                    right.Item().AlignRight().Text($"Date: {orderDate.ToString("MM/dd/yyyy", Format)}");
                    if (_po.ExpectedDeliveryDate.HasValue)
                        right.Item().AlignRight().Text(
                            $"Expected Delivery: {_po.ExpectedDeliveryDate.Value.ToString("MM/dd/yyyy", Format)}");
                    if (!string.IsNullOrWhiteSpace(_po.Vendor.VendorNumber))
                        right.Item().AlignRight().Text($"Vendor #: {_po.Vendor.VendorNumber}");
                    if (_po.IsBlanket)
                        right.Item().AlignRight().Text("Blanket PO").SemiBold();
                });
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Colors.Black);

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Element(ComposeVendorBlock);
                row.ConstantItem(20);
                row.RelativeItem().Element(ComposeShipToBlock);
            });

            col.Item().PaddingTop(10);
        });
    }

    private void ComposeVendorBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Black).Padding(6).Column(block =>
        {
            block.Item().Text("VENDOR").SemiBold().FontSize(8);
            block.Item().Text(_po.Vendor.CompanyName).Bold();
            foreach (var line in VendorAddressLines())
                block.Item().Text(line).FontSize(9);
            var contactLines = VendorContactLines();
            for (var i = 0; i < contactLines.Count; i++)
                block.Item().PaddingTop(i == 0 ? 3 : 0).Text(contactLines[i]).FontSize(9);
        });
    }

    private void ComposeShipToBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Black).Padding(6).Column(block =>
        {
            block.Item().Text("SHIP TO").SemiBold().FontSize(8);
            if (!string.IsNullOrWhiteSpace(_companyName))
                block.Item().Text(_companyName).Bold();
            if (_shipTo is null)
                return;
            if (!string.Equals(_shipTo.Name, _companyName, StringComparison.OrdinalIgnoreCase))
                block.Item().Text(_shipTo.Name).FontSize(9);
            block.Item().Text(_shipTo.Line1).FontSize(9);
            if (!string.IsNullOrWhiteSpace(_shipTo.Line2))
                block.Item().Text(_shipTo.Line2).FontSize(9);
            block.Item().Text(CityLine(_shipTo.City, _shipTo.State, _shipTo.PostalCode)).FontSize(9);
            if (!string.IsNullOrWhiteSpace(_shipTo.Country))
                block.Item().Text(_shipTo.Country).FontSize(9);
            if (!string.IsNullOrWhiteSpace(_shipTo.Phone))
                block.Item().Text($"Phone: {_shipTo.Phone}").FontSize(9);
        });
    }

    private void ComposeContent(IContainer container)
    {
        var currency = _po.QuoteCurrency;
        var lines = _po.Lines.OrderBy(l => l.Id).ToList();
        var subtotal = lines.Sum(l => l.OrderedQuantity * l.UnitPrice);
        var dueDate = _po.ExpectedDeliveryDate?.ToString("MM/dd/yyyy", Format) ?? string.Empty;

        container.Column(col =>
        {
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(18);
                    cols.ConstantColumn(62);
                    cols.ConstantColumn(62);
                    cols.RelativeColumn();
                    cols.ConstantColumn(40);
                    cols.ConstantColumn(38);
                    cols.ConstantColumn(50);
                    cols.ConstantColumn(56);
                    cols.ConstantColumn(66);
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "#");
                    HeaderCell(header.Cell(), "Part #");
                    HeaderCell(header.Cell(), "Vendor Part #");
                    HeaderCell(header.Cell(), "Description");
                    HeaderCell(header.Cell().AlignRight(), "Qty");
                    HeaderCell(header.Cell(), "UOM");
                    HeaderCell(header.Cell().AlignRight(), "Unit Price");
                    HeaderCell(header.Cell(), "Due");
                    HeaderCell(header.Cell().AlignRight(), "Total");
                });

                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var description = string.IsNullOrWhiteSpace(line.Description)
                        ? line.Part?.Description ?? string.Empty
                        : line.Description;

                    BodyCell(table.Cell()).Text((i + 1).ToString(Format));
                    BodyCell(table.Cell()).Text(line.Part?.PartNumber ?? string.Empty);
                    BodyCell(table.Cell()).Text(VendorPartNumberFor(line));
                    BodyCell(table.Cell()).Column(desc =>
                    {
                        desc.Item().Text(description);
                        if (!string.IsNullOrWhiteSpace(line.Notes))
                            desc.Item().Text(line.Notes).FontSize(7).Italic();
                    });
                    BodyCell(table.Cell()).AlignRight().Text(line.OrderedQuantity.ToString("#,##0.####", Format));
                    BodyCell(table.Cell()).Text(UomFor(line));
                    BodyCell(table.Cell()).AlignRight().Text(UnitPrice(line.UnitPrice));
                    BodyCell(table.Cell()).Text(dueDate);
                    BodyCell(table.Cell()).AlignRight().Text(Money(line.OrderedQuantity * line.UnitPrice, currency));
                }
            });

            col.Item().PaddingTop(8).AlignRight().Width(230).Column(totals =>
            {
                TotalRow(totals, "Subtotal:", Money(subtotal, currency), false);
                if (_po.EstimatedFreight.HasValue)
                    TotalRow(totals, "Estimated Freight:", Money(_po.EstimatedFreight.Value, currency), false);
                totals.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Black);
                TotalRow(totals, "Total:", Money(subtotal + (_po.EstimatedFreight ?? 0m), currency), true);
            });

            col.Item().PaddingTop(16).Column(terms =>
            {
                terms.Item().Text("Terms").SemiBold().FontSize(9);
                if (!string.IsNullOrWhiteSpace(_po.Vendor.PaymentTerms))
                    terms.Item().Text($"Payment terms: {_po.Vendor.PaymentTerms}").FontSize(9);
                terms.Item().Text($"Shipping terms: {_po.Incoterm.ToString().Replace('_', ' ')}").FontSize(9);
                terms.Item().Text($"Currency: {currency}").FontSize(9);
                if (_po.IsBlanket && _po.BlanketExpirationDate.HasValue)
                    terms.Item().Text(
                        $"Blanket order expires: {_po.BlanketExpirationDate.Value.ToString("MM/dd/yyyy", Format)}").FontSize(9);
                terms.Item().Text($"Please reference PO {_po.PONumber} on all packing slips and invoices.").FontSize(9);
            });

            if (!string.IsNullOrWhiteSpace(_po.Notes))
            {
                col.Item().PaddingTop(12).Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold().FontSize(9);
                    notes.Item().PaddingTop(2).Text(_po.Notes).FontSize(9);
                });
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(8));
            text.Span($"PO {_po.PONumber}  -  Page ");
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

    private static void TotalRow(ColumnDescriptor totals, string label, string value, bool emphasise)
    {
        totals.Item().PaddingTop(2).Row(row =>
        {
            var labelText = row.RelativeItem().Text(label);
            var valueText = row.ConstantItem(120).AlignRight().Text(value);
            if (emphasise)
            {
                labelText.Bold().FontSize(11);
                valueText.Bold().FontSize(11);
            }
            else
            {
                labelText.SemiBold();
            }
        });
    }

    private static IReadOnlyList<string> NonBlank(params string?[] values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? Labelled(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{label}: {value}";

    private static string CityLine(string? city, string? state, string? postalCode)
    {
        var cityState = string.Join(", ", new[] { city, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.Join(" ", new[] { cityState, postalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
