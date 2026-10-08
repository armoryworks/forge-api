using System.Security.Claims;
using System.Text.Json;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record ImplementEcoCommand(int Id) : IRequest;

public class ImplementEcoHandler(AppDbContext db, IHttpContextAccessor httpContext, IClock clock, IMediator mediator)
    : IRequestHandler<ImplementEcoCommand>
{
    private const string PartEntityType = "Part";
    private const int MaxRevisionLength = 10;

    public async Task Handle(ImplementEcoCommand request, CancellationToken cancellationToken)
    {
        var eco = await db.EngineeringChangeOrders
            .Include(e => e.AffectedItems)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.Id} not found");

        if (eco.Status != EcoStatus.Approved && eco.Status != EcoStatus.InImplementation)
            throw new InvalidOperationException("ECO must be in Approved or InImplementation status");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await ReviseAffectedPartsAsync(eco.EcoNumber, eco.Title, eco.AffectedItems, now, cancellationToken);

        // Mark all affected items as implemented
        foreach (var item in eco.AffectedItems)
        {
            item.IsImplemented = true;
        }

        eco.Status = EcoStatus.Implemented;
        eco.ImplementedAt = now;
        eco.ImplementedById = userId;

        await db.SaveChangesAsync(cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }

    private async Task ReviseAffectedPartsAsync(
        string ecoNumber,
        string ecoTitle,
        IEnumerable<EcoAffectedItem> affectedItems,
        DateTimeOffset effectiveDate,
        CancellationToken cancellationToken)
    {
        var reason = $"ECO {ecoNumber}: {ecoTitle}";
        var partItems = affectedItems
            .Where(i => !i.IsImplemented && string.Equals(i.EntityType, PartEntityType, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.EntityId);

        foreach (var group in partItems)
        {
            var partId = group.Key;
            var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == partId, cancellationToken);
            if (part is null)
                continue;

            var existing = await db.PartRevisions
                .Where(r => r.PartId == partId)
                .Select(r => r.Revision)
                .ToListAsync(cancellationToken);
            var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

            var requested = group.Select(i => RequestedRevision(i.NewValue)).FirstOrDefault(r => r is not null);
            if (requested is not null
                && (taken.Contains(requested) || string.Equals(part.Revision, requested, StringComparison.OrdinalIgnoreCase)))
                continue;

            var code = requested ?? NextRevisionCode(part.Revision, taken);
            if (code.Length > MaxRevisionLength)
                throw new InvalidOperationException(
                    $"Cannot revise part {part.PartNumber}: revision '{code}' is longer than {MaxRevisionLength} characters");

            await mediator.Send(
                new CreatePartRevisionCommand(partId, code, null, reason, effectiveDate),
                cancellationToken);
        }
    }

    private static string? RequestedRevision(string? newValue)
    {
        if (string.IsNullOrWhiteSpace(newValue))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(newValue);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "revision", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString()?.Trim();
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string NextRevisionCode(string? current, IReadOnlySet<string> taken)
    {
        var candidate = Increment(string.IsNullOrWhiteSpace(current) ? string.Empty : current.Trim());
        for (var guard = 0; taken.Contains(candidate) && guard < 1000; guard++)
            candidate = Increment(candidate);
        return candidate;
    }

    private static string Increment(string code)
    {
        if (code.Length == 0)
            return "A";

        var end = code.Length;
        if (char.IsAsciiDigit(code[end - 1]))
        {
            var start = end;
            while (start > 0 && char.IsAsciiDigit(code[start - 1]))
                start--;
            var digits = code[start..end];
            var next = (long.Parse(digits) + 1).ToString().PadLeft(digits.Length, '0');
            return code[..start] + next;
        }

        if (char.IsAsciiLetter(code[end - 1]))
        {
            var chars = code.ToCharArray();
            var i = end - 1;
            while (i >= 0 && char.IsAsciiLetter(chars[i]))
            {
                var upper = char.IsAsciiLetterUpper(chars[i]);
                var z = upper ? 'Z' : 'z';
                if (chars[i] != z)
                {
                    chars[i]++;
                    return new string(chars);
                }
                chars[i] = upper ? 'A' : 'a';
                i--;
            }
            var first = char.IsAsciiLetterUpper(code[end - 1]) ? "A" : "a";
            return new string(chars, 0, i + 1) + first + new string(chars, i + 1, end - i - 1);
        }

        return code + "A";
    }
}
