using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record UpdateECommerceIntegrationCommand(int Id, UpdateECommerceIntegrationRequestModel Model) : IRequest<ECommerceIntegrationResponseModel>;

public class UpdateECommerceIntegrationValidator : AbstractValidator<UpdateECommerceIntegrationCommand>
{
    public UpdateECommerceIntegrationValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
    }
}

public class UpdateECommerceIntegrationHandler(
    AppDbContext db, IMediator mediator, IECommerceCredentialProtector protector)
    : IRequestHandler<UpdateECommerceIntegrationCommand, ECommerceIntegrationResponseModel>
{
    public async Task<ECommerceIntegrationResponseModel> Handle(
        UpdateECommerceIntegrationCommand request, CancellationToken cancellationToken)
    {
        var integration = await db.ECommerceIntegrations
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"ECommerceIntegration {request.Id} not found");

        var model = request.Model;
        integration.Name = model.Name;
        integration.Platform = model.Platform;
        integration.StoreUrl = model.StoreUrl;
        integration.IsActive = model.IsActive;
        integration.AutoImportOrders = model.AutoImportOrders;
        integration.SyncInventory = model.SyncInventory;
        integration.DefaultCustomerId = model.DefaultCustomerId;

        // Blank means "leave the stored secret alone" — the API never returns
        // it, so a blank field is the UI saying unchanged, not clear.
        if (!string.IsNullOrEmpty(model.Credentials))
            integration.EncryptedCredentials = protector.Protect(model.Credentials) ?? string.Empty;

        await db.SaveChangesAsync(cancellationToken);

        var result = await mediator.Send(new GetECommerceIntegrationsQuery(), cancellationToken);
        return result.First(i => i.Id == integration.Id);
    }
}
