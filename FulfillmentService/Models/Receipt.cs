namespace FulfillmentService.Models;

public class Receipt
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public List<ReceiptItem> Items { get; set; } = [];
}
