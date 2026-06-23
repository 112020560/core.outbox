## ADDED Requirements

### Requirement: RabbitMqPublisher publishes raw JSON payload
`RabbitMqPublisher` SHALL implement `IEventPublisher` and publish `OutboxEvent.Payload` (raw JSON string) as the MassTransit message body to the exchange and routing key defined in the publisher's `PublisherRoute` configuration. The payload SHALL NOT be deserialized before publishing.

#### Scenario: Payload forwarded to correct exchange
- **WHEN** `RabbitMqPublisher.PublishAsync` is called for a `CustomerCreated` event configured with `Exchange = "crm.events"` and `RoutingKey = "customer.created"`
- **THEN** the raw JSON payload is published to the `crm.events` exchange with routing key `customer.created`

#### Scenario: Broker unavailable throws
- **WHEN** RabbitMQ is unreachable and `PublishAsync` is called
- **THEN** a `MassTransitException` or connection exception propagates to the caller (OutboxProcessor handles retry)

### Requirement: Per-EventType route configuration
Each `PublisherRoute` SHALL carry `Exchange` (string, required) and `RoutingKey` (string, required). Missing or empty values SHALL cause a configuration validation error on worker startup.

#### Scenario: Valid route configuration accepted
- **WHEN** `appsettings.json` defines `"CustomerCreated": { "Exchange": "crm.events", "RoutingKey": "customer.created" }`
- **THEN** the worker starts and routes `CustomerCreated` events to that exchange

#### Scenario: Empty Exchange fails startup validation
- **WHEN** a publisher route entry has an empty `Exchange` value
- **THEN** the worker fails to start with a descriptive configuration error

### Requirement: Known EventType routing table
The worker's initial `appsettings.json` SHALL include routes for all known SharedKernel contracts:
- CRM: `CustomerCreated`, `CustomerUpdated`, `ProspectConverted`, `CreditApplicationCreated`, `CreditApplicationSubmitted`, `RiskEvaluationCompleted`, `CreditApplicationApproved`, `CreditApplicationRejected` → `crm.events`
- Payments: `PaymentProcessed`, `RevolvingPaymentProcessed`, `PaymentFailed`, `PaymentRejected` → `payments.events`
- Sales: `SaleQuoteCreatedEvent`, `SaleQuoteCancelledEvent`, `SaleQuoteExpiredEvent`, `SaleInvoiceConfirmedEvent` → `sales.events`
- Catalogs: `IProductCreated`, `IProductUpdated`, `IProductPriceChanged`, `IProductActivated`, `IProductDeactivated`, `ICategoryCreated`, `IBrandCreated` → `catalogs.events`
- Inventory: `IStockMovementConfirmed`, `ILowStockDetected`, `IStockReservationCreated`, `IStockReservationReleased`, `IPhysicalInventoryCompleted` → `inventory.events`
- ElectronicInvoice: `ResultadoFacturaElectronica`, `ElectronicDocumentProcessedEvent`, `FacturaElectronicaContract`, `NotaCreditoElectronicaContract` → `einvoice.events`

#### Scenario: CustomerCreated routed to crm.events
- **WHEN** an event with `EventType = "CustomerCreated"` is processed
- **THEN** it is published to the `crm.events` exchange
