namespace Forge.Core.Interfaces;

public interface IRfqService
{
    Task<string> GenerateRfqNumberAsync(CancellationToken ct);
    Task SendToVendorsAsync(int rfqId, IEnumerable<int> vendorIds, CancellationToken ct);
    Task<int> AwardAndCreatePoAsync(int rfqId, int vendorResponseId, CancellationToken ct);
}
