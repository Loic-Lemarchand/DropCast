using DropCast.Android.Models;
using Microsoft.Extensions.Logging;

namespace DropCast.Android.Services;

/// <summary>
/// Receives WhatsApp messages forwarded from the NotificationListenerService
/// and raises events for the overlay to consume (same pattern as DiscordService).
/// </summary>
public class WhatsAppService
{
    private readonly ILogger<WhatsAppService> _logger;

    public event EventHandler<DropCastMessage>? MessageReceived;

    /// <summary>
    /// The WhatsApp group name to listen to. Only messages from this group trigger the overlay.
    /// </summary>
    public string GroupName { get; set; } = "";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(GroupName);

    public WhatsAppService(ILogger<WhatsAppService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called by the notification listener when a WhatsApp message arrives.
    /// Filters by group name and fires the event.
    /// </summary>
    public void OnNotificationMessage(string group, string sender, string text)
    {
        if (!IsEnabled) return;
        if (!string.Equals(group.Trim(), GroupName.Trim(), StringComparison.OrdinalIgnoreCase)) return;

        _logger.LogInformation("📱 WhatsApp [{Group}] {Sender}: {Text}", group, sender, text);

        var msg = new DropCastMessage
        {
            Text = text,
            Caption = text,
            AuthorName = sender,
            SourcePlatform = "WhatsApp",
            Attachments = []
        };

        MessageReceived?.Invoke(this, msg);
    }
}
