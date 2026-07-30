using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.UI;
using GamesApp.UI.Effects;

namespace GamesApp.Games.Bubbles;

/// <summary>
/// Balon tarlası: yavaşça yukarı süzülen renkli balonları ve patlama konfetisini
/// TEK kontrol içinde çizer.
///
/// NEDEN TEK KONTROL: Konfetinin balonların ÜSTÜNDE görünmesi gerekir; WinForms'ta
/// kardeş kontroller arasında gerçek saydamlık olmadığı için katmanlar aynı yüzeye
/// sırayla çizilir. Parçacık/halka üretimi için piyano ve davulda da kullanılan
/// <see cref="EffectEngine"/> yeniden kullanılır.
///
/// NOT: Bu oyunda hayvan sürprizi YOKTUR (kullanıcı kararı); ödül tamamen patlama
/// anının kendisidir. Hayvanlar piyano ve davul oyunlarında çıkmaya devam eder.
///
/// Kare zamanı Timer aralığına güvenilmeden <see cref="Stopwatch"/> ile ölçülür.
/// </summary>
internal sealed class BalloonFieldView : Control
{
    /// <summary>~60 FPS için kare aralığı (ms).</summary>
    private const int FrameIntervalMs = 16;

    /// <summary>Ekranda tutulmak istenen balon sayısı.</summary>
    private const int TargetBalloonCount = 12;

    /// <summary>Aynı anda izin verilen en fazla balon (güvenlik sınırı).</summary>
    private const int MaxBalloonCount = 26;

    /// <summary>Yeni balon üretme aralığı (saniye).</summary>
    private const float SpawnIntervalSeconds = 0.45f;

    /// <summary>
    /// Canlı ve yüksek kontrastlı balon renkleri. 1,5 yaş tasarım kuralı: parlak,
    /// doygun renkler ve belirgin hatlar daha kolay takip edilir.
    /// </summary>
    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 255, 60, 90),    // kırmızı
        Color.FromArgb(255, 255, 145, 30),   // turuncu
        Color.FromArgb(255, 255, 215, 45),   // sarı
        Color.FromArgb(255, 70, 215, 110),   // yeşil
        Color.FromArgb(255, 45, 175, 255),   // mavi
        Color.FromArgb(255, 170, 100, 255),  // mor
        Color.FromArgb(255, 255, 105, 190),  // pembe
        Color.FromArgb(255, 60, 225, 220)    // turkuaz
    };

    private readonly List<Balloon> _balloons = new(MaxBalloonCount);
    private readonly Random _random = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();

    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White, 2f);

    private long _lastTicks;
    private float _spawnAccumulator;
    private Size _lastSize;
    private bool _disposedResources;

    public BalloonFieldView()
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

    /// <summary>Efekt motoru (konfeti ve halkalar).</summary>
    public EffectEngine Engine { get; }

    /// <summary>Fare ile bir balona tıklandı (balonun merkezi ve rengi).</summary>
    public event Action<PointF, Color>? BalloonClicked;

    /// <summary>Ekrandaki balon sayısı.</summary>
    public int BalloonCount => _balloons.Count;

    /// <summary>Animasyon döngüsünü başlatır ve tarlayı balonlarla doldurur.</summary>
    public void Start()
    {
        Fill();

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
        _balloons.Clear();
    }

    /// <summary>
    /// En görünür balonu patlatır: büyüklüğü ve ekran ortasına yakınlığı yüksek olan
    /// seçilir; böylece çocuk hangi balonun patladığını kolayca görür (neden-sonuç).
    /// Balon yoksa false döner.
    /// </summary>
    public bool PopMostVisible(out PointF center, out Color color)
    {
        center = PointF.Empty;
        color = Color.White;

        if (_balloons.Count == 0)
        {
            return false;
        }

        int width = Math.Max(1, ClientSize.Width);
        int height = Math.Max(1, ClientSize.Height);

        int bestIndex = 0;
        float bestScore = float.MinValue;

        for (int i = 0; i < _balloons.Count; i++)
        {
            Balloon balloon = _balloons[i];

            // Ekran dışındaki (henüz alttan girmemiş) balonlar seçilmesin.
            if (balloon.Y - balloon.RadiusY > height)
            {
                continue;
            }

            // Yatayda ve dikeyde merkeze yakınlık + büyüklük puanı.
            float centerX = 1f - Math.Abs(balloon.X - width * 0.5f) / (width * 0.5f);
            float centerY = 1f - Math.Abs(balloon.Y - height * 0.5f) / (height * 0.5f);
            float score = centerX * 0.8f + centerY * 1.0f + balloon.Radius / height * 4f;

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return PopAt(bestIndex, out center, out color);
    }

    /// <summary>Verilen indeksteki balonu patlatır ve konfeti üretir.</summary>
    private bool PopAt(int index, out PointF center, out Color color)
    {
        center = PointF.Empty;
        color = Color.White;

        if (index < 0 || index >= _balloons.Count)
        {
            return false;
        }

        Balloon balloon = _balloons[index];
        center = new PointF(balloon.X, balloon.Y);
        color = balloon.Color;

        _balloons.RemoveAt(index);

        // Konfeti: balonun renginde cömert bir patlama + beyaz ışık parlaması.
        Engine.SpawnBurst(color, center, 1.3f, 0.55f, extraParticles: 18);
        Engine.SpawnBurst(Color.White, center, 0.7f, 0.15f, extraParticles: 4);

        Invalidate();
        return true;
    }

    /// <summary>
    /// Basılı tutulan tuş (auto-repeat) için hafif geri bildirim: rastgele bir
    /// balonun yanında küçük bir parıltı çıkar ama balon PATLAMAZ. Böylece tarla
    /// bir tuşa yüklenildiğinde bir anda boşalmaz.
    /// </summary>
    public void Sparkle()
    {
        if (_balloons.Count == 0)
        {
            return;
        }

        Balloon balloon = _balloons[_random.Next(_balloons.Count)];
        Engine.SpawnBurst(balloon.Color, new PointF(balloon.X, balloon.Y), 0.35f, 0.2f);
        Invalidate();
    }

    // ---------------- Fare ----------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // Öndeki (listede sonda olan) balonlar önce denenir.
        for (int i = _balloons.Count - 1; i >= 0; i--)
        {
            if (_balloons[i].Contains(e.X, e.Y))
            {
                if (PopAt(i, out PointF center, out Color color))
                {
                    BalloonClicked?.Invoke(center, color);
                }

                return;
            }
        }
    }

    // ---------------- Kare döngüsü ----------------

    private void OnFrameTick(object? sender, EventArgs e)
    {
        long now = _stopwatch.ElapsedTicks;
        float delta = (float)((now - _lastTicks) / (double)Stopwatch.Frequency);
        _lastTicks = now;

        // Uygulama askıya alınıp geri döndüğünde dev bir delta ile ekranı bozmayalım.
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

        int height = ClientSize.Height;

        for (int i = _balloons.Count - 1; i >= 0; i--)
        {
            Balloon balloon = _balloons[i];
            balloon.Update(deltaSeconds);

            // Üstten çıkanlar silinir (yerine alttan yenisi doğar).
            if (balloon.Y + balloon.RadiusY < -20f)
            {
                _balloons.RemoveAt(i);
            }
        }

        // Tarla yarıdan fazla boşaldıysa (hızlı patlatan çocuk) acil besleme devreye
        // girer: aralık kısalır, iki balon birden doğar ve ekranın hemen altından
        // girerler. Böylece "sahne asla boş kalmaz" kuralı hızlı basımda da tutar.
        bool needsBoost = _balloons.Count < TargetBalloonCount / 2;
        float interval = needsBoost ? SpawnIntervalSeconds * 0.35f : SpawnIntervalSeconds;

        _spawnAccumulator += deltaSeconds;
        if (_spawnAccumulator >= interval && height > 0)
        {
            _spawnAccumulator = 0f;

            if (_balloons.Count < TargetBalloonCount)
            {
                _balloons.Add(CreateBalloon(fromBottom: true, hurried: needsBoost));

                if (needsBoost && _balloons.Count < MaxBalloonCount)
                {
                    _balloons.Add(CreateBalloon(fromBottom: true, hurried: true));
                }
            }
        }

        Engine.Update(deltaSeconds);
    }

    // ---------------- Balon üretimi ----------------

    /// <summary>
    /// Oyun açılırken tarlayı hazır balonlarla doldurur: ekran boş başlamaz,
    /// çocuk ilk saniyeden itibaren patlatacak balon bulur.
    /// Selftest/stres testi de zamanlayıcı başlatmadan bunu kullanır.
    /// </summary>
    public void Fill()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0 || _balloons.Count > 0)
        {
            return;
        }

        // Yatayda şerit şerit dağıtılır (rastgele X kümelenmeye yol açıyordu),
        // dikeyde rastgele: açılışta ekranın her yerinde balon bulunur.
        for (int i = 0; i < TargetBalloonCount; i++)
        {
            _balloons.Add(CreateBalloon(fromBottom: false, column: i, columnCount: TargetBalloonCount));
        }
    }

    /// <summary>
    /// Yeni balon üretir.
    /// </summary>
    /// <param name="fromBottom">
    /// true: ekranın altından girer (oyun sırasında).
    /// false: ekran yüksekliğine rastgele serpilir (açılış dolgusu).
    /// </param>
    /// <param name="column">
    /// Verilirse balon bu yatay şeride yerleştirilir (açılışta eşit dağılım için).
    /// -1 ise yatay konum rastgele seçilir.
    /// </param>
    /// <param name="columnCount">Şerit sayısı (yalnızca <paramref name="column"/> ile anlamlı).</param>
    /// <param name="hurried">
    /// Acil besleme: balon ekranın hemen altından girer (uzun süre görünmez kalmaz),
    /// böylece hızlı patlatan çocuk anında yeni hedef bulur.
    /// </param>
    private Balloon CreateBalloon(bool fromBottom, int column = -1, int columnCount = 1, bool hurried = false)
    {
        int width = Math.Max(1, ClientSize.Width);
        int height = Math.Max(1, ClientSize.Height);

        float radius = height * (0.065f + (float)_random.NextDouble() * 0.045f);
        float margin = radius * 1.4f;
        float usableWidth = Math.Max(1f, width - margin * 2f);

        float baseX;
        if (column >= 0 && columnCount > 0)
        {
            // Şerit ortası + şerit genişliğinin yarısı kadar sapma.
            float bandWidth = usableWidth / columnCount;
            float jitter = ((float)_random.NextDouble() - 0.5f) * bandWidth * 0.9f;
            baseX = margin + bandWidth * (column + 0.5f) + jitter;
        }
        else
        {
            baseX = margin + (float)_random.NextDouble() * usableWidth;
        }

        float y;
        if (!fromBottom)
        {
            y = (float)_random.NextDouble() * height;
        }
        else if (hurried)
        {
            // Gövdesinin yarısı ekranda: hemen görünür ve patlatılabilir olur.
            y = height + radius * (0.2f + (float)_random.NextDouble() * 0.5f);
        }
        else
        {
            y = height + radius * (1.2f + (float)_random.NextDouble() * 1.5f);
        }

        return new Balloon
        {
            BaseX = baseX,
            Y = y,
            Radius = radius,
            RiseSpeed = height * (0.035f + (float)_random.NextDouble() * 0.040f),
            WobbleAmplitude = width * (0.008f + (float)_random.NextDouble() * 0.022f),
            WobbleSpeed = 0.6f + (float)_random.NextDouble() * 0.9f,
            WobblePhase = (float)(_random.NextDouble() * Math.PI * 2.0),
            Color = Palette[_random.Next(Palette.Length)]
        };
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // Boyut değişince balonlar oransal olarak taşınır; ekran dışında kalmazlar.
        if (_lastSize.Width > 0 && _lastSize.Height > 0 &&
            ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            float scaleX = ClientSize.Width / (float)_lastSize.Width;
            float scaleY = ClientSize.Height / (float)_lastSize.Height;

            for (int i = 0; i < _balloons.Count; i++)
            {
                _balloons[i].Rescale(scaleX, scaleY);
            }
        }

        _lastSize = ClientSize;
    }

    // ---------------- Çizim ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        // İstisna OnPaint'ten kaçarsa WinForms kontrolü kalıcı "kırmızı çarpı"
        // moduna sokar ve oyun görseli bir daha çizilmez; bu yüzden kare bazında
        // yutulur ve hata %TEMP%\gamesapp-paint.log dosyasına yazılır.
        try
        {
            PaintCore(e.Graphics);
        }
        catch (Exception ex)
        {
            PaintGuard.Report(nameof(BalloonFieldView), ex);
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

        // Arka plan: koyu gökyüzü gradyanı (balonların canlı renkleri öne çıksın).
        using (var background = new LinearGradientBrush(
                   bounds,
                   Theme.Background,
                   Theme.BackgroundDeep,
                   LinearGradientMode.Vertical))
        {
            g.FillRectangle(background, bounds);
        }

        // Balonlar: küçükler (uzaktakiler) önce çizilir, büyükler önde kalır.
        _balloons.Sort(static (a, b) => a.Radius.CompareTo(b.Radius));
        for (int i = 0; i < _balloons.Count; i++)
        {
            DrawBalloon(g, _balloons[i]);
        }

        // Konfeti/halkalar balonların ÜSTÜNDE.
        Engine.Draw(g);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Arka planı OnPaint içinde tamamen kendimiz çiziyoruz.
    }

    /// <summary>Parlak, belirgin hatlı balon: ip, düğüm, gövde gradyanı ve ışık lekesi.</summary>
    private void DrawBalloon(Graphics g, Balloon balloon)
    {
        float x = balloon.X;
        float y = balloon.Y;
        float rx = balloon.Radius;
        float ry = balloon.RadiusY;

        // GDI+ TUHAFLIĞI: sıfıra yakın boyutlu şekiller/gradyanlar sahte
        // OutOfMemoryException fırlatır. Çok küçük balon hiç çizilmez.
        if (rx < 6f || ry < 6f)
        {
            return;
        }

        var body = new RectangleF(x - rx, y - ry, rx * 2f, ry * 2f);

        // --- İp: hafifçe sallanan kavis ---
        float stringLength = ry * 1.5f;
        float sway = (float)Math.Sin(balloon.WobblePhase * 1.6) * rx * 0.35f;
        _pen.Color = Color.FromArgb(150, 240, 240, 250);
        _pen.Width = Math.Max(1.5f, rx * 0.045f);
        using (var stringPath = new GraphicsPath())
        {
            stringPath.AddBezier(
                new PointF(x, y + ry),
                new PointF(x + sway, y + ry + stringLength * 0.35f),
                new PointF(x - sway, y + ry + stringLength * 0.70f),
                new PointF(x + sway * 0.5f, y + ry + stringLength));
            g.DrawPath(_pen, stringPath);
        }

        // --- Düğüm: gövdenin altında küçük üçgen ---
        float knot = rx * 0.18f;
        _brush.Color = Theme.Lerp(balloon.Color, Color.Black, 0.25f);
        g.FillPolygon(_brush, new[]
        {
            new PointF(x - knot, y + ry + knot * 0.2f),
            new PointF(x + knot, y + ry + knot * 0.2f),
            new PointF(x, y + ry - knot * 0.6f)
        });

        // --- Gövde: merkezden dışa doğru açıktan koyuya (parlak lastik hissi) ---
        using (var bodyPath = new GraphicsPath())
        {
            bodyPath.AddEllipse(body);

            using var gradient = new PathGradientBrush(bodyPath)
            {
                CenterPoint = new PointF(x - rx * 0.28f, y - ry * 0.30f),
                CenterColor = Theme.Lerp(balloon.Color, Color.White, 0.55f),
                SurroundColors = new[] { Theme.Lerp(balloon.Color, Color.Black, 0.30f) }
            };

            g.FillPath(gradient, bodyPath);
        }

        // --- Belirgin dış hat (yüksek kontrast kuralı) ---
        _pen.Color = Theme.Lerp(balloon.Color, Color.Black, 0.55f);
        _pen.Width = Math.Max(2f, rx * 0.055f);
        g.DrawEllipse(_pen, body);

        // --- Işık lekesi: sol üstte küçük beyaz parlama ---
        float shine = rx * 0.30f;
        if (shine >= 2f)
        {
            _brush.Color = Color.FromArgb(170, 255, 255, 255);
            g.FillEllipse(
                _brush,
                x - rx * 0.45f - shine * 0.5f,
                y - ry * 0.50f - shine * 0.5f,
                shine,
                shine * 1.25f);
        }
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
        }

        base.Dispose(disposing);
    }
}
