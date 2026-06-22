## Why

With `SmartCore.Outbox` (Phase 1) in place, events are durably persisted to `outbox_db` but never delivered. The `outbox-worker` is the standalone service that closes this gap: it polls `outbox_db`, claims batches of pending events, and routes each one to the correct RabbitMQ exchange using a configuration-driven publisher factory — enabling reliable, at-least-once delivery across all SmartCore microservices.

## What Changes

- New `OutboxWorker` Worker Service project (.NET 9) added to `SmartCoreOutbox.sln`
- New `OutboxWorker.IntegrationTests` xUnit project added to `SmartCoreOutbox.sln`
- Implements `IEventPublisher` and `IPublisherFactory` for extensible, per-EventType routing
- Implements `RabbitMqPublisher` via MassTransit for publishing to configured exchanges
- Implements `ClaimManager` for claim-based distributed locking on the `Events` table
- Implements `OutboxProcessor` (`BackgroundService`) — the main polling loop
- Configuration-driven routing: `appsettings.json` maps `EventType` → `Exchange` + `RoutingKey`
- References `SmartCore.Outbox` NuGet for `OutboxDbContext` access
- References `SharedKernel` NuGet for event type name resolution
- Stale claim recovery: releases claims from crashed worker instances automatically
- Docker support: `Dockerfile` and `docker-compose` entry for infrastructure deployment

## Capabilities

### New Capabilities

- `event-claim`: Claim a batch of `Pending` events from `outbox_db` using atomic UPDATE...RETURNING with `ClaimedBy` and `ClaimedAt` columns, preventing concurrent workers from processing the same event.
- `event-publisher`: Route a claimed event to RabbitMQ via `IPublisherFactory.GetPublisher(eventType)`. Supports `Published`, `Skipped` (no publisher configured), and `Failed` (after max retries) outcomes.
- `rabbitmq-publisher`: Concrete `IEventPublisher` implementation using MassTransit to publish raw JSON payload to a configured exchange and routing key.
- `stale-claim-recovery`: Detect and release claims held by crashed or timed-out worker instances so events are not permanently stuck.
- `outbox-processor-loop`: `BackgroundService` orchestrating the full poll → claim → publish → update cycle on a configurable interval.
- `worker-configuration`: Typed configuration (`OutboxWorkerOptions`, `RabbitMqOptions`, per-EventType `PublisherRoute`) bound from `appsettings.json`.

### Modified Capabilities

*(none — this is a new project; outbox-db-schema owned by SmartCore.Outbox NuGet is read-only from the worker's perspective)*

## Impact

- New projects added to `SmartCoreOutbox.sln`: `OutboxWorker`, `OutboxWorker.IntegrationTests`
- References `SmartCore.Outbox` (NuGet) for `OutboxDbContext` and entity types
- References `SharedKernel` (GitHub Packages NuGet) for event type name mapping
- Requires access to `outbox_db` (same PostgreSQL instance used by all microservices)
- Requires access to RabbitMQ broker (URI via config)
- Deployed as a single instance in infrastructure (Docker / docker-compose)
- No changes to `SmartCore.Outbox` NuGet or any microservice code

## Non-goals

- Writing events to `outbox_db` (handled by `SmartCore.Outbox` NuGet)
- Consumer-side idempotency (handled by `IIdempotencyGuard` from the NuGet)
- Horizontal scaling / multiple worker instances (single instance design; claim-based locking is defensive, not a scale-out mechanism)
- Non-RabbitMQ publishers (e.g., HTTP webhooks, Azure Service Bus) — extensible via `IEventPublisher` but not implemented here
- Dead-letter queue management or manual event replay UI
