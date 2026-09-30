# Foodics Order Fulfillment — Low-Level Design

## 1. Database Schema

A single PostgreSQL database (`foodics`) is shared; each service owns its own tables and `DbContext`.

### OrderService (OrderDb)

| Table | Columns | Keys / Notes |
|-------|---------|--------------|
| `Orders` | `Id`, `CustomerName`, `OrderDate`, `Status`, `FailureReason` | `Id` PK; `Status` is an enum stored as int; `FailureReason` nullable |
| `OrderItems` | `Id`, `OrderId`, `ProductId`, `Quantity`, `UnitPrice` | `Id` PK; `OrderId` FK → Orders (cascade); `ProductId` FK → Products (restrict) |
| `Products` | `Id`, `Name`, `Description`, `Price`, `Category`, `IsAvailable` | `Id` PK; seeded with fixed IDs 1–10 |

`UnitPrice` on `OrderItems` is a snapshot of `Products.Price` taken at order time.

### InventoryService (InventoryDb)

| Table | Columns | Keys / Notes |
|-------|---------|--------------|
| `InventoryItems` | `ProductId`, `QuantityOnHand` | `ProductId` PK; seeded with IDs 1–10 |
| `ProcessedOrders` | `OrderId` | `OrderId` PK (idempotency key) |
| `ProcessedOrderItems` | `Id`, `OrderId`, `ProductId`, `Quantity`, `UnitPrice` | `Id` PK; `OrderId` FK → ProcessedOrders (cascade) |

`ProcessedOrders` is the idempotency ledger: a row exists iff the order has already been processed.

### FulfillmentService (FulfillmentDb)

| Table | Columns | Keys / Notes |
|-------|---------|--------------|
| `Receipts` | `OrderId`, `CustomerName`, `TotalAmount` | `OrderId` PK (also the idempotency key) |
| `ReceiptItems` | `Id`, `OrderId`, `ProductId`, `Quantity`, `UnitPrice` | `Id` PK; `OrderId` FK → Receipts (cascade) |

`TotalAmount` is computed as the sum of `Quantity × UnitPrice` over all line items.

## 2. Message Contracts

All events are JSON-serialized `record` types (System.Text.Json).

### OrderItemContract

```csharp
public record OrderItemContract(int ProductId, int Quantity, decimal UnitPrice);
```

### OrderCreatedEvent

```csharp
public record OrderCreatedEvent(int OrderId, string CustomerName, List<OrderItemContract> Items);
```

Produced by OrderService, consumed by InventoryService.

### OrderProcessedEvent

```csharp
public record OrderProcessedEvent(int OrderId, string CustomerName, List<OrderItemContract> Items);
```

Produced by InventoryService (on success), consumed by FulfillmentService.

### OrderFailedEvent

```csharp
public record OrderFailedEvent(int OrderId, string Reason);
```

Produced by InventoryService (on failure), consumed by OrderService.

### OrderCompletedEvent

```csharp
public record OrderCompletedEvent(int OrderId);
```

Produced by FulfillmentService, consumed by OrderService.

## 3. Queue Topology

| Queue | Exchange | Routing key | Message | Producer | Consumer |
|-------|----------|-------------|---------|----------|----------|
| `OrderQueue` | `orders` | `order-exchange-routing` | `OrderCreatedEvent` | OrderService | InventoryService |
| `OrderStatusQueue` | `order-status` | `order-status-routing` | `OrderFailedEvent` | InventoryService | OrderService |
| `ProcessedOrderQueue` | `order-processed` | `order-processed-routing` | `OrderProcessedEvent` | InventoryService | FulfillmentService |
| `OrderCompletedQueue` | `order-completed` | `order-completed-routing` | `OrderCompletedEvent` | FulfillmentService | OrderService |

All exchanges are `Direct`, all queues are durable (`durable: true`, `exclusive: false`, `autoDelete: false`). Producers and consumers both declare the exchange/queue/binding idempotently, so startup order does not matter.

## 4. Consumer Processing Logic

All consumers are `BackgroundService`s using `AsyncEventingBasicConsumer` with `autoAck: false`. Each message runs in its own DI scope so every message gets a fresh, isolated `DbContext`.

### InventoryService consumer

1. Deserialize the body to `OrderCreatedEvent`; if null, ack and discard (poison message).
2. Idempotency check: if `ProcessedOrders` already contains the `OrderId`, ack and skip.
3. For each item:
   - find `InventoryItems` by `ProductId`; if missing, throw (unknown product);
   - if `QuantityOnHand < Quantity`, throw (insufficient stock);
   - decrement `QuantityOnHand`.
4. Add a `ProcessedOrder` row (the idempotency key) plus its `ProcessedOrderItems`.
5. `SaveChangesAsync` — the single commit point (nothing persists if any item failed).
6. Publish `OrderProcessedEvent` to `ProcessedOrderQueue`.
7. Ack the message.

On exception: publish `OrderFailedEvent` (with the reason) to `OrderStatusQueue`, then ack the original message.

### FulfillmentService consumer

1. Deserialize the body to `OrderProcessedEvent`; if null, ack and discard.
2. Idempotency check: if a `Receipt` with this `OrderId` exists, ack and skip.
3. Compute `TotalAmount = sum(Quantity × UnitPrice)`.
4. Add the `Receipt` row and its `ReceiptItems`.
5. `SaveChangesAsync`.
6. Publish `OrderCompletedEvent` to `OrderCompletedQueue`.
7. Ack the message.

On exception: nack with `requeue: true` (transient error — retry later).

### OrderService consumers

`OrderStatusConsumer` (failed) and `OrderCompletedConsumer` (completed) share the same shape:

1. Deserialize the event; if null, ack and discard.
2. Find the order by `OrderId`; if not found, ack and discard.
3. Set `Status = Failed` (with `FailureReason`) or `Status = Complete` as appropriate.
4. `SaveChangesAsync`.
5. Ack the message.

On exception: nack with `requeue: true`.

## 5. API Endpoints

### OrderService

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/orders` | Create an order (resolves prices, publishes `OrderCreatedEvent`). Response items include product names. |
| GET | `/orders` | List orders (summary only, no items). |
| GET | `/orders/{id}` | Get one order including items (with product names). |
| PATCH | `/orders/{id}` | Update customer name and/or items. |
| DELETE | `/orders/{id}` | Delete an order. |
| GET | `/products` | List the menu (sorted). |
| GET | `/products/{id}` | Get a single menu item. |

### InventoryService

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/health` | Liveness check. |
| GET | `/inventory` | List current stock, with the product name mapped from a `Product` enum. |

### FulfillmentService

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/health` | Liveness check. |
| GET | `/receipts` | List all receipts (order id, customer, total). |
| GET | `/receipts/{orderId}` | Get a full receipt including line items and total. |

## 6. Error Handling

- **Business failure (insufficient stock / unknown product):** InventoryService publishes `OrderFailedEvent` and acks the original message. The message is considered *handled* (its outcome was decided), so it is not requeued.
- **Poison message (null deserialization):** ack and discard — retrying would loop forever.
- **Transient failure (e.g. database down):** nack with `requeue: true` so the message is redelivered later (FulfillmentService and OrderService consumers).
- **At-least-once duplicate delivery:** handled by the `ProcessedOrders` / `Receipts` idempotency key rather than relying on exactly-once delivery.
- **Downstream service outage:** messages remain in the durable queue and are processed when the consumer returns.

Known simplification: InventoryService currently treats *any* processing exception as an order failure and publishes `OrderFailedEvent`, rather than distinguishing transient errors (which should be retried) from genuine business failures.

## 7. Project Structure

Each service follows the same layout:

```
ServiceName/
├── Program.cs            # minimal API endpoints, DI, startup migrations
├── Models/               # entities + enums
├── Data/                 # EF Core DbContext + seed data
├── DTO/
│   ├── Requests/         # incoming HTTP request contracts (OrderService)
│   └── Events/           # cross-service message contracts
├── Services/             # RabbitMQ producers/consumers (BackgroundService)
├── Migrations/           # EF Core migrations
└── appsettings.json      # connection string + config
```

- **OrderService** services: `MessageBus` (producer), `OrderStatusConsumer`, `OrderCompletedConsumer`.
- **InventoryService** services: `MessageBus` (consumer + failure/processed publisher).
- **FulfillmentService** services: `MessageBus` (consumer + completed publisher).
