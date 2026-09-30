public record OrderItemContract(int ProductId, int Quantity, decimal UnitPrice);

public record OrderCreatedEvent(int OrderId, string CustomerName, List<OrderItemContract> Items);
