using System.Drawing;
using GamesApp.Audio;
using GamesApp.Input;
using GamesApp.UI;

namespace GamesApp.Games.Fireworks;

/// <summary>
/// Havai Fişek oyunu: gece göğü ve şehir silüeti üzerinde KLAVYEDEKİ HERHANGİ BİR
/// TUŞA basıldığında yerden bir roket fırlar ("vıiiii..."), tepede rengarenk patlar
/// ("PAT!") ve kıvılcımlar süzülerek dökülür. Her basış farklıdır: patlama deseni
/// torbadan gelir (küre, halka, söğüt, çift patlama, KALP, YILDIZ), renk tekerleği
/// altın açıyla döner ve arada patlamanın içinden ışıl ışıl bir sürpriz misafir
/// (hayvan dostu) çıkar.
///
/// TASARIM KURALLARI (bkz. docs/TASARIM-KURALLARI.md):
///  - Omni-input: her tuş bir roket fırlatır, muaf tuş yoktur (kural 1).
///  - Ses ve görsel aynı karede başlar (kural 5): tuş anında fırlama sesi çalar ve
///    roket görünür; patlama sesi de patlama KARESİNDE çalar (gerçek fişek gibi iki
///    ayrı ses anı vardır, ikisi de kendi görseliyle eşzamanlıdır).
///  - Sesler ortak <see cref="WaveMixer"/> üzerinden, tam ölçeğe normalize edilmiş
///    örneklerle çalar (kural 6); diğer oyunlarla dengeli gürlükte duyulur.
///  - Auto-repeat'te yeni roket fırlamaz; yerden kısa bir kıvılcım fıskiyesi yükselir
///    (kural 7).
///  - Sahne asla boş kalmaz (kural 8): yıldızlar parıldar, şehir pencereleri yanıp
///    söner; oyuna girişte karşılama roketi fırlar ve gök 3,5 saniye boş kalırsa bir
///    roket kendiliğinden fırlayıp çocuğu basmaya davet eder.
///  - TUŞ = YER: her tuşun sabit bir fırlatma noktası vardır (Boyama ile aynı ilke);
///    çocuk aynı tuşun roketi hep aynı yerden kaldırdığını keşfeder.
///
/// KURAL 9 (hayvan sürprizi) BU OYUNDA DESENE GÖMÜLÜDÜR: ayrı bir sürpriz katmanı
/// yerine her 6-10 fişekte bir, patlamanın içinden torbadan gelen bir hayvan çıkar ve
/// sesini verir. Sürpriz mekaniği korunur ama fişeğin kendisine ait hissedilir.
///
/// KURAL 4 GEREĞİ arka plan müziği bu oyunda BAŞLATILMAZ: gösterinin ritmi çocuğun
/// kendi fırlattığı roketlerin vınlama ve gümlemelerindedir.
/// </summary>
internal sealed class FireworksGameControl : Control, IGameModule
{
    /// <summary>Fırlama sesi gürlük çarpanı (patlamadan belirgin biçimde hafif).</summary>
    private const float LaunchGain = 0.75f;

    /// <summary>Patlama sesi gürlük çarpanı (gösterinin ana vuruşu).</summary>
    private const float BoomGain = 1.15f;

    /// <summary>Sürpriz misafirin ses gürlüğü.</summary>
    private const float GuestGain = 1.0f;

    private readonly WaveMixer _mixer;
    private readonly Random _random = new();
    private readonly AnimalShuffleBag _guestBag;
    private readonly FireworksStageView _stage;

    /// <summary>Desen torbası: altı patlama deseni karıştırılıp sırayla tüketilir.</summary>
    private readonly int[] _styleOrder = new int[6];
    private int _styleIndex;

    /// <summary>Bu kadar fişek sonra patlamadan sürpriz misafir çıkar (6-10 arası yenilenir).</summary>
    private int _guestCountdown;

    /// <summary>Basılı tutulan tuşlar (auto-repeat'te yeni roket fırlatmamak için).</summary>
    private readonly HashSet<int> _pressedKeys = new();

    private bool _disposedResources;

    public FireworksGameControl(WaveMixer mixer)
    {
        _mixer = mixer;
        _guestBag = new AnimalShuffleBag(_random);
        _guestCountdown = _random.Next(6, 11);

        for (int i = 0; i < _styleOrder.Length; i++)
        {
            _styleOrder[i] = i;
        }

        ShuffleStyles();

        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;

        _stage = new FireworksStageView();
        _stage.Exploded += OnExploded;
        _stage.LaunchRequested += OnLaunchRequested;
        _stage.StageClicked += OnStageClicked;
        Controls.Add(_stage);
    }

    public string MenuIcon => "🎆";

    public string MenuTitle => "Fişek";

    public Color MenuColor => Theme.ColorFromHsv(250.0, 0.70, 0.95);

    public Control View => this;

    /// <summary>Üretilen toplam kıvılcım sayısı (selftest raporu için).</summary>
    internal int TotalEffectsSpawned => _stage.TotalSparksSpawned;

    /// <summary>Selftest: gökte şu anda bir şey var mı?</summary>
    internal bool HasActivity => _stage.HasActivity;

    /// <summary>Selftest: havadaki roket sayısı (üst sınır denetimi).</summary>
    internal int ActiveRocketCount => _stage.ActiveRocketCount;

    public void Start()
    {
        _stage.Start();

        // Oyun açılır açılmaz karşılama roketi fırlar: gök boş başlamaz (kural 8).
        LaunchNext(_random.Next(256));
    }

    public void Stop()
    {
        _stage.Stop();
        _pressedKeys.Clear();

        // Uzun gümleme/çıtırtı sesleri sonraki oyuna taşmasın.
        _mixer.StopAll();
    }

    // ---------------- Tuş akışı ----------------

    /// <summary>
    /// Kancadan gelen her tuş basımı bir roket fırlatır. HİÇBİR TUŞ MUAF DEĞİLDİR:
    /// Esc, Windows tuşu, Alt, Tab, Ctrl, Shift, CapsLock ve medya tuşları da fırlatır.
    /// </summary>
    public void HandleKeyDown(int vkCode)
    {
        if (_pressedKeys.Contains(vkCode))
        {
            // Auto-repeat: yeni roket yok ama tepki verilir (yer fıskiyesi).
            _stage.Cheer();
            return;
        }

        _pressedKeys.Add(vkCode);
        LaunchNext(vkCode);
    }

    public void HandleKeyUp(int vkCode)
    {
        _pressedKeys.Remove(vkCode);
    }

    private void OnStageClicked()
    {
        // Fareyle göğe dokunmak da gerçek bir fırlatmadır (ebeveyn deneyebilsin).
        LaunchNext(_random.Next(256));
    }

    private void OnLaunchRequested()
    {
        // Gök uzun süre boş kaldı: davet roketi fırlasın.
        LaunchNext(_random.Next(256));
    }

    /// <summary>Roket tepede patladı: patlama sesi ve varsa misafirin sesi AYNI KAREDE çalar.</summary>
    private void OnExploded(FireworkPlan plan)
    {
        _mixer.Play(FireworkSoundSynth.GetBoomSample(plan.BoomVariant), BoomGain);

        if (plan.Guest is AnimalKind guest)
        {
            _mixer.Play(AnimalSoundSynth.GetMixerSample(guest), GuestGain);
        }
    }

    /// <summary>
    /// Fırlama sesini ANINDA çalar ve roketi tuşun sabit noktasından kaldırır.
    /// Desen torbadan, patlama sesi rastgele, misafir sayaçla belirlenir.
    /// </summary>
    private void LaunchNext(int vkCode)
    {
        // Tuşun sabit fırlatma noktası (Boyama'daki "tuş = yer" ilkesiyle aynı karma).
        uint hash = (uint)vkCode * 2654435761u;
        float xRatio = 0.08f + (hash & 0xFFFF) / 65535f * 0.84f;

        AnimalKind? guest = null;
        if (--_guestCountdown <= 0)
        {
            guest = _guestBag.Take();
            _guestCountdown = _random.Next(6, 11);
        }

        var plan = new FireworkPlan(
            (FireworkStyle)NextStyle(),
            _random.Next(FireworkSoundSynth.BoomVariantCount),
            guest);

        // Ses aygıtı yoksa oyun sessiz ama tam işlevli çalışmaya devam eder.
        _mixer.Play(
            FireworkSoundSynth.GetLaunchSample(_random.Next(FireworkSoundSynth.LaunchVariantCount)),
            LaunchGain);

        _stage.Launch(xRatio, plan);
    }

    /// <summary>Desen torbasından sıradakini verir; torba bitince yeniden karıştırır.</summary>
    private int NextStyle()
    {
        if (_styleIndex >= _styleOrder.Length)
        {
            ShuffleStyles();
        }

        return _styleOrder[_styleIndex++];
    }

    /// <summary>Desen torbasını Fisher-Yates ile karıştırır ve başa sarar.</summary>
    private void ShuffleStyles()
    {
        for (int i = _styleOrder.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_styleOrder[i], _styleOrder[j]) = (_styleOrder[j], _styleOrder[i]);
        }

        _styleIndex = 0;
    }

    // ---------------- Yerleşim ----------------

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        int width = ClientSize.Width;
        int height = ClientSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Gökyüzü tüm alanı kaplar (açık hava gösterisi hissi).
        _stage.SetBounds(0, 0, width, height);
    }

    // ---------------- Selftest yardımcıları ----------------

    /// <summary>Selftest/stres: bir kareyi elle ilerletir (zamanlayıcı çalışmadan).</summary>
    internal void SelfTestAdvance(float deltaSeconds)
    {
        _stage.Advance(deltaSeconds);
    }

    /// <summary>
    /// Selftest: bir tuşu GERÇEK tuş işleme yolundan geçirir (kanca kurulmadan).
    /// Roket fırladıysa true döner.
    /// </summary>
    internal bool SelfTestFeedKey(int vkCode)
    {
        int launchedBefore = _stage.TotalLaunched;

        HandleKeyDown(vkCode);
        bool reacted = _stage.TotalLaunched > launchedBefore;
        HandleKeyUp(vkCode);

        return reacted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;

            _stage.Exploded -= OnExploded;
            _stage.LaunchRequested -= OnLaunchRequested;
            _stage.StageClicked -= OnStageClicked;

            // Mikser Program'a aittir; burada kapatılmaz.
        }

        base.Dispose(disposing);
    }
}
