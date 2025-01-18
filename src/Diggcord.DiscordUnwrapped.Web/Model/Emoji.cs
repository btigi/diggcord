namespace Diggcord.DiscordUnwrapped.Web.Model
{
    public class Emoji
    {
        public int Id { get; set; }
        public string AuthorId { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public string Url { get; set; }
        public string LocalPath { get; set; }
        public DateTime? Timestamp { get; set; }
        public int UsageCount{ get; set; }
    }
}