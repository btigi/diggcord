using System.Data.SQLite;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Diggcord.DiscordBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                })
                .UseWindowsService();

        public class Worker(ILogger<Worker> logger) : BackgroundService
        {
            private readonly ILogger<Worker> _logger = logger;
            private string DbPath;
            private string ImagePath;

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                var builder = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", false, true);
                var app = builder.Build();
                DbPath = app["DbPath"];
                ImagePath = app["ImagePath"];

                var discordToken = app["DiscordToken"];

                var config = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
                };

                var _client = new DiscordSocketClient(config);
                _client.Log += LogAsync;
                _client.MessageReceived += MessageReceivedAsync;

                await _client.LoginAsync(TokenType.Bot, discordToken);
                await _client.StartAsync();
                await InitializeDatabase();

                await Task.Delay(-1, stoppingToken);
            }

            private Task LogAsync(LogMessage log)
            {
                var msg = log.ToString();
                _logger.LogInformation("{msg}", msg);
                return Task.CompletedTask;
            }

            private async Task MessageReceivedAsync(SocketMessage message)
            {
                if (message.Author.IsBot)
                {
                    return;
                }

                var guild = GetGuildInfo(message);
                if (message.Content == "optout")
                {
                    await DeleteMessages(message.Author.Id, guild.id);
                    await DeleteAccount(message.Author.Id, guild.id);
                    return;
                }

                if (message.Content == "optin")
                {
                    await OptIn(message.Author.Id, guild.id);
                    return;
                }

                await LogMessageToDatabase(message);
                _logger.LogInformation("{Author}: {Content} ({ChannelName} {Guild})", message.Author, message.Content, message.Channel.Name, guild);
            }

            private async Task InitializeDatabase()
            {
                if (!File.Exists(DbPath))
                {
                    SQLiteConnection.CreateFile(DbPath);
                }

                using var connection = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                await connection.OpenAsync();

                var createAccountsTableQuery = @"
                    CREATE TABLE IF NOT EXISTS Accounts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        AuthorId TEXT NOT NULL,
                        GuildId TEXT NOT NULL,
                        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE (AuthorId, GuildId)
                    )";
                using var accountsCommand = new SQLiteCommand(createAccountsTableQuery, connection);
                await accountsCommand.ExecuteNonQueryAsync();

                var createMessagesTableQuery = @"
                    CREATE TABLE IF NOT EXISTS Messages (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        AuthorId TEXT NOT NULL,
                        GlobalAuthor TEXT NOT NULL,
                        DisplayAuthor TEXT NOT NULL,
                        Content TEXT NOT NULL,
                        Channel TEXT NOT NULL,
                        GuildId TEXT NOT NULL,
                        Guild TEXT NOT NULL,
                        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";
                using var messagesCommand = new SQLiteCommand(createMessagesTableQuery, connection);
                await messagesCommand.ExecuteNonQueryAsync();

                var createEmojiTableQuery = @"
                    CREATE TABLE IF NOT EXISTS Emojis (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        AuthorId TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        Value TEXT NOT NULL,
                        Url TEXT NOT NULL,
                        LocalPath TEXT NOT NULL,
                        GuildId TEXT NOT NULL,
                        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";
                using var emojiCommand = new SQLiteCommand(createEmojiTableQuery, connection);
                await emojiCommand.ExecuteNonQueryAsync();

                await connection.CloseAsync();
            }

            private async Task DeleteMessages(ulong authorId, ulong guildId)
            {
                using var connection = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                await connection.OpenAsync();
                var deleteQuery = "DELETE FROM Messages WHERE AuthorId = @authorId AND GuildId = @guildId";
                using var command = new SQLiteCommand(deleteQuery, connection);
                command.Parameters.AddWithValue("@authorId", Convert.ToString(authorId));
                command.Parameters.AddWithValue("@guildId", Convert.ToString(guildId));
                await command.ExecuteNonQueryAsync();
                await connection.CloseAsync();
            }

            private async Task DeleteAccount(ulong authorId, ulong guildId)
            {
                using var connection = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                await connection.OpenAsync();
                var deleteQuery = "DELETE FROM Accounts WHERE AuthorId = @authorId AND GuildId = @guildId";
                using var command = new SQLiteCommand(deleteQuery, connection);
                command.Parameters.AddWithValue("@authorId", Convert.ToString(authorId));
                command.Parameters.AddWithValue("@guildId", Convert.ToString(guildId));
                await command.ExecuteNonQueryAsync();
                await connection.CloseAsync();
            }

            private async Task OptIn(ulong authorId, ulong guildId)
            {
                using var connection = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                await connection.OpenAsync();
                var insertQuery = "INSERT OR IGNORE INTO Accounts (AuthorId, GuildId) VALUES (@authorId, @guildId)";
                using var insertCmd = new SQLiteCommand(insertQuery, connection);
                insertCmd.Parameters.AddWithValue("@authorId", Convert.ToString(authorId));
                insertCmd.Parameters.AddWithValue("@guildId", Convert.ToString(guildId));
                await insertCmd.ExecuteNonQueryAsync();
                await connection.CloseAsync();
            }

            private async Task LogMessageToDatabase(SocketMessage message)
            {
                await LogEmojiToDatabase(message);

                var guild = GetGuildInfo(message);

                using var connection = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                await connection.OpenAsync();
                var insertQuery = @"
                    INSERT INTO Messages
                    (AuthorId, GlobalAuthor, DisplayAuthor, Channel, GuildId, Guild, Content)
                    SELECT AuthorId, @globalAuthor, @displayAuthor, @channel, @guildId, @guild, @content
                    FROM Accounts 
                    WHERE 
                    AuthorId = @authorId";
                using var command = new SQLiteCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@authorId", Convert.ToString(message.Author.Id));
                command.Parameters.AddWithValue("@globalAuthor", message.Author.GlobalName);
                command.Parameters.AddWithValue("@displayAuthor", (message.Author as SocketGuildUser).DisplayName);
                command.Parameters.AddWithValue("@channel", message.Channel.Name);
                command.Parameters.AddWithValue("@guildId", guild.id);
                command.Parameters.AddWithValue("@guild", guild.name);
                command.Parameters.AddWithValue("@content", message.Content);
                await command.ExecuteNonQueryAsync();
                await connection.CloseAsync();
            }

            private async Task LogEmojiToDatabase(SocketMessage message)
            {
                var emojis = message.Tags.Where(w => w.Type == TagType.Emoji);
                if (emojis.Any())
                {
                    using var connection = new SQLiteConnection($"Data Source={DbPath};Version=3;");
                    await connection.OpenAsync();
                    foreach (var tag in emojis.Select(s => s.Value))
                    {
                        if (tag is Emote emote)
                        {
                            if (!File.Exists($"{ImagePath}{emote.Id}.png"))
                            {
                                await ImageDownloader.DownloadImageAsync(emote.Url, $"{ImagePath}{emote.Id}.png");
                            }

                            var guild = GetGuildInfo(message);

                            var insertQuery = @"
                            INSERT INTO Emojis
                            (AuthorId, Name, Value, Url, LocalPath, GuildId)
                            values
                            (@authorId, @name, @value, @url, @localPath, @guildId)";
                            using var command = new SQLiteCommand(insertQuery, connection);
                            command.Parameters.AddWithValue("@authorId", Convert.ToString(message.Author.Id));
                            command.Parameters.AddWithValue("@name", emote.Name);
                            command.Parameters.AddWithValue("@value", Convert.ToString(emote.Id));
                            command.Parameters.AddWithValue("@url", emote.Url);
                            command.Parameters.AddWithValue("@localPath", $"{emote.Id}.png");
                            command.Parameters.AddWithValue("@guildId", guild.id);
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    await connection.CloseAsync();
                }
            }

            private static (ulong id, string name) GetGuildInfo(SocketMessage message)
            {
                if (message.Channel is SocketGuildChannel guildChannel)
                {
                    return (guildChannel.Guild.Id, guildChannel.Guild.Name);
                }
                return (0, string.Empty);
            }
        }
    }
}