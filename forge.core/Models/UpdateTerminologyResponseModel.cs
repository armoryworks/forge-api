namespace Forge.Core.Models;

public record UpdateTerminologyResponseModel(List<TerminologyEntryResponseModel> Entries, List<string> UntranslatedTerms);
