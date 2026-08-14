using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesChannels;

public record CreateSalesChannelCommand(CreateSalesChannelRequestModel Model)
    : IRequest<SalesChannelResponseModel>;

public class CreateSalesChannelValidator : AbstractValidator<CreateSalesChannelCommand>
{
    public CreateSalesChannelValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Model.Description).MaximumLength(1000);
        RuleFor(x => x.Model.OrderNumberPrefix).MaximumLength(10);

        // Code is the stable handle integrations and order numbering key on, so
        // it is constrained to an identifier shape rather than free text —
        // "EBAY-US" not "eBay (US, main account)".
        RuleFor(x => x.Model.Code)
            .NotEmpty().MaximumLength(40)
            .Matches("^[A-Z0-9][A-Z0-9-]*$")
            .WithMessage("Code must be uppercase letters, digits and hyphens (e.g. EBAY-US).");

        // The single invariant that makes the whole model work: a retail order's
        // receivable must land on a real account. Enforced at create rather than
        // at order time so the failure surfaces while an admin is looking at the
        // channel form, not mid-import at 3am.
        RuleFor(x => x.Model.SoldToCustomerId)
            .NotNull()
            .When(x => x.Model.ChannelType is SalesChannelType.DirectRetail or SalesChannelType.Marketplace)
            .WithMessage("Retail and marketplace channels require a sold-to house account — the consumer never carries the receivable.");
    }
}

public class CreateSalesChannelHandler(AppDbContext db)
    : IRequestHandler<CreateSalesChannelCommand, SalesChannelResponseModel>
{
    public async Task<SalesChannelResponseModel> Handle(
        CreateSalesChannelCommand request, CancellationToken cancellationToken)
    {
        var model = request.Model;
        var code = model.Code.Trim().ToUpperInvariant();

        if (await db.SalesChannels.AnyAsync(c => c.Code == code, cancellationToken))
            throw new InvalidOperationException($"A sales channel with code '{code}' already exists.");

        if (model.SoldToCustomerId is int soldTo
            && !await db.Customers.AnyAsync(c => c.Id == soldTo, cancellationToken))
        {
            throw new KeyNotFoundException($"Customer {soldTo} not found");
        }

        if (model.ECommerceIntegrationId is int integrationId
            && !await db.ECommerceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            throw new KeyNotFoundException($"ECommerceIntegration {integrationId} not found");
        }

        var channel = new SalesChannel
        {
            Name = model.Name.Trim(),
            Code = code,
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            ChannelType = model.ChannelType,
            SoldToCustomerId = model.SoldToCustomerId,
            // Marketplaces are facilitators by default — that is the whole legal
            // point of the category. Callers can still override per channel,
            // since a marketplace may be seller-liable in some jurisdictions.
            TaxCollectedBy = model.TaxCollectedBy
                ?? (model.ChannelType == SalesChannelType.Marketplace
                    ? TaxCollectedBy.Marketplace
                    : TaxCollectedBy.Seller),
            OrderNumberPrefix = string.IsNullOrWhiteSpace(model.OrderNumberPrefix)
                ? null
                : model.OrderNumberPrefix.Trim().ToUpperInvariant(),
            ECommerceIntegrationId = model.ECommerceIntegrationId,
            // Never auto-default. The default channel is established by seeding
            // and changed through an explicit set-default action, so creating a
            // channel can't silently re-route every order with a null channel.
            IsDefault = false,
            IsActive = true,
        };

        db.SalesChannels.Add(channel);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt(
            "created",
            $"Created sales channel: {channel.Name} ({channel.Code}), type {channel.ChannelType}",
            ("SalesChannel", channel.Id));
        await db.SaveChangesAsync(cancellationToken);

        return await GetSalesChannelById.ProjectAsync(db, channel.Id, cancellationToken);
    }
}
