namespace Forge.Core.Enums;

/// <summary>
/// What happens to returned goods when a customer return is resolved: back into stock at their original cost,
/// scrapped, sent to rework, or nothing came back.
/// </summary>
public enum ReturnDisposition
{
    ReturnToStock = 0,
    Scrap = 1,
    Rework = 2,
    NoReturn = 3,
}
