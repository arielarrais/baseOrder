using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderAccumulator.Application.Handlers;
using OrderAccumulator.Application.Interfaces;
using OrderAccumulator.Application.Services;
using OrderAccumulator.Domain.Interfaces;
using OrderAccumulator.Infrastructure.Fix;
using OrderAccumulator.Infrastructure.Persistence;
using OrderAccumulator.Worker;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()
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
    .Build();

await host.RunAsync();
