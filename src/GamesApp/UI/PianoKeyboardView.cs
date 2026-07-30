using System.Drawing;
using System.Drawing.Drawing2D;

namespace GamesApp.UI;

/// <summary>
/// Ekranın altında çizilen görsel piyano (MIDI 48 - 96, yaklaşık 4 oktav).
/// Çalan nota parlar, sonra kısa süre içinde söner.
/// Fare ile sol tıklandığında da nota çalar (opsiyonel ekstra).
/// </summary>
internal sealed class PianoKeyboardView : Control
{
    /// <summary>Görüntülenen en kalın nota (C3).</summary>
    private const int FirstNote = 48;

    /// <summary>Görüntülenen en tiz nota (C7).</summary>
    private const int LastNote = 96;

    /// <summary>Parlaklığın saniyedeki sönümlenme oranı.</summary>
    private const float GlowDecayPerSecond = 2.2f;

    /// <summary>Siyah tuş yüksekliğinin beyaz tuşa oranı.</summary>
    private const float BlackKeyHeightRatio = 0.62f;

    /// <summary>Nota başına parlaklık (0-1). 128 elemanlı dizi: sözlük yerine sabit maliyet.</summary>
    private readonly float[] _glow = new float[128];

    /// <summary>Beyaz tuşların notaları (soldan sağa).</summary>
    private readonly List<int> _whiteNotes = new();

    /// <summary>Siyah tuşlar: nota + solundaki beyaz tuşun indeksi.</summary>
    private readonly List<(int Note, int LeftWhiteIndex)> _blackNotes = new();

    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Theme.KeyBorder, 1f);

    private int _mouseNote = -1;
    private bool _disposedResources;

    public PianoKeyboardView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);

        TabStop = false;
        BackColor = Theme.BackgroundDeep;

        BuildKeyLayout();
    }

    /// <summary>Fare ile tuşa basıldı (nota, velocity).</summary>
    public event Action<int, int>? NotePressed;

    /// <summary>Fare ile basılan tuş bırakıldı (nota).</summary>
    public event Action<int>? NoteReleased;

    /// <summary>Bir notayı parlat.</summary>
    public void Highlight(int midiNote)
    {
        if (midiNote is >= 0 and < 128)
        {
            _glow[midiNote] = 1f;
        }
    }

    /// <summary>Parlaklıkları zamanla sönümler. Her karede EffectCanvas tarafından çağrılır.</summary>
    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return;
        }

        float decay = GlowDecayPerSecond * deltaSeconds;
        bool changed = false;

        for (int i = 0; i < _glow.Length; i++)
        {
            if (_glow[i] > 0f)
            {
                _glow[i] = Math.Max(0f, _glow[i] - decay);
                changed = true;
            }
        }

        if (changed)
        {
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // İstisna OnPaint'ten kaçarsa WinForms kontrolü kalıcı "kırmızı çarpı"
        // moduna sokar ve görsel bir daha çizilmez; bu yüzden kare bazında yutulur.
        try
        {
            PaintCore(e.Graphics);
        }
        catch (Exception ex)
        {
            PaintGuard.Report(nameof(PianoKeyboardView), ex);
        }
    }

    private void PaintCore(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0 || _whiteNotes.Count == 0)
        {
            return;
        }

        _brush.Color = Theme.BackgroundDeep;
        g.FillRectangle(_brush, bounds);

        float whiteWidth = bounds.Width / (float)_whiteNotes.Count;
        float whiteHeight = bounds.Height;
        float blackWidth = whiteWidth * 0.62f;
        float blackHeight = whiteHeight * BlackKeyHeightRatio;

        // Beyaz tuşlar
        for (int i = 0; i < _whiteNotes.Count; i++)
        {
            int note = _whiteNotes[i];
            float x = i * whiteWidth;
            float glow = _glow[note];

            Color fill = glow > 0f
                ? Theme.Lerp(Theme.WhiteKey, Theme.GetNoteColor(note), glow)
                : Theme.WhiteKey;

            _brush.Color = fill;
            g.FillRectangle(_brush, x, 0f, whiteWidth - 1f, whiteHeight);
            g.DrawRectangle(_pen, x, 0f, whiteWidth - 1f, whiteHeight);
        }

        // Siyah tuşlar (beyazların üstüne)
        for (int i = 0; i < _blackNotes.Count; i++)
        {
            (int note, int leftWhiteIndex) = _blackNotes[i];
            float x = (leftWhiteIndex + 1) * whiteWidth - blackWidth * 0.5f;
            float glow = _glow[note];

            Color fill = glow > 0f
                ? Theme.Lerp(Theme.BlackKey, Theme.GetNoteColor(note), glow)
                : Theme.BlackKey;

            _brush.Color = fill;
            g.FillRectangle(_brush, x, 0f, blackWidth, blackHeight);
            g.DrawRectangle(_pen, x, 0f, blackWidth, blackHeight);
        }

        // Üst kenara ince bir vurgu çizgisi.
        using var edgePen = new Pen(Color.FromArgb(90, 255, 255, 255), 2f);
        g.DrawLine(edgePen, 0f, 1f, bounds.Width, 1f);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Arka plan OnPaint içinde çiziliyor.
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        int note = HitTest(e.Location);
        if (note < 0)
        {
            return;
        }

        _mouseNote = note;
        Highlight(note);
        NotePressed?.Invoke(note, 108);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_mouseNote >= 0)
        {
            NoteReleased?.Invoke(_mouseNote);
            _mouseNote = -1;
        }
    }

    /// <summary>Verilen noktadaki notayı bulur; siyah tuşlar öncelikli. Bulunamazsa -1.</summary>
    private int HitTest(Point location)
    {
        if (_whiteNotes.Count == 0 || ClientRectangle.Width <= 0)
        {
            return -1;
        }

        float whiteWidth = ClientRectangle.Width / (float)_whiteNotes.Count;
        float blackWidth = whiteWidth * 0.62f;
        float blackHeight = ClientRectangle.Height * BlackKeyHeightRatio;

        if (location.Y <= blackHeight)
        {
            for (int i = 0; i < _blackNotes.Count; i++)
            {
                (int note, int leftWhiteIndex) = _blackNotes[i];
                float x = (leftWhiteIndex + 1) * whiteWidth - blackWidth * 0.5f;
                if (location.X >= x && location.X <= x + blackWidth)
                {
                    return note;
                }
            }
        }

        int index = (int)(location.X / whiteWidth);
        if (index >= 0 && index < _whiteNotes.Count)
        {
            return _whiteNotes[index];
        }

        return -1;
    }

    /// <summary>Beyaz/siyah tuş yerleşimini bir kez hesaplar.</summary>
    private void BuildKeyLayout()
    {
        for (int note = FirstNote; note <= LastNote; note++)
        {
            int pitchClass = note % 12;
            bool isBlack = pitchClass is 1 or 3 or 6 or 8 or 10;

            if (isBlack)
            {
                // Siyah tuş, o ana kadar eklenmiş son beyaz tuşun sağına konumlanır.
                int leftWhiteIndex = _whiteNotes.Count - 1;
                if (leftWhiteIndex >= 0)
                {
                    _blackNotes.Add((note, leftWhiteIndex));
                }
            }
            else
            {
                _whiteNotes.Add(note);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;
            _brush.Dispose();
            _pen.Dispose();
        }

        base.Dispose(disposing);
    }
}
