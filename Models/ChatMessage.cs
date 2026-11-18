
using System;
using System.Text.Json.Serialization;

namespace CoreSwap.Models;

public class ChatMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsMine { get; set; }

    [JsonIgnore]
    public bool IsOther => !IsMine;

    [JsonIgnore]
    public string TimeText =>
        SentAtUtc.ToLocalTime().ToString("hh:mm tt");
}
