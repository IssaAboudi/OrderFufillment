namespace OrderService.Models;

public enum OrderStatus
{
    Pending,
    Processed,
    Complete,
    Failed,
}

public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = String.Empty;
    public List<OrderItem> Items { get; set; } = [];
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public string? FailureReason { get; set; }
}
