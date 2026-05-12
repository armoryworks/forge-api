namespace Forge.Core.Models;

public record AccountingPayStub(
    string ExternalId,
    DateTimeOffset PayPeriodStart,
    DateTimeOffset PayPeriodEnd,
    DateTimeOffset PayDate,
    decimal GrossPay,
    decimal NetPay,
    List<AccountingPayStubDeduction> Deductions);
