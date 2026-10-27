using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.CloudStorage;

public record GetCloudStorageProviderTypesQuery : IRequest<List<CloudStorageProviderTypeModel>>;

public class GetCloudStorageProviderTypesHandler(
    ICapabilitySnapshotProvider capabilities,
    ICloudStorageResolver resolver) : IRequestHandler<GetCloudStorageProviderTypesQuery, List<CloudStorageProviderTypeModel>>
{
    public Task<List<CloudStorageProviderTypeModel>> Handle(GetCloudStorageProviderTypesQuery request, CancellationToken ct)
    {
        var registered = resolver.RegisteredProviderCodes;
        var rows = CloudStorageProviderCodes.CapabilityByCode
            .Select(kv => new CloudStorageProviderTypeModel(
                kv.Key,
                kv.Value,
                kv.Value is null || capabilities.IsEnabled(kv.Value),
                registered.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        return Task.FromResult(rows);
    }
}
