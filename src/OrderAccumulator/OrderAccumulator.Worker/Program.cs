using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderAccumulator.Application.Handlers;
using OrderAccumulator.Application.Interfaces;
using OrderAccumulator.Application.Services;
using OrderAccumulator.Domain.Interfaces;
using OrderAccumulator.Infrastructure.Fix;
using OrderAccumulator.Infrastructure.Persistence;
using OrderAccumulator.Worker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "fix_config.cfg");

        services.AddSingleton<IExposureRepository, ExposureRepository>();
        services.AddSingleton<IExposureService, ExposureService>();
        services.AddSingleton<IOrderHandler, OrderHandler>();
        services.AddSingleton(sp =>
        {
            var orderHandler = sp.GetRequiredService<IOrderHandler>();
            var logger = sp.GetRequiredService<ILogger<FixAccumulator>>();
            return new FixAccumulator(orderHandler, logger, configPath);
        });
        services.AddHostedService<Worker>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

await host.RunAsync();
