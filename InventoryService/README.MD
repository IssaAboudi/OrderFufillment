# InventoryService

An ASP.NET Core microservice that tracks product stock levels and reacts to order events. Part of the Foodics interview solution.

## Overview

InventoryService listens for `OrderCreated` events published by OrderService, decrements stock for each ordered item, and records processed orders (including line-item prices) for idempotency. If any item is out of stock, it publishes an `OrderFailed` event back to OrderService so the order can be marked as failed.

## Tech Stack

- .NET 10 (ASP.NET Core minimal APIs)
- Entity Framework Core 10 + Npgsql (PostgreSQL)
- RabbitMQ.Client 7.x (messaging)
- OpenAPI (Swagger UI in Development)

## Prerequisites

- .NET 10 SDK
- PostgreSQL (with a `foodics` database)
- RabbitMQ (AMQP on port `5672`)

## Getting Started

1. Configure the connection string in `appsettings.json`:

   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Host=localhost;Port=5432;Database=foodics;Username=postgres;Password=<your-password>"
   }
   ```

2. Start PostgreSQL and RabbitMQ (from the repo root):

   ```bash
   docker compose up -d
   ```

3. Run the service (migrations apply and the inventory seeds automatically on startup):

   ```bash
   dotnet run --launch-profile http
   ```

   The service listens on `http://localhost:5289` (`https://localhost:7062`).

4. Health check:

   ```http
   GET /health
   ```

## Data Model

### InventoryItem

| Field            | Type | Notes                                    |
| ---------------- | ---- | ---------------------------------------- |
| `ProductId`      | int  | Primary key (matches OrderService catalog IDs) |
| `QuantityOnHand` | int  | Current stock level                      |

### ProcessedOrder

| Field     | Type | Notes                                     |
| --------- | ---- | ----------------------------------------- |
| `OrderId` | int  | Primary key — the idempotency key         |

### ProcessedOrderItem

| Field       | Type    | Notes                                            |
| ----------- | ------- | ------------------------------------------------ |
| `Id`        | int     | Primary key                                      |
| `OrderId`   | int     | FK -> ProcessedOrder (cascade)                   |
| `ProductId` | int     | Product ordered                                  |
| `Quantity`  | int     | Quantity ordered                                 |
| `UnitPrice` | decimal | Price snapshot carried on the order event        |

Stock is seeded via `HasData` in `InventoryDbContext.OnModelCreating` with fixed product IDs (1-10) matching the OrderService catalog.

## Messaging

### Consumes

| Queue       | Exchange | Routing key            | Message             |
| ----------- | -------- | ---------------------- | ------------------- |
| `OrderQueue` | `orders` | `order-exchange-routing` | `OrderCreatedEvent` |

### Publishes (on failure)

| Queue             | Exchange        | Routing key              | Message            |
| ----------------- | --------------- | ------------------------ | ------------------ |
| `OrderStatusQueue` | `order-status` | `order-status-routing`   | `OrderFailedEvent` |

### Processing flow

1. Consume an `OrderCreatedEvent`.
2. **Idempotency** — skip if the `OrderId` already exists in `ProcessedOrders`.
3. Decrement stock for each item; fail the whole order if any item is out of stock.
4. Record the `ProcessedOrder` + its `ProcessedOrderItems` (with prices) in one transaction.
5. On failure, publish an `OrderFailedEvent` back to OrderService.

## API Endpoints

| Method | Route        | Description                    |
| ------ | ------------ | ------------------------------ |
| GET    | `/health`    | Liveness check                 |
| GET    | `/inventory` | List current stock levels      |

### List inventory

```http
GET /inventory
```

```json
[
  { "productId": 1, "quantityOnHand": 100 },
  { "productId": 2, "quantityOnHand": 80 }
]
```

## Project Structure

- `Program.cs` — minimal API endpoints, DI wiring, startup migrations
- `Models/` — `InventoryItem`, `ProcessedOrder`, `ProcessedOrderItem`
- `Data/` — `InventoryDb` (EF Core `DbContext`) + seed data
- `DTO/Events/` — `OrderCreatedEvent`, `OrderFailedEvent` message contracts
- `Services/` — `MessageBus` (RabbitMQ consumer + failure publisher)
- `Migrations/` — EF Core migrations
