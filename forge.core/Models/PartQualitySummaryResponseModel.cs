namespace Forge.Core.Models;

public record PartQualitySummaryResponseModel(
    int PartId,
    List<PartQualityInspectionModel> RecentInspections,
    List<PartQualityNcrModel> OpenNcrs,
    List<PartQualityLotModel> Lots,
    List<PartQualityTemplateModel> InspectionTemplates,
    int SpcCharacteristicCount);
