using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Site.Models
{
    public class Users
    {
        public int Id { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [NotMapped]
        [Required]
        [Compare("Password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string MobileNumber { get; set; } = string.Empty;

        public string VerificationCode { get; set; } = string.Empty;
        public bool IsVerified { get; set; } = false;

        public string? FullName { get; set; }
        public string? Nickname { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Pin { get; set; }
        public string? ProfilePicture { get; set; }
        public bool FingerprintEnabled { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeen { get; set; }

        // Moderation fields
        public int WarningCount { get; set; } = 0;
        public DateTime? SuspendedUntil { get; set; }

        // Profile fields
        public string? About { get; set; }
        public string? Address { get; set; }
        public string? MaritalStatus { get; set; }
        public string? Hobbies { get; set; }
        public string? Likes { get; set; }
        public string? Dislikes { get; set; }
        public string? Cuisines { get; set; }
        public string? Sports { get; set; }
        public string? Qualification { get; set; }
        public string? School { get; set; }
        public string? College { get; set; }
        public string? WorkStatus { get; set; }
        public string? Organization { get; set; }
        public string? Designation { get; set; }

        // Privacy Settings
        public string LastSeenPrivacy { get; set; } = "everyone"; // everyone, contacts, nobody
        public string ProfilePicPrivacy { get; set; } = "everyone";
        public string AboutPrivacy { get; set; } = "contacts";
        public string StatusPrivacy { get; set; } = "contacts";
        public string GroupsPrivacy { get; set; } = "everyone";
        public bool ReadReceipts { get; set; } = true;
        public bool BlockUnknownMessages { get; set; } = false;
        public bool DisableLinkPreviews { get; set; } = false;
        public string DisappearingTimer { get; set; } = "off"; // off, 24h, 7d, 90d

        // Chat Settings
        public string Theme { get; set; } = "system"; // light, dark, system
        public string Wallpaper { get; set; } = "default";
        public string MediaUploadQuality { get; set; } = "standard"; // standard, hd
        public string MediaAutoDownload { get; set; } = "wifi"; // none, wifi, all
        public bool SpellCheck { get; set; } = true;
        public bool ReplaceTextWithEmoji { get; set; } = true;
        public bool EnterIsSend { get; set; } = false;
    }
}
