using System;

namespace Site.Models
{
    public class CallLog
    {
        public int Id { get; set; }

        public int CallerId { get; set; }
        public Users? Caller { get; set; }

        public int ReceiverId { get; set; }
        public Users? Receiver { get; set; }

        public string CallType { get; set; } = "Video"; // Audio, Video
        public string Status { get; set; } = "Completed"; // Missed, Completed, Rejected

        public int DurationSeconds { get; set; } = 0;
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    }
}
