using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.UI;
using GamesApp.UI.Effects;

namespace GamesApp.Games.Paint;

/// <summary>
/// Sihirli Fırça tuvali: koyu bir tuval üzerine tuş başına büyük, parlak boya
/// lekeleri (ana daire + fırça izi + saçılan damlacıklar) KALICI olarak boyanır.
/// Tuval bir ekran-dışı <see cref="Bitmap"/>'te birikir; her kare yalnızca bu bitmap
/// ve üstündeki geçici efektler çizilir. Böylece yüzlerce leke birikse de kare
/// maliyeti sabit kalır.
///
/// TUŞ = YER: Her tuşun tuval üzerinde SABİT bir bölgesi vardır (vkCode'dan türetilir,
/// üstüne küçük bir titreşim eklenir). Çocuk aynı tuşa basınca boyanın hep aynı yere
/// geldiğini keşfeder; farklı tuşlara basınca tuvalin farklı köşeleri renklenir.
///
/// TAMAMLANMA: Doluluk, piksellerden bağımsız NORMALİZE bir hücre ızgarasıyla izlenir
/// (24x14). Tuval yeterince dolunca (%80) kutlama başlar: konfeti yağar, fanfar çalar
/// (<see cref="CanvasCompleted"/> ile oyun modülü çalar), beyaz bir parlama içinde
/// tuval temizlenir ve boyama BAŞTAN başlar.
///
/// SAHNE ASLA BOŞ KALMAZ (tasarım kuralı 8): Oyuna girişte karşılama lekesi gelir
/// (modül yapar); tuvale 4 saniye dokunulmazsa <see cref="SplatRequested"/> tetiklenir
/// ve kendiliğinden bir leke düşüp çocuğu basmaya davet eder.
///
/// Kare zamanı Timer aralığına güvenilmeden <see cref="Stopwatch"/> ile ölçülür.
/// </summary>
internal sealed class PaintCanvasView : Control
{
    /// <summary>~60 FPS için kare aralığı (ms).</summary>
    private const int FrameIntervalMs = 16;

    /// <summary>Tuvale bu kadar saniye dokunulmazsa kendiliğinden bir leke istenir.</summary>
    private const float IdleInviteSeconds = 4.0f;

    /// <summary>Bu dolulukta tablo "tamamlandı" sayılır ve kutlamalı sıfırlama başlar.</summary>
    private const float ResetThreshold = 0.80f;

    /// <summary>Kutlamalı sıfırlama animasyonunun süresi (saniye).</summary>
    private const float ResetSeconds = 1.6f;

    /// <summary>Doluluk ızgarası (normalize hücreler; pencere boyutundan bağımsız).</summary>
    private const int GridColumns = 24;
    private const int GridRows = 14;

    /// <summary>Tuval zemini: lekelerin parlaklığını en çok gösteren koyu ton (kural 3).</summary>
    private static readonly Color CanvasColor = Theme.BackgroundDeep;

    private readonly Random _random = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();

    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White, 3f);

    private readonly bool[] _cells = new bool[GridColumns * GridRows];
    private int _coveredCells;

    private Bitmap? _canvas;
    private Graphics? _canvasGraphics;

    /// <summary>Altın açı ile dönen renk tekerleği konumu (ardışık lekeler hep zıt renkte).</summary>
    private double _hue;

    /// <summary>Sıfırlama animasyonu ilerlemesi; negatifse animasyon yok.</summary>
    private float _resetProgress = -1f;

    /// <summary>Sıfırlama animasyonu içinde tuval gerçekten temizlendi mi?</summary>
    private bool _resetCleared;

    private float _idleSeconds;
    private long _lastTicks;
    private bool _disposedResources;

    public PaintCanvasView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);

        TabStop = false;
        BackColor = CanvasColor;

        Engine = new EffectEngine();
        _hue = _random.NextDouble() * 360.0;

        _timer = new System.Windows.Forms.Timer { Interval = FrameIntervalMs };
        _timer.Tick += OnFrameTick;
    }

    /// <summary>Efekt motoru (leke halkaları ve kutlama konfetisi).</summary>
    public EffectEngine Engine { get; }

    /// <summary>
    /// Tuvale uzun süre dokunulmadı: bir leke istenir. Sesin de çalınması gerektiği
    /// için çağrıyı oyun modülü yapar (ses motoru oradadır).
    /// </summary>
    public event Action? SplatRequested;

    /// <summary>Tablo tamamlandı: kutlama başladı (fanfarı oyun modülü çalar).</summary>
    public event Action? CanvasCompleted;

    /// <summary>Fareyle tuvale tıklandı (ebeveyn için ikinci bir tetikleme yolu).</summary>
    public event Action? StageClicked;

    /// <summary>Tuvalin dolu hücre oranı (0-1; selftest için).</summary>
    public float CoverageRatio => _coveredCells / (float)_cells.Length;

    /// <summary>Tablo kaç kez tamamlanıp sıfırlandı (selftest için).</summary>
    public int ResetCount { get; private set; }

    /// <summary>Animasyon döngüsünü başlatır. Var olan resim korunur (oyuna geri dönülebilir).</summary>
    public void Start()
    {
        _idleSeconds = 0f;
        _stopwatch.Restart();
        _lastTicks = _stopwatch.ElapsedTicks;
        _timer.Start();
    }

    /// <summary>
    /// Animasyon döngüsünü durdurur. Tuvaldeki RESİM SİLİNMEZ: çocuk başka oyuna
    /// geçip geri döndüğünde tablosunu kaldığı yerde bulur.
    /// </summary>
    public void Stop()
    {
        _timer.Stop();
        _stopwatch.Stop();

        Engine.Clear();
        _idleSeconds = 0f;
    }

    /// <summary>
    /// Tuşun bölgesine büyük bir boya lekesi vurur: ana daire, fırça izi ve saçılan
    /// damlacıklar tuvale KALICI çizilir; üstüne geçici bir halka efekti biner.
    /// </summary>
    public void Splat(int vkCode)
    {
        EnsureCanvas();
        if (_canvas == null || _canvasGraphics == null)
        {
            return;
        }

        float w = _canvas.Width;
        float h = _canvas.Height;

        PointF pos = GetKeyPosition(vkCode, w, h);
        float radius = Math.Min(w, h) * 0.11f * (0.85f + (float)_random.NextDouble() * 0.50f);
        Color color = NextColor();

        if (radius >= 6f)
        {
            PaintSplat(_canvasGraphics, pos, radius, color);
            MarkCoverage(pos, radius * 1.15f, w, h);
        }

        // Geçici halka + parçacıklar: vuruş anı ekranda "hissedilir" (kural 5).
        Engine.SpawnBurst(color, pos, 0.85f, 0.5f, extraParticles: 5);

        _idleSeconds = 0f;

        // Tablo doldu mu? Kutlamalı sıfırlama yalnızca bir kez tetiklenir.
        if (_resetProgress < 0f && CoverageRatio >= ResetThreshold)
        {
            BeginCelebration(w, h);
        }

        Invalidate();
    }

    /// <summary>
    /// Auto-repeat tepkisi: yeni leke vurulmaz, tuşun bölgesine küçük damlacıklar
    /// serpilir (tasarım kuralı 7: tepki kesilmez ama ana eylem tekrarlanmaz).
    /// </summary>
    public void Sprinkle(int vkCode)
    {
        EnsureCanvas();
        if (_canvas == null || _canvasGraphics == null)
        {
            return;
        }

        float w = _canvas.Width;
        float h = _canvas.Height;

        PointF pos = GetKeyPosition(vkCode, w, h);
        float radius = Math.Min(w, h) * 0.11f;
        Color color = Theme.ColorFromHsv(_hue, 0.88, 0.97);

        _brush.Color = color;
        for (int i = 0; i < 4; i++)
        {
            double angle = _random.NextDouble() * Math.PI * 2.0;
            float distance = radius * (0.6f + (float)_random.NextDouble() * 1.4f);
            float size = radius * (0.10f + (float)_random.NextDouble() * 0.14f);

            if (size < 2f)
            {
                continue;
            }

            _canvasGraphics.FillEllipse(
                _brush,
                pos.X + (float)Math.Cos(angle) * distance - size * 0.5f,
                pos.Y + (float)Math.Sin(angle) * distance - size * 0.5f,
                size,
                size);
        }

        Engine.SpawnBurst(color, pos, 0.3f, 0.2f);
        Invalidate();
    }

    // ---------------- Kutlama ve sıfırlama ----------------

    /// <summary>Kutlamayı başlatır: konfeti yağmuru + fanfar isteği; tuval parlamada temizlenir.</summary>
    private void BeginCelebration(float w, float h)
    {
        _resetProgress = 0f;
        _resetCleared = false;
        ResetCount++;

        // Konfeti yağmuru: tuvalin dört bir yanında renkli patlamalar.
        for (int i = 0; i < 6; i++)
        {
            Engine.SpawnBurst(
                Theme.ColorFromHsv(_random.NextDouble() * 360.0, 0.9, 1.0),
                new PointF(
                    w * (0.15f + 0.7f * (float)_random.NextDouble()),
                    h * (0.15f + 0.6f * (float)_random.NextDouble())),
                1.1f,
                0.7f,
                extraParticles: 12);
        }

        CanvasCompleted?.Invoke();
    }

    /// <summary>Tuvali ve doluluk ızgarasını temizler (yeni tabloya hazırlar).</summary>
    private void ClearCanvas()
    {
        _canvasGraphics?.Clear(CanvasColor);
        Array.Clear(_cells);
        _coveredCells = 0;
    }

    // ---------------- Fare ----------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            StageClicked?.Invoke();
        }
    }

    // ---------------- Kare döngüsü ----------------

    private void OnFrameTick(object? sender, EventArgs e)
    {
        long now = _stopwatch.ElapsedTicks;
        float delta = (float)((now - _lastTicks) / (double)Stopwatch.Frequency);
        _lastTicks = now;

        // Uygulama askıya alınıp geri döndüğünde dev bir delta ile sahneyi bozmayalım.
        delta = Math.Clamp(delta, 0f, 0.1f);

        Advance(delta);
        Invalidate();
    }

    /// <summary>
    /// Bir kareyi ilerletir. Selftest/stres testinden de doğrudan çağrılabilir
    /// (zamanlayıcı çalışmadan mantığın işletilebilmesi için).
    /// </summary>
    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return;
        }

        Engine.Update(deltaSeconds);

        // Kutlamalı sıfırlama: parlama tepe noktasındayken tuval temizlenir; çocuk
        // silinme anını değil, parlamadan çıkan bembeyaz yeni tuvali görür.
        if (_resetProgress >= 0f)
        {
            _resetProgress += deltaSeconds / ResetSeconds;

            if (!_resetCleared && _resetProgress >= 0.5f)
            {
                ClearCanvas();
                _resetCleared = true;
            }

            if (_resetProgress >= 1f)
            {
                _resetProgress = -1f;
            }
        }
        else
        {
            // Davet lekesi: tuvale uzun süre dokunulmadıysa kendiliğinden bir leke gelir.
            _idleSeconds += deltaSeconds;
            if (_idleSeconds >= IdleInviteSeconds)
            {
                _idleSeconds = 0f;
                SplatRequested?.Invoke();
            }
        }
    }

    // ---------------- Konum, renk ve doluluk ----------------

    /// <summary>
    /// Tuşun tuval üzerindeki sabit bölgesi + küçük titreşim. vkCode'dan türetilen
    /// karma DETERMİNİSTİKTİR: aynı tuş hep aynı bölgeyi boyar (neden-sonuç öğrenimi),
    /// titreşim aynı noktada üst üste binmeyi önler.
    /// </summary>
    private PointF GetKeyPosition(int vkCode, float w, float h)
    {
        uint hash = (uint)vkCode * 2654435761u;

        float marginX = w * 0.08f;
        float marginY = h * 0.10f;

        float x = marginX + (hash & 0xFFFF) / 65535f * (w - marginX * 2f);
        float y = marginY + ((hash >> 16) & 0xFFFF) / 65535f * (h - marginY * 2f);

        float jitter = Math.Min(w, h) * 0.05f;
        x += ((float)_random.NextDouble() * 2f - 1f) * jitter;
        y += ((float)_random.NextDouble() * 2f - 1f) * jitter;

        return new PointF(Math.Clamp(x, 0f, w), Math.Clamp(y, 0f, h));
    }

    /// <summary>
    /// Sıradaki leke rengi: renk tekerleği altın açıyla (137,5°) döner; ardışık
    /// lekeler her zaman birbirinden uzak, canlı renkler alır (kural 3).
    /// </summary>
    private Color NextColor()
    {
        _hue = (_hue + 137.5) % 360.0;
        return Theme.ColorFromHsv(_hue, 0.88, 0.97);
    }

    /// <summary>Lekenin kapladığı hücreleri işaretler (normalize ızgara, boyuttan bağımsız).</summary>
    private void MarkCoverage(PointF pos, float radius, float w, float h)
    {
        float cellW = w / GridColumns;
        float cellH = h / GridRows;

        if (cellW <= 0f || cellH <= 0f)
        {
            return;
        }

        int colFrom = Math.Max(0, (int)((pos.X - radius) / cellW));
        int colTo = Math.Min(GridColumns - 1, (int)((pos.X + radius) / cellW));
        int rowFrom = Math.Max(0, (int)((pos.Y - radius) / cellH));
        int rowTo = Math.Min(GridRows - 1, (int)((pos.Y + radius) / cellH));

        float radiusSquared = radius * radius;

        for (int row = rowFrom; row <= rowTo; row++)
        {
            for (int col = colFrom; col <= colTo; col++)
            {
                float dx = (col + 0.5f) * cellW - pos.X;
                float dy = (row + 0.5f) * cellH - pos.Y;

                if (dx * dx + dy * dy > radiusSquared)
                {
                    continue;
                }

                int index = row * GridColumns + col;
                if (!_cells[index])
                {
                    _cells[index] = true;
                    _coveredCells++;
                }
            }
        }
    }

    // ---------------- Tuvale çizim ----------------

    /// <summary>
    /// Tek bir boya lekesini tuvale çizer: fırça izi (uzun eğik damla), ana daire,
    /// parlak öz ve saçılan damlacıklar.
    /// </summary>
    private void PaintSplat(Graphics g, PointF pos, float radius, Color color)
    {
        // Fırça izi: lekeden rastgele yöne uzanan basık elips (sürülme hissi).
        double strokeAngle = _random.NextDouble() * 360.0;
        float strokeLength = radius * (1.9f + (float)_random.NextDouble() * 1.0f);
        float strokeHeight = radius * 0.55f;

        if (strokeLength >= 8f && strokeHeight >= 4f)
        {
            GraphicsState state = g.Save();
            try
            {
                g.TranslateTransform(pos.X, pos.Y);
                g.RotateTransform((float)strokeAngle);

                _brush.Color = Color.FromArgb(200, color);
                g.FillEllipse(_brush, -strokeLength * 0.15f, -strokeHeight * 0.5f, strokeLength, strokeHeight);
            }
            finally
            {
                g.Restore(state);
            }
        }

        // Ana daire: dolgun leke + koyu dış hat (kural 3: belirgin kontur).
        _brush.Color = color;
        g.FillEllipse(_brush, pos.X - radius, pos.Y - radius, radius * 2f, radius * 2f);

        _pen.Color = Theme.Lerp(color, Color.Black, 0.35f);
        _pen.Width = Math.Max(2.5f, radius * 0.07f);
        g.DrawEllipse(_pen, pos.X - radius, pos.Y - radius, radius * 2f, radius * 2f);

        // Parlak öz: lekenin sol üstünde ışık yansıması (ıslak boya hissi).
        float core = radius * 0.42f;
        if (core >= 3f)
        {
            _brush.Color = Theme.Lerp(color, Color.White, 0.45f);
            g.FillEllipse(
                _brush,
                pos.X - radius * 0.38f - core * 0.5f,
                pos.Y - radius * 0.38f - core * 0.5f,
                core,
                core);
        }

        // Damlacıklar: lekenin etrafına saçılan 5-8 küçük nokta.
        int droplets = 5 + _random.Next(4);
        _brush.Color = color;
        for (int i = 0; i < droplets; i++)
        {
            double angle = _random.NextDouble() * Math.PI * 2.0;
            float distance = radius * (1.2f + (float)_random.NextDouble() * 1.1f);
            float size = radius * (0.10f + (float)_random.NextDouble() * 0.20f);

            if (size < 2f)
            {
                continue;
            }

            g.FillEllipse(
                _brush,
                pos.X + (float)Math.Cos(angle) * distance - size * 0.5f,
                pos.Y + (float)Math.Sin(angle) * distance - size * 0.5f,
                size,
                size);
        }
    }

    // ---------------- Tuval yönetimi ----------------

    /// <summary>
    /// Tuval bitmap'ini pencere boyutuna uydurur. Boyut değişiminde eski resim yeni
    /// tuvale ÖLÇEKLENEREK aktarılır: çocuğun tablosu pencereyle birlikte büyür,
    /// asla silinmez.
    /// </summary>
    private void EnsureCanvas()
    {
        int w = ClientSize.Width;
        int h = ClientSize.Height;

        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (_canvas != null && _canvas.Width == w && _canvas.Height == h)
        {
            return;
        }

        var next = new Bitmap(w, h);
        Graphics nextGraphics = Graphics.FromImage(next);
        nextGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        nextGraphics.Clear(CanvasColor);

        if (_canvas != null)
        {
            nextGraphics.DrawImage(_canvas, new Rectangle(0, 0, w, h));
            _canvasGraphics?.Dispose();
            _canvas.Dispose();
        }

        _canvas = next;
        _canvasGraphics = nextGraphics;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        EnsureCanvas();
    }

    // ---------------- Çizim ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        // İstisna OnPaint'ten kaçarsa WinForms kontrolü kalıcı "kırmızı çarpı"
        // moduna sokar; bu yüzden kare bazında yutulur ve log'a yazılır (kural 10).
        try
        {
            PaintCore(e.Graphics);
        }
        catch (Exception ex)
        {
            PaintGuard.Report(nameof(PaintCanvasView), ex);
        }
    }

    private void PaintCore(Graphics g)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 8 || bounds.Height <= 8)
        {
            return;
        }

        EnsureCanvas();

        if (_canvas != null)
        {
            g.DrawImageUnscaled(_canvas, 0, 0);
        }
        else
        {
            g.Clear(CanvasColor);
        }

        // Konfeti ve halkalar resmin üstünde.
        Engine.Draw(g);

        // Kutlama parlaması: beyaz ışık yükselir (tepede tuval temizlenmiştir) ve söner.
        if (_resetProgress >= 0f)
        {
            int alpha = (int)Math.Clamp(Math.Sin(_resetProgress * Math.PI) * 255.0, 0.0, 255.0);
            if (alpha > 2)
            {
                _brush.Color = Color.FromArgb(alpha, 255, 252, 240);
                g.FillRectangle(_brush, bounds);
            }
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Arka planı OnPaint içinde tamamen kendimiz çiziyoruz.
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
            _brush.Dispose();
            _pen.Dispose();

            _canvasGraphics?.Dispose();
            _canvas?.Dispose();
        }

        base.Dispose(disposing);
    }
}
