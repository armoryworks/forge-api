using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forge.Api.Features.Accounting.Training;

/// <summary>One end-state predicate over the sandbox ledger (design doc §2 — six validator types).</summary>
public record ScenarioValidator(
    string Type,
    string? MemoContains = null,
    string? AccountNumber = null,
    string? DrAccountNumber = null,
    string? CrAccountNumber = null,
    decimal? Amount = null,
    decimal? Expected = null);

/// <summary>A fix-it scenario (shipped JSON asset, decision D3). All UI strings are i18n keys.</summary>
public record TrainingScenario(
    string Id,
    string Track, // "A" | "B" | "both"
    int Order,
    string TitleKey,
    string BriefKey,
    string? BaitKey,
    IReadOnlyList<string> HintKeys,
    IReadOnlyList<ScenarioValidator> Validators,
    string SuccessKey);

/// <summary>Loads the shipped scenario catalog once (Training/scenarios.json, copied to output).</summary>
public interface ITrainingScenarioProvider
{
    IReadOnlyList<TrainingScenario> All { get; }
}

/// <inheritdoc />
public sealed class TrainingScenarioProvider : ITrainingScenarioProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public IReadOnlyList<TrainingScenario> All { get; }

    public TrainingScenarioProvider()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Training", "scenarios.json");
        var json = File.ReadAllText(path);
        All = JsonSerializer.Deserialize<List<TrainingScenario>>(json, Options)
              ?? throw new InvalidOperationException("Training/scenarios.json deserialized to null.");
    }
}
