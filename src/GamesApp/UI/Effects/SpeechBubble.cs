using System.Drawing;
using System.Drawing.Drawing2D;

namespace GamesApp.UI.Effects;

/// <summary>
/// Hayvanın yanında beliren yuvarlak köşeli konuşma balonunu çizer.
///
/// NEDEN AYRI SINIF: Hem piyano/davul oyunlarındaki hayvan sürprizi
/// (<see cref="AnimalCue"/>) hem Hayvanat Bahçesi oyunundaki hayvanlar aynı balonu
/// kullanır. Balon, verilen alanın dışına taşmayacak şekilde hayvanın sağına, soluna
/// ya da üstüne yerleştirilir; kuyruğu her zaman hayvana bakar.
/// </summary>
internal static class SpeechBubble
{
    private static readonly Color BubbleFill = Color.FromArgb(255, 252, 250, 244);
    private static readonly Color BubbleBorder = Color.FromArgb(255, 32, 24, 20);
    private static readonly Color BubbleText = Color.FromArgb(255, 28, 22, 40);

    /// <summary>
    /// Balonu çizer.
    /// </summary>
    /// <param name="g">Çizim yüzeyi.</param>
    /// <param name="area">Balonun taşmaması gereken alan (oyun alanı).</param>
    /// <param name="animalBox">Hayvanın çizildiği kutu; balon buna göre konumlanır.</param>
    /// <param name="text">Balondaki Türkçe ses metni.</param>
    /// <param name="font">Metin yazı tipi.</param>
    /// <param name="alpha">Saydamlık (0-1); hayvanla birlikte kaybolur.</param>
    /// <param name="preferAbove">
    /// true ise balon öncelikle hayvanın ÜSTÜNE konur. Sahnede yan yana birkaç hayvan
    /// varken (Hayvanat Bahçesi) yana konan balon komşunun yüzüne biniyordu; üstteki
    /// boş gökyüzü hem daha temiz hem daha okunur. Üste sığmazsa yan yerleşime düşülür.
    /// Tek hayvanlı sürprizde (piyano/davul) yan yerleşim korunur: hayvan büyük çizilir
    /// ve üstünde yer kalmaz.
    /// </param>
    public static void Draw(
        Graphics g,
        RectangleF area,
        RectangleF animalBox,
        string text,
        Font font,
        float alpha,
        bool preferAbove = false)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        if (alpha <= 0.02f || string.IsNullOrEmpty(text) || animalBox.Width <= 8f)
        {
            return;
        }

        SizeF textSize = g.MeasureString(text, font);
        float padX = 26f;
        float padY = 16f;
        float bubbleWidth = textSize.Width + padX * 2f;
        float bubbleHeight = textSize.Height + padY * 2f;
        float gap = animalBox.Width * 0.10f;

        // İstenirse önce üst yerleşim denenir (yan yana hayvanların olduğu sahneler).
        bool above = false;
        bool toRight = true;
        float bubbleX;

        if (preferAbove && animalBox.Y - bubbleHeight - gap >= area.Y + 8f)
        {
            above = true;
            bubbleX = Math.Clamp(
                animalBox.X + (animalBox.Width - bubbleWidth) * 0.5f,
                area.X + 16f,
                Math.Max(area.X + 16f, area.Right - bubbleWidth - 16f));

            DrawBubble(g, area, animalBox, text, font, alpha, bubbleX, bubbleWidth, bubbleHeight, toRight, above);
            return;
        }

        // Önce sağa yerleştirmeyi dene; taşarsa sola geç.
        toRight = animalBox.Right + gap + bubbleWidth <= area.Right - 16f;
        bubbleX = toRight
            ? animalBox.Right + gap
            : animalBox.X - gap - bubbleWidth;

        // Sola da sığmıyorsa hayvanın üstüne koy.
        if (bubbleX < area.X + 16f)
        {
            bubbleX = Math.Clamp(
                animalBox.X + (animalBox.Width - bubbleWidth) * 0.5f,
                area.X + 16f,
                Math.Max(area.X + 16f, area.Right - bubbleWidth - 16f));
            above = true;
        }

        DrawBubble(g, area, animalBox, text, font, alpha, bubbleX, bubbleWidth, bubbleHeight, toRight, above);
    }

    /// <summary>
    /// Yerleşimi belirlenmiş balonu çizer: gövde, kuyruk ve metin.
    /// Dikey konum burada hesaplanır ve balon her zaman alanın içinde tutulur.
    /// </summary>
    private static void DrawBubble(
        Graphics g,
        RectangleF area,
        RectangleF animalBox,
        string text,
        Font font,
        float alpha,
        float bubbleX,
        float bubbleWidth,
        float bubbleHeight,
        bool toRight,
        bool above)
    {
        float gap = animalBox.Width * 0.10f;

        float bubbleY = above
            ? animalBox.Y - bubbleHeight - gap
            : animalBox.Y + animalBox.Height * 0.10f;

        // Balon her zaman alanın içinde kalsın (hayvan ekran kenarına yaklaşırsa).
        bubbleY = Math.Clamp(
            bubbleY,
            area.Y + 8f,
            Math.Max(area.Y + 8f, area.Bottom - bubbleHeight - 8f));

        var bubble = new RectangleF(bubbleX, bubbleY, bubbleWidth, bubbleHeight);
        float radius = Math.Min(26f, bubbleHeight * 0.45f);

        using var fill = new SolidBrush(Theme.WithAlpha(BubbleFill, alpha));
        using var border = new Pen(Theme.WithAlpha(BubbleBorder, alpha), 4f)
        {
            LineJoin = LineJoin.Round
        };
        using var textBrush = new SolidBrush(Theme.WithAlpha(BubbleText, alpha));

        // Balonun kuyruğu (hayvana bakan küçük üçgen)
        PointF[] tail = BuildTail(animalBox, bubble, toRight, above);

        using (GraphicsPath path = BuildRoundedRectangle(bubble, radius))
        {
            g.FillPath(fill, path);
            g.FillPolygon(fill, tail);
            g.DrawPath(border, path);
        }

        // Kuyruğun kenar çizgileri (balonun gövdesiyle birleşen kenar hariç)
        g.DrawLine(border, tail[0], tail[1]);
        g.DrawLine(border, tail[1], tail[2]);

        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString(text, font, textBrush, bubble, format);
    }

    private static PointF[] BuildTail(RectangleF animalBox, RectangleF bubble, bool toRight, bool above)
    {
        float animalCenterY = animalBox.Y + animalBox.Height * 0.35f;
        float animalCenterX = animalBox.X + animalBox.Width * 0.5f;

        if (above)
        {
            float x = Math.Clamp(animalCenterX, bubble.X + 20f, bubble.Right - 20f);
            return new[]
            {
                new PointF(x - 16f, bubble.Bottom - 2f),
                new PointF(x + 6f, bubble.Bottom + 26f),
                new PointF(x + 20f, bubble.Bottom - 2f)
            };
        }

        float y = Math.Clamp(animalCenterY, bubble.Y + 20f, bubble.Bottom - 20f);

        if (toRight)
        {
            return new[]
            {
                new PointF(bubble.X + 2f, y - 16f),
                new PointF(bubble.X - 26f, y + 4f),
                new PointF(bubble.X + 2f, y + 20f)
            };
        }

        return new[]
        {
            new PointF(bubble.Right - 2f, y - 16f),
            new PointF(bubble.Right + 26f, y + 4f),
            new PointF(bubble.Right - 2f, y + 20f)
        };
    }

    /// <summary>Yuvarlak köşeli dikdörtgen yolu üretir.</summary>
    private static GraphicsPath BuildRoundedRectangle(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;

        if (d <= 0f || rect.Width <= d || rect.Height <= d)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
