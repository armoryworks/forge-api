using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record GetDatabaseTransferSummaryQuery : IRequest<DatabaseTransferSummaryModel>;

public class GetDatabaseTransferSummaryHandler(IDatabaseTransferService transfer)
    : IRequestHandler<GetDatabaseTransferSummaryQuery, DatabaseTransferSummaryModel>
{
    public Task<DatabaseTransferSummaryModel> Handle(GetDatabaseTransferSummaryQuery request, CancellationToken ct) =>
        transfer.GetSummaryAsync(ct);
}
