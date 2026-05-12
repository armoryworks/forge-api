using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Ai;

public record AiSearchSuggestCommand(string Query) : IRequest<List<AiSearchSuggestion>>;
public record AiSearchSuggestion(string Label, string Description, string Url, string Icon);

public class AiSearchSuggestValidator : AbstractValidator<AiSearchSuggestCommand>
{
    public AiSearchSuggestValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MaximumLength(200);
    }
}

public class AiSearchSuggestHandler(
    IAiService aiService,
    ILogger<AiSearchSuggestHandler> logger) : IRequestHandler<AiSearchSuggestCommand, List<AiSearchSuggestion>>
{
    public async Task<List<AiSearchSuggestion>> Handle(AiSearchSuggestCommand request, CancellationToken ct)
    {
        var prompt = $"""
            You are a search assistant for a manufacturing operations app called QB Engineer.
            The app has these pages (use these exact URL paths):
            - /backlog — Job backlog list (searchable by job number, title, customer)
            - /kanban — Kanban board (visual job workflow)
            - /customers — Customer list
            - /parts — Parts catalog (searchable by part number, description)
            - /leads — Sales leads
            - /assets — Equipment/asset registry
            - /expenses — Expense tracking
            - /inventory — Inventory & stock levels
            - /invoices — Invoice management
            - /reports — Reports & analytics
            - /vendors — Vendor list
            - /purchase-orders — Purchase orders
            - /sales-orders — Sales orders
            - /quotes — Quotes
            - /shipments — Shipments
            - /quality — QC inspections & lot tracking
            - /time-tracking — Time entries
            - /admin — Admin settings

            Given the user's search query, suggest 2-4 relevant pages they likely want to visit.
            For each suggestion, include a search parameter if applicable: append ?search=<term> to the URL.

            Return ONLY a JSON array of objects with these fields:
            - "label": short action label (e.g., "Search Parts for 'widget'")
            - "description": one-line explanation
            - "url": the URL path with query params
            - "icon": a Material Icons name (use: work, people, inventory_2, trending_up, precision_manufacturing, receipt_long, local_shipping, request_quote, shopping_cart, assessment, settings, schedule, verified_user, warehouse)

            User query: "{request.Query}"
            """;

        string response;
        try
        {
            response = await aiService.GenerateTextAsync(prompt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI service unavailable for search suggestions");
            return GetFallbackSuggestions(request.Query);
        }

        try
        {
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                return GetFallbackSuggestions(request.Query);

            var json = response[jsonStart..(jsonEnd + 1)];
            var suggestions = JsonSerializer.Deserialize<List<AiSearchSuggestion>>(json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

            return suggestions ?? GetFallbackSuggestions(request.Query);
        }
        catch
        {
            return GetFallbackSuggestions(request.Query);
        }
    }

    private static List<AiSearchSuggestion> GetFallbackSuggestions(string query)
    {
        var encoded = Uri.EscapeDataString(query);
        return
        [
            new("Search Jobs", $"Find jobs matching '{query}'", $"/backlog?search={encoded}", "work"),
            new("Search Parts", $"Find parts matching '{query}'", $"/parts?search={encoded}", "inventory_2"),
            new("Search Customers", $"Find customers matching '{query}'", $"/customers?search={encoded}", "people"),
        ];
    }
}
