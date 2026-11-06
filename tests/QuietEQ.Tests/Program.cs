using System.Globalization;
using System.Net;
using System.Text.Json;
using QuietEQ.Core;

var results = new List<object>();
var failures = 0;
var root = Path.Combine(Path.GetTempPath(), "QuietEQ-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
void Test(string name, Action action)
{
    try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
async Task AsyncTest(string name, Func<Task> action)
{
    try { await action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
const string aId = "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}";
const string bId = "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}";
LibraryState State()
{
    var a = EqCodec.Parse("Preamp: -6 dB\nFilter 1: ON PK Fc 100 Hz Gain 3 dB Q 1.2", "A EQ");
    var b = EqCodec.Parse("Preamp: -3 dB\nFilter 1: ON HSC Fc 5000 Hz Gain -2 dB Q 0.707", "B EQ");
    return new LibraryState { Effects = [a, b], Profiles = [new() { Name = "A", DeviceId = aId, DeviceName = "Same display name", EffectId = a.Id }, new() { Name = "B", DeviceId = bId, DeviceName = "Same display name", EffectId = b.Id }] };
}
string ConfigDir()
{
    var dir = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "config.txt"), "Include: peace.txt\r\n"); return dir;
}

Test("PEQ parsing, comments, BOM, disabled bands", () =>
{
    var e = EqCodec.Parse("\uFEFF# header\nPreamp: -6.3 dB # comment\nFilter 1: ON LSC Fc 105 Hz Gain 6.5 dB Q 0.70\nFilter 2: OFF PEQ Fc 125 Hz Gain -2.7 dB Q 0.55", "AutoEQ");
    Assert(e.Preamp == -6.3 && e.Bands.Count == 2 && !e.Bands[1].Enabled && e.Bands[1].Type == "PK", "Parse mismatch");
    Assert(EqCodec.Serialize(EqCodec.Parse(EqCodec.Serialize(e), e.Name)) == EqCodec.Serialize(e), "Round trip mismatch");
});
Test("Multiple preamps accumulate", () => Assert(EqCodec.Parse("Preamp: -2 dB\nPreamp: -3 dB", "x").Preamp == -5, "Preamps must add"));
Test("Graphic EQ round trip", () =>
{
    var e = EqCodec.Parse("GraphicEQ: 20 -3.4; 100 0; 1000 -5; 20000 1", "graphic");
    Assert(e.Graphic.Count == 4 && EqCodec.Parse(EqCodec.Serialize(e), "copy").Graphic[2].Gain == -5, "Graphic data lost");
});
Test("Unsupported routing commands rejected", () =>
{
    foreach (var command in new[] { "Include: peace.txt", "Device: all", "Channel: R", "VSTPlugin: Library x.dll", "Filter 1: ON LS Fc 100 Hz Gain 3 dB" }) Throws<FormatException>(() => EqCodec.Parse("Preamp: 0 dB\n" + command, "bad"));
});
Test("Empty and malformed inputs rejected", () =>
{
    Throws<FormatException>(() => EqCodec.Parse("# comment only", "bad"));
    Throws<FormatException>(() => EqCodec.Parse("GraphicEQ:", "bad"));
    Throws<FormatException>(() => EqCodec.Parse("GraphicEQ: 20 0\nGraphicEQ: 100 0", "bad"));
    Throws<ArgumentException>(() => EqCodec.Parse("GraphicEQ: 100 0; 20 0", "bad"));
    Throws<ArgumentException>(() => EqCodec.Parse("GraphicEQ: 100 0; 100 1", "bad"));
});
Test("Reject unsafe numeric parameters", () =>
{
    foreach (var value in new[] { double.NaN, double.PositiveInfinity, 31, -101 }) Throws<ArgumentException>(() => EqCodec.Serialize(new EqEffect { Preamp = value }));
    Throws<ArgumentException>(() => EqCodec.Parse("Filter 1: ON PK Fc 1000 Hz Gain 3 dB Q 0", "bad"));
    Throws<ArgumentException>(() => EqCodec.Parse("Filter 1: ON PK Fc 50000 Hz Gain 3 dB Q 1", "bad"));
});
Test("Invariant formatting in non-English locale", () =>
{
    var previous = CultureInfo.CurrentCulture;
    try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); Assert(EqCodec.Serialize(EqCodec.Parse("Preamp: -6.3 dB", "test")).Contains("-6.3 dB"), "Decimal comma leaked"); }
    finally { CultureInfo.CurrentCulture = previous; }
});
Test("Two same-name devices receive only their own EQ", () =>
{
    var state = State(); var compiled = ConfigCompiler.Compile(state, @"QuietEQ\test");
    foreach (var device in new[] { aId, bId, "{0.0.0.00000000}.{33333333-3333-3333-3333-333333333333}" })
    {
        var scope = "all"; var includes = new List<string>();
        foreach (var line in compiled.Text.Replace("\r", "").Split('\n'))
        {
            if (line.StartsWith("Device: ")) scope = line[8..];
            if (line.StartsWith("Include: ") && (scope == "all" || device.EndsWith(scope, StringComparison.OrdinalIgnoreCase))) includes.Add(line[9..]);
        }
        var profile = state.Profiles.FirstOrDefault(p => p.DeviceId == device);
        Assert(profile is null ? includes.Count == 0 : includes.Count == 1 && includes[0].EndsWith(profile.EffectId + ".txt"), "Device scopes leaked");
    }
    Assert(compiled.Text.Contains("Channel: ALL"), "Channels not reset");
});
Test("Duplicate device bindings rejected, including GUID case variants", () =>
{
    var s = State(); s.Profiles[1].DeviceId = aId.ToUpperInvariant();
    Throws<InvalidOperationException>(() => ConfigCompiler.Compile(s, @"QuietEQ\test"));
});
Test("Disabled profiles emit no filters", () =>
{
    var s = State(); s.Profiles.ForEach(p => p.Enabled = false);
    var c = ConfigCompiler.Compile(s, @"QuietEQ\test"); Assert(c.Effects.Count == 0 && !c.Text.Contains("Include:"), "Disabled EQ emitted");
});
Test("Missing effect blocks configuration", () =>
{
    var s = State(); s.Effects.RemoveAt(0); Throws<InvalidOperationException>(() => ConfigCompiler.Compile(s, @"QuietEQ\test"));
});
Test("Path and device command injection rejected", () =>
{
    var s = State(); Throws<ArgumentException>(() => ConfigCompiler.Compile(s, @"..\bad"));
    s.Profiles[0].DeviceId = aId + "\nDevice: all"; Throws<ArgumentException>(() => ConfigCompiler.Compile(s, @"QuietEQ\test"));
    s = State(); s.Effects[0].Id = "../bad"; s.Profiles[0].EffectId = "../bad"; Throws<ArgumentException>(() => ConfigCompiler.Compile(s, @"QuietEQ\test"));
});
Test("Profile names cannot inject APO instructions", () =>
{
    var s = State(); s.Profiles[0].Name = "name\nPreamp: 25 dB";
    Assert(!ConfigCompiler.Compile(s, @"QuietEQ\test").Text.Replace("\r", "").Split('\n').Contains("Preamp: 25 dB"), "Comment injected");
});
Test("Atomic install writes includes, keeps original backup and restores", () =>
{
    var dir = ConfigDir(); var s = State(); var original = File.ReadAllText(Path.Combine(dir, "config.txt"));
    var receipt = new ConfigInstaller().Apply(s, dir, ConfigInstaller.ReadHash(dir));
    Assert(receipt.BackupPath is not null && File.ReadAllText(receipt.BackupPath) == original, "Original not backed up");
    var rootConfig = File.ReadAllText(Path.Combine(dir, "config.txt"));
    foreach (var include in rootConfig.Replace("\r", "").Split('\n').Where(l => l.StartsWith("Include: ")))
        Assert(File.Exists(Path.Combine(dir, include[9..].Replace('\\', Path.DirectorySeparatorChar))), "Missing include");
    Assert(!rootConfig.Contains("peace.txt") && receipt.ConfigHash == ConfigInstaller.ReadHash(dir), "Root not swapped");
    new ConfigInstaller().Restore(dir); Assert(File.ReadAllText(Path.Combine(dir, "config.txt")) == original, "Restore mismatch");
});
Test("External edits since preview abort application", () =>
{
    var dir = ConfigDir(); var expected = ConfigInstaller.ReadHash(dir); File.WriteAllText(Path.Combine(dir, "config.txt"), "# external");
    Throws<IOException>(() => new ConfigInstaller().Apply(State(), dir, expected));
    Assert(File.ReadAllText(Path.Combine(dir, "config.txt")) == "# external", "External config overwritten");
});
Test("External edits after apply prevent restore", () =>
{
    var dir = ConfigDir(); new ConfigInstaller().Apply(State(), dir, ConfigInstaller.ReadHash(dir));
    File.WriteAllText(Path.Combine(dir, "config.txt"), "# external"); Throws<IOException>(() => new ConfigInstaller().Restore(dir));
});
Test("Validation failure leaves current configuration unchanged", () =>
{
    var dir = ConfigDir(); var before = ConfigInstaller.ReadHash(dir); var s = State(); s.Effects[0].Preamp = double.NaN;
    Throws<ArgumentException>(() => new ConfigInstaller().Apply(s, dir, before)); Assert(ConfigInstaller.ReadHash(dir) == before, "Partial root write");
});
Test("Repeated application preserves previous generation for rollback", () =>
{
    var dir = ConfigDir(); var s = State(); var installer = new ConfigInstaller(); installer.Apply(s, dir, ConfigInstaller.ReadHash(dir));
    var first = File.ReadAllText(Path.Combine(dir, "config.txt")); s.Effects[0].Preamp = -12;
    installer.Apply(s, dir, ConfigInstaller.ReadHash(dir)); installer.Restore(dir);
    Assert(File.ReadAllText(Path.Combine(dir, "config.txt")) == first, "Previous generation lost");
    foreach (var line in first.Replace("\r", "").Split('\n').Where(l => l.StartsWith("Include: "))) Assert(File.Exists(Path.Combine(dir, line[9..].Replace('\\', Path.DirectorySeparatorChar))), "Prior include lost");
});
Test("Library persists EQ and bindings across restart", () =>
{
    var directory = Path.Combine(root, "library"); var s = State(); new LibraryStore(directory).Save(s);
    var loaded = new LibraryStore(directory).Load();
    Assert(loaded.Profiles.Count == 2 && loaded.Effects[0].Bands[0].Frequency == 100, "Persistence mismatch");
    Assert(File.Exists(Path.Combine(directory, "effects", s.Effects[0].Id + ".txt")), "Effect file missing");
});
Test("Edited EQ changes pending revision", () =>
{
    var s = State(); var revision = ConfigCompiler.Revision(s); s.Effects[0].Bands[0].Gain = 5;
    Assert(ConfigCompiler.Revision(s) != revision, "Edit not marked dirty");
});
Test("Invalid library save preserves last valid snapshot", () =>
{
    var directory = Path.Combine(root, "validated-library"); var store = new LibraryStore(directory); var s = State(); store.Save(s);
    var before = File.ReadAllText(Path.Combine(directory, "library.json")); s.Effects[0].Bands[0].Q = 0;
    Throws<ArgumentException>(() => store.Save(s)); Assert(File.ReadAllText(Path.Combine(directory, "library.json")) == before, "Invalid save replaced valid library");
});
Test("Frequency response peak, shelf and disabled band", () =>
{
    var e = EqCodec.Parse("Filter 1: ON PK Fc 1000 Hz Gain 6 dB Q 1", "peak");
    Assert(Math.Abs(FrequencyResponse.At(e, 1000) - 6) < 0.001, "Peak response wrong");
    e.Bands[0].Enabled = false; Assert(Math.Abs(FrequencyResponse.At(e, 1000)) < 0.001, "Disabled filter rendered");
    e = EqCodec.Parse("Filter 1: ON LSC Fc 1000 Hz Gain 6 dB Q 0.707", "shelf");
    Assert(Math.Abs(FrequencyResponse.At(e, 20) - 6) < 0.1 && Math.Abs(FrequencyResponse.At(e, 20000)) < 0.1, "Shelf response wrong");
});
Test("Graphic curve logarithmic interpolation", () =>
{
    var e = EqCodec.Parse("Preamp: -2 dB\nGraphicEQ: 100 0; 10000 10", "g");
    Assert(Math.Abs(FrequencyResponse.At(e, 1000) - 3) < 0.001, "Wrong interpolation");
});
const string sampleIndex = "# Index\n- [Brand Ear (ANC On)](./source/in-ear/Brand%20Ear%20(ANC%20On)) by source on 711\n- [Other](./another/over-ear/Other) by another\n- [Bad](./../escape) by bad\n";
Test("AutoEQ parses model parentheses and source metadata", () =>
{
    var entries = AutoEqClient.ParseIndex(sampleIndex); Assert(entries.Count == 2, "Index parse count");
    Assert(entries[0].ModelFolder == "Brand Ear (ANC On)" && entries[0].Source == "source on 711", "Metadata lost");
    Assert(AutoEqClient.Search(entries, "BRAND 711").Count == 1 && AutoEqClient.Search(entries, "absent").Count == 0, "Search incorrect");
});
await AsyncTest("AutoEQ download URL encoding and parsed EQ", async () =>
{
    string? url = null;
    using var http = new HttpClient(new StubHandler(r => { url = r.RequestUri!.AbsoluteUri; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Preamp: -2.5 dB\nFilter 1: ON PK Fc 500 Hz Gain 2 dB Q 1") }; }));
    var client = new AutoEqClient(http, Path.Combine(root, "cache1"));
    var effect = await client.DownloadAsync(AutoEqClient.ParseIndex(sampleIndex)[0], false);
    Assert(effect.Preamp == -2.5 && effect.Source.Contains("source on 711") && url!.Contains("%20ParametricEQ.txt") && url.Contains("%28ANC%20On%29"), "Download metadata or URI incorrect");
});
await AsyncTest("AutoEQ cached index works offline", async () =>
{
    var cache = Path.Combine(root, "cache2");
    using var good = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sampleIndex) }));
    await new AutoEqClient(good, cache).LoadIndexAsync();
    using var bad = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));
    var client = new AutoEqClient(bad, cache); var list = await client.LoadIndexAsync();
    Assert(client.UsedCache && list.Count == 2, "Offline cache failed");
});
await AsyncTest("HTTP errors do not yield an EQ", async () =>
{
    using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
    try { await new AutoEqClient(http, root).DownloadAsync(AutoEqClient.ParseIndex(sampleIndex)[0], true); throw new Exception("404 accepted"); }
    catch (HttpRequestException) { }
});
if (args.Contains("--network"))
{
    await AsyncTest("LIVE AutoEQ index and HD600 parametric / graphic downloads", async () =>
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) }; http.DefaultRequestHeaders.UserAgent.ParseAdd("QuietEQ.Tests/0.1");
        var client = new AutoEqClient(http, Path.Combine(root, "live-cache")); var list = await client.LoadIndexAsync();
        Assert(list.Count > 1000 && !client.UsedCache, "Live index empty or cached");
        var entry = list.First(x => x.Name == "Sennheiser HD 600" && x.Source.StartsWith("oratory1990"));
        var peq = await client.DownloadAsync(entry, false); var geq = await client.DownloadAsync(entry, true);
        Assert(peq.Bands.Count > 0 && geq.Graphic.Count > 0, "Live files missing bands");
        Console.WriteLine($"LIVE evidence: {list.Count} catalog entries; {peq.Bands.Count} PEQ bands; {geq.Graphic.Count} graphic points; source={peq.SourceUrl}");
    });
}
var report = new { total = results.Count, passed = results.Count - failures, failed = failures, results, temporaryDirectory = root };
var reportIndex = Array.IndexOf(args, "--report");
if (reportIndex >= 0 && reportIndex + 1 < args.Length)
{
    var path = Path.GetFullPath(args[reportIndex + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"RESULT: {results.Count - failures}/{results.Count} passed; {failures} failed. Evidence temp: {root}");
return failures == 0 ? 0 : 1;

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
}
