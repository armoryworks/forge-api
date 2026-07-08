using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Terms;

/// <summary>
/// S3 — resolves a public terms access token to the immutable snapshot that
/// was compiled at send time. Unknown / soft-deleted tokens throw
/// <see cref="KeyNotFoundException"/> (→ 404 via the exception middleware) so
/// the endpoint leaks nothing about token validity.
/// </summary>
public record GetPublicTermsQuery(string Token) : IRequest<PublicTermsResponseModel>;

public class GetPublicTermsHandler(AppDbContext db)
    : IRequestHandler<GetPublicTermsQuery, PublicTermsResponseModel>
{
    public async Task<PublicTermsResponseModel> Handle(GetPublicTermsQuery request, CancellationToken ct)
    {
        // The soft-delete global query filter excludes deleted snapshots and
        // string equality on the unique access_token index makes this O(1).
        var snapshot = await db.QuoteTermsSnapshots
            .AsNoTracking()
            .Include(s => s.Quote)
            .FirstOrDefaultAsync(s => s.AccessToken == request.Token, ct)
            ?? throw new KeyNotFoundException("Terms not found");

        return new PublicTermsResponseModel(
            QuoteNumber: snapshot.Quote.QuoteNumber ?? $"Q-{snapshot.QuoteId}",
            CompiledHtml: snapshot.CompiledHtml,
            SentAt: snapshot.CreatedAt);
    }
}
