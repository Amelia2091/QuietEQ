using System.Net;
using System.Text.RegularExpressions;

namespace QuietEQ.Core;

public sealed class AutoEqClient(HttpClient http, string cacheDirectory)
{
    private const string Root = "https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/";
    public bool UsedCache { get; private set; }
    public DateTime? CacheTime { get; private set; }

    public async Task<IReadOnlyList<AutoEqEntry>> LoadIndexAsync(CancellationToken token = default)
    {
        var cache = Path.Combine(cacheDirectory, "autoeq-index.md");
        UsedCache = false;
        string text;
        try
        {
            text = await http.GetStringAsync(Root + "INDEX.md", token);
            if (ParseIndex(text).Count < 1) throw new InvalidDataException("AutoEQ 目录格式发生变化。");
            Directory.CreateDirectory(cacheDirectory);
            AtomicFile.Write(cache, text);
        }
        catch (Exception e) when (File.Exists(cache) && (e is HttpRequestException or TaskCanceledException or InvalidDataException) && !token.IsCancellationRequested)
        {
            text = await File.ReadAllTextAsync(cache, token);
            UsedCache = true;
        }
        CacheTime = File.Exists(cache) ? File.GetLastWriteTime(cache) : null;
        return ParseIndex(text);
    }

    public static IReadOnlyList<AutoEqEntry> ParseIndex(string markdown)
    {
        var result = new List<AutoEqEntry>();
        // Greedy path match deliberately handles parentheses in model names and directory names.
        var regex = new Regex(@"^- \[(.+)\]\(\./(.+)\) by (.+)$");
        foreach (var line in markdown.Replace("\r", "").Split('\n'))
        {
            var m = regex.Match(line.Trim());
            if (!m.Success) continue;
            var path = m.Groups[2].Value;
            var decoded = Uri.UnescapeDataString(path);
            if (decoded.Split('/').Any(p => p is "." or ".." || p.Contains('\\')) || decoded.Contains('?') || decoded.Contains('#')) continue;
            result.Add(new AutoEqEntry(WebUtility.HtmlDecode(m.Groups[1].Value), path, m.Groups[3].Value));
        }
        return result;
    }

    public static IReadOnlyList<AutoEqEntry> Search(IEnumerable<AutoEqEntry> index, string query) => index
        .Where(e => query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term => (e.Name + " " + e.Source).Contains(term, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<EqEffect> DownloadAsync(AutoEqEntry entry, bool graphic, CancellationToken token = default)
    {
        var path = string.Join('/', entry.RelativePath.Split('/').Select(p => Uri.EscapeDataString(Uri.UnescapeDataString(p))));
        var filename = entry.ModelFolder + (graphic ? " GraphicEQ.txt" : " ParametricEQ.txt");
        var url = Root + path + "/" + Uri.EscapeDataString(filename);
        var content = await http.GetStringAsync(url, token);
        if (content.Length > 1_000_000) throw new InvalidDataException("下载的 EQ 文件过大。");
        var effect = EqCodec.Parse(content, entry.Name);
        effect.Source = "AutoEQ · " + entry.Source;
        effect.SourceUrl = url;
        return effect;
    }
}
