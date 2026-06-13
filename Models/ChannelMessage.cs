using System;

namespace Site.Models
{
    public class ChannelMessage
    {
        public int Id { get; set; }
        public int ChannelId { get; set; }
        public int SenderId { get; set; }
        public string Content { get; set; } = string.Empty;
        
        // Type: "Text", "Image", "Video", "File"
        public string Type { get; set; } = "Text";
        public string? FileUrl { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual Channel? Channel { get; set; }
        public virtual Users? Sender { get; set; }
    }
}
