using FluentValidation;
using MediatR;

using Forge.Api.Features.VendorParts;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PurchaseOrders;

public record ReviewPriceOverrideQuery(ReviewPriceOverrideRequestModel Data)
    : IRequest<ReviewPriceOverrideResponseModel>;

public class ReviewPriceOverrideQueryValidator : AbstractValidator<ReviewPriceOverrideQuery>
{
    public ReviewPriceOverrideQueryValidator()
    {
        RuleFor(x => x.Data.VendorId).GreaterThan(0);
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.EnteredUnitPrice).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// AI-assisted price-variance review for a manual PO unit-price override (forge#6).
///
/// The deterministic half reuses <see cref="CheckTierVarianceQuery"/> (the same
/// option-aware tier match the off-tier prompt uses) to get the expected tier
/// price + variance, and bands a risk level off it. The AI half is a best-effort
/// narrative + flag via <see cref="IAiService"/>; when the model is offline the
/// review still returns the deterministic assessment and a templated
/// justification (AiAvailable = false), so the feature degrades gracefully.
/// </summary>
public class ReviewPriceOverrideHandler(IMediator mediator, IAiService ai)
    : IRequestHandler<ReviewPriceOverrideQuery, ReviewPriceOverrideResponseModel>
{
    public async Task<ReviewPriceOverrideResponseModel> Handle(ReviewPriceOverrideQuery request, CancellationToken ct)
    {
        var d = request.Data;

        var variance = await mediator.Send(new CheckTierVarianceQuery(d.VendorId,
            new List<CheckTierVarianceLineModel>
            {
                new(d.PartId, d.Quantity, d.EnteredUnitPrice, d.PurchaseUnitId),
            }), ct);
        var line = variance.Lines.Count > 0 ? variance.Lines[0] : null;
        var tierPrice = line?.TierPrice;
        var variancePct = line?.VariancePct;
        var isOffTier = line?.IsOffTier ?? true;

        var riskLevel = !isOffTier ? "Low"
            : variancePct is decimal v && v > 25m ? "High"
            : "Medium";

        var suggestedJustification = BuildJustification(d, tierPrice, variancePct);

        var aiAvailable = await ai.IsAvailableAsync(ct);
        string assessment;
        if (aiAvailable)
        {
            var prompt =
                $"A buyer entered a unit price of {d.EnteredUnitPrice:0.####} for part #{d.PartId} from vendor #{d.VendorId} " +
                $"(quantity {d.Quantity}). The expected tier price is " +
                (tierPrice is decimal tp ? $"{tp:0.####} (variance {variancePct:0.#}%)." : "not on file.") +
                $" The buyer's stated reason: \"{d.Reason ?? "(none given)"}\". " +
                "In 2-3 sentences, assess whether this manual price override looks reasonable or risky for a " +
                "procurement reviewer, and note any red flags.";
            assessment = (await ai.GenerateTextAsync(
                prompt, "You are a concise procurement controls assistant. Be factual and brief.", 0.3, ct)).Trim();
        }
        else
        {
            assessment = riskLevel switch
            {
                "Low" => "Within tolerance of the expected tier price — no review concern.",
                "High" => "Significantly off the expected tier price — recommend supervisor review.",
                _ => "Moderately off the expected tier price — confirm the stated reason is sufficient.",
            };
        }

        return new ReviewPriceOverrideResponseModel(
            tierPrice, variancePct, isOffTier, riskLevel, assessment, suggestedJustification, aiAvailable);
    }

    private static string BuildJustification(ReviewPriceOverrideRequestModel d, decimal? tierPrice, decimal? variancePct)
    {
        var reason = string.IsNullOrWhiteSpace(d.Reason) ? "no reason provided" : d.Reason!.Trim();
        return tierPrice is decimal tp
            ? $"Entered {d.EnteredUnitPrice:0.####} vs tier {tp:0.####} ({variancePct:0.#}% variance) for qty {d.Quantity}: {reason}."
            : $"Entered {d.EnteredUnitPrice:0.####} for qty {d.Quantity} (no tier on file): {reason}.";
    }
}
