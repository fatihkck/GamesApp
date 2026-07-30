using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.Audio;
using GamesApp.UI;
using GamesApp.UI.Effects;

namespace GamesApp.Games.Peekaboo;

/// <summary>
/// "Cee-e!" sahnesi: ekranın ortasında altın çerçeveli bir kukla tiyatrosu ve kırmızı
/// kadife perde vardır. Karakter çağrıldığında perde hızla aralanır ve karakter
/// sahne zemininin ARKASINDAN yukarı fırlayıp "CEE-E!" der; bir süre neşeyle sallanır,
/// sonra aşağı kayboluverir ve perde kapanır.
///
/// NEDEN TEK KONTROL: Karakter perdenin arkasından çıkıyormuş gibi görünmek zorundadır;
/// bu, karakterin pencere dikdörtgenine KIRPILARAK çizilmesi ve perdenin ondan SONRA
/// çizilmesiyle sağlanır. Katman sırası tek yüzeyde kurulur (Zoo sahnesiyle aynı sebep).
///
/// SAHNE ASLA BOŞ KALMAZ (tasarım kuralı 8): Perde kapalıyken bile sahne canlıdır:
/// perde salınır, çerçevedeki ampuller sırayla yanıp söner, gökyüzünde yıldızlar
/// parıldar. Ayrıca perde uzun süre kapalı kalırsa <see cref="RevealRequested"/>
/// tetiklenir ve bir karakter kendiliğinden "Cee-e!" yapar (çocuğu basmaya davet eder).
///
/// Kare zamanı Timer aralığına güvenilmeden <see cref="Stopwatch"/> ile ölçülür.
/// </summary>
internal sealed class PeekabooStageView : Control
{
    /// <summary>~60 FPS için kare aralığı (ms).</summary>
    private const int FrameIntervalMs = 16;

    /// <summary>Perde bu kadar saniye kapalı kalırsa kendiliğinden bir "Cee-e!" istenir.</summary>
    private const float IdleInviteSeconds = 3.5f;

    /// <summary>Perdenin açılma süresi (saniye). Kısa: tepki tuşla aynı anda hissedilmeli.</summary>
    private const float OpenSeconds = 0.24f;

    /// <summary>
    /// Perdenin kapanma süresi (saniye). Açılış kadar hızlı: perde "şak" diye kapanır
    /// ve merak hemen yeniden kurulur (kullanıcı isteği: kapanış hızlı olsun).
    /// </summary>
    private const float CloseSeconds = 0.22f;

    /// <summary>Yıldız sayısı (perde kapalıyken sahneyi canlı tutan parıltılar).</summary>
    private const int StarCount = 34;

    /// <summary>Çerçeve ampul sayısı (üst kemer boyunca sırayla yanıp sönerler).</summary>
    private const int BulbCount = 13;

    private static readonly Color CurtainLight = Color.FromArgb(255, 206, 32, 54);
    private static readonly Color CurtainDark = Color.FromArgb(255, 116, 8, 28);
    private static readonly Color FrameGold = Color.FromArgb(255, 214, 168, 60);
    private static readonly Color FrameGoldDark = Color.FromArgb(255, 140, 102, 28);
    private static readonly Color StageBack = Color.FromArgb(255, 16, 26, 58);
    private static readonly Color FloorLight = Color.FromArgb(255, 122, 78, 44);
    private static readonly Color FloorDark = Color.FromArgb(255, 66, 40, 22);

    /// <summary>Konfeti patlamalarında kullanılan canlı renkler (kural 3).</summary>
    private static readonly Color[] ConfettiColors =
    {
        Color.FromArgb(255, 255, 214, 64),
        Color.FromArgb(255, 96, 228, 255),
        Color.FromArgb(255, 255, 96, 132),
        Color.FromArgb(255, 128, 255, 128),
        Color.FromArgb(255, 200, 128, 255)
    };

    private readonly Random _random = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();

    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White, 2f);
    private readonly Font _speechFont = new("Segoe UI", 34f, FontStyle.Bold);

    private readonly Star[] _stars = new Star[StarCount];

    private PeekActor? _actor;

    /// <summary>Perdenin açıklığı: 0 = kapalı, 1 = tamamen açık.</summary>
    private float _open;

    /// <summary>Perde kapalıyken biriken süre (davet zamanlayıcısı).</summary>
    private float _idleSeconds;

    /// <summary>Sürekli akan sahne zamanı (salınım ve ampul animasyonları için).</summary>
    private float _time;

    private long _lastTicks;
    private bool _disposedResources;

    public PeekabooStageView()
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

        var random = new Random(20260730);
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = Star.Create(random);
        }

        _timer = new System.Windows.Forms.Timer { Interval = FrameIntervalMs };
        _timer.Tick += OnFrameTick;
    }

    /// <summary>Efekt motoru (konfeti ve parıltılar).</summary>
    public EffectEngine Engine { get; }

    /// <summary>
    /// Perde uzun süre kapalı kaldı: bir "Cee-e!" istenir. Sesin de çalınması
    /// gerektiği için çağrıyı oyun modülü yapar (ses motoru oradadır).
    /// </summary>
    public event Action? RevealRequested;

    /// <summary>Fareyle sahneye tıklandı (ebeveyn için ikinci bir tetikleme yolu).</summary>
    public event Action? StageClicked;

    /// <summary>Sahnede şu anda görünen bir karakter var mı? (Selftest için.)</summary>
    public bool HasCharacter => _actor != null;

    /// <summary>Animasyon döngüsünü başlatır.</summary>
    public void Start()
    {
        _idleSeconds = 0f;
        _stopwatch.Restart();
        _lastTicks = _stopwatch.ElapsedTicks;
        _timer.Start();
    }

    /// <summary>Animasyon döngüsünü durdurur ve sahneyi temizler.</summary>
    public void Stop()
    {
        _timer.Stop();
        _stopwatch.Stop();

        Engine.Clear();
        _actor = null;
        _open = 0f;
        _idleSeconds = 0f;
    }

    /// <summary>
    /// Perdenin arkasından bir karakter fırlatır. Sahnede zaten bir karakter varsa
    /// küçük bir "puf" bulutuyla anında yenisiyle değişir: HER tuş basımı yeni bir
    /// fırlama üretir, neden-sonuç asla kesilmez.
    /// </summary>
    /// <param name="kind">Fırlayacak karakter.</param>
    /// <param name="soundSeconds">Çalınan sesin süresi (karakterin kalma süresini belirler).</param>
    public void Reveal(AnimalKind kind, float soundSeconds)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        RectangleF window = GetStageWindow();

        // Eski karakter varsa değişim anını "puf" bulutu örter (ani kaybolma sırıtmaz).
        if (_actor != null)
        {
            Engine.SpawnBurst(
                Color.FromArgb(255, 235, 235, 245),
                new PointF(window.X + window.Width * 0.5f, window.Y + window.Height * 0.55f),
                0.45f,
                0.3f);
        }

        _actor = new PeekActor(kind, soundSeconds, _random);
        _idleSeconds = 0f;

        // Konfeti: fırlama anı bir kutlamadır (renk her seferinde değişir).
        Color confetti = ConfettiColors[_random.Next(ConfettiColors.Length)];
        Engine.SpawnBurst(
            confetti,
            new PointF(window.X + window.Width * 0.5f, window.Y + window.Height * 0.35f),
            0.9f,
            0.55f,
            extraParticles: 10);

        Invalidate();
    }

    /// <summary>
    /// Auto-repeat tepkisi: yeni karakter fırlamaz, sahnedeki karakter neşeyle
    /// zıplar ve küçük bir parıltı çıkar (tasarım kuralı 7). Karakter yoksa perdenin
    /// ortasında parıltı çıkar: tepki asla kesilmez.
    /// </summary>
    public void Cheer()
    {
        RectangleF window = GetStageWindow();

        if (_actor == null)
        {
            Engine.SpawnBurst(
                Color.FromArgb(255, 255, 226, 120),
                new PointF(window.X + window.Width * 0.5f, window.Y + window.Height * 0.5f),
                0.35f,
                0.2f);
            Invalidate();
            return;
        }

        _actor.Cheer();
        Engine.SpawnBurst(
            Color.FromArgb(255, 255, 226, 120),
            new PointF(window.X + window.Width * 0.5f, window.Y + window.Height * 0.4f),
            0.35f,
            0.2f);
        Invalidate();
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

        _time += deltaSeconds;

        if (_actor != null)
        {
            _actor.Update(deltaSeconds);
            if (_actor.IsGone)
            {
                _actor = null;
            }
        }

        // Perde hedefi: karakter saklanmaya başlayana kadar açık kalır.
        bool wantOpen = _actor != null && !_actor.IsLeaving;
        if (wantOpen)
        {
            _open = Math.Min(1f, _open + deltaSeconds / OpenSeconds);
        }
        else
        {
            _open = Math.Max(0f, _open - deltaSeconds / CloseSeconds);
        }

        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i].Update(deltaSeconds);
        }

        Engine.Update(deltaSeconds);

        // Perde kapalı ve sahne boşsa bir süre sonra davet "Cee-e!"si istenir.
        if (_actor == null && _open <= 0.05f)
        {
            _idleSeconds += deltaSeconds;
            if (_idleSeconds >= IdleInviteSeconds)
            {
                _idleSeconds = 0f;
                RevealRequested?.Invoke();
            }
        }
        else
        {
            _idleSeconds = 0f;
        }
    }

    // ---------------- Yerleşim ----------------

    /// <summary>Sahne penceresi: perdelerin ve karakterin yaşadığı iç dikdörtgen.</summary>
    private RectangleF GetStageWindow()
    {
        float w = Math.Max(1, ClientSize.Width);
        float h = Math.Max(1, ClientSize.Height);

        return new RectangleF(w * 0.14f, h * 0.16f, w * 0.72f, h * 0.66f);
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
            PaintGuard.Report(nameof(PeekabooStageView), ex);
        }
    }

    private void PaintCore(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.Low;

        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 8 || bounds.Height <= 8)
        {
            return;
        }

        RectangleF window = GetStageWindow();

        DrawBackdrop(g, bounds);
        DrawStars(g, bounds);
        DrawStageInterior(g, window);

        // Karakter pencereye KIRPILARAK çizilir: alt kenardan "arkadan çıkıyormuş"
        // gibi belirir, perde de üstüne çizilebildiği için arkasında kalır.
        if (_actor != null)
        {
            GraphicsState state = g.Save();
            try
            {
                g.SetClip(window);
                _actor.Draw(g, window);
            }
            finally
            {
                g.Restore(state);
            }
        }

        DrawCurtains(g, window);
        DrawFrame(g, bounds, window);
        DrawFloor(g, bounds, window);

        // Konuşma balonu perdelerden ve çerçeveden SONRA çizilir: yazı hep okunur.
        _actor?.DrawBubble(g, new RectangleF(0f, 0f, bounds.Width, bounds.Height), window, _speechFont);

        // Konfeti ve parıltılar en üstte.
        Engine.Draw(g);
    }

    /// <summary>Gece göğü fonu: koyu lacivertten mor-laciverte inen gradyan.</summary>
    private void DrawBackdrop(Graphics g, Rectangle bounds)
    {
        using var sky = new LinearGradientBrush(
            bounds,
            Theme.Background,
            Color.FromArgb(255, 34, 14, 52),
            LinearGradientMode.Vertical);

        g.FillRectangle(sky, bounds);
    }

    private void DrawStars(Graphics g, Rectangle bounds)
    {
        for (int i = 0; i < _stars.Length; i++)
        {
            Star star = _stars[i];

            float size = Math.Max(2f, bounds.Height * 0.006f) * (0.6f + star.Brightness);
            float x = bounds.Width * star.X;
            float y = bounds.Height * star.Y;

            _brush.Color = Color.FromArgb(
                (int)Math.Clamp(star.Brightness * 200f, 0f, 255f),
                255,
                244,
                190);

            g.FillEllipse(_brush, x - size * 0.5f, y - size * 0.5f, size, size);
        }
    }

    /// <summary>Sahnenin içi: karakterin arkasındaki koyu fon ve zemin çizgisi.</summary>
    private void DrawStageInterior(Graphics g, RectangleF window)
    {
        if (window.Width < 8f || window.Height < 8f)
        {
            return;
        }

        using var back = new LinearGradientBrush(
            window,
            Color.FromArgb(255, 30, 48, 96),
            StageBack,
            LinearGradientMode.Vertical);

        g.FillRectangle(back, window);

        // İçerideki spot ışığı: karakterin duracağı yeri yumuşakça aydınlatır.
        float spotW = window.Width * 0.62f;
        float spotH = window.Height * 0.78f;
        if (spotW >= 12f && spotH >= 12f)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(
                window.X + (window.Width - spotW) * 0.5f,
                window.Bottom - spotH,
                spotW,
                spotH);

            using var spot = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(52, 255, 240, 190),
                SurroundColors = new[] { Color.FromArgb(0, 255, 240, 190) }
            };

            g.FillPath(spot, path);
        }
    }

    /// <summary>
    /// Kırmızı kadife perdeler. Açıldıkça iki yana toplanırlar ama tamamen yok
    /// olmazlar: kenarlarda büzülmüş bir tutam perde kalır (gerçek tiyatrodaki gibi).
    /// Kapalıyken her iki kanadın ortasında altın bir yıldız parlar: "arkasında ne
    /// var acaba?" davetiyesi.
    /// </summary>
    private void DrawCurtains(Graphics g, RectangleF window)
    {
        if (window.Width < 16f || window.Height < 16f)
        {
            return;
        }

        // Yumuşatılmış açıklık: perde ani değil süzülerek açılır.
        float eased = _open * _open * (3f - 2f * _open);
        float halfWidth = window.Width * (0.5f - 0.43f * eased);

        DrawCurtainHalf(g, window, halfWidth, left: true, eased);
        DrawCurtainHalf(g, window, halfWidth, left: false, eased);

        // Üst saçak (her zaman kapalı): perdenin "asıldığı" hissini verir.
        DrawValance(g, window);
    }

    private void DrawCurtainHalf(Graphics g, RectangleF window, float halfWidth, bool left, float eased)
    {
        if (halfWidth < 6f)
        {
            return;
        }

        var rect = new RectangleF(
            left ? window.X : window.Right - halfWidth,
            window.Y,
            halfWidth,
            window.Height);

        using (var velvet = new LinearGradientBrush(
                   rect,
                   left ? CurtainLight : CurtainDark,
                   left ? CurtainDark : CurtainLight,
                   LinearGradientMode.Horizontal))
        {
            g.FillRectangle(velvet, rect);
        }

        // Kadife kıvrımları: alt uçları hafifçe salınan dikey şeritler. Perde
        // toplandıkça şeritler sıklaşır (büzülme hissi kendiliğinden oluşur).
        int folds = Math.Max(3, (int)(halfWidth / 34f));
        float foldWidth = rect.Width / folds;

        if (foldWidth >= 3f)
        {
            for (int i = 0; i < folds; i++)
            {
                if (i % 2 == 0)
                {
                    continue; // her iki şeritten biri koyu: dokuyu bu kontrast verir
                }

                float sway = (float)Math.Sin(_time * 1.4 + i * 1.1 + (left ? 0.0 : 2.2))
                             * foldWidth * 0.35f * (0.4f + 0.6f * (1f - eased));

                float x = rect.X + i * foldWidth;
                _brush.Color = Color.FromArgb(70, 30, 0, 8);
                g.FillPolygon(_brush, new[]
                {
                    new PointF(x, rect.Y),
                    new PointF(x + foldWidth, rect.Y),
                    new PointF(x + foldWidth + sway, rect.Bottom),
                    new PointF(x + sway, rect.Bottom)
                });
            }
        }

        // İç kenarda altın şerit ve püskül: perdenin ağzı belirgin olsun (kural 3).
        float trim = Math.Max(3f, window.Width * 0.006f);
        _brush.Color = FrameGold;
        g.FillRectangle(
            _brush,
            left ? rect.Right - trim : rect.X,
            rect.Y,
            trim,
            rect.Height);

        float tassel = Math.Max(6f, trim * 2.6f);
        g.FillEllipse(
            _brush,
            (left ? rect.Right : rect.X) - tassel * 0.5f,
            rect.Y + rect.Height * 0.52f - tassel * 0.5f,
            tassel,
            tassel);

        // Kapalı perdedeki altın yıldız (açıldıkça kaybolur).
        float starAlpha = 1f - eased;
        float starRadius = Math.Min(window.Width, window.Height) * 0.055f;
        if (starAlpha > 0.05f && starRadius >= 6f && halfWidth > starRadius * 2.4f)
        {
            DrawStarShape(
                g,
                new PointF(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.42f),
                starRadius,
                Color.FromArgb((int)(220 * starAlpha), FrameGold));
        }
    }

    /// <summary>Beş köşeli yıldız (dekoratif; GDI+ ile vektör olarak çizilir).</summary>
    private void DrawStarShape(Graphics g, PointF center, float radius, Color color)
    {
        var points = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            float r = i % 2 == 0 ? radius : radius * 0.42f;
            double angle = -Math.PI / 2.0 + i * Math.PI / 5.0;
            points[i] = new PointF(
                center.X + (float)Math.Cos(angle) * r,
                center.Y + (float)Math.Sin(angle) * r);
        }

        _brush.Color = color;
        g.FillPolygon(_brush, points);
    }

    /// <summary>Perde saçağı: pencerenin üstünden sarkan kısa fisto şeridi.</summary>
    private void DrawValance(Graphics g, RectangleF window)
    {
        float height = window.Height * 0.10f;
        if (height < 6f)
        {
            return;
        }

        _brush.Color = CurtainDark;
        g.FillRectangle(_brush, window.X, window.Y, window.Width, height * 0.55f);

        int scallops = Math.Max(4, (int)(window.Width / 90f));
        float scallopWidth = window.Width / scallops;

        if (scallopWidth >= 6f && height >= 6f)
        {
            _brush.Color = CurtainLight;
            for (int i = 0; i < scallops; i++)
            {
                g.FillEllipse(
                    _brush,
                    window.X + i * scallopWidth,
                    window.Y,
                    scallopWidth,
                    height);
            }
        }
    }

    /// <summary>Altın çerçeve ve üzerindeki sırayla yanıp sönen ampuller.</summary>
    private void DrawFrame(Graphics g, Rectangle bounds, RectangleF window)
    {
        float thickness = Math.Max(6f, bounds.Height * 0.022f);

        var outer = new RectangleF(
            window.X - thickness,
            window.Y - thickness,
            window.Width + thickness * 2f,
            window.Height + thickness * 2f);

        if (outer.Width < 12f || outer.Height < 12f)
        {
            return;
        }

        // Çerçeve: dışı koyu, içi parlak altın (hacim hissi).
        _pen.Color = FrameGoldDark;
        _pen.Width = thickness;
        g.DrawRectangle(_pen, outer.X, outer.Y, outer.Width, outer.Height);

        _pen.Color = FrameGold;
        _pen.Width = thickness * 0.5f;
        g.DrawRectangle(_pen, outer.X, outer.Y, outer.Width, outer.Height);

        // Üst kemer ampulleri: kapalı perdeyi bile "bir şey olacak" hissiyle süsler.
        float bulbSize = Math.Max(4f, thickness * 0.7f);
        for (int i = 0; i < BulbCount; i++)
        {
            float x = outer.X + outer.Width * (i + 0.5f) / BulbCount;
            float y = outer.Y;

            // Kovalayan ışık: parlaklık dalgası ampuller boyunca akar.
            float wave = (float)(0.5 + 0.5 * Math.Sin(_time * 4.0 - i * 0.9));

            _brush.Color = Theme.Lerp(
                Color.FromArgb(255, 110, 84, 30),
                Color.FromArgb(255, 255, 240, 160),
                wave);

            g.FillEllipse(_brush, x - bulbSize * 0.5f, y - bulbSize * 0.5f, bulbSize, bulbSize);
        }
    }

    /// <summary>Sahne önündeki ahşap zemin: pencerenin altını ekran kenarına bağlar.</summary>
    private void DrawFloor(Graphics g, Rectangle bounds, RectangleF window)
    {
        float top = window.Bottom + Math.Max(6f, bounds.Height * 0.022f);
        var floor = new RectangleF(0f, top, bounds.Width, bounds.Height - top);

        if (floor.Height < 4f)
        {
            return;
        }

        using (var wood = new LinearGradientBrush(floor, FloorLight, FloorDark, LinearGradientMode.Vertical))
        {
            g.FillRectangle(wood, floor);
        }

        // Tahta derzleri: sahneye doğru daralan perspektif çizgileri.
        _pen.Color = Color.FromArgb(120, 40, 22, 10);
        _pen.Width = Math.Max(1.5f, bounds.Height * 0.003f);

        const int planks = 9;
        float centerX = bounds.Width * 0.5f;
        for (int i = 1; i < planks; i++)
        {
            float t = i / (float)planks;
            float xTop = centerX + (t - 0.5f) * window.Width * 1.05f;
            float xBottom = centerX + (t - 0.5f) * bounds.Width * 1.25f;
            g.DrawLine(_pen, xTop, floor.Y, xBottom, floor.Bottom);
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
            _speechFont.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---------------- Sahne öğeleri ----------------

    /// <summary>Yavaşça yanıp sönen gökyüzü yıldızı.</summary>
    private struct Star
    {
        private float _phase;
        private float _speed;

        public float X { get; private set; }

        public float Y { get; private set; }

        /// <summary>0-1 arası parlaklık.</summary>
        public float Brightness { get; private set; }

        public static Star Create(Random random) => new()
        {
            X = (float)random.NextDouble(),
            Y = (float)random.NextDouble() * 0.85f,
            _phase = (float)(random.NextDouble() * Math.PI * 2.0),
            _speed = 0.8f + (float)random.NextDouble() * 1.8f,
            Brightness = (float)random.NextDouble()
        };

        public void Update(float deltaSeconds)
        {
            _phase += deltaSeconds * _speed;
            Brightness = 0.2f + 0.8f * (float)Math.Abs(Math.Sin(_phase));
        }
    }

    /// <summary>
    /// Perdenin arkasından fırlayan tek bir karakterin ömrü:
    /// fırlar (aşırıp geri oturan zıplama) → neşeyle sallanır → aşağı kayboluverir.
    /// Çizim <see cref="AnimalArtist"/> ile, "CEE-E!" balonu <see cref="SpeechBubble"/>
    /// ile yapılır; ikisi de diğer oyunlarla ortaktır.
    /// </summary>
    private sealed class PeekActor
    {
        /// <summary>Fırlama süresi (saniye). Kısa: ses ve görüntü aynı anda patlar (kural 5).</summary>
        private const float PopSeconds = 0.30f;

        /// <summary>Saklanma (aşağı kayboluş) süresi (saniye).</summary>
        private const float HideSeconds = 0.24f;

        private readonly float _holdSeconds;
        private readonly float _wobblePhase;
        private readonly int _tiltDirection;

        private float _age;

        /// <summary>Kalan neşe zıplaması (auto-repeat tepkisi); 1'den 0'a iner.</summary>
        private float _cheer;

        public PeekActor(AnimalKind kind, float soundSeconds, Random random)
        {
            Kind = kind;

            // Karakter ses bitene kadar sahnede kalır (en az 1,5 sn: bebek yüzü
            // görmeye doysun; en çok 3 sn: perde kapanıp merak yeniden kurulsun).
            _holdSeconds = Math.Clamp(soundSeconds + 0.5f, 1.5f, 3.0f);
            _wobblePhase = (float)(random.NextDouble() * Math.PI * 2.0);
            _tiltDirection = random.Next(2) == 0 ? 1 : -1;
        }

        public AnimalKind Kind { get; }

        /// <summary>Karakter saklanma (aşağı inme) aşamasına geçti mi? (Perde bununla kapanır.)</summary>
        public bool IsLeaving => _age >= PopSeconds + _holdSeconds;

        public bool IsGone => _age >= PopSeconds + _holdSeconds + HideSeconds;

        public void Update(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            _age += deltaSeconds;

            if (_cheer > 0f)
            {
                _cheer = Math.Max(0f, _cheer - deltaSeconds * 2.5f);
            }
        }

        public void Cheer()
        {
            _cheer = 1f;
        }

        /// <summary>Karakteri sahne penceresinin içine çizer (pencereye kırpılmış hâlde).</summary>
        public void Draw(Graphics g, RectangleF window)
        {
            RectangleF box = GetBox(window, out float rotation);

            if (box.Width < 12f)
            {
                // GDI+ TUZAĞI: sıfıra yakın boyutlu şekiller sahte OutOfMemoryException
                // fırlatır; çok küçük kutuda karakter hiç çizilmez (kural 10).
                return;
            }

            if (Math.Abs(rotation) < 0.5f)
            {
                AnimalArtist.Draw(g, Kind, box, 1f);
                return;
            }

            // Sallanma eğimi: karakter kendi merkezi etrafında döndürülür.
            GraphicsState state = g.Save();
            try
            {
                g.TranslateTransform(box.X + box.Width * 0.5f, box.Y + box.Height * 0.5f);
                g.RotateTransform(rotation);
                g.TranslateTransform(-(box.X + box.Width * 0.5f), -(box.Y + box.Height * 0.5f));
                AnimalArtist.Draw(g, Kind, box, 1f);
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>
        /// "CEE-E!" balonunu çizer. Balon kırpma alanının DIŞINDA, perdeden sonra
        /// çizilir: yazı perdeye ve çerçeveye rağmen her zaman okunur.
        /// </summary>
        public void DrawBubble(Graphics g, RectangleF area, RectangleF window, Font font)
        {
            float alpha = GetBubbleAlpha();
            if (alpha <= 0.02f)
            {
                return;
            }

            RectangleF box = GetBox(window, out _);
            if (box.Width < 12f)
            {
                return;
            }

            SpeechBubble.Draw(g, area, box, "CEE-E!", font, alpha, preferAbove: true);
        }

        /// <summary>Balon fırlamanın sonunda belirir, saklanırken hızla kaybolur.</summary>
        private float GetBubbleAlpha()
        {
            if (_age < PopSeconds * 0.55f)
            {
                return 0f;
            }

            if (_age < PopSeconds)
            {
                return (_age - PopSeconds * 0.55f) / (PopSeconds * 0.45f);
            }

            if (_age < PopSeconds + _holdSeconds)
            {
                return 1f;
            }

            float p = (_age - PopSeconds - _holdSeconds) / HideSeconds;
            return Math.Max(0f, 1f - p * 2f);
        }

        /// <summary>Karakterin o anki kutusunu ve eğim açısını hesaplar.</summary>
        private RectangleF GetBox(RectangleF window, out float rotation)
        {
            float side = Math.Min(window.Width * 0.46f, window.Height * 0.80f);
            float centerX = window.X + window.Width * 0.5f;
            float feetY = window.Bottom - window.Height * 0.02f;

            float yOffset;
            rotation = 0f;

            if (_age < PopSeconds)
            {
                // Fırlama: hedefi hafifçe AŞIP geri oturur (easeOutBack); zıplayan
                // kutudan fırlayan oyuncak hissi verir.
                float p = Math.Clamp(_age / PopSeconds, 0f, 1f);
                const float c1 = 1.70158f;
                const float c3 = c1 + 1f;
                float eased = 1f + c3 * (p - 1f) * (p - 1f) * (p - 1f) + c1 * (p - 1f) * (p - 1f);

                yOffset = (1f - eased) * side * 1.08f;
                rotation = (1f - p) * 10f * _tiltDirection;
            }
            else if (_age < PopSeconds + _holdSeconds)
            {
                // Bekleme: iki yana sallanma + hafif hoplama (asla donuk durmaz).
                float t = _age - PopSeconds;
                yOffset = -(float)Math.Abs(Math.Sin(t * 3.4 + _wobblePhase)) * side * 0.04f;
                rotation = (float)Math.Sin(t * 4.6 + _wobblePhase) * 5f;
            }
            else
            {
                // Saklanma: hızlanarak aşağı kayar (perde arkasına düşer).
                float p = Math.Clamp((_age - PopSeconds - _holdSeconds) / HideSeconds, 0f, 1f);
                yOffset = p * p * side * 1.2f;
                rotation = p * 8f * -_tiltDirection;
            }

            // Neşe zıplaması (auto-repeat): her fazın üstüne binen kısa hoplama.
            if (_cheer > 0f)
            {
                yOffset -= (float)Math.Sin(_cheer * Math.PI) * side * 0.14f;
            }

            return new RectangleF(
                centerX - side * 0.5f,
                feetY - side + yOffset,
                side,
                side);
        }
    }
}
