using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.Audio;

namespace GamesApp.UI.Effects;

/// <summary>
/// Sahneye çıkan hayvanın animasyonu:
/// 1) Ekranın altından zıplayarak girer (ease-out),
/// 2) ~1,8 saniye durur (hafif nefes alma/salınım),
/// 3) büyüyüp saydamlaşarak kaybolur.
///
/// Yanında yuvarlak köşeli konuşma balonu ve içinde büyük punto Türkçe ses metni
/// gösterilir. Balon, ekran kenarına taşmayacak şekilde hayvanın sağına ya da
/// soluna yerleştirilir.
///
/// Hayvan gösterilirken piyano çalışmaya devam eder; bu yalnızca ayrı bir çizim
/// katmanıdır ve girdiyi hiç etkilemez.
/// </summary>
internal sealed class AnimalCue
{
    private const float EnterSeconds = 0.45f;
    private const float ExitSeconds = 0.55f;

    /// <summary>Sentezlenen sesler için sabit sahne süresi (saniye). Sentez sesleri kısadır.</summary>
    public const float DefaultSceneSeconds = 2.8f;

    /// <summary>En kısa sahne süresi (saniye).</summary>
    public const float MinSceneSeconds = 1.8f;

    /// <summary>En uzun sahne süresi (saniye).</summary>
    public const float MaxSceneSeconds = 4.0f;

    private readonly float _sceneSeconds;
    private readonly float _holdSeconds;

    private float _age;
    private bool _exitNotified;

    /// <param name="kind">Sahneye çıkan hayvan.</param>
    /// <param name="soundDurationMs">
    /// Çalınan sesin ölçülmüş süresi (ms). 0 ise sentezlenen ses çalınıyordur ve
    /// sabit <see cref="DefaultSceneSeconds"/> süresi kullanılır.
    /// </param>
    public AnimalCue(AnimalKind kind, int soundDurationMs = 0)
    {
        Kind = kind;
        Text = AnimalInfo.GetSoundText(kind);

        _sceneSeconds = soundDurationMs > 0
            ? ComputeSceneSeconds(soundDurationMs / 1000f)
            : DefaultSceneSeconds;

        // Giriş ve çıkış süreleri sabittir; kalan süre bekleme (hold) olur.
        _holdSeconds = Math.Max(0.25f, _sceneSeconds - EnterSeconds - ExitSeconds);
    }

    /// <summary>
    /// SAF FONKSİYON: Sahne süresi = <c>clamp(ses süresi + 0,4 sn; 1,8 sn; 4,0 sn)</c>.
    /// Kısa seslerde hayvan en az 1,8 sn görünür kalır, uzun seslerde 4 sn'yi geçmez.
    /// </summary>
    public static float ComputeSceneSeconds(float soundSeconds) =>
        Math.Clamp(soundSeconds + 0.4f, MinSceneSeconds, MaxSceneSeconds);

    public AnimalKind Kind { get; }

    /// <summary>Konuşma balonundaki Türkçe ses metni.</summary>
    public string Text { get; }

    /// <summary>Bu hayvanın toplam sahne süresi (saniye).</summary>
    public float TotalSeconds => _sceneSeconds;

    public bool IsAlive => _age < _sceneSeconds;

    /// <summary>Hayvan kaybolma aşamasına geçti mi? (Ses bu anda kesilir.)</summary>
    public bool IsExiting => _age >= EnterSeconds + _holdSeconds;

    /// <summary>
    /// Hayvan çıkış aşamasına geçtiğinde BİR KEZ tetiklenir. Çalan hayvan sesi bu anda
    /// durdurulur; böylece ses hayvanla birlikte biter ve sonraki hayvana taşmaz.
    /// </summary>
    public event Action? ExitStarted;

    public void Update(float deltaSeconds)
    {
        if (deltaSeconds > 0f)
        {
            _age += deltaSeconds;
        }

        if (!_exitNotified && IsExiting)
        {
            _exitNotified = true;
            ExitStarted?.Invoke();
        }
    }

    /// <summary>Hayvanı ve konuşma balonunu çizer.</summary>
    public void Draw(Graphics g, RectangleF area, Font speechFont)
    {
        if (!IsAlive || area.Width <= 40f || area.Height <= 40f)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Hayvan kutusunun temel boyutu
        float side = Math.Max(140f, Math.Min(area.Width * 0.26f, area.Height * 0.52f));

        // Hedef konum: sol-orta bölge (sağ üstteki ÇIKIŞ butonunun altında kalmaz,
        // buton her zaman erişilebilir olsun diye sağ üst köşeden uzak durulur).
        float targetCenterX = area.X + area.Width * 0.34f;
        float targetCenterY = area.Y + area.Height * 0.62f;
        float startCenterY = area.Bottom + side;

        float alpha = 1f;
        float scale = 1f;
        float centerY;

        if (_age < EnterSeconds)
        {
            // Giriş: hızlı başlayıp yavaşlayan zıplama
            float p = _age / EnterSeconds;
            float eased = 1f - (1f - p) * (1f - p) * (1f - p);
            centerY = startCenterY + (targetCenterY - startCenterY) * eased;
            scale = 0.8f + 0.2f * eased;
        }
        else if (_age < EnterSeconds + _holdSeconds)
        {
            // Bekleme: hafif nefes alma / zıplama salınımı
            float t = _age - EnterSeconds;
            centerY = targetCenterY - (float)Math.Abs(Math.Sin(t * 3.4)) * side * 0.05f;
            scale = 1f + (float)Math.Sin(t * 6.8) * 0.02f;
        }
        else
        {
            // Çıkış: büyüyerek saydamlaş
            float p = Math.Clamp((_age - EnterSeconds - _holdSeconds) / ExitSeconds, 0f, 1f);
            centerY = targetCenterY - side * 0.12f * p;
            scale = 1f + 0.35f * p;
            alpha = 1f - p;
        }

        float drawSide = side * scale;
        var box = new RectangleF(
            targetCenterX - drawSide * 0.5f,
            centerY - drawSide * 0.5f,
            drawSide,
            drawSide);

        AnimalArtist.Draw(g, Kind, box, alpha);
        SpeechBubble.Draw(g, area, box, Text, speechFont, alpha);
    }
}
