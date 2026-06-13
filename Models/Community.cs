using System;
using System.Collections.Generic;

namespace Site.Models
{
    public class Community
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Avatar { get; set; }
        public int OwnerId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual Users? Owner { get; set; }
        public virtual ICollection<CommunityGroup> CommunityGroups { get; set; } = new List<CommunityGroup>();
    }
}
