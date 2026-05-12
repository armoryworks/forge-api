using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record CreateECommerceIntegrationCommand(CreateECommerceIntegrationRequestModel Model) : IRequest<ECommerceIntegrationResponseModel>;

public class CreateECommerceIntegrationValidator : AbstractValidator<CreateECommerceIntegrationCommand>
{
    public CreateECommerceIntegrationValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Model.Credentials).NotEmpty();
    }
}

public class CreateECommerceIntegrationHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<CreateECommerceIntegrationCommand, ECommerceIntegrationResponseModel>
{
    public async Task<ECommerceIntegrationResponseModel> Handle(
        CreateECommerceIntegrationCommand request, CancellationToken cancellationToken)
    {
        var model = request.Model;
        var integration = new ECommerceIntegration
        {
            Name = model.Name,
            Platform = model.Platform,
            EncryptedCredentials = model.Credentials,
            StoreUrl = model.StoreUrl,
            AutoImportOrders = model.AutoImportOrders,
            SyncInventory = model.SyncInventory,
            DefaultCustomerId = model.DefaultCustomerId,
        };

        db.ECommerceIntegrations.Add(integration);
        await db.SaveChangesAsync(cancellationToken);

        var result = await mediator.Send(new GetECommerceIntegrationsQuery(), cancellationToken);
        return result.First(i => i.Id == integration.Id);
    }
}
