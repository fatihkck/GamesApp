using System.Drawing;

namespace GamesApp.UI;

/// <summary>
/// Çocuk dostu renk paleti. Koyu (neredeyse siyah-lacivert) arka plan üzerine
/// yüksek doygunluklu neon renkler; nota → renk eşlemesi perde sınıfına göre yapılır.
/// </summary>
internal static class Theme
{
    /// <summary>Genel arka plan (koyu lacivert).</summary>
    public static readonly Color Background = Color.FromArgb(255, 8, 10, 26);

    /// <summary>Arka plan gradyanının alt tonu.</summary>
    public static readonly Color BackgroundDeep = Color.FromArgb(255, 3, 4, 12);

    /// <summary>Çıkış butonu zemini.</summary>
    public static readonly Color ExitButton = Color.FromArgb(255, 208, 32, 48);

    /// <summary>Çıkış butonu hover zemini (daha koyu).</summary>
    public static readonly Color ExitButtonHover = Color.FromArgb(255, 150, 16, 30);

    /// <summary>İkincil bilgilendirme yazısı.</summary>
    public static readonly Color Hint = Color.FromArgb(255, 150, 155, 180);

    /// <summary>Uyarı yazısı (MIDI yoksa).</summary>
    public static readonly Color Warning = Color.FromArgb(255, 255, 190, 60);

    /// <summary>Beyaz piyano tuşu.</summary>
    public static readonly Color WhiteKey = Color.FromArgb(255, 238, 240, 250);

    /// <summary>Siyah piyano tuşu.</summary>
    public static readonly Color BlackKey = Color.FromArgb(255, 24, 26, 40);

    /// <summary>Piyano tuş kenarlığı.</summary>
    public static readonly Color KeyBorder = Color.FromArgb(255, 60, 64, 90);

    /// <summary>
    /// Notanın rengi: perde sınıfı (12 sınıf) HSV hue değerine (0-360) eşlenir.
    /// Aynı nota her zaman aynı rengi verir; oktav değişimi parlaklığı hafifçe etkiler.
    /// </summary>
    public static Color GetNoteColor(int midiNote)
    {
        int pitchClass = ((midiNote % 12) + 12) % 12;
        double hue = pitchClass * (360.0 / 12.0);

        // Yüksek oktavlar biraz daha parlak, kalın oktavlar biraz daha derin.
        int octave = Math.Clamp(midiNote / 12, 2, 8);
        double value = 0.85 + (octave - 2) * 0.025;

        return ColorFromHsv(hue, 0.95, Math.Min(value, 1.0));
    }

    /// <summary>HSV (hue 0-360, doygunluk 0-1, parlaklık 0-1) değerini RGB renge çevirir.</summary>
    public static Color ColorFromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360.0) + 360.0) % 360.0;
        saturation = Math.Clamp(saturation, 0.0, 1.0);
        value = Math.Clamp(value, 0.0, 1.0);

        int sector = (int)Math.Floor(hue / 60.0) % 6;
        double fraction = hue / 60.0 - Math.Floor(hue / 60.0);

        double v = value * 255.0;
        double p = v * (1.0 - saturation);
        double q = v * (1.0 - saturation * fraction);
        double t = v * (1.0 - saturation * (1.0 - fraction));

        return sector switch
        {
            0 => Color.FromArgb(255, ToByte(v), ToByte(t), ToByte(p)),
            1 => Color.FromArgb(255, ToByte(q), ToByte(v), ToByte(p)),
            2 => Color.FromArgb(255, ToByte(p), ToByte(v), ToByte(t)),
            3 => Color.FromArgb(255, ToByte(p), ToByte(q), ToByte(v)),
            4 => Color.FromArgb(255, ToByte(t), ToByte(p), ToByte(v)),
            _ => Color.FromArgb(255, ToByte(v), ToByte(p), ToByte(q))
        };
    }

    /// <summary>İki rengi doğrusal olarak karıştırır (t: 0 = a, 1 = b).</summary>
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        int r = (int)(a.R + (b.R - a.R) * t);
        int g = (int)(a.G + (b.G - a.G) * t);
        int bl = (int)(a.B + (b.B - a.B) * t);
        int al = (int)(a.A + (b.A - a.A) * t);
        return Color.FromArgb(
            Math.Clamp(al, 0, 255),
            Math.Clamp(r, 0, 255),
            Math.Clamp(g, 0, 255),
            Math.Clamp(bl, 0, 255));
    }

    /// <summary>Rengin alfa değerini değiştirir.</summary>
    public static Color WithAlpha(Color color, float alpha)
    {
        int a = (int)Math.Clamp(alpha * 255f, 0f, 255f);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    private static int ToByte(double value) => (int)Math.Clamp(Math.Round(value), 0, 255);
}
