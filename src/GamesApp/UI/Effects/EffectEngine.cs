using System.Drawing;
using System.Drawing.Drawing2D;

namespace GamesApp.UI.Effects;

/// <summary>
/// Halka ve parçacık efektlerini yönetir. 60 FPS ve aynı anda 400+ nesne hedeflenir.
/// Bellek baskısını azaltmak için:
///  - Efektler struct, listeler önceden kapasiteli,
///  - Ölü nesneler ters döngüyle yerinde (in-place) silinir,
///  - Pen/Brush örnekleri yeniden kullanılır (her karede yenisi üretilmez).
/// Bu sınıf yalnızca UI thread'inden kullanılır; kilit yoktur.
/// </summary>
internal sealed class EffectEngine : IDisposable
{
    private const int MaxRipples = 160;
    private const int MaxParticles = 900;

    private readonly List<Ripple> _ripples = new(MaxRipples);
    private readonly List<Particle> _particles = new(MaxParticles);
    private readonly Random _random = new();

    // Yeniden kullanılan çizim nesneleri.
    private readonly Pen _ringPen = new(Color.White, 6f);
    private readonly SolidBrush _fillBrush = new(Color.White);

    // Yıldız çizimi için tekrar kullanılan tampon (10 köşe: 5 dış + 5 iç).
    private readonly PointF[] _starBuffer = new PointF[10];

    private bool _disposed;

    /// <summary>Uygulama başından beri üretilen efekt nesnesi sayısı (selftest için).</summary>
    public int TotalSpawned { get; private set; }

    /// <summary>Şu anda yaşayan nesne sayısı.</summary>
    public int ActiveCount => _ripples.Count + _particles.Count;

    /// <summary>En son çalınan notanın rengi (arka plan geçişi için).</summary>
    public Color LastNoteColor { get; private set; } = Theme.Background;

    /// <summary>
    /// Bir nota için efekt üretir.
    /// </summary>
    /// <param name="midiNote">Çalan MIDI notası (renk ve boyut buna göre belirlenir).</param>
    /// <param name="origin">Efektin doğduğu nokta.</param>
    /// <param name="intensity">0.3 - 1.0 arası şiddet (auto-repeat için düşük değer verilir).</param>
    public void Spawn(int midiNote, PointF origin, float intensity = 1f)
    {
        // Kalın notalar daha büyük halka üretir.
        float sizeFactor = 1f - Math.Clamp((midiNote - 48) / 48f, 0f, 1f);
        SpawnBurst(Theme.GetNoteColor(midiNote), origin, intensity, sizeFactor);
    }

    /// <summary>
    /// Verilen RENKTE patlama üretir (balon patlaması gibi nota kavramı olmayan
    /// olaylar için). Nota tabanlı <see cref="Spawn"/> de bu yolu kullanır.
    /// </summary>
    /// <param name="color">Halka ve parçacık rengi.</param>
    /// <param name="origin">Patlamanın merkezi.</param>
    /// <param name="intensity">0.2 - 1.5 arası şiddet.</param>
    /// <param name="sizeFactor">0 = küçük/dar halka, 1 = büyük/kalın halka.</param>
    /// <param name="extraParticles">Ek parçacık sayısı (konfeti için cömert kullanılır).</param>
    public void SpawnBurst(
        Color color,
        PointF origin,
        float intensity = 1f,
        float sizeFactor = 0.5f,
        int extraParticles = 0)
    {
        intensity = Math.Clamp(intensity, 0.2f, 1.5f);
        sizeFactor = Math.Clamp(sizeFactor, 0f, 1f);
        LastNoteColor = color;

        float maxRadius = (110f + sizeFactor * 190f) * intensity;
        float thickness = (5f + sizeFactor * 7f) * intensity;

        if (_ripples.Count < MaxRipples)
        {
            _ripples.Add(new Ripple(origin, maxRadius, color, thickness));
            TotalSpawned++;
        }

        int particleCount = (int)(10 * intensity) + 4 + Math.Max(0, extraParticles);
        for (int i = 0; i < particleCount; i++)
        {
            if (_particles.Count >= MaxParticles)
            {
                break;
            }

            double angle = _random.NextDouble() * Math.PI * 2.0;
            float speed = 90f + (float)_random.NextDouble() * 320f * intensity;

            var velocity = new PointF(
                (float)Math.Cos(angle) * speed * 0.7f,
                (float)Math.Sin(angle) * speed - 160f * intensity);

            float life = 0.6f + (float)_random.NextDouble() * 0.8f;
            float size = (5f + (float)_random.NextDouble() * 12f) * intensity;
            bool isStar = _random.Next(0, 3) == 0;
            float rotation = (float)(_random.NextDouble() * Math.PI * 2.0);
            float rotationSpeed = (float)((_random.NextDouble() - 0.5) * 8.0);

            _particles.Add(new Particle(origin, velocity, life, size, color, isStar, rotation, rotationSpeed));
            TotalSpawned++;
        }
    }

    /// <summary>Tüm efektleri ilerletir ve ölenleri siler.</summary>
    public void Update(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return;
        }

        // Ters döngü: silme işlemi kalan indeksleri bozmaz, yeni liste üretilmez.
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            Ripple ripple = _ripples[i];
            ripple.Update(deltaSeconds);

            if (ripple.IsAlive)
            {
                _ripples[i] = ripple;
            }
            else
            {
                _ripples.RemoveAt(i);
            }
        }

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            Particle particle = _particles[i];
            particle.Update(deltaSeconds);

            if (particle.IsAlive)
            {
                _particles[i] = particle;
            }
            else
            {
                _particles.RemoveAt(i);
            }
        }
    }

    /// <summary>Tüm efektleri çizer.</summary>
    public void Draw(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        for (int i = 0; i < _ripples.Count; i++)
        {
            Ripple ripple = _ripples[i];
            float p = ripple.Progress;
            float alpha = 1f - p;

            _ringPen.Color = Theme.WithAlpha(ripple.Color, alpha * 0.95f);
            _ringPen.Width = Math.Max(1.2f, ripple.StartThickness * (1f - p * 0.65f));

            float d = ripple.Radius * 2f;
            g.DrawEllipse(
                _ringPen,
                ripple.Center.X - ripple.Radius,
                ripple.Center.Y - ripple.Radius,
                d,
                d);

            // İçte daha soluk ikinci bir halka: derinlik hissi verir.
            float innerRadius = ripple.Radius * 0.55f;
            _ringPen.Color = Theme.WithAlpha(ripple.Color, alpha * 0.35f);
            _ringPen.Width = Math.Max(1f, _ringPen.Width * 0.5f);
            g.DrawEllipse(
                _ringPen,
                ripple.Center.X - innerRadius,
                ripple.Center.Y - innerRadius,
                innerRadius * 2f,
                innerRadius * 2f);
        }

        for (int i = 0; i < _particles.Count; i++)
        {
            Particle particle = _particles[i];
            float alpha = 1f - particle.Progress;
            _fillBrush.Color = Theme.WithAlpha(particle.Color, alpha);

            if (particle.IsStar)
            {
                BuildStar(particle.Position, particle.Size, particle.Rotation);
                g.FillPolygon(_fillBrush, _starBuffer);
            }
            else
            {
                float half = particle.Size * 0.5f;
                g.FillEllipse(
                    _fillBrush,
                    particle.Position.X - half,
                    particle.Position.Y - half,
                    particle.Size,
                    particle.Size);
            }
        }
    }

    /// <summary>Tüm efektleri temizler.</summary>
    public void Clear()
    {
        _ripples.Clear();
        _particles.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ringPen.Dispose();
        _fillBrush.Dispose();
        Clear();
    }

    /// <summary>Beş köşeli yıldızı tampona yazar (Matrix kullanmadan, allocation'sız).</summary>
    private void BuildStar(PointF center, float size, float rotation)
    {
        float outer = size * 0.85f;
        float inner = outer * 0.42f;

        for (int i = 0; i < 10; i++)
        {
            float radius = (i % 2 == 0) ? outer : inner;
            double angle = rotation + i * Math.PI / 5.0 - Math.PI / 2.0;
            _starBuffer[i] = new PointF(
                center.X + (float)Math.Cos(angle) * radius,
                center.Y + (float)Math.Sin(angle) * radius);
        }
    }
}
