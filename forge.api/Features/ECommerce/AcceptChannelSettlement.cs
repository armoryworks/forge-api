using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.ECommerce;

/// <summary>
/// Signs off on a settlement batch whose lines do not tie to the reported net.
///
/// <para>Some variances are real and permanent — a marketplace withholds a
/// reserve it never itemises, or restates a fee outside the batch. Leaving those
/// batches in Discrepancy forever trains people to ignore the list, which
/// defeats the point of having it. Accepting requires a written reason, and
/// re-importing never overwrites an accepted batch.</para>
/// </summary>
public record AcceptChannelSettlementCommand(int Id, string ResolutionNotes)
    : IRequest<ChannelSettlementResponseModel>;

public class AcceptChannelSettlementValidator : AbstractValidator<AcceptChannelSettlementCommand>
{
    public AcceptChannelSettlementValidator()
    {
        RuleFor(x => x.ResolutionNotes)
            .NotEmpty().MaximumLength(2000)
            .WithMessage("Accepting a variance requires a reason — an unexplained sign-off is worse than none.");
    }
}

public class AcceptChannelSettlementHandler(AppDbContext db)
    : IRequestHandler<AcceptChannelSettlementCommand, ChannelSettlementResponseModel>
{
    public async Task<ChannelSettlementResponseModel> Handle(
        AcceptChannelSettlementCommand request, CancellationToken ct)
    {
        var settlement = await db.ChannelSettlements
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"ChannelSettlement {request.Id} not found");

        if (settlement.Status == ChannelSettlementStatus.Reconciled)
        {
            throw new InvalidOperationException(
                "This batch already ties out — there is no variance to accept.");
        }

        settlement.Status = ChannelSettlementStatus.Accepted;
        settlement.ResolutionNotes = request.ResolutionNotes.Trim();

        db.LogActivityAt(
            "settlement-variance-accepted",
            $"Accepted a {settlement.Variance:N2} {settlement.CurrencyCode} variance on settlement "
                + $"{settlement.ExternalSettlementId}: {settlement.ResolutionNotes}",
            ("SalesChannel", settlement.ChannelId));

        await db.SaveChangesAsync(ct);

        return await GetHeaderAsync(db, settlement.Id, ct);
    }

    private static async Task<ChannelSettlementResponseModel> GetHeaderAsync(
        AppDbContext db, int id, CancellationToken ct)
    {
        var result = await db.ChannelSettlements
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new ChannelSettlementResponseModel
            {
                Id = s.Id,
                ChannelId = s.ChannelId,
                ChannelName = s.Channel.Name,
                ExternalSettlementId = s.ExternalSettlementId,
                PeriodStart = s.PeriodStart,
                PeriodEnd = s.PeriodEnd,
                DepositedAt = s.DepositedAt,
                ReportedNetAmount = s.ReportedNetAmount,
                ComputedNetAmount = s.Lines.Sum(l => (decimal?)l.Amount) ?? 0m,
                Variance = s.ReportedNetAmount - (s.Lines.Sum(l => (decimal?)l.Amount) ?? 0m),
                CurrencyCode = s.CurrencyCode,
                Status = s.Status,
                ResolutionNotes = s.ResolutionNotes,
                LineCount = s.Lines.Count,
                UnmatchedLineCount = s.Lines.Count(l => l.ExternalOrderId != null && l.SalesOrderId == null),
                CreatedAt = s.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);

        return result ?? throw new KeyNotFoundException($"ChannelSettlement {id} not found");
    }
}
