namespace Forge.Core.Models;

/// <summary>What a dump of this database would contain — shown before dumping/importing.</summary>
public record DatabaseTransferSummaryModel(
    string DatabaseName,
    int TableCount,
    long EstimatedRows,
    long TotalBytes,
    List<DumpTableInfoModel> Tables);
