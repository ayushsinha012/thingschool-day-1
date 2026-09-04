using MaintainXpert.Maintenance.Application;
using MaintainXpert.Maintenance.Domain;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Api.Endpoints;

public static class WorkOrderEndpoints
{
    public static RouteGroupBuilder MapWorkOrderEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateWorkOrderRequest request, WorkOrderService service) =>
        {
            var workOrder = await service.CreateAsync(new AssetId(request.AssetId), request.Description, request.Priority);
            return Results.Created($"/work-orders/{workOrder.Id}", ToResponse(workOrder));
        });

        group.MapGet("/{id:guid}", async (Guid id, IWorkOrderRepository repository) =>
        {
            var workOrder = await repository.GetByIdAsync(new WorkOrderId(id));
            return workOrder is null ? Results.NotFound() : Results.Ok(ToResponse(workOrder));
        });

        group.MapPost("/{id:guid}/assign", async (Guid id, AssignTechnicianRequest request, WorkOrderService service) =>
        {
            var workOrder = await service.AssignTechnicianAsync(new WorkOrderId(id), new TechnicianId(request.TechnicianId));
            return Results.Ok(ToResponse(workOrder));
        });

        group.MapPost("/{id:guid}/start", async (Guid id, WorkOrderService service) =>
        {
            var workOrder = await service.StartAsync(new WorkOrderId(id));
            return Results.Ok(ToResponse(workOrder));
        });

        group.MapPost("/{id:guid}/complete", async (Guid id, WorkOrderService service) =>
        {
            var workOrder = await service.CompleteAsync(new WorkOrderId(id));
            return Results.Ok(ToResponse(workOrder));
        });

        return group;
    }

    private static WorkOrderResponse ToResponse(WorkOrder workOrder) => new(
        workOrder.Id.Value,
        workOrder.AssetId.Value,
        workOrder.Description,
        workOrder.Priority.ToString(),
        workOrder.Status.ToString(),
        workOrder.AssignedTechnicianId?.Value,
        workOrder.CreatedAt);
}

public sealed record CreateWorkOrderRequest(Guid AssetId, string Description, WorkOrderPriority Priority);

public sealed record AssignTechnicianRequest(Guid TechnicianId);

public sealed record WorkOrderResponse(
    Guid Id,
    Guid AssetId,
    string Description,
    string Priority,
    string Status,
    Guid? AssignedTechnicianId,
    DateTimeOffset CreatedAt);
