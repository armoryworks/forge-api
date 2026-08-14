using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesChannels;

public record SetDefaultSalesChannelCommand(int Id) : IRequest;

public class SetDefaultSalesChannelHandler(AppDbContext db)
    : IRequestHandler<SetDefaultSalesChannelCommand>
{
    public async Task Handle(SetDefaultSalesChannelCommand request, CancellationToken ct)
    {
        var channel = await db.SalesChannels.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"SalesChannel {request.Id} not found");

        if (!channel.IsActive)
            throw new InvalidOperationException("Cannot make an inactive channel the default.");

        // A retail channel must never be the default. The default absorbs every
        // order created without an explicit channel — including the B2B ones
        // from quote conversion and manual entry — and routing those through a
        // retail channel would bill them to a marketplace house account and,
        // on a marketplace channel, mark their tax as somebody else's liability.
        if (channel.IsRetail)
        {
            throw new InvalidOperationException(
                $"Channel '{channel.Code}' is a {channel.ChannelType} channel and cannot be the default. " +
                "The default absorbs orders created without an explicit channel, which are account business.");
        }

        // Atomic default swap — same shape as SetDefaultCompanyLocation. The
        // filtered unique index (is_default = true) rejects a batched
        // clear-old + set-new SaveChanges when EF orders the set before the
        // clear, so the clear runs as its own statement inside the transaction.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.SalesChannels
            .Where(c => c.IsDefault && c.Id != request.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false), ct);

        channel.IsDefault = true;

        db.LogActivityAt(
            "default-channel-changed",
            $"Made '{channel.Name}' ({channel.Code}) the default sales channel",
            ("SalesChannel", channel.Id));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
