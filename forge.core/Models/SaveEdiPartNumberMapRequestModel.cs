namespace Forge.Core.Models;

/// <summary>⚡ EDI BOUNDARY — replace a partner's full part-number map with these typed rows.</summary>
public record SaveEdiPartNumberMapRequestModel(List<EdiPartNumberMapRow> Rows);
