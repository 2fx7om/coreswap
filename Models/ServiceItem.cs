namespace CoreSwap.Models;

public class ServiceItem
{
    public string ServiceId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PricingType { get; set; } = "Negotiable";
    public decimal? Price { get; set; }
    public string Availability { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string ImageSource { get; set; } = "laptop_repair.jpg";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public string PriceText => PricingType switch
    {
        "Free" => "Free",
        "Swap / Exchange" => Price.HasValue ? $"Swap / RM{Price:0.##}" : "Swap / Exchange",
        "Fixed Price" => Price.HasValue ? $"RM{Price:0.##}" : "Fixed Price",
        _ => Price.HasValue ? $"RM{Price:0.##} • Negotiable" : "Negotiable"
    };
}
