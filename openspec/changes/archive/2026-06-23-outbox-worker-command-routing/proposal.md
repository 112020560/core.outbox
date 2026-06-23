## Why

El `OutboxWorker` actualmente solo soporta **broadcast** (fanout exchange), por lo que no puede enrutar mensajes punto-a-punto a una queue específica. Algunos `EventType` en el sistema son en realidad **commands** que deben ser consumidos por un único servicio (ej. `CustomerCreated` → `credit-service-customer-events`), y el modelo fanout no aplica para estos casos.

## What Changes

- `PublisherRoute` pasa de tener solo `Exchange`/`RoutingKey` a incluir `RouteType` (enum: `Event` | `Command`) y `Queue` (opcional).
- `RabbitMqPublisher` construye la URI según `RouteType`: `exchange:...?type=fanout` para eventos, `queue:...` para commands.
- `appsettings.json` se actualiza para incluir `RouteType` en todas las rutas existentes y agregar `CustomerCreated` como `Command` con `Queue: "credit-service-customer-events"`.
- La validación de startup en `Program.cs` se actualiza: commands requieren `Queue` no vacío, events requieren `Exchange` no vacío.

## Capabilities

### New Capabilities

- `command-routing`: Soporte para enrutar `EventType` de tipo command directamente a una queue RabbitMQ (punto-a-punto), diferenciado del enrutamiento broadcast existente mediante un campo `RouteType` en la configuración de publishers.

### Modified Capabilities

*(ninguna — no hay specs existentes en `openspec/specs/` todavía)*

## Impact

- `Src/OutboxWorker/Publishers/PublisherRoute.cs` — nuevo campo `RouteType` y `Queue`
- `Src/OutboxWorker/Publishers/RabbitMq/RabbitMqPublisher.cs` — lógica de URI condicional
- `Src/OutboxWorker/Program.cs` — validación de startup actualizada
- `Src/OutboxWorker/appsettings.json` — todas las rutas actualizadas con `RouteType`, `CustomerCreated` cambia a `Command`
- Sin cambios en `SmartCore.Outbox` (NuGet), `OutboxDbContext`, ni contratos de SharedKernel
