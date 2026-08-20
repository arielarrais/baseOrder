using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using OrderGenerator.Application.Services;
using OrderGenerator.Web.Services;
using Serilog;
using Shared.Infrastructure.Fix;

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
builder.Services.AddSingleton<IFixClient>(sp =>
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "fix_config.cfg");
    var logger = sp.GetRequiredService<ILogger<FixClient>>();
    return new FixClient(configPath, logger);
});
builder.Services.AddScoped<IOrderService, OrderService>();

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

var fixClient = app.Services.GetRequiredService<IFixClient>();
fixClient.Connect();

app.Run();
