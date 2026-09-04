using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Domain;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Api.Endpoints;

public static class AssetEndpoints
{
    public static RouteGroupBuilder MapAssetEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (RegisterAssetRequest request, IAssetRepository repository) =>
        {
            var asset = Asset.Register(request.Name);
            await repository.AddAsync(asset);
            return Results.Created($"/assets/{asset.Id}", ToResponse(asset));
        });

        group.MapGet("/{id:guid}", async (Guid id, IAssetRepository repository) =>
        {
            var asset = await repository.GetByIdAsync(new AssetId(id));
            return asset is null ? Results.NotFound() : Results.Ok(ToResponse(asset));
        });

        return group;
    }

    private static AssetResponse ToResponse(Asset asset) => new(
        asset.Id.Value,
        asset.Name,
        asset.Status.ToString(),
        asset.LastMaintenanceCompletedAt);
}

public sealed record RegisterAssetRequest(string Name);

public sealed record AssetResponse(Guid Id, string Name, string Status, DateTimeOffset? LastMaintenanceCompletedAt);
