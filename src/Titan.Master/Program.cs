using Serilog;
using StackExchange.Redis;
using Titan.Master.Services;
using Titan.Shared.Protos;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Configure Redis
// "Do NOT use Magic Strings. Use appsettings.json"
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

// 3. Register Services
builder.Services.AddSingleton<JobQueueService>();
builder.Services.AddGrpc();

var app = builder.Build();

// 4. Map gRPC Service
app.MapGrpcService<TitanJobService>();

// 5. Map REST API
app.MapPost("/api/jobs", async (JobRequest request, JobQueueService queueService) =>
{
    if (string.IsNullOrEmpty(request.JobId))
    {
        request.JobId = Guid.NewGuid().ToString();
    }
    
    await queueService.EnqueueJobAsync(request);
    return Results.Accepted($"/api/jobs/{request.JobId}", new { request.JobId, Status = "Queued" });
});

app.MapGet("/", () => "Titan Master is running...");

app.Run();
