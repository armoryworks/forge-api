using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SerialGenealogyResponseModel(
    int Id,
    string SerialValue,
    string PartNumber,
    SerialNumberStatus Status,
    List<SerialGenealogyResponseModel> Children);
