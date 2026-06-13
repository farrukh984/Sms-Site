using System;
using System.Collections.Generic;

namespace Site.Models
{
    public class Channel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Avatar { get; set; }
        public int OwnerId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual Users? Owner { get; set; }
        public virtual ICollection<ChannelFollower> Followers { get; set; } = new List<ChannelFollower>();
        public virtual ICollection<ChannelMessage> Messages { get; set; } = new List<ChannelMessage>();
    }
}
