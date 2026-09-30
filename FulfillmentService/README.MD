# FulfillmentService

An ASP.NET Core microservice that creates receipts for processed orders and marks orders as complete. Part of the Foodics interview solution.

## Overview

FulfillmentService consumes `OrderProcessed` events published by InventoryService, calculates each order's total, and stores a receipt with its line items. It then publishes an `OrderCompleted` event back to OrderService so the order can be marked as complete.

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

3. Run the service (migrations apply automatically on startup):

   ```bash
   dotnet run --launch-profile http
   ```

   The service listens on `http://localhost:5077` (`https://localhost:7125`).

4. Health check:

   ```http
   GET /health
   ```

## Data Model

### Receipt

| Field          | Type   | Notes                              |
| -------------- | ------ | ---------------------------------- |
| `OrderId`      | int    | Primary key — the idempotency key  |
| `CustomerName` | string | Customer name                      |
| `TotalAmount`  | decimal| Sum of `quantity * unitPrice`      |
| `Items`        | List<ReceiptItem> | Line items (cascade delete) |

### ReceiptItem

| Field       | Type    | Notes                                  |
| ----------- | ------- | -------------------------------------- |
| `Id`        | int     | Primary key                            |
| `OrderId`   | int     | FK -> Receipt (cascade)                |
| `ProductId` | int     | Product ordered                        |
| `Quantity`  | int     | Quantity ordered                       |
| `UnitPrice` | decimal | Price snapshot carried on the event    |

## Messaging

### Consumes

| Queue               | Exchange         | Routing key             | Message              |
| ------------------- | ---------------- | ----------------------- | -------------------- |
| `ProcessedOrderQueue` | `order-processed` | `order-processed-routing` | `OrderProcessedEvent` |

### Publishes (on receipt created)

| Queue                | Exchange          | Routing key              | Message             |
| -------------------- | ----------------- | ------------------------ | ------------------- |
| `OrderCompletedQueue` | `order-completed` | `order-completed-routing` | `OrderCompletedEvent` |

### Flow

1. Consume an `OrderProcessedEvent`.
2. **Idempotency** — skip if a receipt for that `OrderId` already exists.
3. Calculate `TotalAmount = sum(quantity * unitPrice)`.
4. Store the `Receipt` + its `ReceiptItems` in one transaction.
5. Publish an `OrderCompletedEvent` back to OrderService.

## API Endpoints

| Method | Route               | Description                                  |
| ------ | ------------------- | -------------------------------------------- |
| GET    | `/health`           | Liveness check                               |
| GET    | `/receipts`         | List all receipts (orderId, customer, total) |
| GET    | `/receipts/{orderId}` | Get a full receipt incl. line items        |

### Get a receipt

```http
GET /receipts/1
```

```json
{
  "orderId": 1,
  "customerName": "Jane Doe",
  "totalAmount": 23.23,
  "items": [
    { "productId": 1, "quantity": 2, "unitPrice": 9.99 },
    { "productId": 9, "quantity": 1, "unitPrice": 3.25 }
  ]
}
```

## Project Structure

- `Program.cs` — minimal API endpoints, DI wiring, startup migrations
- `Models/` — `Receipt`, `ReceiptItem`
- `Data/` — `FulfillmentDb` (EF Core `DbContext`)
- `DTO/Events/` — `OrderProcessedEvent`, `OrderCompletedEvent`, `OrderItemContract` message contracts
- `Services/` — `MessageBus` (RabbitMQ consumer + publisher)
- `Migrations/` — EF Core migrations
