namespace Forge.Core.Models;

public record UploadPayStubRequestModel(
    DateTimeOffset PayPeriodStart,
    DateTimeOffset PayPeriodEnd,
    DateTimeOffset PayDate,
    decimal GrossPay,
    decimal NetPay,
    int FileAttachmentId);
