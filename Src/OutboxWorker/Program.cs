using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OutboxWorker;
using OutboxWorker.Configuration;
using OutboxWorker.Publishers;
using SmartCore.Outbox.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services
    .AddOptions<OutboxWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Outbox"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection("RabbitMq"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<Dictionary<string, PublisherRoute>>()
    .Bind(builder.Configuration.GetSection("Publishers"));

// ── Publisher routes startup validation ─────────────────────────────────────
var routes = builder.Configuration.GetSection("Publishers")
    .Get<Dictionary<string, PublisherRoute>>() ?? [];

foreach (var (eventType, route) in routes)
{
    if (route.RouteType == RouteType.Command)
    {
        if (string.IsNullOrWhiteSpace(route.Queue))
            throw new InvalidOperationException(
                $"Publisher route for '{eventType}' is type Command but has an empty Queue.");
    }
    else
    {
        if (string.IsNullOrWhiteSpace(route.Exchange))
            throw new InvalidOperationException(
                $"Publisher route for '{eventType}' is type Event but has an empty Exchange.");
    }
}

// ── Database ─────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration["Outbox:ConnectionString"]
    ?? throw new InvalidOperationException("Outbox:ConnectionString is required.");

builder.Services.AddDbContext<OutboxDbContext>(db => db.UseNpgsql(connectionString));

// ── MassTransit / RabbitMQ ───────────────────────────────────────────────────
var rabbitMqUri = builder.Configuration["RabbitMq:Uri"]
    ?? throw new InvalidOperationException("RabbitMq:Uri is required.");

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(new Uri(rabbitMqUri));
        cfg.ConfigureEndpoints(ctx);
    });
});

// ── Worker dependencies ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IMessageTypeRegistry, SharedKernelTypeRegistry>();
builder.Services.AddScoped<ClaimManager>();
builder.Services.AddScoped<IPublisherFactory, ConfigurationPublisherFactory>();
builder.Services.AddHostedService<OutboxProcessor>();

var host = builder.Build();
host.Run();
