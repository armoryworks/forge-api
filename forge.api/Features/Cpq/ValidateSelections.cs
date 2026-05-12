using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Cpq;

public record ValidateSelectionsQuery(ConfigureProductRequestModel Request) : IRequest<CpqValidationResponseModel>;

public record CpqValidationResponseModel(bool IsValid, List<string> Errors);

public class ValidateSelectionsHandler(ICpqService cpqService) : IRequestHandler<ValidateSelectionsQuery, CpqValidationResponseModel>
{
    public async Task<CpqValidationResponseModel> Handle(ValidateSelectionsQuery query, CancellationToken cancellationToken)
    {
        var result = await cpqService.ConfigureAsync(
            query.Request.ConfiguratorId,
            query.Request.Selections,
            cancellationToken);

        return new CpqValidationResponseModel(result.IsValid, result.ValidationErrors.ToList());
    }
}
