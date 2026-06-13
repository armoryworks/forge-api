namespace Forge.Core.Models;

/// <summary>⚡ EDI BOUNDARY — outcome of a CSV part-number-map import (PartnerPartNumber,OurPartNumber).</summary>
public record EdiPartNumberMapImportResultModel(
    int Imported,
    int Updated,
    int Skipped,
    int Unresolved,
    int TotalRows);
