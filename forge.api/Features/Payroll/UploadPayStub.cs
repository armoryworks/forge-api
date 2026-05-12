using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public record UploadPayStubCommand(int UserId, UploadPayStubRequestModel Request) : IRequest<PayStubResponseModel>;

public class UploadPayStubHandler(AppDbContext db)
    : IRequestHandler<UploadPayStubCommand, PayStubResponseModel>
{
    public async Task<PayStubResponseModel> Handle(
        UploadPayStubCommand request, CancellationToken ct)
    {
        var data = request.Request;

        var stub = new PayStub
        {
            UserId = request.UserId,
            PayPeriodStart = data.PayPeriodStart,
            PayPeriodEnd = data.PayPeriodEnd,
            PayDate = data.PayDate,
            GrossPay = data.GrossPay,
            NetPay = data.NetPay,
            TotalDeductions = data.GrossPay - data.NetPay,
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
