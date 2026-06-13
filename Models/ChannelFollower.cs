using System;

namespace Site.Models
{
    public class ChannelFollower
    {
        public int Id { get; set; }
        public int ChannelId { get; set; }
        public int UserId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual Channel? Channel { get; set; }
        public virtual Users? User { get; set; }
    }
}
