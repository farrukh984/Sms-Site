namespace Site.Models
{
    public class ChatSettingsDto
    {
        public string? Theme { get; set; }
        public string? Wallpaper { get; set; }
        public string? MediaUploadQuality { get; set; }
        public string? MediaAutoDownload { get; set; }
        public bool SpellCheck { get; set; }
        public bool ReplaceTextWithEmoji { get; set; }
        public bool EnterIsSend { get; set; }
    }
}
