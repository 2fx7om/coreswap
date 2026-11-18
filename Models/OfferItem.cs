namespace CoreSwap.Models;

public class OfferItem
{
    public string OfferId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string OfferedById { get; set; } = string.Empty;
    public string OfferedByName { get; set; } = string.Empty;
    public decimal ProposedPrice { get; set; }
    public string ProposedDate { get; set; } = string.Empty; // yyyy-MM-dd, campus local date
    public string ProposedTime { get; set; } = string.Empty; // HH:mm, campus local time
    public string Location { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string LastUpdatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public string Summary => $"RM{ProposedPrice:0.##} • " +
        (string.IsNullOrWhiteSpace(ProposedDate) ? "" : $"{ProposedDate} ") +
        $"{ProposedTime} • {Location}";
}
