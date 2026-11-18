
using System;

namespace CoreSwap.Models;

public class NotificationItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string OfferId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public bool IsUnread { get; set; }

    public string TimeText =>
        CreatedAtUtc.ToLocalTime()
            .ToString("dd MMM yyyy, hh:mm tt");

    public string StatusText =>
        IsUnread ? "NEW" : "";
}
