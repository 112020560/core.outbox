## 1. Modelo de datos

- [x] 1.1 Crear enum `RouteType` (`Event` | `Command`) en `Src/OutboxWorker/Publishers/RouteType.cs`
- [x] 1.2 Actualizar `Src/OutboxWorker/Publishers/PublisherRoute.cs` — agregar propiedades `RouteType RouteType` y `string? Queue`

## 2. Publisher

- [x] 2.1 Actualizar `Src/OutboxWorker/Publishers/RabbitMq/RabbitMqPublisher.cs` — construir URI según `RouteType`: `queue:{Queue}` para `Command`, `exchange:{Exchange}?type=fanout&routingKey={RoutingKey}` para `Event`

## 3. Configuración

- [x] 3.1 Actualizar `Src/OutboxWorker/appsettings.json` — agregar `"RouteType": "Event"` a todos los publishers existentes
- [x] 3.2 Actualizar `Src/OutboxWorker/appsettings.json` — cambiar `CustomerCreated` a `"RouteType": "Command"`, `"Queue": "credit-service-customer-events"` (eliminar `Exchange` y `RoutingKey`)

## 4. Validación de startup

- [x] 4.1 Actualizar la validación de publishers en `Src/OutboxWorker/Program.cs` — routes de tipo `Event` deben tener `Exchange` no vacío; routes de tipo `Command` deben tener `Queue` no vacía

## 5. Type Registry

- [x] 5.1 Crear `Src/OutboxWorker/Publishers/IMessageTypeRegistry.cs` — interfaz con `Type? Resolve(string eventType)`
- [x] 5.2 Crear `Src/OutboxWorker/Publishers/SharedKernelTypeRegistry.cs` — implementación que escanea el assembly `SharedKernel` por reflection y construye un diccionario `Name → Type` de todos los tipos en namespace `SharedKernel.Contracts.*`
- [x] 5.3 Actualizar `Src/OutboxWorker/Publishers/RabbitMq/RabbitMqPublisher.cs` — inyectar `IMessageTypeRegistry`; si el tipo se resuelve, deserializar payload y enviar con `endpoint.Send(message, type, ct)`; si no se resuelve, enviar `RawJsonMessage` como fallback
- [x] 5.4 Registrar `IMessageTypeRegistry` como singleton en `Src/OutboxWorker/Program.cs`
