namespace Forge.Core.Models;

public record SaveFormDataRequestModel(string FormDataJson, int? FormDefinitionVersionId = null);
