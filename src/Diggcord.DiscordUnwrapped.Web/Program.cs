using Diggcord.DiscordUnwrapped.Web.Model;
using System.Data.SQLite;
using System.Text;
using System.Text.RegularExpressions;
using Diggcord.DiscordUnwrapped.Web;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.MapGet("/unwrapped", async (string authorid, int year, string guildId) =>
{
    if (year > DateTime.Today.Year)
    {
        return Results.Content("", "text/html");
    }

    var dbPath = app.Configuration["DbPath"];
    var messages = await GetData(authorid, year, guildId, dbPath);

    if (messages.Count == 0)
    {
        return Results.Content("No data found", "text/html");
    }


    // Intro text
    var template = await File.ReadAllTextAsync("template.html");
    var globalName = messages.OrderByDescending(o => o.Timestamp).Select(s => s.GlobalAuthor).FirstOrDefault();
    var guildName = messages.Select(s => s.Guild).FirstOrDefault();
    template = template.Replace("{Guild}", guildName);
    template = template.Replace("{GlobalName}", globalName);
    template = template.Replace("{Year}", Convert.ToString(year));


    // Wordcloud
    // https://github.com/timdream/wordcloud2.js/blob/gh-pages/API.md
    var mergedText = string.Join(" ", messages.Select(s => s.Content));
    mergedText = mergedText.Replace("\"", string.Empty);

    // Remove urls
    var pattern = @"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)";
    mergedText = Regex.Replace(mergedText, pattern, string.Empty);

    // Remove irrelevant punctuation and discord formatting
    mergedText = mergedText.Replace(")", string.Empty);
    mergedText = mergedText.Replace("(", string.Empty);
    mergedText = mergedText.Replace("**", string.Empty);
    mergedText = mergedText.Replace("__", string.Empty);
    mergedText = mergedText.Replace("`", string.Empty);

    // Remove stopwords
    var stopwords = Utilities.GetStopWords();
    pattern = @"<:[a-zA-Z0-9]+:[0-9]+>";
    mergedText = Regex.Replace(mergedText, pattern, string.Empty);

    var words = mergedText.Split([' ', '.', ',', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
    var wordCounts = new Dictionary<string, int>();
    foreach (string word in words)
    {
        var lowerWord = word.ToLower();
        lowerWord = lowerWord.Replace("\n", string.Empty).Trim();
        if (!stopwords.Contains(lowerWord))
        {
            if (wordCounts.TryGetValue(lowerWord, out int value))
            {
                wordCounts[lowerWord] = ++value;
            }
            else
            {
                wordCounts[lowerWord] = 1;
            }
        }
    }

    var mostCommonWords = wordCounts.OrderByDescending(w => w.Value)
        .Take(200)
        .ToList();

    var sb = new StringBuilder();
    foreach (var w in mostCommonWords)
    {
        sb.Append($"[\"{w.Key}\", {w.Value}],");
    }
    var wordCloudWords = sb.ToString().TrimEnd(',');
    template = template.Replace("{WordCloudWords}", wordCloudWords);


    // Discord emote count
    var emoteData = await GetEmoteData(year, authorid, dbPath, guildId);
    var emotes = emoteData.GroupBy(gb => gb.Value)
                                 .OrderByDescending(o => o.Count())
                                 .Take(5)
                                 .Select(s => new Emoji()
                                 {
                                     UsageCount = s.Count(),
                                     Name = s.First().Name,
                                     LocalPath = s.First().LocalPath,
                                     Url = s.First().Url,
                                     Value = s.First().Value,
                                     Id = s.First().Id,
                                 });

    var topEmotesSb = new StringBuilder();
    foreach (var emote in emotes)
    {
        topEmotesSb.Append($"<div class=\"emote-item\"><img src=\"/images/{emote.LocalPath}\" /> <span>{emote.UsageCount}</span></div>");
    }
    template = template.Replace("{TopEmotes}", topEmotesSb.ToString());

    var allEmotesSb = new StringBuilder();
    foreach (var emote in emoteData.Select(s => new { s.Name, s.LocalPath }).Distinct())
    {
        allEmotesSb.Append($"<img src=\"/images/{emote.LocalPath}\" />");
    }
    template = template.Replace("{AllEmotes}", allEmotesSb.ToString());


    // Normal emoji count
    var emojiRegex = @"[#*0-9]\uFE0F?\u20E3|\u00A9\uFE0F?|[\u00AE\u203C\u2049\u2122\u2139\u2194-\u2199\u21A9\u21AA]\uFE0F?|[\u231A\u231B]|[\u2328\u23CF]\uFE0F?|[\u23E9-\u23EC]|[\u23ED-\u23EF]\uFE0F?|\u23F0|[\u23F1\u23F2]\uFE0F?|\u23F3|[\u23F8-\u23FA\u24C2\u25AA\u25AB\u25B6\u25C0\u25FB\u25FC]\uFE0F?|[\u25FD\u25FE]|[\u2600-\u2604\u260E\u2611]\uFE0F?|[\u2614\u2615]|\u2618\uFE0F?|\u261D(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F)?|[\u2620\u2622\u2623\u2626\u262A\u262E\u262F\u2638-\u263A\u2640\u2642]\uFE0F?|[\u2648-\u2653]|[\u265F\u2660\u2663\u2665\u2666\u2668\u267B\u267E]\uFE0F?|\u267F|\u2692\uFE0F?|\u2693|[\u2694-\u2697\u2699\u269B\u269C\u26A0]\uFE0F?|\u26A1|\u26A7\uFE0F?|[\u26AA\u26AB]|[\u26B0\u26B1]\uFE0F?|[\u26BD\u26BE\u26C4\u26C5]|\u26C8\uFE0F?|\u26CE|[\u26CF\u26D1]\uFE0F?|\u26D3(?:\u200D\uD83D\uDCA5|\uFE0F(?:\u200D\uD83D\uDCA5)?)?|\u26D4|\u26E9\uFE0F?|\u26EA|[\u26F0\u26F1]\uFE0F?|[\u26F2\u26F3]|\u26F4\uFE0F?|\u26F5|[\u26F7\u26F8]\uFE0F?|\u26F9(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?|\uFE0F(?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\u26FA\u26FD]|\u2702\uFE0F?|\u2705|[\u2708\u2709]\uFE0F?|[\u270A\u270B](?:\uD83C[\uDFFB-\uDFFF])?|[\u270C\u270D](?:\uD83C[\uDFFB-\uDFFF]|\uFE0F)?|\u270F\uFE0F?|[\u2712\u2714\u2716\u271D\u2721]\uFE0F?|\u2728|[\u2733\u2734\u2744\u2747]\uFE0F?|[\u274C\u274E\u2753-\u2755\u2757]|\u2763\uFE0F?|\u2764(?:\u200D(?:\uD83D\uDD25|\uD83E\uDE79)|\uFE0F(?:\u200D(?:\uD83D\uDD25|\uD83E\uDE79))?)?|[\u2795-\u2797]|\u27A1\uFE0F?|[\u27B0\u27BF]|[\u2934\u2935\u2B05-\u2B07]\uFE0F?|[\u2B1B\u2B1C\u2B50\u2B55]|[\u3030\u303D\u3297\u3299]\uFE0F?|\uD83C(?:[\uDC04\uDCCF]|[\uDD70\uDD71\uDD7E\uDD7F]\uFE0F?|[\uDD8E\uDD91-\uDD9A]|\uDDE6\uD83C[\uDDE8-\uDDEC\uDDEE\uDDF1\uDDF2\uDDF4\uDDF6-\uDDFA\uDDFC\uDDFD\uDDFF]|\uDDE7\uD83C[\uDDE6\uDDE7\uDDE9-\uDDEF\uDDF1-\uDDF4\uDDF6-\uDDF9\uDDFB\uDDFC\uDDFE\uDDFF]|\uDDE8\uD83C[\uDDE6\uDDE8\uDDE9\uDDEB-\uDDEE\uDDF0-\uDDF7\uDDFA-\uDDFF]|\uDDE9\uD83C[\uDDEA\uDDEC\uDDEF\uDDF0\uDDF2\uDDF4\uDDFF]|\uDDEA\uD83C[\uDDE6\uDDE8\uDDEA\uDDEC\uDDED\uDDF7-\uDDFA]|\uDDEB\uD83C[\uDDEE-\uDDF0\uDDF2\uDDF4\uDDF7]|\uDDEC\uD83C[\uDDE6\uDDE7\uDDE9-\uDDEE\uDDF1-\uDDF3\uDDF5-\uDDFA\uDDFC\uDDFE]|\uDDED\uD83C[\uDDF0\uDDF2\uDDF3\uDDF7\uDDF9\uDDFA]|\uDDEE\uD83C[\uDDE8-\uDDEA\uDDF1-\uDDF4\uDDF6-\uDDF9]|\uDDEF\uD83C[\uDDEA\uDDF2\uDDF4\uDDF5]|\uDDF0\uD83C[\uDDEA\uDDEC-\uDDEE\uDDF2\uDDF3\uDDF5\uDDF7\uDDFC\uDDFE\uDDFF]|\uDDF1\uD83C[\uDDE6-\uDDE8\uDDEE\uDDF0\uDDF7-\uDDFB\uDDFE]|\uDDF2\uD83C[\uDDE6\uDDE8-\uDDED\uDDF0-\uDDFF]|\uDDF3\uD83C[\uDDE6\uDDE8\uDDEA-\uDDEC\uDDEE\uDDF1\uDDF4\uDDF5\uDDF7\uDDFA\uDDFF]|\uDDF4\uD83C\uDDF2|\uDDF5\uD83C[\uDDE6\uDDEA-\uDDED\uDDF0-\uDDF3\uDDF7-\uDDF9\uDDFC\uDDFE]|\uDDF6\uD83C\uDDE6|\uDDF7\uD83C[\uDDEA\uDDF4\uDDF8\uDDFA\uDDFC]|\uDDF8\uD83C[\uDDE6-\uDDEA\uDDEC-\uDDF4\uDDF7-\uDDF9\uDDFB\uDDFD-\uDDFF]|\uDDF9\uD83C[\uDDE6\uDDE8\uDDE9\uDDEB-\uDDED\uDDEF-\uDDF4\uDDF7\uDDF9\uDDFB\uDDFC\uDDFF]|\uDDFA\uD83C[\uDDE6\uDDEC\uDDF2\uDDF3\uDDF8\uDDFE\uDDFF]|\uDDFB\uD83C[\uDDE6\uDDE8\uDDEA\uDDEC\uDDEE\uDDF3\uDDFA]|\uDDFC\uD83C[\uDDEB\uDDF8]|\uDDFD\uD83C\uDDF0|\uDDFE\uD83C[\uDDEA\uDDF9]|\uDDFF\uD83C[\uDDE6\uDDF2\uDDFC]|\uDE01|\uDE02\uFE0F?|[\uDE1A\uDE2F\uDE32-\uDE36]|\uDE37\uFE0F?|[\uDE38-\uDE3A\uDE50\uDE51\uDF00-\uDF20]|[\uDF21\uDF24-\uDF2C]\uFE0F?|[\uDF2D-\uDF35]|\uDF36\uFE0F?|[\uDF37-\uDF43]|\uDF44(?:\u200D\uD83D\uDFEB)?|[\uDF45-\uDF4A]|\uDF4B(?:\u200D\uD83D\uDFE9)?|[\uDF4C-\uDF7C]|\uDF7D\uFE0F?|[\uDF7E-\uDF84]|\uDF85(?:\uD83C[\uDFFB-\uDFFF])?|[\uDF86-\uDF93]|[\uDF96\uDF97\uDF99-\uDF9B\uDF9E\uDF9F]\uFE0F?|[\uDFA0-\uDFC1]|\uDFC2(?:\uD83C[\uDFFB-\uDFFF])?|\uDFC3(?:\u200D(?:[\u2640\u2642](?:\u200D\u27A1\uFE0F?|\uFE0F(?:\u200D\u27A1\uFE0F?)?)?|\u27A1\uFE0F?)|\uD83C[\uDFFB-\uDFFF](?:\u200D(?:[\u2640\u2642](?:\u200D\u27A1\uFE0F?|\uFE0F(?:\u200D\u27A1\uFE0F?)?)?|\u27A1\uFE0F?))?)?|\uDFC4(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDFC5\uDFC6]|\uDFC7(?:\uD83C[\uDFFB-\uDFFF])?|[\uDFC8\uDFC9]|\uDFCA(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDFCB\uDFCC](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?|\uFE0F(?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDFCD\uDFCE]\uFE0F?|[\uDFCF-\uDFD3]|[\uDFD4-\uDFDF]\uFE0F?|[\uDFE0-\uDFF0]|\uDFF3(?:\u200D(?:\u26A7\uFE0F?|\uD83C\uDF08)|\uFE0F(?:\u200D(?:\u26A7\uFE0F?|\uD83C\uDF08))?)?|\uDFF4(?:\u200D\u2620\uFE0F?|\uDB40\uDC67\uDB40\uDC62\uDB40(?:\uDC65\uDB40\uDC6E\uDB40\uDC67|\uDC73\uDB40\uDC63\uDB40\uDC74|\uDC77\uDB40\uDC6C\uDB40\uDC73)\uDB40\uDC7F)?|[\uDFF5\uDFF7]\uFE0F?|[\uDFF8-\uDFFF])|\uD83D(?:[\uDC00-\uDC07]|\uDC08(?:\u200D\u2B1B)?|[\uDC09-\uDC14]|\uDC15(?:\u200D\uD83E\uDDBA)?|[\uDC16-\uDC25]|\uDC26(?:\u200D(?:\u2B1B|\uD83D\uDD25))?|[\uDC27-\uDC3A]|\uDC3B(?:\u200D\u2744\uFE0F?)?|[\uDC3C-\uDC3E]|\uDC3F\uFE0F?|\uDC40|\uDC41(?:\u200D\uD83D\uDDE8\uFE0F?|\uFE0F(?:\u200D\uD83D\uDDE8\uFE0F?)?)?|[\uDC42\uDC43](?:\uD83C[\uDFFB-\uDFFF])?|[\uDC44\uDC45]|[\uDC46-\uDC50](?:\uD83C[\uDFFB-\uDFFF])?|[\uDC51-\uDC65]|[\uDC66\uDC67](?:\uD83C[\uDFFB-\uDFFF])?|\uDC68(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?|[\uDC68\uDC69]\u200D\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?)|[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92])|\uD83E(?:\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?))|\uD83C(?:\uDFFB(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFC-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFC(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFB\uDFFD-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFD(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFB\uDFFC\uDFFE\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFE(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFB-\uDFFD\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFF(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFB-\uDFFE]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?))?|\uDC69(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?[\uDC68\uDC69]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?|\uDC69\u200D\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?)|[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92])|\uD83E(?:\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?))|\uD83C(?:\uDFFB(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFC-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFC(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB\uDFFD-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFD(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB\uDFFC\uDFFE\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFE(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFD\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFF(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFE]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?))?|\uDC6A|[\uDC6B-\uDC6D](?:\uD83C[\uDFFB-\uDFFF])?|\uDC6E(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDC6F(?:\u200D[\u2640\u2642]\uFE0F?)?|[\uDC70\uDC71](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDC72(?:\uD83C[\uDFFB-\uDFFF])?|\uDC73(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDC74-\uDC76](?:\uD83C[\uDFFB-\uDFFF])?|\uDC77(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDC78(?:\uD83C[\uDFFB-\uDFFF])?|[\uDC79-\uDC7B]|\uDC7C(?:\uD83C[\uDFFB-\uDFFF])?|[\uDC7D-\uDC80]|[\uDC81\uDC82](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDC83(?:\uD83C[\uDFFB-\uDFFF])?|\uDC84|\uDC85(?:\uD83C[\uDFFB-\uDFFF])?|[\uDC86\uDC87](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDC88-\uDC8E]|\uDC8F(?:\uD83C[\uDFFB-\uDFFF])?|\uDC90|\uDC91(?:\uD83C[\uDFFB-\uDFFF])?|[\uDC92-\uDCA9]|\uDCAA(?:\uD83C[\uDFFB-\uDFFF])?|[\uDCAB-\uDCFC]|\uDCFD\uFE0F?|[\uDCFF-\uDD3D]|[\uDD49\uDD4A]\uFE0F?|[\uDD4B-\uDD4E\uDD50-\uDD67]|[\uDD6F\uDD70\uDD73]\uFE0F?|\uDD74(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F)?|\uDD75(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?|\uFE0F(?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDD76-\uDD79]\uFE0F?|\uDD7A(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD87\uDD8A-\uDD8D]\uFE0F?|\uDD90(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F)?|[\uDD95\uDD96](?:\uD83C[\uDFFB-\uDFFF])?|\uDDA4|[\uDDA5\uDDA8\uDDB1\uDDB2\uDDBC\uDDC2-\uDDC4\uDDD1-\uDDD3\uDDDC-\uDDDE\uDDE1\uDDE3\uDDE8\uDDEF\uDDF3\uDDFA]\uFE0F?|[\uDDFB-\uDE2D]|\uDE2E(?:\u200D\uD83D\uDCA8)?|[\uDE2F-\uDE34]|\uDE35(?:\u200D\uD83D\uDCAB)?|\uDE36(?:\u200D\uD83C\uDF2B\uFE0F?)?|[\uDE37-\uDE41]|\uDE42(?:\u200D[\u2194\u2195]\uFE0F?)?|[\uDE43\uDE44]|[\uDE45-\uDE47](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDE48-\uDE4A]|\uDE4B(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDE4C(?:\uD83C[\uDFFB-\uDFFF])?|[\uDE4D\uDE4E](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDE4F(?:\uD83C[\uDFFB-\uDFFF])?|[\uDE80-\uDEA2]|\uDEA3(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDEA4-\uDEB3]|[\uDEB4\uDEB5](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDEB6(?:\u200D(?:[\u2640\u2642](?:\u200D\u27A1\uFE0F?|\uFE0F(?:\u200D\u27A1\uFE0F?)?)?|\u27A1\uFE0F?)|\uD83C[\uDFFB-\uDFFF](?:\u200D(?:[\u2640\u2642](?:\u200D\u27A1\uFE0F?|\uFE0F(?:\u200D\u27A1\uFE0F?)?)?|\u27A1\uFE0F?))?)?|[\uDEB7-\uDEBF]|\uDEC0(?:\uD83C[\uDFFB-\uDFFF])?|[\uDEC1-\uDEC5]|\uDECB\uFE0F?|\uDECC(?:\uD83C[\uDFFB-\uDFFF])?|[\uDECD-\uDECF]\uFE0F?|[\uDED0-\uDED2\uDED5-\uDED7\uDEDC-\uDEDF]|[\uDEE0-\uDEE5\uDEE9]\uFE0F?|[\uDEEB\uDEEC]|[\uDEF0\uDEF3]\uFE0F?|[\uDEF4-\uDEFC\uDFE0-\uDFEB\uDFF0])|\uD83E(?:\uDD0C(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD0D\uDD0E]|\uDD0F(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD10-\uDD17]|[\uDD18-\uDD1F](?:\uD83C[\uDFFB-\uDFFF])?|[\uDD20-\uDD25]|\uDD26(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDD27-\uDD2F]|[\uDD30-\uDD34](?:\uD83C[\uDFFB-\uDFFF])?|\uDD35(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDD36(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD37-\uDD39](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDD3A|\uDD3C(?:\u200D[\u2640\u2642]\uFE0F?)?|[\uDD3D\uDD3E](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDD3F-\uDD45\uDD47-\uDD76]|\uDD77(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD78-\uDDB4]|[\uDDB5\uDDB6](?:\uD83C[\uDFFB-\uDFFF])?|\uDDB7|[\uDDB8\uDDB9](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDDBA|\uDDBB(?:\uD83C[\uDFFB-\uDFFF])?|[\uDDBC-\uDDCC]|\uDDCD(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDDCE(?:\u200D(?:[\u2640\u2642](?:\u200D\u27A1\uFE0F?|\uFE0F(?:\u200D\u27A1\uFE0F?)?)?|\u27A1\uFE0F?)|\uD83C[\uDFFB-\uDFFF](?:\u200D(?:[\u2640\u2642](?:\u200D\u27A1\uFE0F?|\uFE0F(?:\u200D\u27A1\uFE0F?)?)?|\u27A1\uFE0F?))?)?|\uDDCF(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDDD0|\uDDD1(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?|(?:\uDDD1\u200D\uD83E)?\uDDD2(?:\u200D\uD83E\uDDD2)?))|\uD83C(?:\uDFFB(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFC-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFC(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFB\uDFFD-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFD(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFB\uDFFC\uDFFE\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFE(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFB-\uDFFD\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?|\uDFFF(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFB-\uDFFE]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|\uDDAF(?:\u200D\u27A1\uFE0F?)?|[\uDDB0-\uDDB3]|[\uDDBC\uDDBD](?:\u200D\u27A1\uFE0F?)?)))?))?|[\uDDD2\uDDD3](?:\uD83C[\uDFFB-\uDFFF])?|\uDDD4(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDDD5(?:\uD83C[\uDFFB-\uDFFF])?|[\uDDD6-\uDDDD](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDDDE\uDDDF](?:\u200D[\u2640\u2642]\uFE0F?)?|[\uDDE0-\uDDFF\uDE70-\uDE7C\uDE80-\uDE89\uDE8F-\uDEC2]|[\uDEC3-\uDEC5](?:\uD83C[\uDFFB-\uDFFF])?|[\uDEC6\uDECE-\uDEDC\uDEDF-\uDEE9]|\uDEF0(?:\uD83C[\uDFFB-\uDFFF])?|\uDEF1(?:\uD83C(?:\uDFFB(?:\u200D\uD83E\uDEF2\uD83C[\uDFFC-\uDFFF])?|\uDFFC(?:\u200D\uD83E\uDEF2\uD83C[\uDFFB\uDFFD-\uDFFF])?|\uDFFD(?:\u200D\uD83E\uDEF2\uD83C[\uDFFB\uDFFC\uDFFE\uDFFF])?|\uDFFE(?:\u200D\uD83E\uDEF2\uD83C[\uDFFB-\uDFFD\uDFFF])?|\uDFFF(?:\u200D\uD83E\uDEF2\uD83C[\uDFFB-\uDFFE])?))?|[\uDEF2-\uDEF8](?:\uD83C[\uDFFB-\uDFFF])?)";
    var emojiMatches = Regex.Matches(mergedText, emojiRegex);
    var allEmojies = string.Join(" ", emojiMatches);
    template = template.Replace("{AllEmojies}", allEmojies);



    // Names
    var displayNames = messages.Select(s => s.DisplayAuthor).Distinct();
    template = template.Replace("{DisplayNames}", string.Join(", ", displayNames));

    var messagesPerName = messages.GroupBy(gb => gb.DisplayAuthor).Select(gb => new { Name = gb.Key, Count = gb.Count() });
    var nameKeys = string.Empty;
    var nameValues = string.Empty;
    foreach (var c in messagesPerName)
    {
        nameKeys = $"{nameKeys}'{c.Name}',";
        nameValues = $"{nameValues}{c.Count},";
    }

    nameKeys = nameKeys.Trim(',');
    nameValues = nameValues.Trim(',');

    template = template.Replace("{NameLabels}", nameKeys);
    template = template.Replace("{NameValues}", nameValues);
    template = template.Replace("{NameLabels}", string.Join(',', displayNames));


    // Stats
    template = template.Replace("{MessagesSent}", Convert.ToString(messages.Count));
    var lengthOrdered = messages.OrderBy(ob => ob.Content.Length);
    var averageLength = lengthOrdered.Select(s => s.Content.Length).Sum() / lengthOrdered.Count();

    template = template.Replace("{AverageLength}", Convert.ToString(averageLength));


    // Messages by time of day
    var messagesByTimeOfDay = messages.GroupBy(gb => gb.Timestamp.Value.Hour);
    var hoursOfTheDay = new int[24];
    foreach (var group in messagesByTimeOfDay)
    {
        hoursOfTheDay[group.Key] = group.Count();
    }
    string messagesByHour = string.Join(",", hoursOfTheDay);
    template = template.Replace("{MessagesByHour}", messagesByHour);



    // Messages per month
    var messagesJan = messages.Count(w => w.Timestamp >= new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 2, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesFeb = messages.Count(w => w.Timestamp >= new DateTime(year, 2, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 3, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesMar = messages.Count(w => w.Timestamp >= new DateTime(year, 3, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesApr = messages.Count(w => w.Timestamp >= new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesMay = messages.Count(w => w.Timestamp >= new DateTime(year, 5, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesJun = messages.Count(w => w.Timestamp >= new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesJul = messages.Count(w => w.Timestamp >= new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 8, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesAug = messages.Count(w => w.Timestamp >= new DateTime(year, 8, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 9, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesSep = messages.Count(w => w.Timestamp >= new DateTime(year, 9, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesOct = messages.Count(w => w.Timestamp >= new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 11, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesNov = messages.Count(w => w.Timestamp >= new DateTime(year, 11, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year, 12, 1, 0, 0, 0, DateTimeKind.Utc));
    var messagesDec = messages.Count(w => w.Timestamp >= new DateTime(year, 12, 1, 0, 0, 0, DateTimeKind.Utc) && w.Timestamp < new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    template = template.Replace("{MessagesJan}", Convert.ToString(messagesJan));
    template = template.Replace("{MessagesFeb}", Convert.ToString(messagesFeb));
    template = template.Replace("{MessagesMar}", Convert.ToString(messagesMar));
    template = template.Replace("{MessagesApr}", Convert.ToString(messagesApr));
    template = template.Replace("{MessagesMay}", Convert.ToString(messagesMay));
    template = template.Replace("{MessagesJun}", Convert.ToString(messagesJun));
    template = template.Replace("{MessagesJul}", Convert.ToString(messagesJul));
    template = template.Replace("{MessagesAug}", Convert.ToString(messagesAug));
    template = template.Replace("{MessagesSep}", Convert.ToString(messagesSep));
    template = template.Replace("{MessagesOct}", Convert.ToString(messagesOct));
    template = template.Replace("{MessagesNov}", Convert.ToString(messagesNov));
    template = template.Replace("{MessagesDec}", Convert.ToString(messagesDec));


    // Messages per channel
    var channels = messages.GroupBy(gb => gb.Channel);
    var keys = string.Empty;
    var values = string.Empty;
    foreach (var c in channels)
    {
        keys = $"{keys}'{c.Key}',";
        values = $"{values}{c.Count()},";
    }

    keys = keys.Trim(',');
    values = values.Trim(',');

    template = template.Replace("{ChannelNames}", keys);
    template = template.Replace("{ChannelPercentages}", values);


    // Milestones
    var nextThresholdIndex = 0;
    var cumulativeCount = 0;
    DateTime? startDate = null;
    var dayGroupedMessages = messages.GroupBy(gb => gb.Timestamp.Value.Date).Select(s => new { Date = s.Key, Count = s.Count() });
    foreach (var dayGroupedMessage in dayGroupedMessages)
    {
        cumulativeCount += dayGroupedMessage.Count;
        while (nextThresholdIndex < Utilities.Milestones.Length && cumulativeCount >= Utilities.Milestones[nextThresholdIndex])
        {
            if (startDate == null)
            {
                startDate = dayGroupedMessage.Date;
            }
            var days = (dayGroupedMessage.Date - startDate.Value).Days;

            var daysDescription = days == 1 ? "day" : "days";
            template = template.Replace($"{{Messages{Utilities.Milestones[nextThresholdIndex]}}}", $"{days} {daysDescription}");
            template = template.Replace($"class=\"hide{Utilities.Milestones[nextThresholdIndex]}\"", string.Empty);

            nextThresholdIndex++;
            if (nextThresholdIndex < Utilities.Milestones.Length)
            {
                startDate = dayGroupedMessage.Date;
            }
        }
    }

    return Results.Content(template, "text/html");
});

await app.RunAsync();

static async Task<List<Message>> GetData(string authorid, int year, string guildId, string dbPath)
{
    var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
    await connection.OpenAsync();

    var insertQuery = @"
SELECT AuthorId, GlobalAuthor, DisplayAuthor, Content, Channel, Guild, Timestamp
FROM Messages
WHERE
AuthorId = @authorId
AND
Timestamp >= @yearStart
and
Timestamp < @yearEnd
and
GuildId = @guildId";
    var command = new SQLiteCommand(insertQuery, connection);
    command.Parameters.AddWithValue("@authorId", authorid);
    command.Parameters.AddWithValue("@yearStart", new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    command.Parameters.AddWithValue("@yearEnd", new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    command.Parameters.AddWithValue("@guildId", guildId);
    var reader = await command.ExecuteReaderAsync();

    var messages = new List<Message>();
    while (await reader.ReadAsync())
    {
        var message = new Message
        {
            AuthorId = reader["AuthorId"] != DBNull.Value ? (string)reader["AuthorId"] : null,
            GlobalAuthor = reader["GlobalAuthor"] != DBNull.Value ? (string)reader["GlobalAuthor"] : null,
            DisplayAuthor = reader["DisplayAuthor"] != DBNull.Value ? (string)reader["DisplayAuthor"] : null,
            Channel = reader["Channel"] != DBNull.Value ? (string)reader["Channel"] : null,
            Content = reader["Content"] != DBNull.Value ? (string)reader["Content"] : null,
            Guild = reader["Guild"] != DBNull.Value ? (string)reader["Guild"] : null,
            Timestamp = reader["Timestamp"] != DBNull.Value ? (DateTime)reader["Timestamp"] : null
        };
        messages.Add(message);
    }
    await connection.CloseAsync();

    return messages;
}

static async Task<List<Emoji>> GetEmoteData(int year, string authorId, string dbPath, string guildId)
{
    var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
    await connection.OpenAsync();

    var insertQuery = @"
SELECT AuthorId, Name, Value, Url, LocalPath, Timestamp
FROM Emojis
WHERE
AuthorId = @authorId
AND
Timestamp >= @yearStart
AND
Timestamp < @yearEnd
AND
GuildId = @guildId";
    var command = new SQLiteCommand(insertQuery, connection);
    command.Parameters.AddWithValue("@authorId", authorId);
    command.Parameters.AddWithValue("@yearStart", new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    command.Parameters.AddWithValue("@yearEnd", new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    command.Parameters.AddWithValue("@guildId", guildId);
    var reader = await command.ExecuteReaderAsync();

    var messages = new List<Emoji>();
    while (await reader.ReadAsync())
    {
        var emoji = new Emoji
        {
            AuthorId = reader["AuthorId"] != DBNull.Value ? (string)reader["AuthorId"] : null,
            Name = reader["Name"] != DBNull.Value ? (string)reader["Name"] : null,
            Value = reader["Value"] != DBNull.Value ? (string)reader["Value"] : null,
            Url = reader["Url"] != DBNull.Value ? (string)reader["Url"] : null,
            LocalPath = reader["LocalPath"] != DBNull.Value ? (string)reader["LocalPath"] : null,
            Timestamp = reader["Timestamp"] != DBNull.Value ? (DateTime)reader["Timestamp"] : null
        };
        messages.Add(emoji);
    }
    await connection.CloseAsync();

    return messages;
}

namespace Diggcord.DiscordUnwrapped.Web
{
    public static class Utilities
    {
        public static readonly int[] Milestones = [1, 100, 1000, 10000, 100000, 1000000, 10000000];


        public static string[] GetStopWords()
        {
            var stopwords = File.ReadAllLines("stopwords.txt");
            return stopwords;
        }
    }
}