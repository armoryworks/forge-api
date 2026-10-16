namespace Forge.Api.Features.Terminology;

public sealed record TerminologyTerm(string Id, string PluralId)
{
    public string SingularKey => $"entity_{Id}";

    public string PluralKey => $"label_{PluralId}";

    public string GenderKey => $"gender_{Id}";

    public static string ForLanguage(string key, string languageCode) =>
        languageCode == "en" ? key : $"{key}:{languageCode}";
}
