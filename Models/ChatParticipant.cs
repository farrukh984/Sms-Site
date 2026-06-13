using System;

namespace Site.Models
{
    public class ChatParticipant
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public Chat? Chat { get; set; }

        public int UserId { get; set; }
        public Users? User { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public bool IsAdmin { get; set; } = false;
    }
}
