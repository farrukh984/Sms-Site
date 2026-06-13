using System;
using System.ComponentModel.DataAnnotations;

namespace Site.Models
{
    public class UserService
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int ServiceId { get; set; }
        public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
        public decimal AmountPaid { get; set; }

        public Users User { get; set; } = null!;
        public Service Service { get; set; } = null!;
    }
}
