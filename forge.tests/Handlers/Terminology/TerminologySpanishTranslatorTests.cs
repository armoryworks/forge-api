using FluentAssertions;
using Moq;

using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Presets.Apply.Layers;
using Forge.Api.Features.Terminology;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Terminology;

public class TerminologySpanishTranslatorTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<II18nTranslationService> _translator = new();

    private void Translates(string english, string? spanish) =>
        _translator.Setup(t => t.TranslateLabelAsync($"the {english}", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync(spanish is null ? I18nTranslationResult.Unavailable : I18nTranslationResult.Translated(spanish));

    private string? Label(string key) => _db.TerminologyEntries.SingleOrDefault(e => e.Key == key)?.Label;

    [Fact]
    public async Task A_preset_rename_gets_machine_translated_spanish()
    {
        var preset = PresetCatalog.All.Single(p => p.Id == "PRESET-08");
        await TerminologyBundleApplier.ApplyAsync(preset.TerminologyBundle!, _db, preset.Id, CancellationToken.None);
        await _db.SaveChangesAsync();
        Translates("Engagement", "el Compromiso");
        Translates("Engagements", "los Compromisos");

        var untranslated = await new TerminologySpanishTranslator(_db, _translator.Object)
            .TranslateChangedTermsAsync([.. preset.TerminologyBundle!.Labels.Keys], CancellationToken.None);

        Label("entity_job:es").Should().Be("Compromiso");
        Label("label_jobs:es").Should().Be("Compromisos");
        Label("gender_job:es").Should().Be("m");
        _db.TerminologyEntries.Single(e => e.Key == "entity_job:es").SourcePresetId
            .Should().Be(TerminologyEntry.MachineTranslationSource);
        untranslated.Should().NotContain("job");
    }

    [Fact]
    public async Task Admin_typed_spanish_survives_a_preset_translation()
    {
        _db.TerminologyEntries.AddRange(
            new TerminologyEntry { Key = "entity_job", Label = "Engagement", SourcePresetId = "PRESET-08" },
            new TerminologyEntry { Key = "label_jobs", Label = "Engagements", SourcePresetId = "PRESET-08" },
            new TerminologyEntry { Key = "entity_job:es", Label = "Encargo", IsAdminEdited = true });
        await _db.SaveChangesAsync();

        await new TerminologySpanishTranslator(_db, _translator.Object)
            .TranslateChangedTermsAsync(["entity_job", "label_jobs"], CancellationToken.None);

        Label("entity_job:es").Should().Be("Encargo");
        _translator.Verify(t => t.TranslateLabelAsync(It.IsAny<string>(), "en", "es", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Decomposed_accents_are_stored_composed()
    {
        _db.TerminologyEntries.AddRange(
            new TerminologyEntry { Key = "entity_customer", Label = "Company", IsAdminEdited = true },
            new TerminologyEntry { Key = "label_customers", Label = "Companies", IsAdminEdited = true });
        await _db.SaveChangesAsync();
        Translates("Company", "la Compañía");
        Translates("Companies", "las Compañías");

        await new TerminologySpanishTranslator(_db, _translator.Object)
            .TranslateChangedTermsAsync(["entity_customer"], CancellationToken.None);

        Label("entity_customer:es").Should().Be("Compañía");
        Label("label_customers:es").Should().Be("Compañías");
        Label("gender_customer:es").Should().Be("f");
    }

    [Theory]
    [InlineData("Canned assistant answer. This response is fixed for testing")]
    [InlineData("Here is the translation: el Compromiso")]
    [InlineData("el Compromiso que se usa en las órdenes de trabajo de la planta principal")]
    public async Task A_reply_that_is_not_a_noun_is_left_untranslated(string reply)
    {
        _db.TerminologyEntries.AddRange(
            new TerminologyEntry { Key = "entity_job", Label = "Engagement", IsAdminEdited = true },
            new TerminologyEntry { Key = "label_jobs", Label = "Engagements", IsAdminEdited = true });
        await _db.SaveChangesAsync();
        Translates("Engagement", reply);
        Translates("Engagements", "los Compromisos");

        var untranslated = await new TerminologySpanishTranslator(_db, _translator.Object)
            .TranslateChangedTermsAsync(["entity_job", "label_jobs"], CancellationToken.None);

        untranslated.Should().Contain("job");
        Label("entity_job:es").Should().BeNullOrEmpty();
        Label("label_jobs:es").Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task A_rejected_reply_does_not_stop_the_next_term()
    {
        _db.TerminologyEntries.AddRange(
            new TerminologyEntry { Key = "entity_job", Label = "Engagement", IsAdminEdited = true },
            new TerminologyEntry { Key = "label_jobs", Label = "Engagements", IsAdminEdited = true },
            new TerminologyEntry { Key = "entity_customer", Label = "Company", IsAdminEdited = true },
            new TerminologyEntry { Key = "label_customers", Label = "Companies", IsAdminEdited = true });
        await _db.SaveChangesAsync();
        _translator.Setup(t => t.TranslateLabelAsync("the Engagement", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Rejected);
        Translates("Engagements", "los Compromisos");
        Translates("Company", "la Compañía");
        Translates("Companies", "las Compañías");

        var untranslated = await new TerminologySpanishTranslator(_db, _translator.Object)
            .TranslateChangedTermsAsync(["entity_job", "label_jobs", "entity_customer", "label_customers"], CancellationToken.None);

        untranslated.Should().Contain("job").And.NotContain("customer");
        Label("entity_job:es").Should().BeNullOrEmpty();
        Label("entity_customer:es").Should().Be("Compañía");
    }
}
