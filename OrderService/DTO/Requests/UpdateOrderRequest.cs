namespace OrderService.DTO;

public class UpdateOrderRequest
{
    public string? CustomerName { get; set; } = String.Empty;
    public List<OrderItemRequest>? Items { get; set; } = [];
}
