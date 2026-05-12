namespace Forge.Api.Capabilities.Discovery;

/// <summary>
/// Phase 4 Phase-F — Discovery question catalog entry. Stable shape used by
/// both the wizard (UI rendering) and the recommendation engine (input).
/// One row per question; the catalog is static (encoded in
/// <see cref="DiscoveryQuestionCatalog"/>) so consultants can rely on the
/// numbering / ordering / branching being identical install-to-install.
///
/// Per 4C decision #1, free-text questions (Q-O2 walkthrough, Q-O6 audit,
/// Q-V1 worst-case, Q-V2 unusual) are captured verbatim — they appear in
/// the rationale paragraph but the engine does NOT branch on them.
/// </summary>
public record DiscoveryQuestion(
    string Id,
    DiscoveryStage Stage,
    DiscoveryCategory Category,
    DiscoveryQuestionType Type,
    string Text,
    string WhyAsking,
    IReadOnlyList<DiscoveryChoice>? Choices = null,
    string? Branch = null,
    bool VisibleInSelfServe = true,
    bool VisibleInConsultant = true);

/// <summary>Single multiple-choice / radio option for a discovery question.</summary>
public record DiscoveryChoice(string Value, string Label);

/// <summary>
/// Stage = the macro section of the wizard.
/// </summary>
public enum DiscoveryStage
{
    Opening = 0,
    BranchA = 1,
    BranchB = 2,
    BranchC = 3,
    Override = 4,
    Diagnostic = 5,
    Exit = 6,
}

/// <summary>
/// Category = the role the question plays in the recommendation algorithm.
/// </summary>
public enum DiscoveryCategory
{
    Opening = 0,
    BranchSpecific = 1,
    Override = 2,
    Diagnostic = 3,
    Exit = 4,
    /// <summary>Per 4C decision #6 — surfaced only in consultant mode.</summary>
    ConsultantDeepdive = 5,
}

/// <summary>
/// Question type drives the UI rendering (radio set vs free text vs yes/no
/// vs single-numeric-bucket) and the engine's parsing.
/// </summary>
public enum DiscoveryQuestionType
{
    SingleChoice = 0,
    MultiChoice = 1,
    YesNo = 2,
    /// <summary>Bucketed numeric (radio set over fixed buckets, per 4C decision #3).</summary>
    Bucketed = 3,
    /// <summary>Free-text — captured verbatim, NOT parsed (per 4C decision #1).</summary>
    FreeText = 4,
    /// <summary>Yes/no with optional follow-up qualifier (e.g. cert sub-answer on Q-O4).</summary>
    YesNoWithDetail = 5,
}
