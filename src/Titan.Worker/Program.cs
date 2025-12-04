using Serilog;
using Titan.Worker;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
