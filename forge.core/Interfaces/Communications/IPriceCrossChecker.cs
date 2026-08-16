using Forge.Core.Models.Extraction;

namespace Forge.Core.Interfaces.Communications;

/// <summary>
/// Compares an extracted unit price against what the customer has actually been
/// charged for the part.
///
/// <para>Its own seam because the question "is this the right price" has a real
/// answer that lives in pricing history, and the extractor — which only reads
/// text — has no business knowing it.</para>
/// </summary>
public interface IPriceCrossChecker
{
    Task<PriceCrossCheck> CheckAsync(int customerId, int partId, decimal? extractedPrice, CancellationToken ct);
}
