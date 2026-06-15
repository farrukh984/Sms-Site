using System;
using System.ComponentModel.DataAnnotations;

namespace Site.Models
{
    public class Report
    {
        public int Id { get; set; }

        public int ReporterId { get; set; }
        public Users? Reporter { get; set; }

        public int ReportedUserId { get; set; }
        public Users? ReportedUser { get; set; }

        [Required]
        public string Reason { get; set; } = string.Empty;

        public string Status { get; set; } = "Pending"; // Pending, Reviewed

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
