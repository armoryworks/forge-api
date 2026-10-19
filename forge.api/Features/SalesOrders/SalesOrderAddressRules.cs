using FluentValidation;
using FluentValidation.Results;

using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// The rule a caller-supplied ship-to or bill-to address must meet before it is stored
/// on a sales order: it belongs to the order's customer, it is active, and it is of the
/// requested type (or Both).
/// </summary>
public static class SalesOrderAddressRules
{
    /// <summary>
    /// Throws a <see cref="ValidationException"/> naming <paramref name="fieldPath"/> when
    /// <paramref name="address"/> (looked up by <paramref name="id"/>) fails the rule.
    /// </summary>
    public static void EnsureUsable(
        CustomerAddress? address, int id, int customerId, AddressType addressType, string fieldPath)
    {
        if (address is null || address.CustomerId != customerId)
            throw Rejected(fieldPath, id, $"Address {id} does not belong to this customer.");
        if (!address.IsActive)
            throw Rejected(fieldPath, id, $"Address {id} is inactive.");
        if (address.AddressType != addressType && address.AddressType != AddressType.Both)
            throw Rejected(fieldPath, id, $"Address {id} is a {address.AddressType} address, not a {addressType} address.");
    }

    private static ValidationException Rejected(string fieldPath, int id, string message) =>
        new(new[] { new ValidationFailure(fieldPath, message) { AttemptedValue = id } });
}
