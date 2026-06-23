# SmartCore.Outbox — Guía de integración

Este documento explica cómo integrar el NuGet `SmartCore.Outbox` en un microservicio para publicar eventos y comandos de forma confiable usando el patrón Transactional Outbox.

---

## Conceptos clave

| Concepto | Descripción |
|---|---|
| **Event** | Notificación broadcast. Se publica a un fanout exchange en RabbitMQ. Múltiples consumers pueden suscribirse. |
| **Command** | Mensaje punto-a-punto. Se envía directamente a una queue. Un único consumer lo recibe. |

La diferencia de routing la maneja el **OutboxWorker** según la configuración en su `appsettings.json`. El microservicio que usa este NuGet **no necesita saber** si el mensaje es un Event o Command — simplemente escribe en el outbox.

---

## Instalación

```xml
<PackageReference Include="SmartCore.Outbox" Version="*" />
```

> El paquete está publicado en GitHub Packages (organización SmartCore).

---

## Setup

### 1. Connection string

La base de datos del outbox es **exclusiva** — nunca compartida con la DB del dominio.

```json
// appsettings.json
{
  "SmartOutbox": {
    "ConnectionString": "Host=localhost;Database=outbox_db;Username=app;Password=secret",
    "ServiceName": "crm-service"
  }
}
```

### 2. Registro de servicios

```csharp
// Program.cs
builder.Services.AddSmartOutbox(options =>
{
    options.ConnectionString = builder.Configuration["SmartOutbox:ConnectionString"]!;
    options.ServiceName = builder.Configuration["SmartOutbox:ServiceName"]!;
});
```

Esto registra automáticamente:
- `IOutboxWriter` (scoped) — para escribir eventos
- `IIdempotencyGuard` (scoped) — para deduplicar en consumers
- `OutboxDbContext` con migraciones automáticas al iniciar

---

## Uso: publicar un Event (broadcast)

Los eventos de dominio se publican a un fanout exchange. Todos los servicios suscritos al exchange los reciben.

```csharp
public class CustomerService
{
    private readonly IOutboxWriter _outbox;
    private readonly AppDbContext _db;

    public CustomerService(IOutboxWriter outbox, AppDbContext db)
    {
        _outbox = outbox;
        _db = db;
    }

    public async Task CreateCustomerAsync(CreateCustomerCommand cmd, CancellationToken ct)
    {
        var customer = new Customer(cmd.Name, cmd.Email);
        _db.Customers.Add(customer);

        var outboxEvent = new OutboxEvent
        {
            ServiceName    = "crm-service",
            AggregateId    = customer.Id,
            AggregateType  = "Customer",
            EventType      = "CustomerUpdated",           // nombre del tipo en SharedKernel.Contracts
            DeduplicationKey = $"customer-updated-{customer.Id}-{cmd.Version}",
            Payload        = JsonSerializer.Serialize(new
            {
                CustomerId = customer.Id,
                Name       = customer.Name,
                Email      = customer.Email
            })
        };

        await _outbox.AppendAsync(outboxEvent, ct);

        // Ambas operaciones en la misma transaccion de EF Core
        await _db.SaveChangesAsync(ct);
    }
}
```

El OutboxWorker enrutara `CustomerUpdated` al exchange configurado:

```json
// OutboxWorker/appsettings.json (referencia — no modificar en el microservicio)
"CustomerUpdated": { "RouteType": "Event", "Exchange": "crm.events", "RoutingKey": "customer.updated" }
```

---

## Uso: publicar un Command (punto-a-punto)

Los commands se envian a una queue directa. Solo un consumer los recibe. Util para orquestar procesos entre servicios.

```csharp
public class CreditApplicationService
{
    private readonly IOutboxWriter _outbox;
    private readonly AppDbContext _db;

    public CreditApplicationService(IOutboxWriter outbox, AppDbContext db)
    {
        _outbox = outbox;
        _db = db;
    }

    public async Task SubmitApplicationAsync(Guid applicationId, CancellationToken ct)
    {
        var application = await _db.CreditApplications.FindAsync(applicationId, ct);
        application.Submit();

        var outboxEvent = new OutboxEvent
        {
            ServiceName      = "crm-service",
            AggregateId      = application.Id,
            AggregateType    = "CreditApplication",
            EventType        = "CustomerCreated",         // nombre del tipo en SharedKernel.Contracts
            DeduplicationKey = $"customer-created-{application.CustomerId}",
            Payload          = JsonSerializer.Serialize(new
            {
                CustomerId = application.CustomerId,
                Name       = application.CustomerName,
                Email      = application.CustomerEmail
            })
        };

        await _outbox.AppendAsync(outboxEvent, ct);

        await _db.SaveChangesAsync(ct);
    }
}
```

El OutboxWorker enrutara `CustomerCreated` directo a la queue del servicio destino:

```json
// OutboxWorker/appsettings.json (referencia — no modificar en el microservicio)
"CustomerCreated": { "RouteType": "Command", "Queue": "credit-service-customer-events" }
```

> **Importante**: la queue `credit-service-customer-events` debe estar declarada por el consumer (el servicio de credito) antes de que el worker intente enviar. Si la queue no existe, el envio fallara.

---

## TryAppendAsync — insercion idempotente

Usa `TryAppendAsync` cuando el evento puede haberse insertado antes (por retry de la operacion de negocio). Devuelve `false` sin lanzar excepcion si la `DeduplicationKey` ya existe.

```csharp
var inserted = await _outbox.TryAppendAsync(outboxEvent, ct);
if (!inserted)
{
    _logger.LogWarning("Evento duplicado ignorado: {Key}", outboxEvent.DeduplicationKey);
}
await _db.SaveChangesAsync(ct);
```

Usa `AppendAsync` cuando una duplicacion es un error real y debe fallar la operacion.

---

## Atomicidad con EF Core

El outbox y el dominio deben compartir la **misma transaccion**. La forma mas simple es usar `SaveChangesAsync` una sola vez al final, cuando `OutboxDbContext` y el `DbContext` del dominio estan en el mismo scope de DI y usan la misma conexion.

Si usan conexiones separadas, usa `TransactionScope` o coordina con `IDbContextTransaction`:

```csharp
await using var tx = await _domainDb.Database.BeginTransactionAsync(ct);

_domainDb.Products.Add(product);
await _domainDb.SaveChangesAsync(ct);

await _outbox.AppendAsync(outboxEvent, ct);
await _outboxDb.SaveChangesAsync(ct);  // mismo OutboxDbContext inyectado

await tx.CommitAsync(ct);
```

---

## Agregar un nuevo EventType al Worker

Cada `EventType` que el microservicio publique debe estar registrado en el `appsettings.json` del OutboxWorker. Sin esa entrada, el worker no sabra a donde enrutar el mensaje.

### Para un Event:

```json
"MiNuevoEvento": {
  "RouteType": "Event",
  "Exchange": "mi-dominio.events",
  "RoutingKey": "mi-nuevo-evento.ocurrido"
}
```

### Para un Command:

```json
"MiNuevoComando": {
  "RouteType": "Command",
  "Queue": "servicio-destino-queue"
}
```

El `EventType` debe coincidir exactamente con el nombre del tipo en `SharedKernel.Contracts` (ej: `CustomerCreated`, `IProductUpdated`).

---

## IIdempotencyGuard — para consumers

Si tu servicio tambien **consume** eventos de otros servicios via RabbitMQ, usa `IIdempotencyGuard` para evitar procesar el mismo evento dos veces (en caso de redelivery):

```csharp
public class CustomerCreatedConsumer : IConsumer<CustomerCreated>
{
    private readonly IIdempotencyGuard _guard;

    public CustomerCreatedConsumer(IIdempotencyGuard guard) => _guard = guard;

    public async Task Consume(ConsumeContext<CustomerCreated> context)
    {
        var eventId = context.Message.EventId;
        const string consumer = "credit-service-customer-created";

        if (await _guard.AlreadyProcessedAsync(eventId, consumer))
            return;

        // logica de negocio...

        await _guard.MarkAsProcessedAsync(eventId, consumer);
    }
}
```

---

## Resumen de la API publica

```csharp
// Escribir en el outbox (lanza si DeduplicationKey duplicada)
Task AppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default);

// Escribir en el outbox (retorna false si DeduplicationKey duplicada, sin lanzar)
Task<bool> TryAppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default);

// Verificar si el evento ya fue procesado por este consumer
Task<bool> AlreadyProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default);

// Marcar evento como procesado
Task MarkAsProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default);
```
