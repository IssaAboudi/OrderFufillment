public record OrderItemContract(int ProductId, int Quantity, decimal UnitPrice);

public record OrderProcessedEvent(int OrderId, string CustomerName, List<OrderItemContract> Items);
