using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuietEQ.Core;

public sealed record CompiledConfig(string Text, Dictionary<string, string> Effects);
public sealed record ApplyReceipt(string ConfigHash, string Revision, string? BackupPath);

public static class ConfigCompiler
{
    public static string EndpointGuid(string id)
    {
        var start = id.LastIndexOf('{');
        if (start < 0 || !Guid.TryParseExact(id[start..], "B", out var guid)) throw new ArgumentException("设备标识无效，请重新选择 Windows 输出设备。");
        return guid.ToString("B");
    }

    public static CompiledConfig Compile(LibraryState state, string relativeDirectory)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(relativeDirectory, @"^QuietEQ\\[a-zA-Z0-9_-]+$")) throw new ArgumentException("配置目录标识无效。");
        var s = new StringBuilder("# Managed by QuietEQ. Device bindings remain active when the app is closed.\r\n");
        var files = new Dictionary<string, string>();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in state.Profiles.Where(p => p.Enabled))
        {
            var device = EndpointGuid(p.DeviceId);
            if (!selected.Add(device)) throw new InvalidOperationException($"设备「{p.DeviceName}」启用了多份配置，请只保留一份。");
            var effect = state.Effects.SingleOrDefault(e => e.Id == p.EffectId) ?? throw new InvalidOperationException($"配置「{p.Name}」引用的 EQ 已不存在。");
            if (!Guid.TryParseExact(effect.Id, "N", out _)) throw new ArgumentException("EQ 标识无效。");
            var file = effect.Id + ".txt";
            files[file] = EqCodec.Serialize(effect);
            s.AppendLine().AppendLine($"# {SafeComment(p.Name)} | {SafeComment(p.DeviceName)}");
            s.AppendLine($"Device: {device}").AppendLine("Channel: ALL").AppendLine($"Include: {relativeDirectory}\\{file}");
        }
        // Reset scope so later manually appended commands do not inherit the last device.
        s.AppendLine().AppendLine("Device: all").AppendLine("Channel: ALL");
        return new CompiledConfig(s.ToString(), files);
    }
    private static string SafeComment(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
    public static string Revision(LibraryState state) => Hash(Compile(state, @"QuietEQ\preview").Text + string.Concat(state.Effects.OrderBy(e => e.Id).Select(e => e.Id + EqCodec.Serialize(e))));
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public sealed class ConfigInstaller
{
    public static string? ReadHash(string configDirectory)
    {
        var file = Path.Combine(configDirectory, "config.txt");
        return File.Exists(file) ? ConfigCompiler.Hash(File.ReadAllText(file)) : null;
    }

    public ApplyReceipt Apply(LibraryState state, string configDirectory, string? expectedHash)
    {
        if (!Directory.Exists(configDirectory)) throw new DirectoryNotFoundException("找不到 Equalizer APO 配置目录。");
        if (ReadHash(configDirectory) != expectedHash) throw new IOException("config.txt 已被其他程序更改。请重新预览后应用。");
        var generation = DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        var compiled = ConfigCompiler.Compile(state, @"QuietEQ\" + generation);
        var generatedDir = Path.Combine(configDirectory, "QuietEQ", generation);
        Directory.CreateDirectory(generatedDir);
        foreach (var file in compiled.Effects) AtomicFile.Write(Path.Combine(generatedDir, file.Key), file.Value);
        // All includes are complete before the one atomic root-file swap. Old generations are kept for rollback.
        var target = Path.Combine(configDirectory, "config.txt");
        var backupDirectory = Path.Combine(configDirectory, "QuietEQ", "backups");
        Directory.CreateDirectory(backupDirectory);
        string? backup = null;
        if (File.Exists(target))
        {
            backup = Path.Combine(backupDirectory, generation + ".txt");
            File.Copy(target, backup, false);
        }
        if (ReadHash(configDirectory) != expectedHash) throw new IOException("配置在写入前发生变化，已取消应用。");
        AtomicFile.Write(target, compiled.Text);
        var hash = ConfigCompiler.Hash(compiled.Text);
        if (ReadHash(configDirectory) != hash) throw new IOException("写入后配置已被外部程序改变，请退出 Peace 后重试。");
        AtomicFile.Write(Path.Combine(backupDirectory, "last-apply.json"), JsonSerializer.Serialize(new RollbackRecord(backup, hash)));
        return new ApplyReceipt(hash, ConfigCompiler.Revision(state), backup);
    }

    public void Restore(string configDirectory)
    {
        var backupDir = Path.Combine(configDirectory, "QuietEQ", "backups");
        var receiptPath = Path.Combine(backupDir, "last-apply.json");
        if (!File.Exists(receiptPath)) throw new InvalidOperationException("没有可恢复的备份。");
        var record = JsonSerializer.Deserialize<RollbackRecord>(File.ReadAllText(receiptPath)) ?? throw new InvalidDataException("备份记录无效。");
        if (ReadHash(configDirectory) != record.AppliedHash) throw new IOException("配置已被其他程序修改，无法自动恢复以免覆盖外部更改。");
        if (record.BackupPath is null) throw new InvalidOperationException("应用前没有 config.txt；请清空启用配置后重新应用。");
        var path = Path.GetFullPath(record.BackupPath);
        if (!path.StartsWith(Path.GetFullPath(backupDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份路径无效。");
        AtomicFile.Write(Path.Combine(configDirectory, "config.txt"), File.ReadAllText(path));
        File.Delete(receiptPath);
    }
    private sealed record RollbackRecord(string? BackupPath, string AppliedHash);
}
