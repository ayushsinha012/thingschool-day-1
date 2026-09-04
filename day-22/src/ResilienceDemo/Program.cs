using Polly;
using ResilienceDemo.Client;
using ResilienceDemo.Demo;
using ResilienceDemo.Downstream;
using ResilienceDemo.Resilience;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext());

builder.Services.AddSingleton<DownstreamState>();
builder.Services.AddSingleton<ResilienceMetrics>();

builder.Services.Configure<OutboundResilienceOptions>(builder.Configuration.GetSection("Resilience"));

builder.Services.AddSingleton(sp =>
{
    var options = builder.Configuration.GetSection("Resilience").Get<OutboundResilienceOptions>() ?? new OutboundResilienceOptions();
    var metrics = sp.GetRequiredService<ResilienceMetrics>();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("ResiliencePipeline");
    return OutboundResiliencePipelineFactory.Create(options, metrics, logger);
});

var downstreamBaseUrl = builder.Configuration["DownstreamBaseUrl"] ?? "http://localhost:5080";

builder.Services.AddHttpClient<OutboundDependencyClient>(client =>
{
    client.BaseAddress = new Uri(downstreamBaseUrl);
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

app.MapDownstreamEndpoints();
app.MapDemoEndpoints();

app.Run();

public partial class Program
{
}
