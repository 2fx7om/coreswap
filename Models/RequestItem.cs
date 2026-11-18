namespace CoreSwap.Models;

public class RequestItem
{
    public string RequestId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PricingType { get; set; } = "Negotiable";
    public decimal? Budget { get; set; }
    public string PreferredDate { get; set; } = string.Empty; // yyyy-MM-dd, campus local date
    public string PreferredTime { get; set; } = string.Empty; // HH:mm, campus local time
    public string TargetProviderId { get; set; } = string.Empty;
    public string Status { get; set; } = "Available";
    public string AcceptedOfferId { get; set; } = string.Empty;
    public string ImageSource { get; set; } = "laptop_request.jpg";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public string BudgetText => PricingType switch
    {
        "Free" => "Free preferred",
        "Swap / Exchange" => Budget.HasValue ? $"Swap / RM{Budget:0.##}" : "Swap / Exchange",
        "Fixed Budget" => Budget.HasValue ? $"RM{Budget:0.##}" : "Fixed Budget",
        _ => Budget.HasValue ? $"RM{Budget:0.##} • Negotiable" : "Negotiable"
    };
}
