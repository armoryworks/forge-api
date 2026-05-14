
namespace Forge.Core.Entities;

public class PayStub : BaseAuditableEntity
{
    public int UserId { get; set; }
    public DateTimeOffset PayPeriodStart { get; set; }
    public DateTimeOffset PayPeriodEnd { get; set; }
    public DateTimeOffset PayDate { get; set; }
    public decimal GrossPay { get; set; }
    public decimal NetPay { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalTaxes { get; set; }
    public int? FileAttachmentId { get; set; }
    public PayrollDocumentSource Source { get; set; }
    public string? ExternalId { get; set; }

    public FileAttachment? FileAttachment { get; set; }
    public ICollection<PayStubDeduction> Deductions { get; set; } = [];
}
