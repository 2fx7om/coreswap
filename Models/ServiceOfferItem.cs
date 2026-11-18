
using System;
using System.Text.Json.Serialization;

namespace CoreSwap.Models;

public class ServiceOfferItem
{
    public string OfferId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceOwnerId { get; set; } = string.Empty;
    public string OfferedById { get; set; } = string.Empty;
    public string OfferedByName { get; set; } = string.Empty;

    public decimal ProposedPrice { get; set; }
    public string ProposedDate { get; set; } = string.Empty;
    public string ProposedTime { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;

    [JsonIgnore]
    public string Summary =>
        $"RM{ProposedPrice:0.##} • {ProposedDate} at {ProposedTime}";

    [JsonIgnore]
    public bool CanOwnerRespond { get; set; }

    [JsonIgnore]
    public bool CanCustomerAcceptCounter { get; set; }

    [JsonIgnore]
    public bool CanCustomerWithdraw { get; set; }

    [JsonIgnore]
    public bool CanChat { get; set; }
}
