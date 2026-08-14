using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesChannels;

public record UpdateSalesChannelCommand(int Id, UpdateSalesChannelRequestModel Model)
    : IRequest<SalesChannelResponseModel>;

public class UpdateSalesChannelValidator : AbstractValidator<UpdateSalesChannelCommand>
{
    public UpdateSalesChannelValidator()
    {
        RuleFor(x => x.Model.Name).MaximumLength(200).When(x => x.Model.Name is not null);
        RuleFor(x => x.Model.Description).MaximumLength(1000);
        RuleFor(x => x.Model.OrderNumberPrefix).MaximumLength(10);
    }
}

public class UpdateSalesChannelHandler(AppDbContext db)
    : IRequestHandler<UpdateSalesChannelCommand, SalesChannelResponseModel>
{
    public async Task<SalesChannelResponseModel> Handle(
        UpdateSalesChannelCommand request, CancellationToken ct)
    {
        var channel = await db.SalesChannels.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"SalesChannel {request.Id} not found");

        var model = request.Model;
        var changed = new List<string>();

        // ChannelType is deliberately not patchable. Flipping a live channel
        // between account and retail would retroactively change what its
        // existing orders mean — whether they needed a house account, whether
        // their tax was the install's liability. Retire the channel and create
        // a new one instead.

        if (model.Name is not null && model.Name.Trim() != channel.Name)
        {
            channel.Name = model.Name.Trim();
            changed.Add("name");
        }

        if (model.Description is not null && model.Description != channel.Description)
        {
            channel.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            changed.Add("description");
        }

        if (model.SoldToCustomerId.HasValue && model.SoldToCustomerId != channel.SoldToCustomerId)
        {
            if (!await db.Customers.AnyAsync(c => c.Id == model.SoldToCustomerId.Value, ct))
                throw new KeyNotFoundException($"Customer {model.SoldToCustomerId.Value} not found");

            channel.SoldToCustomerId = model.SoldToCustomerId.Value;
            changed.Add("soldToCustomerId");
        }

        if (model.TaxCollectedBy.HasValue && model.TaxCollectedBy != channel.TaxCollectedBy)
        {
            channel.TaxCollectedBy = model.TaxCollectedBy.Value;
            changed.Add("taxCollectedBy");
        }

        if (model.OrderNumberPrefix is not null)
        {
            var prefix = string.IsNullOrWhiteSpace(model.OrderNumberPrefix)
                ? null
                : model.OrderNumberPrefix.Trim().ToUpperInvariant();
            if (prefix != channel.OrderNumberPrefix)
            {
                channel.OrderNumberPrefix = prefix;
                changed.Add("orderNumberPrefix");
            }
        }

        if (model.ECommerceIntegrationId.HasValue && model.ECommerceIntegrationId != channel.ECommerceIntegrationId)
        {
            if (!await db.ECommerceIntegrations.AnyAsync(i => i.Id == model.ECommerceIntegrationId.Value, ct))
                throw new KeyNotFoundException($"ECommerceIntegration {model.ECommerceIntegrationId.Value} not found");

            channel.ECommerceIntegrationId = model.ECommerceIntegrationId.Value;
            changed.Add("ecommerceIntegrationId");
        }

        if (model.IsActive.HasValue && model.IsActive != channel.IsActive)
        {
            // The default channel is the fallback for every order with a null
            // channel_id. Deactivating it would strand those orders.
            if (!model.IsActive.Value && channel.IsDefault)
            {
                throw new InvalidOperationException(
                    "The default sales channel cannot be deactivated. Make another channel the default first.");
            }

            channel.IsActive = model.IsActive.Value;
            changed.Add("isActive");
        }

        // Retail channels must keep a house account across the edit, not only at
        // create — clearing it later would break the next import just as badly.
        if (channel.IsRetail && channel.SoldToCustomerId is null)
        {
            throw new InvalidOperationException(
                $"Channel '{channel.Code}' is a retail channel and requires a sold-to house account.");
        }

        if (changed.Count == 0)
            return await GetSalesChannelById.ProjectAsync(db, channel.Id, ct);

        db.LogActivityAt(
            "updated",
            $"Updated {changed.Count} field(s): {string.Join(", ", changed)}",
            ("SalesChannel", channel.Id));
        await db.SaveChangesAsync(ct);

        return await GetSalesChannelById.ProjectAsync(db, channel.Id, ct);
    }
}
