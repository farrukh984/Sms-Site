using System;

namespace Site.Models
{
    public class CommunityGroup
    {
        public int Id { get; set; }
        public int CommunityId { get; set; }
        public int ChatId { get; set; } // links to existing Chat (which is a group chat)
        public bool IsAnnouncement { get; set; } = false;

        // Navigation
        public virtual Community? Community { get; set; }
        public virtual Chat? Chat { get; set; }
    }
}
