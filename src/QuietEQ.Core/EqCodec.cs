using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuietEQ.Core;

public static partial class EqCodec
{
    private const string Number = @"([+-]?(?:\d+(?:\.\d*)?|\.\d+))";
    private static readonly Regex PreampLine = new("^Preamp:\\s*" + Number + @"\s*dB$", RegexOptions.IgnoreCase);
    private static readonly Regex FilterLine = new(@"^Filter\s*\d*:\s*(ON|OFF)\s+(PK|PEQ|LSC|HSC)\s+Fc\s+" + Number + @"\s*Hz\s+Gain\s+" + Number + @"\s*dB\s+Q\s+" + Number + "$", RegexOptions.IgnoreCase);
    private static readonly Regex GraphicPair = new("^" + Number + @"\s+" + Number + "$", RegexOptions.IgnoreCase);
    public static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    public static EqEffect Parse(string text, string name)
    {
        var effect = new EqEffect { Name = name };
        var found = false;
        var graphicSeen = false;
        var lineNumber = 0;
        foreach (var raw in text.TrimStart('\uFEFF').Replace("\r", "").Split('\n'))
        {
            lineNumber++;
            var line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0) continue;
            var m = PreampLine.Match(line);
            if (m.Success) { effect.Preamp += D(m.Groups[1].Value); found = true; continue; }
            m = FilterLine.Match(line);
            if (m.Success)
            {
                effect.Bands.Add(new EqBand
                {
                    Enabled = m.Groups[1].Value.Equals("ON", StringComparison.OrdinalIgnoreCase),
                    Type = m.Groups[2].Value.ToUpperInvariant().Replace("PEQ", "PK"),
                    Frequency = D(m.Groups[3].Value), Gain = D(m.Groups[4].Value), Q = D(m.Groups[5].Value)
                });
                found = true;
                continue;
            }
            if (line.StartsWith("GraphicEQ:", StringComparison.OrdinalIgnoreCase))
            {
                if (graphicSeen) throw new FormatException("暂不支持一个文件中的多组 GraphicEQ。");
                graphicSeen = true;
                foreach (var pair in line[10..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    m = GraphicPair.Match(pair);
                    if (!m.Success) throw new FormatException($"第 {lineNumber} 行的 GraphicEQ 格式不正确。");
                    effect.Graphic.Add(new GraphicPoint { Frequency = D(m.Groups[1].Value), Gain = D(m.Groups[2].Value) });
                }
                if (effect.Graphic.Count == 0) throw new FormatException("GraphicEQ 至少需要一个频率点。");
                found = true;
                continue;
            }
            // Never silently drop routing, includes, plug-ins, or unsupported filter semantics.
            throw new FormatException($"第 {lineNumber} 行暂不支持：{line}\n支持 Preamp、PK/PEQ/LSC/HSC（带 Q）和 GraphicEQ。请导出单独的 EQ 效果文件。");
        }
        if (!found) throw new FormatException("文件中没有可导入的 EQ 数据。");
        Validate(effect);
        return effect;
    }

    public static void Validate(EqEffect e)
    {
        if (string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 200) throw new ArgumentException("EQ 名称须为 1–200 个字符。");
        Range(e.Preamp, -100, 30, "前级增益");
        if (e.Bands.Count > 100 || e.Graphic.Count > 2048) throw new ArgumentException("频段数量超过限制。");
        if (e.Bands.Count > 0 && e.Graphic.Count > 0) throw new ArgumentException("请将参数 EQ 和图示 EQ 分别导入。");
        foreach (var b in e.Bands)
        {
            if (b.Type is not ("PK" or "LSC" or "HSC")) throw new ArgumentException("滤波器仅支持 PK、LSC、HSC。");
            Range(b.Frequency, 10, 24000, "频率"); Range(b.Gain, -60, 30, "增益"); Range(b.Q, 0.05, 100, "Q");
        }
        double previous = 0;
        foreach (var p in e.Graphic)
        {
            Range(p.Frequency, 10, 24000, "频率"); Range(p.Gain, -100, 30, "增益");
            if (p.Frequency <= previous) throw new ArgumentException("GraphicEQ 频率须递增且不可重复。");
            previous = p.Frequency;
        }
    }

    private static void Range(double v, double min, double max, string label)
    {
        if (!double.IsFinite(v) || v < min || v > max) throw new ArgumentException($"{label}须在 {min} 到 {max} 之间。");
    }

    public static string Serialize(EqEffect e)
    {
        Validate(e);
        var s = new StringBuilder().AppendLine("# QuietEQ effect").AppendLine($"Preamp: {N(e.Preamp)} dB");
        for (var i = 0; i < e.Bands.Count; i++)
        {
            var b = e.Bands[i];
            s.AppendLine($"Filter {i + 1}: {(b.Enabled ? "ON" : "OFF")} {b.Type} Fc {N(b.Frequency)} Hz Gain {N(b.Gain)} dB Q {N(b.Q)}");
        }
        if (e.Graphic.Count > 0) s.AppendLine("GraphicEQ: " + string.Join("; ", e.Graphic.Select(p => $"{N(p.Frequency)} {N(p.Gain)}")));
        return s.ToString();
    }
}
