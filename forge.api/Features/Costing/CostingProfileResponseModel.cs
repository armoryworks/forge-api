namespace Forge.Api.Features.Costing;

/// <summary>Active costing profile for the Tier 2 admin panel: the mode plus its departmental rate grid.</summary>
public record CostingProfileResponseModel(string Mode, List<DepartmentalRateModel> DepartmentalRates);

/// <summary>One departmental overhead rate: a percentage of direct labor applied to a work center's operations.</summary>
public record DepartmentalRateModel(int WorkCenterId, decimal RatePct);
