using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Customers.Segments;

public record UpdateCustomerSegmentCommand(int Id, UpdateCustomerSegmentRequestModel Data) : IRequest<CustomerSegmentResponseModel>;

public class UpdateCustomerSegmentValidator : AbstractValidator<UpdateCustomerSegmentCommand>
{
    public UpdateCustomerSegmentValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Description).MaximumLength(1000).When(x => x.Data.Description is not null);
    }
}

public class UpdateCustomerSegmentHandler(AppDbContext db) : IRequestHandler<UpdateCustomerSegmentCommand, CustomerSegmentResponseModel>
{
    public async Task<CustomerSegmentResponseModel> Handle(UpdateCustomerSegmentCommand request, CancellationToken ct)
    {
        var segment = await db.CustomerSegments.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Customer segment {request.Id} not found");

        var data = request.Data;
        segment.Name = data.Name.Trim();
        segment.Description = data.Description?.Trim();
        segment.FilterCriteria = data.FilterCriteria;
        segment.IsActive = data.IsActive;

        await db.SaveChangesAsync(ct);

        return new CustomerSegmentResponseModel(
            segment.Id, segment.Name, segment.Description, segment.FilterCriteria, segment.IsActive, segment.CreatedAt);
    }
}
