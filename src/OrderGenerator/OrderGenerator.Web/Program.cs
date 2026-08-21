using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using OrderGenerator.Application.DTOs;
using OrderGenerator.Application.Services;
using OrderGenerator.Web.Services;
using Serilog;
using Shared.Infrastructure.Fix;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Find wwwroot: try bin output dir first, then walk up to find project dir
var wwwRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!Directory.Exists(wwwRoot))
{
    var dir = AppContext.BaseDirectory;
    while (dir != null && !Directory.Exists(Path.Combine(dir, "wwwroot")))
        dir = Path.GetDirectoryName(dir);
    if (dir != null)
        wwwRoot = Path.Combine(dir, "wwwroot");
}
if (Directory.Exists(wwwRoot))
    builder.Environment.WebRootPath = wwwRoot;

builder.Services.AddRazorPages();
builder.Services.AddSingleton<ExposureTracker>();
builder.Services.AddSingleton<IdempotencyStore>();
builder.Services.AddSingleton<OrderMetrics>();
builder.Services.AddSingleton<SqliteDatabase>();
builder.Services.AddSingleton<SqliteEventStore>();
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddSingleton<IEventBroker>(sp =>
{
    var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";
    var logger = sp.GetRequiredService<ILogger<KafkaEventBroker>>();
    return new KafkaEventBroker(bootstrapServers, logger);
});
builder.Services.AddSingleton<IFixClient>(sp =>
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "fix_config.cfg");
    var logger = sp.GetRequiredService<ILogger<FixClient>>();
    return new FixClient(configPath, logger);
});
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<IOrderService>(sp => sp.GetRequiredService<OrderService>());
builder.Services.AddHostedService<EventResultConsumerService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("fixed", context =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

app.MapGet("/metrics", (OrderMetrics metrics) => Results.Ok(metrics.GetSnapshot()));

app.MapGet("/orders/{orderId}/status", async (string orderId, IOrderService orderService) =>
{
    var status = await orderService.GetOrderStatusAsync(orderId);
    return status != null
        ? Results.Ok(status)
        : Results.NotFound(new { Error = "Order not found" });
});

app.MapPost("/api/orders", async (
    [FromForm] string Symbol,
    [FromForm] string Side,
    [FromForm] int Quantity,
    [FromForm] decimal Price,
    [FromForm] string? IdempotencyKey,
    IOrderService orderService,
    OrderMetrics metrics) =>
{
    var order = new OrderDto
    {
        Symbol = Symbol,
        Side = Side,
        Quantity = Quantity,
        Price = Price,
        IdempotencyKey = IdempotencyKey
    };

    metrics.RecordSent();
    var result = await orderService.SendOrderAsync(order);
    return Results.Ok(new { orderId = result.ClOrdId });
}).DisableAntiforgery();

var fixClient = app.Services.GetRequiredService<IFixClient>();
fixClient.Connect();

app.Run();
