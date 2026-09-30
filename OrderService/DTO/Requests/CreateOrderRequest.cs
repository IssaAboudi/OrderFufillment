namespace OrderService.DTO;

using OrderService.Models;

public class CreateOrderRequest
{
    public string CustomerName { get; set; } = String.Empty;
    public List<OrderItemRequest> Items { get; set; } = [];
    public OrderStatus Status { get; set; }
}
