using indice.Edi.Serialization;

namespace Forge.Api.Features.Edi.X12;

/// <summary>
/// ⚡ EDI BOUNDARY — EDI.Net deserialization model for an inbound X12 850 (customer purchase
/// order) interchange. Deliberately trimmed to what Forge consumes (EDI_CORE_PLAN §3): the
/// interchange/group identities (partner validation + 997 addressing), BEG (the customer's PO
/// number/date), N1 ship-to loops, and PO1/PID line items. Unmapped segments are skipped by
/// the serializer by design — partners send far more than we read.
///
/// The attribute shapes mirror EDI.Net's own X12 850 test model (the library's reference
/// usage), so parsing behavior is the upstream-proven path.
/// </summary>
public class Edi850Interchange
{
    [EdiValue("X(2)", Path = "ISA/4", Description = "ISA05 - Sender ID qualifier")]
    public string? SenderQualifier { get; set; }

    [EdiValue("X(15)", Path = "ISA/5", Description = "ISA06 - Interchange sender ID")]
    public string? SenderId { get; set; }

    [EdiValue("X(2)", Path = "ISA/6", Description = "ISA07 - Receiver ID qualifier")]
    public string? ReceiverQualifier { get; set; }

    [EdiValue("X(15)", Path = "ISA/7", Description = "ISA08 - Interchange receiver ID")]
    public string? ReceiverId { get; set; }

    [EdiValue("9(9)", Path = "ISA/12", Description = "ISA13 - Interchange control number")]
    public int ControlNumber { get; set; }

    public List<FunctionalGroup>? Groups { get; set; }

    [EdiGroup]
    public class FunctionalGroup
    {
        [EdiValue("X(2)", Path = "GS/0", Description = "GS01 - Functional identifier code")]
        public string? FunctionalIdentifierCode { get; set; }

        [EdiValue("X(15)", Path = "GS/1", Description = "GS02 - Application sender code")]
        public string? ApplicationSenderCode { get; set; }

        [EdiValue("X(15)", Path = "GS/2", Description = "GS03 - Application receiver code")]
        public string? ApplicationReceiverCode { get; set; }

        [EdiValue("9(9)", Path = "GS/5", Description = "GS06 - Group control number")]
        public int GroupControlNumber { get; set; }

        public List<Order>? Orders { get; set; }
    }

    [EdiMessage]
    public class Order
    {
        [EdiValue("X(3)", Path = "ST/0", Description = "ST01 - Transaction set ID code")]
        public string? TransactionSetCode { get; set; }

        [EdiValue("X(9)", Path = "ST/1", Description = "ST02 - Transaction set control number")]
        public string? TransactionSetControlNumber { get; set; }

        [EdiValue("X(2)", Path = "BEG/0", Description = "BEG01 - Transaction set purpose code")]
        public string? PurposeCode { get; set; }

        [EdiValue("X(2)", Path = "BEG/1", Description = "BEG02 - Purchase order type code")]
        public string? PurchaseOrderTypeCode { get; set; }

        [EdiValue(Path = "BEG/2", Description = "BEG03 - Purchase order number")]
        public string? PurchaseOrderNumber { get; set; }

        [EdiValue(Path = "BEG/4", Description = "BEG05 - Purchase order date (CCYYMMDD)")]
        public string? PurchaseOrderDate { get; set; }

        public List<Address>? Addresses { get; set; }

        public List<Line>? Lines { get; set; }
    }

    [EdiSegment, EdiSegmentGroup("N1", SequenceEnd = "PO1")]
    public class Address
    {
        [EdiValue(Path = "N1/0", Description = "N101 - Entity identifier (ST = ship to, BT = bill to)")]
        public string? EntityIdentifier { get; set; }

        [EdiValue(Path = "N1/1", Description = "N102 - Name")]
        public string? Name { get; set; }

        [EdiValue(Path = "N3/0", Description = "N301 - Street address")]
        public string? Street { get; set; }

        [EdiValue(Path = "N4/0", Description = "N401 - City")]
        public string? City { get; set; }

        [EdiValue(Path = "N4/1", Description = "N402 - State")]
        public string? State { get; set; }

        [EdiValue(Path = "N4/2", Description = "N403 - Postal code")]
        public string? PostalCode { get; set; }
    }

    [EdiSegment, EdiSegmentGroup("PO1", SequenceEnd = "CTT")]
    public class Line
    {
        [EdiValue(Path = "PO1/0", Description = "PO101 - Line number")]
        public string? LineNumber { get; set; }

        [EdiValue(Path = "PO1/1", Description = "PO102 - Quantity ordered")]
        public decimal Quantity { get; set; }

        [EdiValue(Path = "PO1/2", Description = "PO103 - Unit of measure")]
        public string? UnitOfMeasure { get; set; }

        [EdiValue(Path = "PO1/3", Description = "PO104 - Unit price")]
        public decimal UnitPrice { get; set; }

        // PO105 is the basis-of-unit-price code; the product-ID qualifier/value pairs
        // start at PO106 (path index 5): PO106=qualifier (BP/VP/…), PO107=value, PO108/PO109 next pair.
        [EdiValue(Path = "PO1/5", Description = "PO106 - Product ID qualifier 1")]
        public string? PartQualifier1 { get; set; }

        [EdiValue(Path = "PO1/6", Description = "PO107 - Product ID 1")]
        public string? PartNumber1 { get; set; }

        [EdiValue(Path = "PO1/7", Description = "PO108 - Product ID qualifier 2")]
        public string? PartQualifier2 { get; set; }

        [EdiValue(Path = "PO1/8", Description = "PO109 - Product ID 2")]
        public string? PartNumber2 { get; set; }

        [EdiValue(Path = "PID/4", Description = "PID05 - Item description")]
        public string? Description { get; set; }
    }
}
