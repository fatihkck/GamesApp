using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.Audio;
using GamesApp.UI;

namespace GamesApp.Games.Fireworks;

/// <summary>Patlamanın deseni. Her fişek farklı görünsün diye torbayla seçilir.</summary>
internal enum FireworkStyle
{
    /// <summary>Klasik küre: kıvılcımlar her yöne saçılır.</summary>
    Sphere = 0,

    /// <summary>Halka: kıvılcımlar eşit hızla çember çizer.</summary>
    Ring = 1,

    /// <summary>Söğüt: altın kıvılcımlar uzun kuyruklarla aşağı süzülür.</summary>
    Willow = 2,

    /// <summary>Çift patlama: küreden yarım saniye sonra ikinci bir halka açılır.</summary>
    Double = 3,

    /// <summary>Kalp: kıvılcımlar pembe bir kalp çizerek açılır.</summary>
    Heart = 4,

    /// <summary>Yıldız: kıvılcımlar altın bir beş köşeli yıldız çizerek açılır.</summary>
    Star = 5
}

/// <summary>Bir fişeğin planı: fırlatan modül belirler, patlama anında geri bildirilir.</summary>
internal readonly struct FireworkPlan
{
    public FireworkPlan(FireworkStyle style, int boomVariant, AnimalKind? guest)
    {
        Style = style;
        BoomVariant = boomVariant;
        Guest = guest;
    }

    /// <summary>Patlama deseni.</summary>
    public FireworkStyle Style { get; }

    /// <summary>Patlama sesinin varyantı (modül patlama anında çalar).</summary>
    public int BoomVariant { get; }

    /// <summary>Patlamanın içinden çıkacak sürpriz misafir (çoğunlukla yok).</summary>
    public AnimalKind? Guest { get; }
}

/// <summary>
/// Havai Fişek gökyüzü: yıldızlı gece, ay ve pencereleri yanıp sönen şehir silüeti
/// üzerinde roketler yükselir, tepede rengarenk desenlerle patlar ve kıvılcımlar
/// yerçekimiyle süzülerek söner. Arada patlamanın içinden ışıl ışıl bir sürpriz
/// misafir (hayvan dostu) çıkar.
///
/// NEDEN KENDİ PARÇACIK SİSTEMİ: Ortak <see cref="UI.Effects.EffectEngine"/> genel
/// amaçlı kısa patlamalar üretir; havai fişeğin yerçekimli, sürüklenmeli, ŞEKİL
/// çizen (kalp/yıldız) ve göz kırpan kıvılcımları için özel bir sistem gerekir.
///
/// SAHNE ASLA BOŞ KALMAZ (tasarım kuralı 8): Gökyüzü kendi başına canlıdır (yıldızlar
/// parıldar, şehir pencereleri yanıp söner); gök 3,5 saniye fişeksiz kalırsa
/// <see cref="LaunchRequested"/> tetiklenir ve bir roket kendiliğinden fırlar.
///
/// Kare zamanı Timer aralığına güvenilmeden <see cref="Stopwatch"/> ile ölçülür.
/// </summary>
internal sealed class FireworksStageView : Control
{
    /// <summary>~60 FPS için kare aralığı (ms).</summary>
    private const int FrameIntervalMs = 16;

    /// <summary>Gök bu kadar saniye fişeksiz kalırsa kendiliğinden bir roket istenir.</summary>
    private const float IdleInviteSeconds = 3.5f;

    /// <summary>
    /// Aynı anda havada olabilecek en fazla roket. Sınır aşılırsa en yaşlı roket
    /// HEMEN patlatılır: hızlı basan çocuk daha çok patlama görür, gök tıkanmaz.
    /// </summary>
    public const int MaxRockets = 6;

    /// <summary>Kıvılcım üst sınırı (çizim maliyeti sabit kalsın; eskiler silinir).</summary>
    private const int MaxSparks = 900;

    /// <summary>Gökyüzü yıldızı sayısı.</summary>
    private const int StarCount = 40;

    private static readonly Color SkyBottom = Color.FromArgb(255, 26, 16, 52);
    private static readonly Color SkylineColor = Color.FromArgb(255, 10, 12, 26);
    private static readonly Color WindowColor = Color.FromArgb(255, 255, 214, 120);

    private readonly Random _random = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();

    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White, 2f);

    private readonly List<Rocket> _rockets = new(MaxRockets + 1);
    private readonly List<Spark> _sparks = new(MaxSparks);
    private readonly List<Guest> _guests = new(2);

    /// <summary>Patlama anının merkez flaşları ("PAT!" ışığı; hızla büyüyüp söner).</summary>
    private readonly List<Flash> _flashes = new(6);

    /// <summary>Çift patlamanın bekleyen ikinci halkaları (konum, renk, kalan süre).</summary>
    private readonly List<PendingBurst> _pendingBursts = new(4);

    private readonly SkyStar[] _stars = new SkyStar[StarCount];
    private readonly List<Building> _buildings = new(18);

    /// <summary>Altın açı ile dönen renk tekerleği (ardışık fişekler hep zıt renkte).</summary>
    private double _hue;

    private float _idleSeconds;
    private float _time;
    private long _lastTicks;
    private Size _layoutSize;
    private bool _disposedResources;

    public FireworksStageView()
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

        var random = new Random(20260731);
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = SkyStar.Create(random);
        }

        _hue = _random.NextDouble() * 360.0;

        _timer = new System.Windows.Forms.Timer { Interval = FrameIntervalMs };
        _timer.Tick += OnFrameTick;
    }

    /// <summary>Roket tepede patladı: modül patlama sesini (ve misafir sesini) çalar.</summary>
    public event Action<FireworkPlan>? Exploded;

    /// <summary>Gök uzun süre boş kaldı: bir roket istenir (sesi modül çalar).</summary>
    public event Action? LaunchRequested;

    /// <summary>Fareyle göğe tıklandı (ebeveyn için ikinci bir tetikleme yolu).</summary>
    public event Action? StageClicked;

    /// <summary>Bugüne kadar üretilen toplam kıvılcım (selftest raporu için).</summary>
    public int TotalSparksSpawned { get; private set; }

    /// <summary>Bugüne kadar fırlatılan toplam roket (selftest için).</summary>
    public int TotalLaunched { get; private set; }

    /// <summary>Havadaki roket sayısı (üst sınır denetimi için).</summary>
    public int ActiveRocketCount => _rockets.Count;

    /// <summary>Gökte şu anda bir şey var mı? (Roket, kıvılcım veya misafir.)</summary>
    public bool HasActivity => _rockets.Count > 0 || _sparks.Count > 0 || _guests.Count > 0;

    /// <summary>Animasyon döngüsünü başlatır.</summary>
    public void Start()
    {
        BuildLayout();

        _idleSeconds = 0f;
        _stopwatch.Restart();
        _lastTicks = _stopwatch.ElapsedTicks;
        _timer.Start();
    }

    /// <summary>Animasyon döngüsünü durdurur ve göğü temizler.</summary>
    public void Stop()
    {
        _timer.Stop();
        _stopwatch.Stop();

        _rockets.Clear();
        _sparks.Clear();
        _guests.Clear();
        _pendingBursts.Clear();
        _flashes.Clear();
        _idleSeconds = 0f;
    }

    /// <summary>
    /// Bir roket fırlatır. Havada zaten <see cref="MaxRockets"/> roket varsa en
    /// yaşlısı ANINDA patlatılır: her tuş basımı görünür bir sonuç üretir ve gök
    /// hiçbir zaman roket trafiğine boğulmaz.
    /// </summary>
    /// <param name="xRatio">Fırlatma noktasının yatay oranı (0-1; tuşa sabittir).</param>
    /// <param name="plan">Patlama deseni, ses varyantı ve olası sürpriz misafir.</param>
    public void Launch(float xRatio, FireworkPlan plan)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        while (_rockets.Count >= MaxRockets)
        {
            Rocket oldest = _rockets[0];
            _rockets.RemoveAt(0);
            Explode(oldest);
        }

        _rockets.Add(new Rocket(
            xRatio,
            0.16f + (float)_random.NextDouble() * 0.28f,
            0.70f + (float)_random.NextDouble() * 0.30f,
            plan,
            _random));

        TotalLaunched++;
        _idleSeconds = 0f;
        Invalidate();
    }

    /// <summary>
    /// Auto-repeat tepkisi: yeni roket fırlamaz, yerden kısa bir kıvılcım fıskiyesi
    /// yükselir (tasarım kuralı 7: tepki asla kesilmez).
    /// </summary>
    public void Cheer()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        float x = ClientSize.Width * (0.2f + 0.6f * (float)_random.NextDouble());
        float y = ClientSize.Height * 0.96f;
        Color color = Theme.ColorFromHsv(_hue, 0.7, 1.0);

        for (int i = 0; i < 8; i++)
        {
            double angle = -Math.PI / 2.0 + (_random.NextDouble() - 0.5) * 1.1;
            float speed = ClientSize.Height * (0.20f + (float)_random.NextDouble() * 0.28f);

            AddSpark(new Spark
            {
                X = x,
                Y = y,
                VelocityX = (float)Math.Cos(angle) * speed,
                VelocityY = (float)Math.Sin(angle) * speed,
                Life = 0.5f + (float)_random.NextDouble() * 0.3f,
                MaxLife = 0.8f,
                Color = color,
                Size = 2.5f + (float)_random.NextDouble() * 2f,
                Drag = 0.9f,
                GravityScale = 1f,
                TwinkleSeed = (float)(_random.NextDouble() * Math.PI * 2.0)
            });
        }

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
        if (deltaSeconds <= 0f || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        _time += deltaSeconds;

        float height = ClientSize.Height;
        float gravity = height * 0.24f;

        // Roketler: yükselir, iz bırakır, fitili bitince patlar.
        for (int i = _rockets.Count - 1; i >= 0; i--)
        {
            Rocket rocket = _rockets[i];
            rocket.Update(deltaSeconds);

            EmitTrail(rocket);

            if (rocket.HasReachedApex)
            {
                _rockets.RemoveAt(i);
                Explode(rocket);
            }
        }

        // Kıvılcımlar: yerçekimi + sürüklenme; ömrü bitenler silinir.
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            Spark spark = _sparks[i];

            spark.VelocityY += gravity * spark.GravityScale * deltaSeconds;

            float drag = (float)Math.Pow(spark.Drag, deltaSeconds * 60f);
            spark.VelocityX *= drag;
            spark.VelocityY *= drag;

            spark.X += spark.VelocityX * deltaSeconds;
            spark.Y += spark.VelocityY * deltaSeconds;
            spark.Life -= deltaSeconds;

            if (spark.Life <= 0f || spark.Y > height + 20f)
            {
                _sparks.RemoveAt(i);
            }
            else
            {
                _sparks[i] = spark;
            }
        }

        // Çift patlamanın bekleyen ikinci halkaları.
        for (int i = _pendingBursts.Count - 1; i >= 0; i--)
        {
            PendingBurst pending = _pendingBursts[i];
            pending.Delay -= deltaSeconds;

            if (pending.Delay <= 0f)
            {
                _pendingBursts.RemoveAt(i);
                SpawnRing(new PointF(pending.X, pending.Y), pending.Color, 0.62f);
            }
            else
            {
                _pendingBursts[i] = pending;
            }
        }

        // Merkez flaşları: hızla büyür ve söner.
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            Flash flash = _flashes[i];
            flash.Age += deltaSeconds;

            if (flash.Age >= Flash.LifeSeconds)
            {
                _flashes.RemoveAt(i);
            }
            else
            {
                _flashes[i] = flash;
            }
        }

        // Sürpriz misafirler: belirir, süzülür, kaybolur.
        for (int i = _guests.Count - 1; i >= 0; i--)
        {
            Guest guest = _guests[i];
            guest.Update(deltaSeconds);

            if (!guest.IsAlive)
            {
                _guests.RemoveAt(i);
            }
        }

        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i].Update(deltaSeconds);
        }

        // Gök boş kaldıysa bir süre sonra davet roketi istenir.
        if (!HasActivity)
        {
            _idleSeconds += deltaSeconds;
            if (_idleSeconds >= IdleInviteSeconds)
            {
                _idleSeconds = 0f;
                LaunchRequested?.Invoke();
            }
        }
        else
        {
            _idleSeconds = 0f;
        }
    }

    /// <summary>Roketin arkasına altın iz kıvılcımları bırakır.</summary>
    private void EmitTrail(Rocket rocket)
    {
        PointF pos = rocket.GetPosition(ClientSize.Width, ClientSize.Height);

        for (int i = 0; i < 2; i++)
        {
            AddSpark(new Spark
            {
                X = pos.X + ((float)_random.NextDouble() - 0.5f) * 4f,
                Y = pos.Y + (float)_random.NextDouble() * 6f,
                VelocityX = ((float)_random.NextDouble() - 0.5f) * 24f,
                VelocityY = 30f + (float)_random.NextDouble() * 40f,
                Life = 0.28f + (float)_random.NextDouble() * 0.22f,
                MaxLife = 0.5f,
                Color = Color.FromArgb(255, 255, 216, 130),
                Size = 2f + (float)_random.NextDouble() * 1.5f,
                Drag = 0.92f,
                GravityScale = 0.25f,
                TwinkleSeed = (float)(_random.NextDouble() * Math.PI * 2.0)
            });
        }
    }

    // ---------------- Patlama ----------------

    /// <summary>Roketi tepe noktasında patlatır: desen kıvılcımları + olası misafir + ses olayı.</summary>
    private void Explode(Rocket rocket)
    {
        PointF center = rocket.GetPosition(ClientSize.Width, ClientSize.Height);
        Color color = NextColor(rocket.Plan.Style);

        switch (rocket.Plan.Style)
        {
            case FireworkStyle.Ring:
                SpawnRing(center, color, 1f);
                break;

            case FireworkStyle.Willow:
                SpawnWillow(center);
                break;

            case FireworkStyle.Double:
                SpawnSphere(center, color, 0.8f);
                _pendingBursts.Add(new PendingBurst
                {
                    X = center.X,
                    Y = center.Y,
                    Color = Theme.ColorFromHsv((_hue + 180.0) % 360.0, 0.85, 1.0),
                    Delay = 0.28f
                });
                break;

            case FireworkStyle.Heart:
                SpawnShape(center, color, HeartPoints);
                break;

            case FireworkStyle.Star:
                SpawnShape(center, color, StarPoints);
                break;

            default:
                SpawnSphere(center, color, 1f);
                break;
        }

        // Merkez flaş: "PAT!" anının gözle görülür ışık patlaması (kural 5).
        _flashes.Add(new Flash { X = center.X, Y = center.Y, Color = color });

        // Sürpriz misafir: patlamanın ışığı içinden bir dost belirir.
        if (rocket.Plan.Guest is AnimalKind guest)
        {
            float side = Math.Min(ClientSize.Width, ClientSize.Height) * 0.30f;
            _guests.Add(new Guest(guest, center, side, _random));
        }

        Exploded?.Invoke(rocket.Plan);
        Invalidate();
    }

    /// <summary>Klasik küre patlaması: her yöne, değişken hızda kıvılcımlar.</summary>
    private void SpawnSphere(PointF center, Color color, float scale)
    {
        float baseSpeed = ClientSize.Height * 0.30f * scale;
        int count = (int)(110 * scale);

        for (int i = 0; i < count; i++)
        {
            double angle = _random.NextDouble() * Math.PI * 2.0;
            float speed = baseSpeed * (0.25f + (float)_random.NextDouble() * 0.75f);

            // İki tonlu küre: kıvılcımların üçte biri beyaza çalar (derinlik hissi).
            Color sparkColor = i % 3 == 0 ? Theme.Lerp(color, Color.White, 0.55f) : color;

            AddSpark(new Spark
            {
                X = center.X,
                Y = center.Y,
                VelocityX = (float)Math.Cos(angle) * speed,
                VelocityY = (float)Math.Sin(angle) * speed,
                Life = 1.2f + (float)_random.NextDouble() * 0.8f,
                MaxLife = 2.0f,
                Color = sparkColor,
                Size = 3.5f + (float)_random.NextDouble() * 3.0f,
                Drag = 0.945f,
                GravityScale = 0.55f,
                TwinkleSeed = (float)(_random.NextDouble() * Math.PI * 2.0)
            });
        }
    }

    /// <summary>Halka patlaması: eşit hızlı kıvılcımlar mükemmel bir çember açar.</summary>
    private void SpawnRing(PointF center, Color color, float scale)
    {
        float speed = ClientSize.Height * 0.26f * scale;
        const int count = 72;

        for (int i = 0; i < count; i++)
        {
            double angle = i / (double)count * Math.PI * 2.0;

            AddSpark(new Spark
            {
                X = center.X,
                Y = center.Y,
                VelocityX = (float)Math.Cos(angle) * speed,
                VelocityY = (float)Math.Sin(angle) * speed,
                Life = 1.1f + (float)_random.NextDouble() * 0.5f,
                MaxLife = 1.6f,
                Color = color,
                Size = 4f + (float)_random.NextDouble() * 2f,
                Drag = 0.94f,
                GravityScale = 0.45f,
                TwinkleSeed = (float)(_random.NextDouble() * Math.PI * 2.0)
            });
        }
    }

    /// <summary>Söğüt: altın kıvılcımlar yavaş açılır, uzun ömürle sarkarak süzülür.</summary>
    private void SpawnWillow(PointF center)
    {
        float baseSpeed = ClientSize.Height * 0.20f;
        const int count = 90;

        for (int i = 0; i < count; i++)
        {
            double angle = _random.NextDouble() * Math.PI * 2.0;
            float speed = baseSpeed * (0.4f + (float)_random.NextDouble() * 0.6f);

            AddSpark(new Spark
            {
                X = center.X,
                Y = center.Y,
                VelocityX = (float)Math.Cos(angle) * speed,
                VelocityY = (float)Math.Sin(angle) * speed * 0.7f,
                Life = 1.6f + (float)_random.NextDouble() * 0.9f,
                MaxLife = 2.5f,
                Color = Color.FromArgb(255, 255, 208, 110),
                Size = 3f + (float)_random.NextDouble() * 2f,
                Drag = 0.90f,
                GravityScale = 0.85f,
                TwinkleSeed = (float)(_random.NextDouble() * Math.PI * 2.0)
            });
        }
    }

    /// <summary>
    /// Şekil patlaması: kıvılcımlar verilen ana hattın üzerine dizilir ve merkezden
    /// dışa doğru AYNI ORANDA hızlanır; şekil büyüyerek açılır ama bozulmaz.
    /// </summary>
    private void SpawnShape(PointF center, Color color, PointF[] outline)
    {
        float scale = ClientSize.Height * 0.55f;

        for (int i = 0; i < outline.Length; i++)
        {
            // Hafif titreşim: şekil "elle çizilmiş" gibi kalır, lazer kesimi gibi durmaz.
            float jitterX = ((float)_random.NextDouble() - 0.5f) * 0.02f;
            float jitterY = ((float)_random.NextDouble() - 0.5f) * 0.02f;

            AddSpark(new Spark
            {
                X = center.X,
                Y = center.Y,
                VelocityX = (outline[i].X + jitterX) * scale,
                VelocityY = (outline[i].Y + jitterY) * scale,
                Life = 1.3f + (float)_random.NextDouble() * 0.5f,
                MaxLife = 1.8f,
                Color = i % 4 == 0 ? Theme.Lerp(color, Color.White, 0.5f) : color,
                Size = 4f + (float)_random.NextDouble() * 2f,
                Drag = 0.93f,
                GravityScale = 0.30f,
                TwinkleSeed = (float)(_random.NextDouble() * Math.PI * 2.0)
            });
        }
    }

    /// <summary>Kıvılcımı ekler; üst sınır aşılırsa en eski kıvılcım silinir.</summary>
    private void AddSpark(Spark spark)
    {
        if (_sparks.Count >= MaxSparks)
        {
            _sparks.RemoveAt(0);
        }

        _sparks.Add(spark);
        TotalSparksSpawned++;
    }

    /// <summary>
    /// Sıradaki fişek rengi. Kalp her zaman pembe-kırmızı, yıldız her zaman altın
    /// tonlarındadır (çocuk şekli rengiyle birlikte öğrenir); diğer desenler renk
    /// tekerleğinden altın açıyla döner.
    /// </summary>
    private Color NextColor(FireworkStyle style)
    {
        if (style == FireworkStyle.Heart)
        {
            return Theme.ColorFromHsv(335.0 + _random.NextDouble() * 20.0, 0.80, 1.0);
        }

        if (style == FireworkStyle.Star)
        {
            return Theme.ColorFromHsv(45.0 + _random.NextDouble() * 12.0, 0.85, 1.0);
        }

        _hue = (_hue + 137.5) % 360.0;
        return Theme.ColorFromHsv(_hue, 0.88, 1.0);
    }

    // ---------------- Şekil ana hatları ----------------

    /// <summary>Kalp ana hattı (birim ölçek, merkez 0,0; ekranda Y aşağı büyür).</summary>
    private static readonly PointF[] HeartPoints = BuildHeart();

    /// <summary>Beş köşeli yıldız ana hattı (birim ölçek, merkez 0,0).</summary>
    private static readonly PointF[] StarPoints = BuildStar();

    private static PointF[] BuildHeart()
    {
        const int count = 64;
        var points = new PointF[count];

        for (int i = 0; i < count; i++)
        {
            double t = i / (double)count * Math.PI * 2.0;

            // Klasik parametrik kalp; 32'ye bölerek birim ölçeğe indirilir.
            double x = 16.0 * Math.Pow(Math.Sin(t), 3.0);
            double y = 13.0 * Math.Cos(t) - 5.0 * Math.Cos(2.0 * t)
                       - 2.0 * Math.Cos(3.0 * t) - Math.Cos(4.0 * t);

            // Ekranda Y aşağı büyüdüğü için kalp dik dursun diye y ters çevrilir.
            points[i] = new PointF((float)(x / 32.0), (float)(-y / 32.0));
        }

        return points;
    }

    private static PointF[] BuildStar()
    {
        // 5 köşeli yıldızın 10 köşesi arasındaki kenarlar eşit aralıkla örneklenir.
        const int perEdge = 6;
        var corners = new PointF[10];

        for (int i = 0; i < 10; i++)
        {
            float r = i % 2 == 0 ? 0.5f : 0.21f;
            double angle = -Math.PI / 2.0 + i * Math.PI / 5.0;
            corners[i] = new PointF(
                (float)Math.Cos(angle) * r,
                (float)Math.Sin(angle) * r);
        }

        var points = new PointF[10 * perEdge];
        for (int i = 0; i < 10; i++)
        {
            PointF a = corners[i];
            PointF b = corners[(i + 1) % 10];

            for (int j = 0; j < perEdge; j++)
            {
                float t = j / (float)perEdge;
                points[i * perEdge + j] = new PointF(
                    a.X + (b.X - a.X) * t,
                    a.Y + (b.Y - a.Y) * t);
            }
        }

        return points;
    }

    // ---------------- Yerleşim ----------------

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        BuildLayout();
    }

    /// <summary>
    /// Şehir silüetini üretir. Tohum SABİTTİR: aynı çözünürlükte şehir her açılışta
    /// aynı görünür (çocuk için tanıdık bir yer olur).
    /// </summary>
    private void BuildLayout()
    {
        if (ClientSize == _layoutSize || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        _layoutSize = ClientSize;
        _buildings.Clear();

        var random = new Random(20260731);

        const int buildingCount = 14;
        float x = 0f;
        for (int i = 0; i < buildingCount && x < 1f; i++)
        {
            float width = 0.05f + (float)random.NextDouble() * 0.06f;
            float height = 0.06f + (float)random.NextDouble() * 0.11f;

            _buildings.Add(new Building(x, width, height, random.Next(1000)));
            x += width + 0.002f;
        }
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
            PaintGuard.Report(nameof(FireworksStageView), ex);
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

        DrawSky(g, bounds);
        DrawStars(g, bounds);
        DrawMoon(g, bounds);
        DrawSkyline(g, bounds);
        DrawFlashes(g, bounds);
        DrawSparks(g);
        DrawRockets(g, bounds);

        // Misafirler en üstte: sürpriz hiçbir kıvılcımın altında kalmaz.
        for (int i = 0; i < _guests.Count; i++)
        {
            _guests[i].Draw(g, _brush);
        }
    }

    private void DrawSky(Graphics g, Rectangle bounds)
    {
        using var sky = new LinearGradientBrush(
            bounds,
            Theme.BackgroundDeep,
            SkyBottom,
            LinearGradientMode.Vertical);

        g.FillRectangle(sky, bounds);
    }

    private void DrawStars(Graphics g, Rectangle bounds)
    {
        for (int i = 0; i < _stars.Length; i++)
        {
            SkyStar star = _stars[i];

            float size = Math.Max(1.5f, bounds.Height * 0.005f) * (0.6f + star.Brightness);
            float x = bounds.Width * star.X;
            float y = bounds.Height * star.Y;

            _brush.Color = Color.FromArgb(
                (int)Math.Clamp(star.Brightness * 190f, 0f, 255f),
                255,
                246,
                200);

            g.FillEllipse(_brush, x - size * 0.5f, y - size * 0.5f, size, size);
        }
    }

    /// <summary>Sol üstte ince hilal: gökyüzü boş bir yüzey gibi durmasın.</summary>
    private void DrawMoon(Graphics g, Rectangle bounds)
    {
        float radius = Math.Max(10f, bounds.Height * 0.05f);
        float cx = bounds.Width * 0.12f;
        float cy = bounds.Height * 0.14f;

        _brush.Color = Color.FromArgb(255, 255, 244, 198);
        g.FillEllipse(_brush, cx - radius, cy - radius, radius * 2f, radius * 2f);

        // Hilal: ayın üstüne gök renginde ikinci bir daire bindirilir.
        _brush.Color = Theme.Lerp(Theme.BackgroundDeep, SkyBottom, 0.3f);
        g.FillEllipse(
            _brush,
            cx - radius * 0.55f,
            cy - radius * 1.05f,
            radius * 2f,
            radius * 2f);
    }

    /// <summary>Şehir silüeti: koyu bloklar ve yanıp sönen sıcak pencereler.</summary>
    private void DrawSkyline(Graphics g, Rectangle bounds)
    {
        float baseY = bounds.Height;

        for (int i = 0; i < _buildings.Count; i++)
        {
            Building building = _buildings[i];

            float bx = bounds.Width * building.X;
            float bw = bounds.Width * building.Width;
            float bh = bounds.Height * building.Height;

            if (bw < 4f || bh < 4f)
            {
                continue;
            }

            _brush.Color = SkylineColor;
            g.FillRectangle(_brush, bx, baseY - bh, bw, bh);

            // Pencereler: 3 sütunlu ızgara; her pencere kendi ritminde yanıp söner.
            float windowW = bw / 5f;
            float windowH = bh / 7f;

            if (windowW < 2.5f || windowH < 2.5f)
            {
                continue;
            }

            for (int col = 0; col < 3; col++)
            {
                for (int row = 0; row < 3; row++)
                {
                    int cell = building.Seed + col * 7 + row * 13;

                    // Kimi pencere hep karanlık, kimi yavaşça yanıp söner.
                    if (cell % 3 == 0)
                    {
                        continue;
                    }

                    float glow = 0.55f + 0.45f * (float)Math.Sin(_time * 0.8 + cell);
                    _brush.Color = Color.FromArgb(
                        (int)Math.Clamp(glow * 210f, 0f, 255f),
                        WindowColor);

                    g.FillRectangle(
                        _brush,
                        bx + bw * (0.16f + col * 0.28f),
                        baseY - bh + bh * (0.14f + row * 0.30f),
                        windowW,
                        windowH);
                }
            }
        }
    }

    /// <summary>
    /// Merkez flaşları: patlama anında hızla büyüyüp sönen iç içe iki ışık dairesi.
    /// Patlamayı uzaktan bile "PAT!" diye hissettiren katmandır.
    /// </summary>
    private void DrawFlashes(Graphics g, Rectangle bounds)
    {
        for (int i = 0; i < _flashes.Count; i++)
        {
            Flash flash = _flashes[i];

            float progress = Math.Clamp(flash.Age / Flash.LifeSeconds, 0f, 1f);

            // Hızla büyür (easeOut), doğrusal söner.
            float eased = 1f - (1f - progress) * (1f - progress);
            float radius = bounds.Height * 0.11f * eased;
            int alpha = (int)Math.Clamp(210f * (1f - progress), 0f, 255f);

            if (radius < 4f || alpha <= 6)
            {
                continue;
            }

            // Dış halka: fişeğin rengi; iç çekirdek: göz alan sıcak beyaz.
            _brush.Color = Color.FromArgb(alpha / 2, flash.Color);
            g.FillEllipse(_brush, flash.X - radius, flash.Y - radius, radius * 2f, radius * 2f);

            float core = radius * 0.55f;
            _brush.Color = Color.FromArgb(alpha, 255, 250, 225);
            g.FillEllipse(_brush, flash.X - core, flash.Y - core, core * 2f, core * 2f);
        }
    }

    /// <summary>
    /// Kıvılcımlar: hız yönünde kısa çizgiler (kuyruk hissi). Ömrünün sonuna
    /// yaklaşan kıvılcım göz kırpar (sönen kor taklidi).
    /// </summary>
    private void DrawSparks(Graphics g)
    {
        for (int i = 0; i < _sparks.Count; i++)
        {
            Spark spark = _sparks[i];

            float lifeRatio = Math.Clamp(spark.Life / spark.MaxLife, 0f, 1f);

            // Göz kırpma: son üçte birlik ömürde parlaklık titrer.
            float twinkle = lifeRatio < 0.35f
                ? 0.5f + 0.5f * (float)Math.Sin(_time * 24.0 + spark.TwinkleSeed)
                : 1f;

            // Üstel olmayan sönüş (kök eğrisi): kıvılcım ömrünün büyük kısmında
            // parlak kalır, yalnızca sonunda hızla söner (canlı renkler, kural 3).
            int alpha = (int)Math.Clamp(255f * (float)Math.Pow(lifeRatio, 0.55) * twinkle, 0f, 255f);
            if (alpha <= 6)
            {
                continue;
            }

            float tailX = spark.X - spark.VelocityX * 0.03f;
            float tailY = spark.Y - spark.VelocityY * 0.03f;

            _pen.Color = Color.FromArgb(alpha, spark.Color);
            _pen.Width = Math.Max(1.5f, spark.Size);

            // Neredeyse duran kıvılcım nokta olarak çizilir (sıfır uzunluklu çizgi
            // GDI+ için anlamsızdır).
            float dx = spark.X - tailX;
            float dy = spark.Y - tailY;
            if (dx * dx + dy * dy < 1f)
            {
                _brush.Color = _pen.Color;
                g.FillEllipse(
                    _brush,
                    spark.X - spark.Size * 0.5f,
                    spark.Y - spark.Size * 0.5f,
                    spark.Size,
                    spark.Size);
            }
            else
            {
                g.DrawLine(_pen, tailX, tailY, spark.X, spark.Y);
            }
        }
    }

    /// <summary>Roketler: parlak baş, titreyen alev ve yumuşak ışıma.</summary>
    private void DrawRockets(Graphics g, Rectangle bounds)
    {
        for (int i = 0; i < _rockets.Count; i++)
        {
            PointF pos = _rockets[i].GetPosition(bounds.Width, bounds.Height);

            float size = Math.Max(3f, bounds.Height * 0.008f);

            // Işıma
            float glow = size * 3f;
            _brush.Color = Color.FromArgb(70, 255, 236, 170);
            g.FillEllipse(_brush, pos.X - glow * 0.5f, pos.Y - glow * 0.5f, glow, glow);

            // Baş
            _brush.Color = Color.FromArgb(255, 255, 250, 220);
            g.FillEllipse(_brush, pos.X - size * 0.5f, pos.Y - size * 0.5f, size, size);

            // Alev: hızla titreyen küçük turuncu damla.
            float flicker = 0.7f + 0.3f * (float)Math.Sin(_time * 40.0 + i * 2.0);
            _brush.Color = Color.FromArgb(230, 255, 150, 60);
            g.FillEllipse(
                _brush,
                pos.X - size * 0.35f,
                pos.Y + size * 0.6f,
                size * 0.7f,
                size * 1.4f * flicker);
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

            _brush.Dispose();
            _pen.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---------------- Sahne öğeleri ----------------

    /// <summary>Yükselen tek bir roket: hafif salınımla tırmanır, fitili bitince patlar.</summary>
    private sealed class Rocket
    {
        private readonly float _xRatio;
        private readonly float _targetYRatio;
        private readonly float _fuseSeconds;
        private readonly float _swayPhase;
        private readonly float _swayAmount;

        private float _age;

        public Rocket(float xRatio, float targetYRatio, float fuseSeconds, FireworkPlan plan, Random random)
        {
            _xRatio = xRatio;
            _targetYRatio = targetYRatio;
            _fuseSeconds = fuseSeconds;
            Plan = plan;

            _swayPhase = (float)(random.NextDouble() * Math.PI * 2.0);
            _swayAmount = 0.006f + (float)random.NextDouble() * 0.012f;
        }

        public FireworkPlan Plan { get; }

        public bool HasReachedApex => _age >= _fuseSeconds;

        public void Update(float deltaSeconds)
        {
            _age += deltaSeconds;
        }

        /// <summary>Roketin o anki konumu: alttan tepeye yavaşlayarak tırmanır.</summary>
        public PointF GetPosition(float width, float height)
        {
            float p = Math.Clamp(_age / _fuseSeconds, 0f, 1f);

            // Yumuşak yavaşlama: roket tepeye "süzülerek" varır (gerçek fişek gibi).
            float eased = 1f - (1f - p) * (1f - p);

            float sway = (float)Math.Sin(p * Math.PI * 3.0 + _swayPhase) * _swayAmount * (1f - p);
            float x = width * (_xRatio + sway);
            float y = height * (1.02f + (_targetYRatio - 1.02f) * eased);

            return new PointF(x, y);
        }
    }

    /// <summary>Tek bir kıvılcım (değer tipi: liste içinde yerinde güncellenir).</summary>
    private struct Spark
    {
        public float X;
        public float Y;
        public float VelocityX;
        public float VelocityY;

        /// <summary>Kalan ömür (saniye).</summary>
        public float Life;

        /// <summary>Başlangıç ömrü (parlaklık oranı bunun üzerinden hesaplanır).</summary>
        public float MaxLife;

        public Color Color;
        public float Size;

        /// <summary>Kare başına hız koruma çarpanı (60 FPS referanslı).</summary>
        public float Drag;

        /// <summary>Yerçekiminden etkilenme oranı (söğüt sarkar, halka süzülür).</summary>
        public float GravityScale;

        /// <summary>Göz kırpma fazı (kıvılcımlar senkron yanıp sönmesin).</summary>
        public float TwinkleSeed;
    }

    /// <summary>Çift patlamanın bekleyen ikinci halkası.</summary>
    private struct PendingBurst
    {
        public float X;
        public float Y;
        public Color Color;
        public float Delay;
    }

    /// <summary>Patlama anının merkez flaşı.</summary>
    private struct Flash
    {
        /// <summary>Flaşın toplam ömrü (saniye).</summary>
        public const float LifeSeconds = 0.35f;

        public float X;
        public float Y;
        public Color Color;
        public float Age;
    }

    /// <summary>
    /// Patlamanın içinden çıkan sürpriz misafir: ışıl ışıl belirir, yavaşça süzülür
    /// ve kaybolur. Çizim <see cref="AnimalArtist"/> ile yapılır (diğer oyunlarla ortak).
    /// </summary>
    private sealed class Guest
    {
        private const float LifeSeconds = 2.2f;

        private readonly AnimalKind _kind;
        private readonly float _side;
        private readonly float _driftX;

        private PointF _center;
        private float _age;

        public Guest(AnimalKind kind, PointF center, float side, Random random)
        {
            _kind = kind;
            _center = center;
            _side = side;
            _driftX = ((float)random.NextDouble() - 0.5f) * side * 0.2f;
        }

        public bool IsAlive => _age < LifeSeconds;

        public void Update(float deltaSeconds)
        {
            _age += deltaSeconds;

            // Balon gibi süzülür: hafif yana, yavaşça aşağı.
            _center = new PointF(
                _center.X + _driftX * deltaSeconds,
                _center.Y + _side * 0.10f * deltaSeconds);
        }

        public void Draw(Graphics g, SolidBrush brush)
        {
            if (!IsAlive)
            {
                return;
            }

            // Beliriş: hedefi hafifçe aşıp geri oturur (fırlama hissi).
            float appear = Math.Clamp(_age / 0.30f, 0f, 1f);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float scale = 1f + c3 * (appear - 1f) * (appear - 1f) * (appear - 1f)
                          + c1 * (appear - 1f) * (appear - 1f);

            // Kayboluş: son yarım saniyede saydamlaşır.
            float alpha = Math.Clamp((LifeSeconds - _age) / 0.5f, 0f, 1f);

            float drawSide = _side * Math.Max(0.05f, scale);
            if (drawSide < 12f || alpha <= 0.02f)
            {
                // GDI+ TUZAĞI: sıfıra yakın boyutlu şekiller sahte OutOfMemoryException
                // fırlatır; çok küçük misafir hiç çizilmez (kural 10).
                return;
            }

            // Işık halesi: misafir patlamanın ışığı içinden çıkıyormuş gibi görünür.
            float glow = drawSide * 1.4f;
            brush.Color = Color.FromArgb((int)(60 * alpha), 255, 240, 180);
            g.FillEllipse(
                brush,
                _center.X - glow * 0.5f,
                _center.Y - glow * 0.5f,
                glow,
                glow);

            var box = new RectangleF(
                _center.X - drawSide * 0.5f,
                _center.Y - drawSide * 0.5f,
                drawSide,
                drawSide);

            AnimalArtist.Draw(g, _kind, box, alpha);
        }
    }

    /// <summary>Yavaşça yanıp sönen gökyüzü yıldızı.</summary>
    private struct SkyStar
    {
        private float _phase;
        private float _speed;

        public float X { get; private set; }

        public float Y { get; private set; }

        /// <summary>0-1 arası parlaklık.</summary>
        public float Brightness { get; private set; }

        public static SkyStar Create(Random random) => new()
        {
            X = (float)random.NextDouble(),
            Y = (float)random.NextDouble() * 0.75f,
            _phase = (float)(random.NextDouble() * Math.PI * 2.0),
            _speed = 0.7f + (float)random.NextDouble() * 1.6f,
            Brightness = (float)random.NextDouble()
        };

        public void Update(float deltaSeconds)
        {
            _phase += deltaSeconds * _speed;
            Brightness = 0.2f + 0.8f * (float)Math.Abs(Math.Sin(_phase));
        }
    }

    /// <summary>Silüetteki tek bir bina (oranlarla saklanır, her boyutta ölçeklenir).</summary>
    private readonly struct Building
    {
        public Building(float x, float width, float height, int seed)
        {
            X = x;
            Width = width;
            Height = height;
            Seed = seed;
        }

        public float X { get; }

        public float Width { get; }

        public float Height { get; }

        /// <summary>Pencere deseninin tohumu (binalar birbirinin kopyası görünmesin).</summary>
        public int Seed { get; }
    }
}
