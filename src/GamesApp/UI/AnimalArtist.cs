using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.Audio;

namespace GamesApp.UI;

/// <summary>
/// Karikatür hayvan yüzlerini GDI+ ile vektör olarak çizer.
///
/// NEDEN EMOJİ DEĞİL: GDI+ (System.Drawing) renkli emoji glifi render'ı güvenilir
/// değildir; yazı tipine göre boş kare veya tek renk siluet çıkabilir. Bu yüzden tüm
/// hayvanlar daire/elips/üçgen/yay ile çizilir. Çocuk kitabı görünümü için kalın
/// koyu kontur kullanılır.
///
/// Tüm koordinatlar verilen kutuya göre orandır; her boyutta bozulmadan ölçeklenir.
/// </summary>
internal static class AnimalArtist
{
    /// <summary>Kontur rengi (koyu kahve-siyah).</summary>
    private static readonly Color Outline = Color.FromArgb(255, 32, 24, 20);

    /// <summary>Göz akı.</summary>
    private static readonly Color EyeWhite = Color.FromArgb(255, 252, 252, 255);

    /// <summary>Hayvanı verilen kutuya, verilen saydamlıkla çizer.</summary>
    public static void Draw(Graphics g, AnimalKind kind, RectangleF box, float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        if (alpha <= 0.01f || box.Width <= 4f || box.Height <= 4f)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(Color.White);
        using var pen = new Pen(Theme.WithAlpha(Outline, alpha), Math.Max(3f, box.Width * 0.022f))
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        switch (kind)
        {
            case AnimalKind.Cat:
                DrawCat(g, box, alpha, brush, pen);
                break;

            case AnimalKind.Dog:
                DrawDog(g, box, alpha, brush, pen);
                break;

            case AnimalKind.Cow:
                DrawCow(g, box, alpha, brush, pen);
                break;

            case AnimalKind.Sheep:
                DrawSheep(g, box, alpha, brush, pen);
                break;

            case AnimalKind.Chick:
                DrawChick(g, box, alpha, brush, pen);
                break;

            case AnimalKind.Duck:
                DrawDuck(g, box, alpha, brush, pen);
                break;

            case AnimalKind.Rooster:
                DrawRooster(g, box, alpha, brush, pen);
                break;

            case AnimalKind.Frog:
                DrawFrog(g, box, alpha, brush, pen);
                break;
        }
    }

    // ---------------- Hayvanlar ----------------

    /// <summary>Kedi: turuncu tekir, üçgen kulaklar, bıyıklar.</summary>
    private static void DrawCat(Graphics g, RectangleF b, float a, SolidBrush brush, Pen pen)
    {
        Color fur = Color.FromArgb(255, 244, 148, 54);
        Color furDark = Color.FromArgb(255, 205, 108, 28);
        Color pink = Color.FromArgb(255, 248, 156, 176);

        // Kulaklar (baştan önce çizilir ki kontur baş tarafından kesilmesin)
        PointF[] leftEar = { Pt(b, 0.16f, 0.34f), Pt(b, 0.10f, 0.02f), Pt(b, 0.42f, 0.16f) };
        PointF[] rightEar = { Pt(b, 0.84f, 0.34f), Pt(b, 0.90f, 0.02f), Pt(b, 0.58f, 0.16f) };
        FillAndOutlinePolygon(g, brush, pen, leftEar, fur, a);
        FillAndOutlinePolygon(g, brush, pen, rightEar, fur, a);

        PointF[] leftInner = { Pt(b, 0.20f, 0.28f), Pt(b, 0.16f, 0.10f), Pt(b, 0.36f, 0.19f) };
        PointF[] rightInner = { Pt(b, 0.80f, 0.28f), Pt(b, 0.84f, 0.10f), Pt(b, 0.64f, 0.19f) };
        Fill(g, brush, leftInner, pink, a);
        Fill(g, brush, rightInner, pink, a);

        // Baş
        RectangleF head = Rect(b, 0.10f, 0.16f, 0.80f, 0.72f);
        FillAndOutlineEllipse(g, brush, pen, head, fur, a);

        // Tekir çizgileri
        pen.Color = Theme.WithAlpha(furDark, a);
        float saved = pen.Width;
        pen.Width = Math.Max(2f, b.Width * 0.016f);
        for (int i = 0; i < 3; i++)
        {
            float x = 0.38f + i * 0.12f;
            g.DrawLine(pen, Pt(b, x, 0.24f), Pt(b, x + 0.02f, 0.34f));
        }

        pen.Width = saved;
        pen.Color = Theme.WithAlpha(Outline, a);

        // Gözler
        DrawEye(g, brush, pen, Rect(b, 0.26f, 0.42f, 0.17f, 0.19f), a, 0.55f);
        DrawEye(g, brush, pen, Rect(b, 0.57f, 0.42f, 0.17f, 0.19f), a, 0.55f);

        // Burun (küçük pembe üçgen)
        PointF[] nose = { Pt(b, 0.44f, 0.63f), Pt(b, 0.56f, 0.63f), Pt(b, 0.50f, 0.71f) };
        FillAndOutlinePolygon(g, brush, pen, nose, pink, a);

        // Ağız (iki yay)
        g.DrawArc(pen, Rect(b, 0.38f, 0.68f, 0.12f, 0.10f), 0f, 130f);
        g.DrawArc(pen, Rect(b, 0.50f, 0.68f, 0.12f, 0.10f), 50f, 130f);

        // Bıyıklar
        for (int i = 0; i < 3; i++)
        {
            float y = 0.62f + i * 0.05f;
            g.DrawLine(pen, Pt(b, 0.36f, y), Pt(b, 0.06f, y - 0.05f + i * 0.04f));
            g.DrawLine(pen, Pt(b, 0.64f, y), Pt(b, 0.94f, y - 0.05f + i * 0.04f));
        }
    }

    /// <summary>Köpek: kahverengi, sarkık kulaklar, siyah burun, dili dışarıda.</summary>
    private static void DrawDog(Graphics g, RectangleF b, float a, SolidBrush brush, Pen pen)
    {
        Color fur = Color.FromArgb(255, 176, 118, 66);
        Color furDark = Color.FromArgb(255, 128, 80, 40);
        Color muzzle = Color.FromArgb(255, 232, 202, 168);
        Color tongue = Color.FromArgb(255, 240, 120, 140);

        // Sarkık kulaklar
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.00f, 0.22f, 0.26f, 0.52f), furDark, a);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.74f, 0.22f, 0.26f, 0.52f), furDark, a);

        // Baş
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.14f, 0.14f, 0.72f, 0.72f), fur, a);

        // Ağız bölgesi
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.30f, 0.52f, 0.40f, 0.34f), muzzle, a);

        // Gözler
        DrawEye(g, brush, pen, Rect(b, 0.28f, 0.36f, 0.16f, 0.17f), a, 0.6f);
        DrawEye(g, brush, pen, Rect(b, 0.56f, 0.36f, 0.16f, 0.17f), a, 0.6f);

        // Burun
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.42f, 0.55f, 0.16f, 0.12f), Outline, a);

        // Ağız çizgisi ve dil
        g.DrawLine(pen, Pt(b, 0.50f, 0.67f), Pt(b, 0.50f, 0.74f));
        g.DrawArc(pen, Rect(b, 0.38f, 0.68f, 0.12f, 0.10f), 0f, 130f);
        g.DrawArc(pen, Rect(b, 0.50f, 0.68f, 0.12f, 0.10f), 50f, 130f);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.45f, 0.76f, 0.12f, 0.14f), tongue, a);
    }

    /// <summary>İnek: beyaz, siyah lekeli, pembe burunlu, küçük boynuzlu.</summary>
    private static void DrawCow(Graphics g, RectangleF b, float a, SolidBrush brush, Pen pen)
    {
        Color body = Color.FromArgb(255, 248, 248, 250);
        Color patch = Color.FromArgb(255, 48, 44, 52);
        Color horn = Color.FromArgb(255, 232, 214, 170);
        Color muzzle = Color.FromArgb(255, 244, 168, 186);

        // Boynuzlar
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.16f, 0.06f, 0.16f, 0.12f), horn, a);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.68f, 0.06f, 0.16f, 0.12f), horn, a);

        // Kulaklar
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.02f, 0.24f, 0.20f, 0.24f), body, a);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.78f, 0.24f, 0.20f, 0.24f), body, a);

        // Baş
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.12f, 0.16f, 0.76f, 0.70f), body, a);

        // Siyah leke
        Fill(g, brush, Rect(b, 0.16f, 0.20f, 0.28f, 0.24f), patch, a);

        // Gözler
        DrawEye(g, brush, pen, Rect(b, 0.30f, 0.36f, 0.15f, 0.16f), a, 0.6f);
        DrawEye(g, brush, pen, Rect(b, 0.55f, 0.36f, 0.15f, 0.16f), a, 0.6f);

        // Pembe burun + delikler
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.28f, 0.58f, 0.44f, 0.28f), muzzle, a);
        Fill(g, brush, Rect(b, 0.38f, 0.66f, 0.07f, 0.09f), Outline, a);
        Fill(g, brush, Rect(b, 0.55f, 0.66f, 0.07f, 0.09f), Outline, a);
    }

    /// <summary>Koyun: bulut şeklinde yapağı, koyu yüz.</summary>
    private static void DrawSheep(Graphics g, RectangleF b, float a, SolidBrush brush, Pen pen)
    {
        Color wool = Color.FromArgb(255, 246, 244, 238);
        Color face = Color.FromArgb(255, 92, 84, 96);

        // Yapağı: üst üste binen daireler bulut silueti oluşturur.
        float[][] puffs =
        {
            new[] { 0.06f, 0.26f, 0.32f, 0.32f },
            new[] { 0.30f, 0.10f, 0.34f, 0.34f },
            new[] { 0.60f, 0.22f, 0.34f, 0.34f },
            new[] { 0.02f, 0.50f, 0.30f, 0.30f },
            new[] { 0.66f, 0.50f, 0.32f, 0.32f },
            new[] { 0.24f, 0.58f, 0.30f, 0.30f },
            new[] { 0.46f, 0.60f, 0.30f, 0.30f }
        };

        for (int i = 0; i < puffs.Length; i++)
        {
            FillAndOutlineEllipse(g, brush, pen, Rect(b, puffs[i][0], puffs[i][1], puffs[i][2], puffs[i][3]), wool, a);
        }

        // İç yapağıyı düz göstermek için ortayı tekrar doldur (konturlar kaybolsun)
        Fill(g, brush, Rect(b, 0.20f, 0.28f, 0.60f, 0.48f), wool, a);

        // Kulaklar
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.18f, 0.42f, 0.16f, 0.12f), face, a);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.66f, 0.42f, 0.16f, 0.12f), face, a);

        // Yüz
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.30f, 0.36f, 0.40f, 0.46f), face, a);

        // Gözler
        DrawEye(g, brush, pen, Rect(b, 0.36f, 0.46f, 0.11f, 0.13f), a, 0.6f);
        DrawEye(g, brush, pen, Rect(b, 0.53f, 0.46f, 0.11f, 0.13f), a, 0.6f);

        // Ağız
        g.DrawArc(pen, Rect(b, 0.42f, 0.62f, 0.16f, 0.12f), 20f, 140f);
    }

    /// <summary>Civciv: sarı, turuncu gaga, tepesinde tüy.</summary>
    private static void DrawChick(Graphics g, RectangleF b, float a, SolidBrush brush, Pen pen)
    {
        Color body = Color.FromArgb(255, 252, 214, 66);
        Color bodyDark = Color.FromArgb(255, 232, 186, 40);
        Color beak = Color.FromArgb(255, 246, 140, 40);

        // Tepe tüyleri
        g.DrawLine(pen, Pt(b, 0.44f, 0.20f), Pt(b, 0.36f, 0.02f));
        g.DrawLine(pen, Pt(b, 0.50f, 0.18f), Pt(b, 0.50f, 0.00f));
        g.DrawLine(pen, Pt(b, 0.56f, 0.20f), Pt(b, 0.64f, 0.02f));

        // Gövde/baş
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.16f, 0.18f, 0.68f, 0.72f), body, a);

        // Kanat (gözlerin altında kalacak şekilde yerleştirilir)
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.66f, 0.58f, 0.22f, 0.24f), bodyDark, a);

        // Gözler
        DrawEye(g, brush, pen, Rect(b, 0.32f, 0.40f, 0.13f, 0.15f), a, 0.62f);
        DrawEye(g, brush, pen, Rect(b, 0.54f, 0.40f, 0.13f, 0.15f), a, 0.62f);

        // Gaga (iki üçgen)
        PointF[] upper = { Pt(b, 0.44f, 0.60f), Pt(b, 0.58f, 0.60f), Pt(b, 0.50f, 0.68f) };
        PointF[] lower = { Pt(b, 0.45f, 0.68f), Pt(b, 0.57f, 0.68f), Pt(b, 0.50f, 0.75f) };
        FillAndOutlinePolygon(g, brush, pen, upper, beak, a);
        FillAndOutlinePolygon(g, brush, pen, lower, beak, a);
    }

    /// <summary>Ördek: açık sarı baş, geniş turuncu gaga.</summary>
    private static void DrawDuck(Graphics g, RectangleF b, float a, SolidBrush brush, Pen pen)
    {
        Color body = Color.FromArgb(255, 250, 232, 150);
        Color beak = Color.FromArgb(255, 246, 152, 32);

        // Baş
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.08f, 0.14f, 0.68f, 0.70f), body, a);

        // Geniş yassı gaga (sağa doğru) + ağız çizgisi ve burun deliği
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.54f, 0.44f, 0.44f, 0.22f), beak, a);
        g.DrawLine(pen, Pt(b, 0.64f, 0.55f), Pt(b, 0.95f, 0.55f));
        Fill(g, brush, Rect(b, 0.70f, 0.47f, 0.05f, 0.04f), Outline, a);

        // Gözler
        DrawEye(g, brush, pen, Rect(b, 0.24f, 0.30f, 0.15f, 0.17f), a, 0.6f);
        DrawEye(g, brush, pen, Rect(b, 0.47f, 0.28f, 0.15f, 0.17f), a, 0.6f);
    }

    /// <summary>Horoz: kırmızı ibik ve sakal, turuncu gaga.</summary>
    private static void DrawRooster(Graphics g, RectangleF b, float a, SolidBrush brush, Pen pen)
    {
        Color body = Color.FromArgb(255, 248, 246, 240);
        Color comb = Color.FromArgb(255, 226, 46, 52);
        Color beak = Color.FromArgb(255, 246, 168, 40);

        // İbik: üç kabarcık
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.28f, 0.02f, 0.18f, 0.20f), comb, a);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.42f, 0.00f, 0.20f, 0.22f), comb, a);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.58f, 0.03f, 0.18f, 0.20f), comb, a);

        // Baş
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.16f, 0.16f, 0.62f, 0.68f), body, a);

        // Gözler
        DrawEye(g, brush, pen, Rect(b, 0.32f, 0.34f, 0.14f, 0.16f), a, 0.6f);
        DrawEye(g, brush, pen, Rect(b, 0.54f, 0.34f, 0.14f, 0.16f), a, 0.6f);

        // Sakal (wattle) - gagadan önce çizilir ki gaga üstte kalsın
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.62f, 0.64f, 0.15f, 0.22f), comb, a);

        // Gaga (sağa bakan üçgen, başın kenarından dışa taşar)
        PointF[] beakShape = { Pt(b, 0.70f, 0.46f), Pt(b, 1.00f, 0.55f), Pt(b, 0.70f, 0.62f) };
        FillAndOutlinePolygon(g, brush, pen, beakShape, beak, a);
    }

    /// <summary>Kurbağa: yeşil, patlak gözler, geniş gülüş.</summary>
    private static void DrawFrog(Graphics g, RectangleF b, float a, SolidBrush brush, Pen pen)
    {
        Color body = Color.FromArgb(255, 108, 200, 88);
        Color bodyDark = Color.FromArgb(255, 74, 158, 62);

        // Patlak gözler (baştan önce, arkada kalan kısımlar için)
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.12f, 0.10f, 0.32f, 0.32f), body, a);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.56f, 0.10f, 0.32f, 0.32f), body, a);

        // Baş
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.06f, 0.34f, 0.88f, 0.54f), body, a);

        // Göz akı + bebek
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.17f, 0.15f, 0.22f, 0.22f), EyeWhite, a);
        FillAndOutlineEllipse(g, brush, pen, Rect(b, 0.61f, 0.15f, 0.22f, 0.22f), EyeWhite, a);
        Fill(g, brush, Rect(b, 0.23f, 0.21f, 0.11f, 0.12f), Outline, a);
        Fill(g, brush, Rect(b, 0.67f, 0.21f, 0.11f, 0.12f), Outline, a);

        // Burun delikleri
        Fill(g, brush, Rect(b, 0.44f, 0.48f, 0.04f, 0.04f), bodyDark, a);
        Fill(g, brush, Rect(b, 0.53f, 0.48f, 0.04f, 0.04f), bodyDark, a);

        // Geniş gülüş
        g.DrawArc(pen, Rect(b, 0.18f, 0.44f, 0.64f, 0.36f), 20f, 140f);
    }

    // ---------------- Yardımcılar ----------------

    /// <summary>Göz: beyaz elips + siyah bebek.</summary>
    private static void DrawEye(Graphics g, SolidBrush brush, Pen pen, RectangleF area, float alpha, float pupilRatio)
    {
        FillAndOutlineEllipse(g, brush, pen, area, EyeWhite, alpha);

        float pw = area.Width * pupilRatio;
        float ph = area.Height * pupilRatio;
        var pupil = new RectangleF(
            area.X + (area.Width - pw) * 0.5f,
            area.Y + (area.Height - ph) * 0.6f,
            pw,
            ph);

        brush.Color = Theme.WithAlpha(Outline, alpha);
        g.FillEllipse(brush, pupil);

        // Küçük parlama noktası: bakış canlansın.
        brush.Color = Theme.WithAlpha(Color.White, alpha * 0.9f);
        g.FillEllipse(
            brush,
            pupil.X + pupil.Width * 0.15f,
            pupil.Y + pupil.Height * 0.12f,
            pupil.Width * 0.32f,
            pupil.Height * 0.32f);
    }

    private static void FillAndOutlineEllipse(Graphics g, SolidBrush brush, Pen pen, RectangleF area, Color color, float alpha)
    {
        brush.Color = Theme.WithAlpha(color, alpha);
        g.FillEllipse(brush, area);
        g.DrawEllipse(pen, area);
    }

    private static void FillAndOutlinePolygon(Graphics g, SolidBrush brush, Pen pen, PointF[] points, Color color, float alpha)
    {
        brush.Color = Theme.WithAlpha(color, alpha);
        g.FillPolygon(brush, points);
        g.DrawPolygon(pen, points);
    }

    private static void Fill(Graphics g, SolidBrush brush, PointF[] points, Color color, float alpha)
    {
        brush.Color = Theme.WithAlpha(color, alpha);
        g.FillPolygon(brush, points);
    }

    private static void Fill(Graphics g, SolidBrush brush, RectangleF area, Color color, float alpha)
    {
        brush.Color = Theme.WithAlpha(color, alpha);
        g.FillEllipse(brush, area);
    }

    /// <summary>Kutuya göre oranlı nokta.</summary>
    private static PointF Pt(RectangleF b, float fx, float fy) =>
        new(b.X + b.Width * fx, b.Y + b.Height * fy);

    /// <summary>Kutuya göre oranlı dikdörtgen.</summary>
    private static RectangleF Rect(RectangleF b, float fx, float fy, float fw, float fh) =>
        new(b.X + b.Width * fx, b.Y + b.Height * fy, b.Width * fw, b.Height * fh);
}
