using System;
using System.ComponentModel.DataAnnotations;

namespace Site.Models
{
    public class Contact
    {
        public int Id { get; set; }

        [Required]
        public int OwnerId { get; set; }
        public Users? Owner { get; set; }

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string ContactNumber { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
