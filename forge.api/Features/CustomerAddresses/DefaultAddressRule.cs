using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerAddresses;

public static class DefaultAddressRule
{
    public static async Task<List<CustomerAddress>> ClearOtherDefaultsAsync(
        AppDbContext db, int customerId, int keepAddressId, AddressType type, CancellationToken ct)
    {
        var others = await db.CustomerAddresses
            .Where(a => a.CustomerId == customerId && a.IsDefault && a.Id != keepAddressId)
            .Where(a => type == AddressType.Both || a.AddressType == type || a.AddressType == AddressType.Both)
            .ToListAsync(ct);
        foreach (var other in others)
            other.IsDefault = false;
        return others;
    }

    public static string DescribeReplaced(List<CustomerAddress> demoted) =>
        demoted.Count == 0 ? "" : $"; replaces {string.Join(", ", demoted.Select(a => a.Label))} as default";
}
