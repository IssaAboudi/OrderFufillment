public record OrderProcessedEvent(int OrderId, string CustomerName, List<OrderItemContract> Items);
