namespace Site.Models
{
    public class PrivacyDto
    {
        public string? LastSeenPrivacy { get; set; }
        public string? ProfilePicPrivacy { get; set; }
        public string? AboutPrivacy { get; set; }
        public string? StatusPrivacy { get; set; }
        public string? GroupsPrivacy { get; set; }
        public bool ReadReceipts { get; set; }
        public bool BlockUnknownMessages { get; set; }
        public bool DisableLinkPreviews { get; set; }
        public string? DisappearingTimer { get; set; }
    }
}
