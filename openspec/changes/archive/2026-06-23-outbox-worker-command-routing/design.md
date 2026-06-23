## Context

El `OutboxWorker` usa `IPublisherFactory` → `RabbitMqPublisher` para enrutar eventos al broker. Actualmente `PublisherRoute` solo contiene `Exchange` y `RoutingKey`, y `RabbitMqPublisher` siempre construye una URI de tipo fanout exchange. Esto funciona para eventos de dominio (broadcast), pero no para commands que deben llegar a un único consumer a través de una queue directa.

## Goals / Non-Goals

**Goals:**
- Agregar `RouteType` (enum `Event` | `Command`) a `PublisherRoute`
- Agregar `Queue` (string opcional) a `PublisherRoute`
- `RabbitMqPublisher` construye URI según `RouteType`: `exchange:...?type=fanout` para `Event`, `queue:...` para `Command`
- Validación de startup distingue entre ambos tipos (commands requieren `Queue`, events requieren `Exchange`)
- Configurar `CustomerCreated` como `Command` con queue `credit-service-customer-events`

**Non-Goals:**
- Soporte para otros tipos de exchange (direct, topic) — se puede agregar después
- Deserialización del payload para commands — se mantiene el forward de raw JSON
- Cambios en `SmartCore.Outbox` (NuGet)

## Decisions

### 1. Enum `RouteType` en lugar de booleano `IsCommand`

**Decisión**: Usar `public enum RouteType { Event, Command }` como propiedad de `PublisherRoute`.

**Alternativa**: `bool IsCommand` o dos clases derivadas de `PublisherRoute`.

**Razón**: El enum es extensible (se puede agregar `Topic`, `Direct` en el futuro), más legible en `appsettings.json` como string, y evita herencia innecesaria para una distinción tan simple.

### 2. URI de MassTransit por tipo

- **Event** → `exchange:{Exchange}?type=fanout&routingKey={RoutingKey}` (comportamiento actual)
- **Command** → `queue:{Queue}` (MassTransit enruta directo a la queue)

**Razón**: MassTransit soporta ambas URIs nativamente con `ISendEndpointProvider.GetSendEndpoint`. No requiere cambios en la configuración del bus.

### 3. Validación de startup por tipo

```
RouteType.Event  → Exchange no vacío (RoutingKey puede ser vacío)
RouteType.Command → Queue no vacía (Exchange y RoutingKey ignorados)
```

**Razón**: Falla rápido en startup en lugar de fallar al intentar procesar el primer evento.

## Risks / Trade-offs

- [Risk] Commands van a una queue que debe existir en RabbitMQ antes de que el worker la use → Mitigation: los consumers deben declarar la queue al arrancar; documentar en README.
- [Risk] `appsettings.json` debe actualizarse para todos los routes existentes con `"RouteType": "Event"` → Mitigation: incluirlo en tasks como paso explícito; sin default para forzar decisión consciente.
