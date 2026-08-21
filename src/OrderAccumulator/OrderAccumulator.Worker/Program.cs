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
using Serilog.Events;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .WriteTo.Console()
    .WriteTo.File("logs/order-accumulator-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

try
{
    Log.Information("Starting OrderAccumulator Worker...");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((hostContext, services) =>
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "fix_config.cfg");

            services.AddSingleton<IExposureRepository, ExposureRepository>();
            services.AddSingleton<IExposureService, ExposureService>();
            services.AddSingleton<IOrderHandler, OrderHandler>();
            services.AddSingleton<SqliteDatabase>();
            services.AddSingleton<SqliteEventStore>();
            services.AddHostedService<OutboxDispatcherService>();
            services.AddSingleton<IEventBroker>(sp =>
            {
                var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";
                var logger = sp.GetRequiredService<ILogger<KafkaEventBroker>>();
                return new KafkaEventBroker(bootstrapServers, logger);
            });
            services.AddSingleton(sp =>
            {
                var orderHandler = sp.GetRequiredService<IOrderHandler>();
                var logger = sp.GetRequiredService<ILogger<FixAccumulator>>();
                return new FixAccumulator(orderHandler, logger, configPath);
            });
            services.AddHostedService<Worker>();
            services.AddHostedService<EventConsumerService>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
