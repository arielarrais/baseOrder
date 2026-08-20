using System.ComponentModel.DataAnnotations;

namespace OrderGenerator.Application.DTOs;

public class OrderDto
{
    [Required(ErrorMessage = "Symbol is required")]
    public string Symbol { get; set; } = string.Empty;

    [Required(ErrorMessage = "Side is required")]
    public string Side { get; set; } = string.Empty;

    [Required(ErrorMessage = "Quantity is required")]
    [Range(1, 99999, ErrorMessage = "Quantity must be between 1 and 99,999")]
    public int Quantity { get; set; }

    [Required(ErrorMessage = "Price is required")]
    [Range(0.01, 999.99, ErrorMessage = "Price must be between 0.01 and 999.99")]
    [RegularExpression(@"^\d+(\.\d{1,2})?$", ErrorMessage = "Price must be a multiple of 0.01")]
    public decimal Price { get; set; }

    public string? IdempotencyKey { get; set; }
}

public class OrderResponseDto
{
    public bool IsAccepted { get; set; }
    public string ClOrdId { get; set; } = string.Empty;
    public string? RejectReason { get; set; }
    public DateTime Timestamp { get; set; }
    public string Status { get; set; } = "Pending";
    public string Message => Status switch
    {
        "Accepted" => "Ordem Aceita",
        "Rejected" => $"Ordem Rejeitada: {RejectReason}",
        _ => "Aguardando processamento..."
    };
}
