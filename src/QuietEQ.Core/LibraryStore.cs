using System.Text.Json;

namespace QuietEQ.Core;

public sealed class LibraryStore(string root)
{
    public string Root { get; } = root;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public LibraryState Load()
    {
        var path = Path.Combine(Root, "library.json");
        if (!File.Exists(path)) return new LibraryState();
        var result = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("效果库为空。");
        if (result.Version != 1) throw new InvalidDataException("效果库版本不受支持，请使用匹配的应用版本。");
        return result;
    }

    public void Save(LibraryState state)
    {
        // Validate the full snapshot before replacing the last valid library.
        var rendered = new Dictionary<string, string>();
        foreach (var effect in state.Effects)
        {
            if (!Guid.TryParseExact(effect.Id, "N", out _) || rendered.ContainsKey(effect.Id)) throw new InvalidDataException("效果标识无效或重复。");
            rendered.Add(effect.Id, EqCodec.Serialize(effect));
        }
        Directory.CreateDirectory(Root);
        var effectDir = Path.Combine(Root, "effects");
        Directory.CreateDirectory(effectDir);
        foreach (var effect in rendered) AtomicFile.Write(Path.Combine(effectDir, effect.Key + ".txt"), effect.Value);
        // library.json is authoritative; generated text files are exportable mirrors.
        AtomicFile.Write(Path.Combine(Root, "library.json"), JsonSerializer.Serialize(state, Options));
    }

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}

public static class AtomicFile
{
    public static void Write(string path, string text)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
