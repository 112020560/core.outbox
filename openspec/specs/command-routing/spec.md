## ADDED Requirements

### Requirement: PublisherRoute soporta tipo de ruta

`PublisherRoute` SHALL incluir una propiedad `RouteType` de tipo enum (`Event` | `Command`) y una propiedad opcional `Queue` de tipo `string?`.

#### Scenario: Ruta de tipo Event tiene Exchange
- **WHEN** una `PublisherRoute` tiene `RouteType = Event`
- **THEN** SHALL tener `Exchange` no vacío y `Queue` puede ser nulo

#### Scenario: Ruta de tipo Command tiene Queue
- **WHEN** una `PublisherRoute` tiene `RouteType = Command`
- **THEN** SHALL tener `Queue` no vacío y `Exchange` puede ser nulo

---

### Requirement: RabbitMqPublisher construye URI según RouteType

El publisher SHALL construir la URI de destino en RabbitMQ basándose en el `RouteType` de la ruta configurada.

#### Scenario: Event publica a fanout exchange
- **WHEN** el `RouteType` de la ruta es `Event`
- **THEN** la URI SHALL ser `exchange:{Exchange}?type=fanout&routingKey={RoutingKey}`

#### Scenario: Command envía a queue directa
- **WHEN** el `RouteType` de la ruta es `Command`
- **THEN** la URI SHALL ser `queue:{Queue}`

---

### Requirement: Validación de startup distingue tipos de ruta

La validación de publishers en `Program.cs` SHALL verificar campos requeridos según el `RouteType`.

#### Scenario: Event inválido por Exchange vacío
- **WHEN** una ruta de tipo `Event` tiene `Exchange` vacío o nulo
- **THEN** startup SHALL lanzar `InvalidOperationException` con mensaje que identifica el EventType

#### Scenario: Command inválido por Queue vacía
- **WHEN** una ruta de tipo `Command` tiene `Queue` vacía o nula
- **THEN** startup SHALL lanzar `InvalidOperationException` con mensaje que identifica el EventType

---

### Requirement: CustomerCreated configurado como Command

El `EventType` `CustomerCreated` SHALL estar configurado en `appsettings.json` con `RouteType: Command` y `Queue: credit-service-customer-events`.

#### Scenario: CustomerCreated se enruta a queue directa
- **WHEN** el worker procesa un evento con `EventType = "CustomerCreated"`
- **THEN** SHALL enviarlo a la queue `credit-service-customer-events` (punto-a-punto)
