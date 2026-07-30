using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.Audio;
using GamesApp.UI;
using GamesApp.UI.Effects;

namespace GamesApp.Games.Zoo;

/// <summary>
/// Hayvanın sahnede durabileceği yer. <c>X</c> genişliğin oranı, <c>Depth</c> ise
/// derinliktir: 0 = uzak (küçük ve yukarıda), 1 = yakın (büyük ve aşağıda).
/// </summary>
internal readonly struct ZooSlot
{
    public ZooSlot(int index, float x, float depth)
    {
        Index = index;
        X = x;
        Depth = depth;
    }

    /// <summary>Slotun sıra numarası (doluluk takibi için).</summary>
    public int Index { get; }

    /// <summary>Yatay konum (genişliğin oranı, 0-1).</summary>
    public float X { get; }

    /// <summary>Derinlik (0 = uzak, 1 = yakın).</summary>
    public float Depth { get; }
}

/// <summary>
/// Hayvanat Bahçesi sahnesi: orman arka planı ve sahnedeki hayvanları TEK kontrol
/// içinde çizer.
///
/// NEDEN TEK KONTROL: Hayvanlar, konuşma balonları ve toz/parıltı efektleri
/// birbirinin üstüne binmek zorundadır; WinForms'ta kardeş kontroller arasında gerçek
/// saydamlık olmadığı için tüm katmanlar aynı yüzeye sırayla çizilir.
///
/// SAHNE ASLA BOŞ KALMAZ (tasarım kuralı 8): Orman kendi başına canlıdır (ağaçlar, ay,
/// süzülen ateş böcekleri, çimen). Buna ek olarak sahnede uzun süre hiç hayvan yoksa
/// <see cref="AnimalRequested"/> tetiklenir ve bir hayvan kendiliğinden gelip çocuğu
/// tekrar tuşa basmaya davet eder.
///
/// Kare zamanı Timer aralığına güvenilmeden <see cref="Stopwatch"/> ile ölçülür.
/// </summary>
internal sealed class ZooStageView : Control
{
    /// <summary>~60 FPS için kare aralığı (ms).</summary>
    private const int FrameIntervalMs = 16;

    /// <summary>Çimenin başladığı yükseklik (yüksekliğin oranı).</summary>
    private const float GrassTop = 0.72f;

    /// <summary>Sahne bu kadar saniye hayvansız kalırsa kendiliğinden bir hayvan çağrılır.</summary>
    private const float IdleInviteSeconds = 2.5f;

    /// <summary>Ateş böceği sayısı (sahneyi canlı tutan küçük ışıklar).</summary>
    private const int FireflyCount = 18;

    /// <summary>
    /// Sahnede aynı anda bulunabilecek en fazla hayvan: 4 duran + 2 gitmekte olan.
    ///
    /// NEDEN SERT SINIR: Slotlar dolduğunda en yaşlı hayvan çıkışa zorlanır ama yarım
    /// saniye daha sahnededir. Tuşlara hızla basıldığında bu "gitmekte olanlar"
    /// birikip sahneyi kalabalıklaştırıyordu (selftest 10 hayvan görmüştü). Sınır
    /// aşılırsa en yaşlı hayvan doğrudan kaldırılır: sahne her zaman okunur kalır.
    /// </summary>
    public const int MaxActors = 6;

    /// <summary>
    /// Hayvanların durabileceği yerler. Yatayda ayrık, derinlikte farklı: aynı anda
    /// birden fazla hayvan varken üst üste binmezler ve sahne düz görünmez.
    /// </summary>
    private static readonly ZooSlot[] Slots =
    {
        new(0, 0.20f, 0.30f),
        new(1, 0.44f, 0.02f),
        new(2, 0.68f, 0.62f),
        new(3, 0.88f, 0.22f)
    };

    /// <summary>Ağaç gövdesi rengi.</summary>
    private static readonly Color TrunkColor = Color.FromArgb(255, 74, 50, 34);

    /// <summary>Çimen şeridinin renkleri (üstte canlı, altta koyu).</summary>
    private static readonly Color GrassLight = Color.FromArgb(255, 46, 132, 62);
    private static readonly Color GrassDark = Color.FromArgb(255, 12, 52, 30);

    /// <summary>Gökyüzünün alt tonu (orman yeşiline çalan koyu mavi).</summary>
    private static readonly Color SkyBottom = Color.FromArgb(255, 10, 34, 30);

    /// <summary>Çiçek renkleri (yüksek kontrast noktalar).</summary>
    private static readonly Color[] FlowerColors =
    {
        Color.FromArgb(255, 255, 96, 132),
        Color.FromArgb(255, 255, 214, 64),
        Color.FromArgb(255, 176, 128, 255),
        Color.FromArgb(255, 96, 228, 255)
    };

    private readonly List<ZooActor> _actors = new(Slots.Length + 2);
    private readonly Random _random = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();

    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White, 2f);
    private readonly Font _speechFont = new("Segoe UI", 30f, FontStyle.Bold);

    /// <summary>Orman düzeni (boyut değişince yeniden üretilir).</summary>
    private readonly List<Tree> _trees = new(14);

    /// <summary>Çimen üstündeki çiçekler (boyutla birlikte üretilir).</summary>
    private readonly List<Flower> _flowers = new(18);

    private readonly Firefly[] _fireflies = new Firefly[FireflyCount];

    private long _lastTicks;
    private float _emptySeconds;
    private Size _layoutSize;
    private bool _disposedResources;

    public ZooStageView()
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

        for (int i = 0; i < _fireflies.Length; i++)
        {
            _fireflies[i] = Firefly.Create(_random);
        }

        _timer = new System.Windows.Forms.Timer { Interval = FrameIntervalMs };
        _timer.Tick += OnFrameTick;
    }

    /// <summary>Efekt motoru (toz bulutu ve parıltılar).</summary>
    public EffectEngine Engine { get; }

    /// <summary>
    /// Sahne uzun süre boş kaldı: bir hayvan çağrılmasını ister. Sesi de çalınması
    /// gerektiği için çağrıyı oyun modülü yapar (ses motoru oradadır).
    /// </summary>
    public event Action? AnimalRequested;

    /// <summary>Fareyle sahneye tıklandı (ebeveyn için ikinci bir tetikleme yolu).</summary>
    public event Action? StageClicked;

    /// <summary>Sahnedeki hayvan sayısı.</summary>
    public int ActorCount => _actors.Count;

    /// <summary>Animasyon döngüsünü başlatır.</summary>
    public void Start()
    {
        BuildLayout();

        _emptySeconds = IdleInviteSeconds; // ilk hayvan hemen istenir
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
        _actors.Clear();
        _emptySeconds = 0f;
    }

    /// <summary>
    /// Sahneye bir hayvan çıkarır. Tüm slotlar doluysa en yaşlı hayvan çıkışa
    /// zorlanır ve yeni hayvan onun yerine gelir; böylece HER tuş basımı görünür
    /// bir sonuç üretir (neden-sonuç kuralı).
    /// </summary>
    /// <param name="kind">Sahneye çıkacak hayvan.</param>
    /// <param name="soundSeconds">Çalınan sesin süresi (bekleme süresini belirler).</param>
    public void Summon(AnimalKind kind, float soundSeconds)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        ZooSlot slot = TakeSlot();
        var actor = new ZooActor(
            kind,
            slot,
            soundSeconds,
            new SizeF(ClientSize.Width, ClientSize.Height),
            _random);

        _actors.Add(actor);
        _emptySeconds = 0f;

        // Sert sınır: fazlalık varsa en yaşlı (gitmekte olan) hayvanlar kaldırılır.
        while (_actors.Count > MaxActors)
        {
            RemoveOldest();
        }

        // Giriş toz bulutu: hayvanın geldiği yer belli olsun.
        PointF center = actor.Center;
        Engine.SpawnBurst(
            Color.FromArgb(255, 214, 196, 150),
            new PointF(center.X, center.Y + actor.Side * 0.45f),
            0.55f,
            0.25f,
            extraParticles: 4);

        Invalidate();
    }

    /// <summary>
    /// Auto-repeat tepkisi: yeni hayvan gelmez, sahnedeki hayvanlar neşeyle zıplar
    /// ve küçük bir parıltı çıkar (tasarım kuralı 7).
    /// </summary>
    public void Cheer()
    {
        if (_actors.Count == 0)
        {
            // Sahne bir an boş kaldıysa (nadir) tepki gene de verilir: sessiz kalmayız.
            Engine.SpawnBurst(
                Color.FromArgb(255, 255, 226, 120),
                new PointF(ClientSize.Width * 0.5f, ClientSize.Height * 0.5f),
                0.35f,
                0.2f);
            Invalidate();
            return;
        }

        ZooActor actor = _actors[_random.Next(_actors.Count)];
        actor.Cheer();

        PointF center = actor.Center;
        Engine.SpawnBurst(Color.FromArgb(255, 255, 226, 120), center, 0.35f, 0.2f);
        Invalidate();
    }

    // ---------------- Slot yönetimi ----------------

    /// <summary>Boş bir slot verir; hepsi doluysa en yaşlı hayvanı çıkışa zorlar.</summary>
    private ZooSlot TakeSlot()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            if (!IsSlotBusy(Slots[i].Index))
            {
                return Slots[i];
            }
        }

        ZooActor oldest = _actors[0];
        for (int i = 1; i < _actors.Count; i++)
        {
            if (_actors[i].Age > oldest.Age)
            {
                oldest = _actors[i];
            }
        }

        oldest.ForceExit();
        return oldest.Slot;
    }

    /// <summary>Sahnede en uzun süredir bulunan hayvanı kaldırır.</summary>
    private void RemoveOldest()
    {
        int oldestIndex = 0;
        for (int i = 1; i < _actors.Count; i++)
        {
            if (_actors[i].Age > _actors[oldestIndex].Age)
            {
                oldestIndex = i;
            }
        }

        _actors.RemoveAt(oldestIndex);
    }

    /// <summary>
    /// Slot dolu mu? Çıkışa geçmiş (gitmekte olan) hayvan slotu bloke etmez:
    /// yeni hayvan onun yerine gelmeye başlayabilir.
    /// </summary>
    private bool IsSlotBusy(int slotIndex)
    {
        for (int i = 0; i < _actors.Count; i++)
        {
            ZooActor actor = _actors[i];
            if (actor.Slot.Index == slotIndex && actor.Age < actor.TotalSeconds - 0.55f)
            {
                return true;
            }
        }

        return false;
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

        for (int i = _actors.Count - 1; i >= 0; i--)
        {
            ZooActor actor = _actors[i];
            actor.Update(deltaSeconds);

            if (!actor.IsAlive)
            {
                _actors.RemoveAt(i);
            }
        }

        for (int i = 0; i < _fireflies.Length; i++)
        {
            _fireflies[i].Update(deltaSeconds);
        }

        Engine.Update(deltaSeconds);

        // Sahne boş kaldıysa bir süre sonra davet hayvanı istenir.
        if (_actors.Count == 0)
        {
            _emptySeconds += deltaSeconds;
            if (_emptySeconds >= IdleInviteSeconds)
            {
                _emptySeconds = 0f;
                AnimalRequested?.Invoke();
            }
        }
        else
        {
            _emptySeconds = 0f;
        }
    }

    // ---------------- Yerleşim ----------------

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        BuildLayout();
    }

    /// <summary>
    /// Orman düzenini (ağaçlar ve çiçekler) üretir. Boyut değişmediyse hiçbir şey
    /// yapılmaz: her karede yeniden üretmek ormanı titretirdi.
    ///
    /// Tohum SABİTTİR: aynı çözünürlükte orman her açılışta aynı görünür (çocuk için
    /// tanıdık bir yer olur) ve stres testi tekrarlanabilir kalır.
    /// </summary>
    private void BuildLayout()
    {
        if (ClientSize == _layoutSize || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        _layoutSize = ClientSize;
        _trees.Clear();
        _flowers.Clear();

        var random = new Random(20260730);

        // Ağaçlar: hayvanların ARKASINDA kalır, bu yüzden yüzleri hiç kapatmaz.
        // Yatayda kenarlara doğru daha yoğun, ortada daha seyrek dizilirler.
        const int treeCount = 11;
        for (int i = 0; i < treeCount; i++)
        {
            float x = (i + 0.5f) / treeCount + (float)(random.NextDouble() - 0.5) * 0.04f;
            bool conifer = random.Next(2) == 0;
            float height = 0.24f + (float)random.NextDouble() * 0.20f;
            float shade = (float)random.NextDouble();

            _trees.Add(new Tree(x, height, conifer, shade));
        }

        // Çiçekler: çimenin üst kısmına serpilir.
        const int flowerCount = 16;
        for (int i = 0; i < flowerCount; i++)
        {
            _flowers.Add(new Flower(
                (i + 0.5f) / flowerCount + (float)(random.NextDouble() - 0.5) * 0.05f,
                GrassTop + 0.02f + (float)random.NextDouble() * 0.20f,
                FlowerColors[random.Next(FlowerColors.Length)]));
        }
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
            PaintGuard.Report(nameof(ZooStageView), ex);
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

        DrawSky(g, bounds);
        DrawMoon(g, bounds);
        DrawTrees(g, bounds);
        DrawFireflies(g, bounds);
        DrawGround(g, bounds);

        // Hayvanlar: uzaktakiler (derinliği düşük) önce çizilir, yakındakiler önde kalır.
        _actors.Sort(static (a, b) => a.Slot.Depth.CompareTo(b.Slot.Depth));

        var area = new RectangleF(0f, 0f, bounds.Width, bounds.Height);
        for (int i = 0; i < _actors.Count; i++)
        {
            _actors[i].Draw(g, area, _speechFont);
        }

        // Ön plandaki çalılar: yalnızca alt köşelerde, hayvanların ayak hizasının
        // altında kalır (yüz kapatmaz ama derinlik hissi verir).
        DrawForegroundBushes(g, bounds);

        // Konuşma balonları TÜM hayvanlardan sonra çizilir: yandaki hayvan balonu
        // kapatmaz, yazı her zaman okunur.
        for (int i = 0; i < _actors.Count; i++)
        {
            _actors[i].DrawBubble(g, area, _speechFont);
        }

        // Toz ve parıltılar en üstte.
        Engine.Draw(g);
    }

    private void DrawSky(Graphics g, Rectangle bounds)
    {
        using var sky = new LinearGradientBrush(
            bounds,
            Theme.Background,
            SkyBottom,
            LinearGradientMode.Vertical);

        g.FillRectangle(sky, bounds);
    }

    /// <summary>Sağ üstte yumuşak ışıklı ay: gökyüzü boş bir yüzey gibi durmasın.</summary>
    private void DrawMoon(Graphics g, Rectangle bounds)
    {
        float radius = Math.Max(10f, bounds.Height * 0.055f);
        float cx = bounds.Width * 0.84f;
        float cy = bounds.Height * 0.14f;

        // Işıma: dıştan içe doğru artan sarı parıltı.
        float glow = radius * 2.6f;
        if (glow >= 6f)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(cx - glow, cy - glow, glow * 2f, glow * 2f);

            using var halo = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(cx, cy),
                CenterColor = Color.FromArgb(70, 255, 238, 170),
                SurroundColors = new[] { Color.FromArgb(0, 255, 238, 170) }
            };

            g.FillPath(halo, path);
        }

        _brush.Color = Color.FromArgb(255, 255, 244, 198);
        g.FillEllipse(_brush, cx - radius, cy - radius, radius * 2f, radius * 2f);
    }

    private void DrawTrees(Graphics g, Rectangle bounds)
    {
        float grassY = bounds.Height * GrassTop;

        for (int i = 0; i < _trees.Count; i++)
        {
            Tree tree = _trees[i];

            float x = bounds.Width * tree.X;
            float treeHeight = bounds.Height * tree.Height;
            float baseY = grassY + bounds.Height * 0.02f;
            float crownWidth = treeHeight * 0.62f;

            if (treeHeight < 12f || crownWidth < 8f)
            {
                continue;
            }

            // Gövde
            float trunkWidth = Math.Max(3f, crownWidth * 0.16f);
            _brush.Color = Theme.Lerp(TrunkColor, Color.Black, 0.25f + tree.Shade * 0.2f);
            g.FillRectangle(
                _brush,
                x - trunkWidth * 0.5f,
                baseY - treeHeight * 0.42f,
                trunkWidth,
                treeHeight * 0.42f);

            // Taç: koyu orman tonları (hayvanların canlı renkleri öne çıksın).
            Color crown = Theme.Lerp(
                Color.FromArgb(255, 24, 92, 60),
                Color.FromArgb(255, 12, 54, 40),
                tree.Shade);

            if (tree.IsConifer)
            {
                // Çam: üst üste üç üçgen.
                for (int layer = 0; layer < 3; layer++)
                {
                    float layerWidth = crownWidth * (1f - layer * 0.22f);
                    float top = baseY - treeHeight * (0.40f + layer * 0.22f);
                    float bottom = top + treeHeight * 0.34f;

                    _brush.Color = Theme.Lerp(crown, Color.White, layer * 0.06f);
                    g.FillPolygon(_brush, new[]
                    {
                        new PointF(x, top),
                        new PointF(x - layerWidth * 0.5f, bottom),
                        new PointF(x + layerWidth * 0.5f, bottom)
                    });
                }
            }
            else
            {
                // Yuvarlak taç: üç kabarcık.
                float r = crownWidth * 0.42f;
                float topY = baseY - treeHeight * 0.52f;

                _brush.Color = crown;
                g.FillEllipse(_brush, x - r, topY - r * 0.6f, r * 2f, r * 1.8f);
                g.FillEllipse(_brush, x - r * 1.15f, topY + r * 0.35f, r * 1.5f, r * 1.4f);
                g.FillEllipse(_brush, x - r * 0.35f, topY + r * 0.35f, r * 1.5f, r * 1.4f);
            }
        }
    }

    private void DrawFireflies(Graphics g, Rectangle bounds)
    {
        for (int i = 0; i < _fireflies.Length; i++)
        {
            Firefly fly = _fireflies[i];

            float size = Math.Max(2f, bounds.Height * 0.006f) * (0.7f + fly.Brightness * 0.9f);
            float x = bounds.Width * fly.X;
            float y = bounds.Height * fly.Y;

            _brush.Color = Color.FromArgb(
                (int)Math.Clamp(fly.Brightness * 220f, 0f, 255f),
                255,
                240,
                150);

            g.FillEllipse(_brush, x - size * 0.5f, y - size * 0.5f, size, size);
        }
    }

    /// <summary>Çimen şeridi, ot tutamları ve çiçekler.</summary>
    private void DrawGround(Graphics g, Rectangle bounds)
    {
        float grassY = bounds.Height * GrassTop;
        var grass = new RectangleF(0f, grassY, bounds.Width, bounds.Height - grassY);

        if (grass.Height < 4f)
        {
            return;
        }

        using (var brush = new LinearGradientBrush(grass, GrassLight, GrassDark, LinearGradientMode.Vertical))
        {
            g.FillRectangle(brush, grass);
        }

        // Üst kenarda ot tutamları. Boyları ve eğimleri belirgin biçimde DEĞİŞİR:
        // eşit boylu diziliş çimen değil çit gibi görünüyordu.
        _pen.Color = Theme.Lerp(GrassLight, Color.White, 0.18f);
        _pen.Width = Math.Max(2f, bounds.Height * 0.005f);
        _pen.StartCap = LineCap.Round;
        _pen.EndCap = LineCap.Round;

        float step = Math.Max(16f, bounds.Width / 40f);
        int index = 0;
        for (float x = step * 0.5f; x < bounds.Width; x += step, index++)
        {
            // Deterministik ama düzensiz görünen boy/eğim (tohumsuz, saf aritmetik).
            float variation = (index * 37 % 11) / 10f;
            float bladeHeight = bounds.Height * (0.012f + variation * 0.028f);
            float lean = (index % 3 - 1) * step * 0.30f;

            g.DrawLine(_pen, x, grassY + bounds.Height * 0.004f, x + lean, grassY - bladeHeight);
        }

        // Çiçekler
        float flowerSize = Math.Max(4f, bounds.Height * 0.012f);
        for (int i = 0; i < _flowers.Count; i++)
        {
            Flower flower = _flowers[i];
            float x = bounds.Width * flower.X;
            float y = bounds.Height * flower.Y;

            _brush.Color = flower.Color;
            g.FillEllipse(_brush, x - flowerSize * 0.5f, y - flowerSize * 0.5f, flowerSize, flowerSize);

            _brush.Color = Color.FromArgb(255, 255, 250, 210);
            float core = flowerSize * 0.4f;
            g.FillEllipse(_brush, x - core * 0.5f, y - core * 0.5f, core, core);
        }
    }

    /// <summary>Alt köşelerdeki çalılar: sahneyi çerçeveler, hayvan yüzlerini kapatmaz.</summary>
    private void DrawForegroundBushes(Graphics g, Rectangle bounds)
    {
        float r = bounds.Height * 0.085f;
        if (r < 8f)
        {
            return;
        }

        // Çimenden AYRIŞACAK kadar farklı bir yeşil ve koyu kontur: aksi hâlde çalılar
        // çimenin içinde kaybolur (ilk denemede hiç görünmüyorlardı).
        Color bush = Color.FromArgb(255, 30, 104, 62);

        _pen.Color = Color.FromArgb(255, 8, 40, 24);
        _pen.Width = Math.Max(2f, r * 0.05f);

        for (int side = 0; side < 2; side++)
        {
            float cx = side == 0 ? -r * 0.3f : bounds.Width + r * 0.3f;
            float inward = side == 0 ? r * 0.7f : -r * 2.7f;

            var main = new RectangleF(cx - r, bounds.Height - r * 0.9f, r * 2f, r * 1.8f);
            var side2 = new RectangleF(cx + inward, bounds.Height - r * 0.6f, r * 2f, r * 1.5f);

            _brush.Color = bush;
            g.FillEllipse(_brush, main);
            g.DrawEllipse(_pen, main);

            _brush.Color = Theme.Lerp(bush, Color.Black, 0.2f);
            g.FillEllipse(_brush, side2);
            g.DrawEllipse(_pen, side2);
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

    // ---------------- Arka plan öğeleri ----------------

    /// <summary>Ormandaki tek bir ağaç (oranlarla saklanır, her boyutta ölçeklenir).</summary>
    private readonly struct Tree
    {
        public Tree(float x, float height, bool isConifer, float shade)
        {
            X = x;
            Height = height;
            IsConifer = isConifer;
            Shade = shade;
        }

        public float X { get; }

        public float Height { get; }

        public bool IsConifer { get; }

        /// <summary>0-1 arası ton farkı: ağaçlar birbirinin kopyası görünmesin.</summary>
        public float Shade { get; }
    }

    /// <summary>Çimen üzerindeki tek bir çiçek.</summary>
    private readonly struct Flower
    {
        public Flower(float x, float y, Color color)
        {
            X = x;
            Y = y;
            Color = color;
        }

        public float X { get; }

        public float Y { get; }

        public Color Color { get; }
    }

    /// <summary>
    /// Yavaşça süzülen ve yanıp sönen ateş böceği. Ekranın üst bölümünde dolaşır;
    /// kenardan çıkınca karşı kenardan girer.
    /// </summary>
    private struct Firefly
    {
        private float _phase;
        private float _blinkSpeed;
        private float _speedX;
        private float _speedY;

        public float X { get; private set; }

        public float Y { get; private set; }

        /// <summary>0-1 arası parlaklık (yanıp sönme).</summary>
        public float Brightness { get; private set; }

        public static Firefly Create(Random random) => new()
        {
            X = (float)random.NextDouble(),
            Y = 0.10f + (float)random.NextDouble() * 0.58f,
            _phase = (float)(random.NextDouble() * Math.PI * 2.0),
            _blinkSpeed = 1.2f + (float)random.NextDouble() * 1.6f,
            _speedX = (float)(random.NextDouble() - 0.5) * 0.03f,
            _speedY = (float)(random.NextDouble() - 0.5) * 0.012f,
            Brightness = (float)random.NextDouble()
        };

        public void Update(float deltaSeconds)
        {
            _phase += deltaSeconds * _blinkSpeed;

            X += _speedX * deltaSeconds;
            Y += _speedY * deltaSeconds;

            // Ekrandan çıkan böcek karşı kenardan girer, dikeyde sınırda yansır.
            if (X < -0.02f)
            {
                X = 1.02f;
            }
            else if (X > 1.02f)
            {
                X = -0.02f;
            }

            if (Y < 0.08f || Y > 0.68f)
            {
                _speedY = -_speedY;
                Y = Math.Clamp(Y, 0.08f, 0.68f);
            }

            Brightness = 0.25f + 0.75f * (float)Math.Abs(Math.Sin(_phase));
        }
    }
}
