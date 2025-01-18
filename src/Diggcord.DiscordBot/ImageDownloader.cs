namespace Diggcord.DiscordBot
{
    public static class ImageDownloader
    {
        private static readonly HttpClient _httpClient = new();

        public static async Task DownloadImageAsync(string imageUrl, string localPath)
        {
            try
            {
                using var response = await _httpClient.GetAsync(imageUrl);
                response.EnsureSuccessStatusCode();
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(localPath, imageBytes);
                Console.WriteLine("Image downloaded and saved to " + localPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
            }
        }
    }
}