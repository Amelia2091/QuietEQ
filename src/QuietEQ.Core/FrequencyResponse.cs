using System.Numerics;

namespace QuietEQ.Core;

public static class FrequencyResponse
{
    // RBJ biquad response at 48 kHz; the editor labels this reference rate explicitly.
    public static double At(EqEffect effect, double frequency, double sampleRate = 48000)
    {
        var db = effect.Preamp;
        if (effect.Graphic.Count > 0)
        {
            var points = effect.Graphic;
            if (frequency <= points[0].Frequency) return db + points[0].Gain;
            if (frequency >= points[^1].Frequency) return db + points[^1].Gain;
            for (int i = 1; i < points.Count; i++)
                if (frequency <= points[i].Frequency)
                {
                    var fraction = Math.Log(frequency / points[i - 1].Frequency) / Math.Log(points[i].Frequency / points[i - 1].Frequency);
                    return db + points[i - 1].Gain + fraction * (points[i].Gain - points[i - 1].Gain);
                }
        }
        var z = Complex.Exp(new Complex(0, -2 * Math.PI * frequency / sampleRate));
        foreach (var band in effect.Bands.Where(b => b.Enabled))
        {
            var a = Math.Pow(10, band.Gain / 40);
            var w = 2 * Math.PI * Math.Min(band.Frequency, sampleRate / 2 - 1) / sampleRate;
            var c = Math.Cos(w);
            var alpha = Math.Sin(w) / (2 * band.Q);
            var beta = 2 * Math.Sqrt(a) * alpha;
            (double b0, double b1, double b2, double a0, double a1, double a2) = band.Type switch
            {
                "LSC" => (a * ((a + 1) - (a - 1) * c + beta), 2 * a * ((a - 1) - (a + 1) * c), a * ((a + 1) - (a - 1) * c - beta), (a + 1) + (a - 1) * c + beta, -2 * ((a - 1) + (a + 1) * c), (a + 1) + (a - 1) * c - beta),
                "HSC" => (a * ((a + 1) + (a - 1) * c + beta), -2 * a * ((a - 1) + (a + 1) * c), a * ((a + 1) + (a - 1) * c - beta), (a + 1) - (a - 1) * c + beta, 2 * ((a - 1) - (a + 1) * c), (a + 1) - (a - 1) * c - beta),
                _ => (1 + alpha * a, -2 * c, 1 - alpha * a, 1 + alpha / a, -2 * c, 1 - alpha / a)
            };
            var h = (b0 + b1 * z + b2 * z * z) / (a0 + a1 * z + a2 * z * z);
            db += 20 * Math.Log10(Math.Max(1e-12, h.Magnitude));
        }
        return db;
    }
}
