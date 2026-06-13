using System;
using System.Collections.Generic;

namespace Site.Models
{
    public class Status
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        // Type can be "Text", "Image", "Video"
        public string Type { get; set; } = "Text"; 
        
        public string? MediaUrl { get; set; }
        public string? TextContent { get; set; }
        public string? BackgroundColor { get; set; } // For Text status e.g. '#25D366'
        public string? FontStyle { get; set; } // For Text status e.g. 'Arial' or 'Georgia'
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(1); // 24 hours duration
        
        // Navigation properties
        public virtual Users? User { get; set; }
        public virtual ICollection<StatusViewer> StatusViewers { get; set; } = new List<StatusViewer>();
    }
}
