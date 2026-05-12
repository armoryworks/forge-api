using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.IoT;

public record TestMachineConnectionCommand(int Id) : IRequest<TestMachineConnectionResult>;

public record TestMachineConnectionResult(bool Success, string? ErrorMessage);

public class TestMachineConnectionHandler(AppDbContext db, IMachineDataService machineDataService)
    : IRequestHandler<TestMachineConnectionCommand, TestMachineConnectionResult>
{
    public async Task<TestMachineConnectionResult> Handle(
        TestMachineConnectionCommand request, CancellationToken cancellationToken)
    {
        var connection = await db.MachineConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MachineConnection {request.Id} not found");

        try
        {
            var success = await machineDataService.TestConnectionAsync(request.Id, cancellationToken);
            return new TestMachineConnectionResult(success, success ? null : "Connection test failed");
        }
        catch (Exception ex)
        {
            return new TestMachineConnectionResult(false, ex.Message);
        }
    }
}
