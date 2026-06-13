using System;

namespace Site.Models
{
    public class StatusViewer
    {
        public int Id { get; set; }
        public int StatusId { get; set; }
        public int ViewerId { get; set; }
        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual Status? Status { get; set; }
        public virtual Users? Viewer { get; set; }
    }
}
