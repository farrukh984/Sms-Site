using System;

namespace Site.Models
{
    public enum MessageType
    {
        Text,
        Image,
        Audio,
        Document,
        Location,
        Contact
    }

    public class Message
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public Chat? Chat { get; set; }

        public int SenderId { get; set; }
        public Users? Sender { get; set; }

        public string Content { get; set; } = string.Empty;
        public MessageType Type { get; set; } = MessageType.Text;
        public string? FileUrl { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;

        public int? ReplyToMessageId { get; set; }
        public Message? ReplyToMessage { get; set; }

        public bool IsDeleted { get; set; } = false;
        public string? Reactions { get; set; }
    }
}
