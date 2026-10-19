using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public record UploadPayStubCommand(int UserId, UploadPayStubRequestModel Request) : IRequest<PayStubResponseModel>;

public class UploadPayStubValidator : AbstractValidator<UploadPayStubCommand>
{
    public const decimal MaxAmount = 10_000_000m;

    public UploadPayStubValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.Request.FileAttachmentId).GreaterThan(0);
        RuleFor(x => x.Request.PayPeriodEnd)
            .GreaterThanOrEqualTo(x => x.Request.PayPeriodStart)
            .WithMessage("Pay period end must be on or after the pay period start.");
        RuleFor(x => x.Request.PayDate)
            .GreaterThanOrEqualTo(x => x.Request.PayPeriodStart)
            .WithMessage("Pay date must be on or after the pay period start.");
        RuleFor(x => x.Request.GrossPay)
            .GreaterThanOrEqualTo(0)
            .LessThan(MaxAmount)
            .PrecisionScale(18, 2, true);
        RuleFor(x => x.Request.NetPay)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.Request.GrossPay).WithMessage("Net pay cannot be more than gross pay.")
            .PrecisionScale(18, 2, true);
    }
}

public class UploadPayStubHandler(AppDbContext db)
    : IRequestHandler<UploadPayStubCommand, PayStubResponseModel>
{
    public async Task<PayStubResponseModel> Handle(
        UploadPayStubCommand request, CancellationToken ct)
    {
        var data = request.Request;

        var failures = new List<ValidationFailure>();
        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
            failures.Add(new ValidationFailure(nameof(request.UserId), $"User {request.UserId} does not exist."));
        if (!await db.FileAttachments.AnyAsync(f => f.Id == data.FileAttachmentId, ct))
            failures.Add(new ValidationFailure(nameof(data.FileAttachmentId), $"File {data.FileAttachmentId} does not exist."));
        if (failures.Count > 0)
            throw new ValidationException(failures);

        var grossPay = Math.Round(data.GrossPay, 2, MidpointRounding.AwayFromZero);
        var netPay = Math.Round(data.NetPay, 2, MidpointRounding.AwayFromZero);

        var stub = new PayStub
        {
            UserId = request.UserId,
            PayPeriodStart = data.PayPeriodStart,
            PayPeriodEnd = data.PayPeriodEnd,
            PayDate = data.PayDate,
            GrossPay = grossPay,
            NetPay = netPay,
            TotalDeductions = grossPay - netPay,
            TotalTaxes = 0,
            FileAttachmentId = data.FileAttachmentId,
            Source = PayrollDocumentSource.Manual,
        };

        db.PayStubs.Add(stub);
        await db.SaveChangesAsync(ct);

        return new PayStubResponseModel(
            stub.Id, stub.UserId, stub.PayPeriodStart, stub.PayPeriodEnd, stub.PayDate,
            stub.GrossPay, stub.NetPay, stub.TotalDeductions, stub.TotalTaxes,
            stub.FileAttachmentId, stub.Source, stub.ExternalId, []);
    }
}
