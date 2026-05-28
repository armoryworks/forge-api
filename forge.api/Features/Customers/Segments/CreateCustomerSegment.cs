using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Customers.Segments;

public record CreateCustomerSegmentCommand(CreateCustomerSegmentRequestModel Data) : IRequest<CustomerSegmentResponseModel>;

public class CreateCustomerSegmentValidator : AbstractValidator<CreateCustomerSegmentCommand>
{
    public CreateCustomerSegmentValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Description).MaximumLength(1000).When(x => x.Data.Description is not null);
    }
}

public class CreateCustomerSegmentHandler(AppDbContext db) : IRequestHandler<CreateCustomerSegmentCommand, CustomerSegmentResponseModel>
{
    public async Task<CustomerSegmentResponseModel> Handle(CreateCustomerSegmentCommand request, CancellationToken ct)
    {
        var data = request.Data;
        var segment = new CustomerSegment
        {
            Name = data.Name.Trim(),
            Description = data.Description?.Trim(),
            FilterCriteria = data.FilterCriteria,
        };
        db.CustomerSegments.Add(segment);
        await db.SaveChangesAsync(ct);

        return new CustomerSegmentResponseModel(
            segment.Id, segment.Name, segment.Description, segment.FilterCriteria, segment.IsActive, segment.CreatedAt);
    }
}
