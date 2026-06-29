namespace Forge.Core.Enums;

public enum TrainingContentType
{
    Article = 0,
    Walkthrough = 2,
    QuickRef = 3,
    Quiz = 4,

    /// <summary>
    /// Advanced / edge-case side documentation — a clearly-delimited reference
    /// note (markdown body, like an Article) collecting a feature's ancillary
    /// fields, power-user details, and admin-config trivia. Kept distinct from the
    /// core Article/Walkthrough/QuickRef/Quiz learning content.
    /// </summary>
    Reference = 5
}
