using System.Globalization;
using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Parts;
using Forge.Api.Features.Quotes;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Services.Cpq;

/// <summary>
/// The configure-price-quote engine registered while CAP-O2C-CPQ is on. Validation and pricing run through
/// <see cref="CpqEngine"/>; a configured part is a duplicate of the base part (BOM and routing) plus the option
/// components and steps; a configured quote is a Draft quote in the customer's currency, created through the
/// normal quote path so numbering, tax and ownership apply.
/// </summary>
public sealed class BuiltInCpqService(
    AppDbContext db,
    ISender sender,
    IClock clock,
    ISystemSettingRepository settings,
    IBomRevisionService bomRevisions,
    TaxOverrideGuard taxGuard) : ICpqService
{
    public const string QuoteValidDaysKey = Forge.Core.Settings.QuoteSettings.DefaultValidDaysKey;
    public const int DefaultQuoteValidDays = 30;
    private const int PartNumberMaxLength = 50;

    public async Task<CpqResult> ConfigureAsync(
        int configuratorId, Dictionary<string, string> selections, decimal quantity, CancellationToken ct)
    {
        var configurator = await LoadConfiguratorAsync(configuratorId, ct);
        return ToResult(CpqEngine.Evaluate(configurator, selections, quantity));
    }

    public async Task<bool> ValidateSelectionsAsync(int configuratorId, Dictionary<string, string> selections, CancellationToken ct)
        => (await ConfigureAsync(configuratorId, selections, 1m, ct)).IsValid;

    public decimal CalculatePrice(ProductConfigurator configurator, Dictionary<string, string> selections, decimal quantity)
        => CpqEngine.Evaluate(configurator, selections, quantity).UnitPrice;

    public IReadOnlyList<BOMLine> GenerateBom(ProductConfigurator configurator, Dictionary<string, string> selections)
        => CpqEngine.Evaluate(configurator, selections, 1m).Components
            .Where(c => c.PartId is not null)
            .Select((c, i) => new BOMLine
            {
                ParentPartId = configurator.BasePartId,
                ChildPartId = c.PartId!.Value,
                Quantity = c.Quantity,
                SortOrder = i,
                Notes = c.OptionName,
            })
            .ToList();

    public IReadOnlyList<Operation> GenerateRouting(ProductConfigurator configurator, Dictionary<string, string> selections)
        => CpqEngine.Evaluate(configurator, selections, 1m).RoutingSteps
            .Select((s, i) => new Operation
            {
                PartId = configurator.BasePartId,
                StepNumber = (i + 1) * 10,
                Title = s.Title,
                WorkCenterId = s.WorkCenterId,
                SetupMinutes = s.SetupMinutes,
                RunMinutesEach = s.RunMinutesEach,
            })
            .ToList();

    public async Task<Part> GeneratePartFromConfigurationAsync(int configurationId, CancellationToken ct)
    {
        var (configuration, configurator, evaluation) = await LoadConfigurationAsync(configurationId, ct);
        if (configuration.PartId is int existing)
            return await db.Parts.FirstAsync(p => p.Id == existing, ct);

        var basePart = configurator.BasePart;
        var componentIds = await ResolveComponentPartIdsAsync(evaluation.Components, ct);
        var partNumber = await NextPartNumberAsync(basePart.PartNumber, configuration.Id, ct);
        var summary = DescribeSelections(evaluation.Selections);

        var clone = await sender.Send(new ClonePartCommand(
            basePart.Id,
            new ClonePartRequestModel(
                Name: Clip($"{basePart.Name} ({configuration.ConfigurationCode})", 256),
                Description: Clip(string.IsNullOrEmpty(summary) ? basePart.Description ?? string.Empty : summary, 2000),
                CopyBom: true,
                CopyRouting: true),
            partNumber), ct);

        var part = await db.Parts.FirstAsync(p => p.Id == clone.Id, ct);
        var bom = await db.BOMLines.Where(b => b.ParentPartId == part.Id).ToListAsync(ct);
        var sort = bom.Count == 0 ? 0 : bom.Max(b => b.SortOrder);
        var childParts = await db.Parts.AsNoTracking()
            .Where(p => componentIds.Values.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        foreach (var component in evaluation.Components)
        {
            var childId = componentIds[component];
            var line = bom.FirstOrDefault(b => b.ChildPartId == childId);
            if (line is not null)
            {
                line.Quantity += component.Quantity;
                continue;
            }
            line = new BOMLine
            {
                ParentPartId = part.Id,
                ChildPartId = childId,
                Quantity = component.Quantity,
                SortOrder = sort += 10,
                SourceType = childParts[childId].ProcurementSource == ProcurementSource.Make ? BOMSourceType.Make : BOMSourceType.Buy,
                Notes = Clip($"Option {component.OptionName}", 500),
            };
            db.BOMLines.Add(line);
            bom.Add(line);
        }

        var step = await db.Operations.Where(o => o.PartId == part.Id).Select(o => (int?)o.StepNumber).MaxAsync(ct) ?? 0;
        foreach (var routingStep in evaluation.RoutingSteps)
            db.Operations.Add(new Operation
            {
                PartId = part.Id,
                StepNumber = step += 10,
                Title = Clip(routingStep.Title, 200),
                Instructions = $"Added by option {routingStep.OptionName}",
                WorkCenterId = routingStep.WorkCenterId,
                SetupMinutes = routingStep.SetupMinutes,
                RunMinutesEach = routingStep.RunMinutesEach,
            });

        part.IsConfigurable = false;
        db.LogActivityAt(
            "configured",
            Clip($"Configured from {basePart.PartNumber} by {configuration.ConfigurationCode}: {evaluation.Components.Count} option component(s), {evaluation.RoutingSteps.Count} option step(s)", 500),
            ("Part", part.Id));
        await db.SaveChangesAsync(ct);

        if (evaluation.Components.Count > 0)
            await bomRevisions.CaptureCurrentStateAsync(
                part.Id, db.CurrentUserId, $"Configured by {configuration.ConfigurationCode}", ct);
        await sender.Send(new RecalculatePartStandardCostCommand(part.Id), ct);

        return part;
    }

    public async Task<Quote> GenerateQuoteFromConfigurationAsync(int configurationId, int customerId, CancellationToken ct)
    {
        var (configuration, configurator, evaluation) = await LoadConfigurationAsync(configurationId, ct);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found");

        var currency = await new DocumentCurrencyResolver(db).ResolveAsync(null, null, customer.CurrencyId, clock.UtcNow, ct);
        var unitPrice = currency.FxRate == 1m
            ? configuration.ComputedPrice
            : Math.Round(configuration.ComputedPrice / currency.FxRate, 4, MidpointRounding.AwayFromZero);

        Part? part = configuration.PartId is int partId
            ? await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partId, ct)
            : null;
        var description = part is not null
            ? $"{part.PartNumber} — {part.Name}"
            : $"{configurator.BasePart.PartNumber} configured ({configuration.ConfigurationCode})";

        var validDays = await QuoteValidDaysAsync(ct);
        var today = clock.UtcNow.UtcDateTime.Date;
        var expiration = new DateTimeOffset(today.AddDays(validDays), TimeSpan.Zero);
        var taxRate = await taxGuard.GetDefaultRateAsync(customerId, ct);

        var created = await sender.Send(new CreateQuoteCommand(
            customerId,
            ShippingAddressId: null,
            ExpirationDate: expiration,
            Notes: Clip($"Configured with {configurator.Name}, configuration {configuration.ConfigurationCode}.", 2000),
            TaxRate: taxRate,
            Lines:
            [
                new CreateQuoteLineModel(
                    part?.Id,
                    Clip(description, 500),
                    configuration.Quantity,
                    unitPrice,
                    Clip(DescribeSelections(evaluation.Selections), 1000)),
            ],
            CurrencyId: currency.CurrencyId,
            FxRate: currency.IsFunctional ? null : currency.FxRate), ct);

        db.LogActivityAt(
            "created",
            Clip($"Created from configuration {configuration.ConfigurationCode} ({configurator.Name})", 500),
            ("Quote", created.Id));
        await db.SaveChangesAsync(ct);

        return await db.Quotes.FirstAsync(q => q.Id == created.Id, ct);
    }

    private static CpqResult ToResult(CpqEvaluation evaluation) => new()
    {
        ComputedPrice = evaluation.UnitPrice,
        Quantity = evaluation.Quantity,
        ExtendedPrice = evaluation.ExtendedPrice,
        LeadTimeDays = evaluation.LeadTimeDays,
        PriceBreakdown = evaluation.Breakdown,
        BomPreview = evaluation.Components.Select(c => new CpqBomPreview
        {
            PartId = c.PartId,
            PartNumber = c.PartNumber ?? string.Empty,
            Quantity = c.Quantity,
            Source = c.OptionName,
        }).ToList(),
        RoutingPreview = evaluation.RoutingSteps.Select(s => new CpqRoutingPreview
        {
            OperationName = s.Title,
            EstimatedMinutes = s.SetupMinutes + s.RunMinutesEach * evaluation.Quantity,
        }).ToList(),
        ValidationErrors = evaluation.Errors,
        IsValid = evaluation.IsValid,
    };

    private async Task<ProductConfigurator> LoadConfiguratorAsync(int configuratorId, CancellationToken ct)
        => await db.ProductConfigurators.AsNoTracking()
               .Include(c => c.BasePart)
               .Include(c => c.Options)
               .FirstOrDefaultAsync(c => c.Id == configuratorId, ct)
           ?? throw new KeyNotFoundException($"Configurator {configuratorId} not found");

    private async Task<(ProductConfiguration Configuration, ProductConfigurator Configurator, CpqEvaluation Evaluation)>
        LoadConfigurationAsync(int configurationId, CancellationToken ct)
    {
        var configuration = await db.ProductConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == configurationId, ct)
            ?? throw new KeyNotFoundException($"Configuration {configurationId} not found");
        var configurator = await LoadConfiguratorAsync(configuration.ConfiguratorId, ct);
        var selections = JsonSerializer.Deserialize<Dictionary<string, string>>(
            string.IsNullOrWhiteSpace(configuration.SelectionsJson) ? "{}" : configuration.SelectionsJson) ?? [];
        var evaluation = CpqEngine.Evaluate(configurator, selections, configuration.Quantity);
        if (!evaluation.IsValid)
            throw new InvalidOperationException(
                "This configuration no longer passes its configurator's rules: " + string.Join(" ", evaluation.Errors));
        return (configuration, configurator, evaluation);
    }

    private async Task<Dictionary<CpqEvaluation.Component, int>> ResolveComponentPartIdsAsync(
        IReadOnlyList<CpqEvaluation.Component> components, CancellationToken ct)
    {
        var ids = components.Where(c => c.PartId is not null).Select(c => c.PartId!.Value).Distinct().ToList();
        var numbers = components.Where(c => c.PartId is null).Select(c => c.PartNumber!).Distinct().ToList();
        var known = await db.Parts.AsNoTracking()
            .Where(p => ids.Contains(p.Id) || numbers.Contains(p.PartNumber))
            .Select(p => new { p.Id, p.PartNumber, p.Status })
            .ToListAsync(ct);

        var map = new Dictionary<CpqEvaluation.Component, int>();
        foreach (var component in components)
        {
            var match = component.PartId is int id
                ? known.FirstOrDefault(p => p.Id == id)
                : known.FirstOrDefault(p => string.Equals(p.PartNumber, component.PartNumber, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new InvalidOperationException(
                    $"Option {component.OptionName} adds part {component.PartNumber ?? component.PartId?.ToString(CultureInfo.InvariantCulture)}, which does not exist. Fix the configurator's BOM impact.");
            if (match.Status == PartStatus.Obsolete)
                throw new InvalidOperationException(
                    $"Option {component.OptionName} adds part {match.PartNumber}, which is obsolete. Fix the configurator's BOM impact.");
            map[component] = match.Id;
        }
        return map;
    }

    private async Task<string> NextPartNumberAsync(string basePartNumber, int configurationId, CancellationToken ct)
    {
        var suffix = $"-C{configurationId.ToString(CultureInfo.InvariantCulture)}";
        var stem = basePartNumber.Length + suffix.Length > PartNumberMaxLength
            ? basePartNumber[..(PartNumberMaxLength - suffix.Length)]
            : basePartNumber;
        var candidate = stem + suffix;
        for (var n = 2; await db.Parts.IgnoreQueryFilters().AnyAsync(p => p.PartNumber == candidate, ct); n++)
        {
            var extra = $"{suffix}-{n.ToString(CultureInfo.InvariantCulture)}";
            candidate = (stem.Length + extra.Length > PartNumberMaxLength ? stem[..(PartNumberMaxLength - extra.Length)] : stem) + extra;
        }
        return candidate;
    }

    private async Task<int> QuoteValidDaysAsync(CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(QuoteValidDaysKey, ct);
        return setting is not null && int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days > 0
            ? days
            : DefaultQuoteValidDays;
    }

    private static string DescribeSelections(IReadOnlyDictionary<string, string> selections)
        => string.Join("; ", selections.Select(s => $"{s.Key}: {s.Value}"));

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
