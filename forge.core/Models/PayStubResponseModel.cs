
namespace Forge.Core.Models;

public record PayStubResponseModel(
    int Id,
    int UserId,
    DateTimeOffset PayPeriodStart,
    DateTimeOffset PayPeriodEnd,
    DateTimeOffset PayDate,
    decimal GrossPay,
    decimal NetPay,
    decimal TotalDeductions,
    decimal TotalTaxes,
    int? FileAttachmentId,
    PayrollDocumentSource Source,
    string? ExternalId,
    List<PayStubDeductionResponseModel> Deductions);
