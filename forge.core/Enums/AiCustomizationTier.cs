namespace Forge.Core.Enums;

/// <summary>
/// ai-fleet-orchestration D-crux: per-client customization depth, hardware-gated and chosen in the
/// onboarding workflow. RAG scaffold is the floor; heavier tiers are opt-in.
/// </summary>
public enum AiCustomizationTier
{
    /// <summary>Tier 0 — RAG scaffold only (index + persona + injectable .md). Fits a shoebox/Pi.</summary>
    Scaffold,

    /// <summary>Tier 1 — RAG + per-client LoRA adapters over a shared base.</summary>
    LoraAdapter,

    /// <summary>Tier 2 — RAG + fuller fine-tune / larger base models.</summary>
    FullFineTune,
}
