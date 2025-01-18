namespace Diggcord.DiscordUnwrapped.Web.Model
{
    public class Message
    {
        public int Id { get; set; }
        public string AuthorId { get; set; }
        public string GlobalAuthor { get; set; }
        public string DisplayAuthor { get; set; }
        public string Content { get; set; }
        public string Channel { get; set; }
        public string Guild { get; set; }
        public DateTime? Timestamp { get; set; }
    }
}