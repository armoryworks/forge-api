using System.Text.Json;

using Microsoft.Extensions.Caching.Memory;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public class ClockEventTypeService(
    IReferenceDataRepository referenceDataRepo,
    IMemoryCache cache) : IClockEventTypeService
{
    private const string CacheKey = "clock_event_types";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(60);

    public async Task<List<ClockEventTypeDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out List<ClockEventTypeDefinition>? cached) && cached is not null)
            return cached;

        var data = await referenceDataRepo.GetByGroupAsync("clock_event_type", ct);

        var definitions = data.Select(r =>
        {
            var meta = ParseMetadata(r.Metadata);
            return new ClockEventTypeDefinition(
                r.Code,
                r.Label,
                meta.StatusMapping,
                meta.OppositeCode,
                meta.Category,
                meta.CountsAsActive,
                meta.IsMismatchable,
                meta.Icon,
                meta.Color);
        }).ToList();

        cache.Set(CacheKey, definitions, CacheDuration);
        return definitions;
    }

    public async Task<ClockEventTypeDefinition?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(d => d.Code == code);
    }

    public void InvalidateCache()
    {
        cache.Remove(CacheKey);
    }

    private static ClockEventTypeMetadata ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ClockEventTypeMetadata();

        return JsonSerializer.Deserialize<ClockEventTypeMetadata>(json, JsonOptions) ?? new ClockEventTypeMetadata();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class ClockEventTypeMetadata
    {
        public string StatusMapping { get; set; } = "Out";
        public string? OppositeCode { get; set; }
        public string Category { get; set; } = "work";
        public bool CountsAsActive { get; set; }
        public bool IsMismatchable { get; set; }
        public string Icon { get; set; } = "schedule";
        public string Color { get; set; } = "#94a3b8";
    }
}
