using MaintainXpert.Api.Endpoints;
using MaintainXpert.Api.Infrastructure;
using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Infrastructure;
using MaintainXpert.Maintenance.Application;
using MaintainXpert.Maintenance.Domain.Events;
using MaintainXpert.Maintenance.Infrastructure;
using MaintainXpert.Notifications.Application;
using MaintainXpert.Notifications.Infrastructure;
using MaintainXpert.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IWorkOrderRepository, InMemoryWorkOrderRepository>();
builder.Services.AddSingleton<IAssetRepository, InMemoryAssetRepository>();
builder.Services.AddSingleton<INotificationSink, ConsoleNotificationSink>();

builder.Services.AddScoped<IDomainEventDispatcher, InProcessDomainEventDispatcher>();
builder.Services.AddScoped<WorkOrderService>();

builder.Services.AddScoped<IDomainEventHandler<WorkOrderCreated>, WorkOrderCreatedNotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<WorkOrderCompleted>, WorkOrderCompletedHandler>();

var app = builder.Build();

app.MapGroup("/assets").MapAssetEndpoints();
app.MapGroup("/work-orders").MapWorkOrderEndpoints();

app.Run();
