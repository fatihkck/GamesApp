using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.Audio;
using GamesApp.UI.Effects;

namespace GamesApp.UI;

/// <summary>
/// Efektlerin çizildiği tuval. Titremeyi (flicker) önlemek için tüm çizim
/// çift tamponlu olarak WM_PAINT içinde yapılır.
/// Kare zamanı Timer aralığına güvenilmeden <see cref="Stopwatch"/> ile ölçülür.
/// </summary>
internal sealed class EffectCanvas : Control
{
    /// <summary>~60 FPS için kare aralığı (ms).</summary>
    private const int FrameIntervalMs = 16;

    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();

    /// <summary>Konuşma balonu yazı tipi (her karede yeniden üretilmesin diye alanda tutulur).</summary>
    private readonly Font _speechFont = new("Segoe UI", 26f, FontStyle.Bold);

    private long _lastTicks;
    private Color _backgroundCurrent = Theme.Background;
    private bool _disposedResources;

    /// <summary>
    /// Sahnedeki hayvan. Aynı anda EN FAZLA BİR hayvan olur; süresi dolmadan yenisi
    /// tetiklenirse mevcut olan anında yerini yenisine bırakır (kuyruk biriktirilmez).
    /// </summary>
    private AnimalCue? _animalCue;

    public EffectCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);

        TabStop = false;
        BackColor = Theme.Background;

        Engine = new EffectEngine();

        _timer = new System.Windows.Forms.Timer { Interval = FrameIntervalMs };
        _timer.Tick += OnFrameTick;
    }

    /// <summary>Efekt motoru (MainForm efekt üretmek için kullanır).</summary>
    public EffectEngine Engine { get; }

    /// <summary>Her karede tetiklenir; delta saniye cinsindendir (piyano görünümünü beslemek için).</summary>
    public event Action<float>? FrameUpdated;

    /// <summary>Animasyon döngüsünü başlatır.</summary>
    public void Start()
    {
        _stopwatch.Restart();
        _lastTicks = _stopwatch.ElapsedTicks;
        _timer.Start();
    }

    /// <summary>Animasyon döngüsünü durdurur.</summary>
    public void Stop()
    {
        _timer.Stop();
        _stopwatch.Stop();
    }

    /// <summary>Efekt üretir.</summary>
    public void Spawn(int midiNote, PointF origin, float intensity = 1f)
    {
        Engine.Spawn(midiNote, origin, intensity);
    }

    /// <summary>Sahnede şu anda bir hayvan var mı?</summary>
    public bool HasActiveAnimal => _animalCue != null;

    /// <summary>Sahnedeki hayvanın toplam süresi (saniye); hayvan yoksa 0.</summary>
    public float ActiveAnimalSeconds => _animalCue?.TotalSeconds ?? 0f;

    /// <summary>
    /// Hayvan kaybolma aşamasına geçtiğinde tetiklenir. MainForm bu anda çalan hayvan
    /// sesini durdurur; böylece ses hayvanla birlikte biter.
    /// </summary>
    public event Action? AnimalExiting;

    /// <summary>
    /// Hayvanı sahneye çıkarır. Var olan hayvan (süresi dolmasa bile) anında değişir.
    /// Piyano çalmayı hiç etkilemez; bu yalnızca ek bir çizim katmanıdır.
    /// </summary>
    /// <param name="kind">Sahneye çıkacak hayvan.</param>
    /// <param name="soundDurationMs">
    /// Çalınan sesin ölçülmüş süresi (ms). Sahne süresi buna göre ayarlanır;
    /// 0 ise sentez sesi için sabit süre kullanılır.
    /// </param>
    public void ShowAnimal(AnimalKind kind, int soundDurationMs = 0)
    {
        var cue = new AnimalCue(kind, soundDurationMs);
        cue.ExitStarted += OnAnimalExitStarted;
        _animalCue = cue;
        Invalidate();
    }

    private void OnAnimalExitStarted()
    {
        AnimalExiting?.Invoke();
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
            PaintGuard.Report(nameof(EffectCanvas), ex);
        }
    }

    private void PaintCore(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.Low;

        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Dikey gradyan: üstte son notanın rengine doğru hafifçe kayan koyu ton.
        using (var brush = new LinearGradientBrush(
                   bounds,
                   _backgroundCurrent,
                   Theme.BackgroundDeep,
                   LinearGradientMode.Vertical))
        {
            g.FillRectangle(brush, bounds);
        }

        Engine.Draw(g);

        // Hayvan katmanı: halka/parçacıkların ÜSTÜNDE çizilir.
        // Not: Çıkış butonu ayrı bir kontrol olduğu ve z-order'da önde durduğu için
        // hayvan onu asla kapatmaz; buton her zaman erişilebilir kalır.
        _animalCue?.Draw(g, new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), _speechFont);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Arka planı OnPaint içinde tamamen kendimiz çiziyoruz.
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        long now = _stopwatch.ElapsedTicks;
        float delta = (float)((now - _lastTicks) / (double)Stopwatch.Frequency);
        _lastTicks = now;

        // Uygulama askıya alınıp geri döndüğünde dev bir delta ile ekranı bozmayalım.
        delta = Math.Clamp(delta, 0f, 0.1f);

        Engine.Update(delta);

        if (_animalCue != null)
        {
            _animalCue.Update(delta);
            if (!_animalCue.IsAlive)
            {
                _animalCue.ExitStarted -= OnAnimalExitStarted;
                _animalCue = null;
            }
        }

        // Arka plan rengini son notanın rengine doğru yumuşakça (lerp) yaklaştır.
        Color target = Theme.Lerp(Theme.Background, Engine.LastNoteColor, 0.16f);
        _backgroundCurrent = Theme.Lerp(_backgroundCurrent, target, Math.Min(1f, delta * 3.5f));

        FrameUpdated?.Invoke(delta);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;
            _timer.Tick -= OnFrameTick;
            _timer.Stop();
            _timer.Dispose();
            Engine.Dispose();
            _speechFont.Dispose();
            _animalCue = null;
        }

        base.Dispose(disposing);
    }
}
