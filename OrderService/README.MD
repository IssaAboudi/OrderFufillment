# OrderService

An ASP.NET Core microservice that manages customer orders and a product catalog (restaurant menu). Part of the Foodics interview solution.

## Overview

OrderService is a minimal-API CRUD service for creating, reading, updating, and deleting orders and their line items. It exposes a seeded product catalog so order line items reference real menu products and prices are snapshotted server-side.

When an order is created, it publishes an `OrderCreated` event for InventoryService. It also consumes `OrderFailed` events back from InventoryService and marks the affected order as failed.

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

3. Run the service (migrations apply and the catalog seeds automatically on startup):

   ```bash
   dotnet run --launch-profile http
   ```

   The service listens on `http://localhost:5149` (`https://localhost:7088`).

4. View the OpenAPI document in Development:
   `http://localhost:5149/openapi/v1.json`

### Reset the database

To wipe and rebuild the database (re-applies all migrations and re-seeds the menu):

```bash
dotnet ef database drop --force
dotnet run
```

## Data Model

### Order

| Field           | Type           | Notes                              |
| --------------- | -------------- | ---------------------------------- |
| `Id`            | int            | Primary key                        |
| `CustomerName`  | string         | Customer name                      |
| `Items`         | List<OrderItem>| Line items (cascade delete)        |
| `OrderDate`     | DateTime       | Set to `DateTime.UtcNow` on create |
| `Status`        | OrderStatus    | See enum below                     |
| `FailureReason` | string?        | Set when an order is marked failed |

### OrderItem

| Field       | Type    | Notes                                           |
| ----------- | ------- | ----------------------------------------------- |
| `Id`        | int     | Primary key                                     |
| `OrderId`   | int     | FK -> Order (cascade)                           |
| `ProductId` | int     | FK -> Product (restrict)                        |
| `Quantity`  | int     | Quantity ordered                                |
| `UnitPrice` | decimal | Price snapshot at time of order (server-filled) |

### Product

| Field         | Type    | Notes                                        |
| ------------- | ------- | -------------------------------------------- |
| `Id`          | int     | Primary key                                  |
| `Name`        | string  | Menu item name                               |
| `Description` | string  | Menu item description                        |
| `Price`       | decimal | Current menu price                           |
| `Category`    | string  | Menu section (e.g. Main, Starter, Dessert)   |
| `IsAvailable` | bool    | Whether the item can currently be ordered    |

The catalog is seeded via `HasData` in `OrderDbContext.OnModelCreating` with fixed IDs (1-10), so `productId` values are stable and meaningful.

### OrderStatus (enum)

`Pending`, `Processed`, `Complete`, `Failed`

Enums are serialized as strings (e.g. `"Pending"`) via `JsonStringEnumConverter`. Integer values are still accepted.

## Messaging

### Publishes (on order created)

| Queue        | Exchange | Routing key            | Message             |
| ------------ | -------- | ---------------------- | ------------------- |
| `OrderQueue` | `orders` | `order-exchange-routing` | `OrderCreatedEvent` |

### Consumes (order failures)

| Queue             | Exchange        | Routing key            | Message            |
| ----------------- | --------------- | ---------------------- | ------------------ |
| `OrderStatusQueue` | `order-status` | `order-status-routing` | `OrderFailedEvent` |

### Flow

1. `POST /orders` saves the order (status `Pending`), then publishes an `OrderCreatedEvent`.
2. InventoryService consumes it and, on failure, publishes an `OrderFailedEvent`.
3. `OrderStatusConsumer` consumes the failure, sets `Status = Failed`, and stores `FailureReason`.

## API Endpoints

### Orders

| Method | Route           | Description                       |
| ------ | --------------- | --------------------------------- |
| POST   | `/orders`       | Create a new order                |
| GET    | `/orders`       | List all orders (no items)        |
| GET    | `/orders/{id}`  | Get one order incl. items         |
| PATCH  | `/orders/{id}`  | Update customer name and/or items |
| DELETE | `/orders/{id}`  | Delete an order                   |

### Products

| Method | Route           | Description                  |
| ------ | --------------- | ---------------------------- |
| GET    | `/products`     | List the menu (sorted)       |
| GET    | `/products/{id}`| Get a single menu item       |

## Examples

### Create an order

The client sends only `productId` and `quantity`. The server looks up the product, validates it exists and is available, and snapshots `UnitPrice` from the catalog.

```http
POST /orders
Content-Type: application/json

{
  "customerName": "Jane Doe",
  "items": [
    { "productId": 1, "quantity": 2 },
    { "productId": 9, "quantity": 1 }
  ]
}
```

Response (prices + product names resolved from the catalog):

```json
{
  "id": 1,
  "customerName": "Jane Doe",
  "items": [
    { "id": 1, "orderId": 1, "product": "ClassicCheeseburger", "quantity": 2, "unitPrice": 9.99 },
    { "id": 2, "orderId": 1, "product": "FreshOrangeJuice", "quantity": 1, "unitPrice": 3.25 }
  ],
  "orderDate": "2026-09-29T00:00:00Z",
  "status": "Pending",
  "failureReason": null
}
```

### List the menu

```http
GET /products
```

## Project Structure

- `Program.cs` — minimal API endpoints, DI wiring, startup migrations
- `Models/` — `Order`, `OrderItem`, `OrderStatus`, `Product`
- `DTO/Requests/` — request contracts (`CreateOrderRequest`, `UpdateOrderRequest`, `OrderItemRequest`)
- `DTO/Events/` — message contracts (`OrderCreatedEvent`, `OrderFailedEvent`)
- `Data/` — `OrderDb` (EF Core `DbContext`) + catalog seed data
- `Services/` — `MessageBus` (order-created publisher) and `OrderStatusConsumer` (failure consumer)
- `Migrations/` — EF Core migrations
