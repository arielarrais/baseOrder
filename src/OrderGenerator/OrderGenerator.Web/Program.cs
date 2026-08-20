using OrderGenerator.Application.Services;
using OrderGenerator.Web.Services;
using Shared.Infrastructure.Fix;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<IFixClient>(sp =>
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "fix_config.cfg");
    return new FixClient(configPath);
});
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

var fixClient = app.Services.GetRequiredService<IFixClient>();
fixClient.Connect();

app.Run();
