namespace Forge.Core.Enums;

public enum EdiTransactionStatus
{
    Received,
    Parsing,
    Parsed,
    Validating,
    Validated,
    Processing,
    Applied,
    Error,
    Acknowledged,
    Rejected
}
