using System;
using System.Threading.Tasks;

namespace DropCast.Abstractions
{
    /// <summary>
    /// Represents a messaging platform that can deliver meme/media messages to DropCast.
    /// Implement this for each platform (Discord, WhatsApp, Telegram, REST API, etc.).
    /// </summary>
    public interface IMessageSource
    {
        string PlatformName { get; }
        event EventHandler<DropCastMessage> MessageReceived;
        Task ConnectAsync();
        Task DisconnectAsync();
    }
}
