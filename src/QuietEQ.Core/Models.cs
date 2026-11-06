using System.Text.Json.Serialization;

namespace QuietEQ.Core;

public sealed class EqBand
{
    public bool Enabled { get; set; } = true;
    public string Type { get; set; } = "PK";
    public double Frequency { get; set; } = 1000;
    public double Gain { get; set; }
    public double Q { get; set; } = 0.707;
}

public sealed class GraphicPoint
{
    public double Frequency { get; set; }
    public double Gain { get; set; }
}

public sealed class EqEffect
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新建 EQ";
    public string Source { get; set; } = "本地";
    public string? SourceUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public double Preamp { get; set; }
    public List<EqBand> Bands { get; set; } = [];
    public List<GraphicPoint> Graphic { get; set; } = [];
    [JsonIgnore] public string Format => Graphic.Count > 0 ? "Graphic EQ" : "Parametric EQ";
    [JsonIgnore] public string Detail => $"{Format} · {(Graphic.Count > 0 ? Graphic.Count : Bands.Count)} 个频段 · {Source}";
}

public sealed class DeviceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新配置";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string EffectId { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class LibraryState
{
    public int Version { get; set; } = 1;
    public List<EqEffect> Effects { get; set; } = [];
    public List<DeviceProfile> Profiles { get; set; } = [];
    public string? ConfigDirectory { get; set; }
    public string? LastAppliedHash { get; set; }
    public string? LastAppliedRevision { get; set; }
}

public sealed record AudioDevice(string Id, string Name, string Interface, uint State, bool IsDefault, bool ApoRegistered)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Interface) ? Name : $"{Name} ({Interface})";
    public string Status => State switch { 1 => "已连接", 2 => "已禁用", 4 => "未连接", 8 => "未插入", _ => "不可用" };
    public string ApoStatus => ApoRegistered ? "存在 APO 登记" : "未检测到 APO 登记";
}

public sealed record AutoEqEntry(string Name, string RelativePath, string Source)
{
    public string Detail => Source;
    public string ModelFolder => Uri.UnescapeDataString(RelativePath.Split('/')[^1]);
}
