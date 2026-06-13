using System;
using System.Collections.Generic;

namespace Site.Models
{
    public class Chat
    {
        public int Id { get; set; }
        public string? ChatName { get; set; }
        public bool IsGroup { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? GroupPicture { get; set; }
        public int? CreatedById { get; set; }

        // Navigation properties
        public ICollection<ChatParticipant> Participants { get; set; } = new List<ChatParticipant>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }
}
