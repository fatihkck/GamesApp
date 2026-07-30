using System.Drawing;
using System.Drawing.Drawing2D;

namespace GamesApp.UI;

/// <summary>
/// Üst menüdeki tek bir oyun butonu: büyük simge, altında oyunun adı.
///
/// NEDEN KENDİ ÇİZİMİ (WinForms Button değil): Menüye çok oyun geldikçe butonlar
/// daralır. Standart Button tek yazı tipiyle tek satır çizer; daralınca yazı kırpılır
/// ve simge de küçülür. Burada simge ile ad AYRI yazı tipleriyle çizilir: buton
/// daraldığında önce ad gizlenir, simge büyük ve tanınır kalır. Okumayı bilmeyen çocuk
/// oyunu simgesinden tanıdığı için en kritik öğe simgedir.
///
/// METİN ÇİZİMİ <see cref="TextRenderer"/> İLE YAPILIR (Graphics.DrawString değil):
/// GDI+ renkli emoji glifini güvenilir çizmez; GDI tarafı (TextRenderer) standart
/// Button'ın kullandığı yoldur ve emojiyi sistem yazı tipi yedeğiyle doğru çizer.
/// </summary>
internal sealed class GameMenuButton : Control
{
    /// <summary>Ad yazısının gizlendiği genişlik sınırı (piksel).</summary>
    private const int CompactWidth = 132;

    /// <summary>Ad yazısının gizlendiği yükseklik sınırı (piksel).</summary>
    private const int CompactHeight = 58;

    private readonly Color _accent;

    private Font _iconFont = new("Segoe UI Emoji", 20f);
    private Font _labelFont = new("Segoe UI", 11f, FontStyle.Bold);

    private bool _hovered;
    private bool _selected;
    private bool _disposedResources;

    public GameMenuButton(int gameIndex, string icon, string title, Color accent)
    {
        GameIndex = gameIndex;
        Icon = icon;
        Title = title;
        _accent = accent;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);

        TabStop = false;
        Cursor = Cursors.Hand;
        BackColor = Theme.BackgroundDeep;
    }

    /// <summary>Bu butonun temsil ettiği oyunun listedeki sırası.</summary>
    public int GameIndex { get; }

    /// <summary>Büyük çizilen simge (emoji).</summary>
    public string Icon { get; }

    /// <summary>Simgenin altında çizilen oyun adı.</summary>
    public string Title { get; }

    /// <summary>Bu oyun şu anda oynanıyor mu? (Buton parlak ve beyaz çerçeveli olur.)</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildFonts();
    }

    /// <summary>
    /// Yazı tiplerini buton boyutuna göre yeniden üretir. Yazı tipi nesneleri her
    /// karede değil yalnızca boyut değişince oluşturulur.
    /// </summary>
    private void RebuildFonts()
    {
        int height = Math.Max(1, ClientSize.Height);
        bool compact = IsCompact;

        // Simge: dar/kısa butonda tüm alanı kullanır, geniş butonda üst kısmı.
        float iconSize = Math.Clamp(height * (compact ? 0.46f : 0.38f), 11f, 34f);
        float labelSize = Math.Clamp(height * 0.19f, 8f, 15f);

        Font oldIcon = _iconFont;
        Font oldLabel = _labelFont;

        _iconFont = new Font("Segoe UI Emoji", iconSize, GraphicsUnit.Pixel);
        _labelFont = new Font("Segoe UI", labelSize, FontStyle.Bold, GraphicsUnit.Pixel);

        oldIcon.Dispose();
        oldLabel.Dispose();
    }

    /// <summary>Buton, oyun adını gösteremeyecek kadar küçük mü?</summary>
    private bool IsCompact =>
        ClientSize.Width < CompactWidth || ClientSize.Height < CompactHeight;

    protected override void OnPaint(PaintEventArgs e)
    {
        // Çizim istisnası kontrolü kalıcı olarak bozmasın (bkz. PaintGuard).
        try
        {
            PaintCore(e.Graphics);
        }
        catch (Exception ex)
        {
            PaintGuard.Report(nameof(GameMenuButton), ex);
        }
    }

    private void PaintCore(Graphics g)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackgroundDeep);

        // Zemin: seçili oyun parlak, üzerine gelinen orta, diğerleri sönük.
        Color fill = _selected
            ? Theme.Lerp(_accent, Color.Black, 0.15f)
            : Theme.Lerp(_accent, Color.Black, _hovered ? 0.38f : 0.62f);

        Color border = _selected
            ? Color.White
            : Theme.Lerp(_accent, Color.White, _hovered ? 0.45f : 0.18f);

        float radius = Math.Min(16f, Math.Min(bounds.Width, bounds.Height) * 0.28f);
        var area = new RectangleF(1.5f, 1.5f, bounds.Width - 3f, bounds.Height - 3f);

        using (GraphicsPath path = BuildRoundedRectangle(area, radius))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border, _selected ? 3f : 2f) { LineJoin = LineJoin.Round })
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        const TextFormatFlags flags =
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis;

        if (IsCompact)
        {
            // Yalnızca simge: adı sığdırmaya çalışmak yerine simgeyi büyük tut.
            TextRenderer.DrawText(g, Icon, _iconFont, bounds, Color.White, flags);
            return;
        }

        int labelHeight = (int)(bounds.Height * 0.34f);
        var iconArea = new Rectangle(0, 2, bounds.Width, bounds.Height - labelHeight - 2);
        var labelArea = new Rectangle(4, bounds.Height - labelHeight - 2, bounds.Width - 8, labelHeight);

        TextRenderer.DrawText(g, Icon, _iconFont, iconArea, Color.White, flags);
        TextRenderer.DrawText(
            g,
            Title,
            _labelFont,
            labelArea,
            _selected ? Color.White : Color.FromArgb(255, 226, 230, 244),
            flags);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;
            _iconFont.Dispose();
            _labelFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
