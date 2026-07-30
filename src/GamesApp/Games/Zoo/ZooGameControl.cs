using System.Drawing;
using GamesApp.Audio;
using GamesApp.Input;
using GamesApp.UI;

namespace GamesApp.Games.Zoo;

/// <summary>
/// Hayvanat Bahçesi oyunu: ekranda bir orman vardır ve KLAVYEDEKİ HERHANGİ BİR TUŞA
/// basıldığında sahneye bir hayvan gelir, sesini verir ve gider. Her hayvanın gelişi
/// farklıdır: fil ağır ağır yürür, kurbağa zıplar, maymun takla atar, penguen kayar,
/// aslan atılıp kükrer, kedi yukarıdan süzülür.
///
/// Çocuk "acaba şimdi ne gelecek?" diye tekrar tekrar basar; hayvanlar torbadan
/// (<see cref="AnimalShuffleBag"/>) çekildiği için aynı hayvan üst üste gelmez.
///
/// TASARIM KURALLARI (bkz. docs/TASARIM-KURALLARI.md):
///  - Omni-input: her tuş bir hayvan çağırır, muaf tuş yoktur (kural 1).
///  - Ses ve görsel aynı karede başlar: tuşa basıldığı anda ses çalar ve hayvan
///    girmeye başlar (kural 5). Giriş animasyonları bu yüzden kısadır (0,4-0,9 sn).
///  - Sesler ortak <see cref="WaveMixer"/> üzerinden, tam ölçeğe normalize edilmiş
///    örneklerle çalar (kural 6). Böylece hem davul/balonla dengeli gürlükte olur hem
///    de aynı anda birkaç hayvan sesi üst üste binebilir.
///  - Auto-repeat'te yeni hayvan gelmez; sahnedeki hayvan neşeyle zıplar (kural 7).
///
/// ESNETİLEN KURAL 9 (hayvan sürprizi): Bu oyunda ayrı bir "sürpriz hayvan" katmanı
/// YOKTUR — hayvanın kendisi oyunun ana mekaniğidir, üstüne bir sürpriz eklemek
/// sahneyi kalabalıklaştırır ve neden-sonuç ilişkisini bulanıklaştırırdı.
///
/// BİLİNÇLİ SES TERCİHİ: Piyano/davul oyunlarındaki hayvan sürprizi, varsa
/// <c>Assets\Sounds</c> altındaki GERÇEK kayıtları çalar (<see cref="AnimalSoundPlayer"/>).
/// Bu oyun ise sentezlenmiş sesleri mikserden çalar: hayvan sesi burada saniyede birkaç
/// kez tetiklenebildiği için düşük gecikme ve çok seslilik, kayıt gerçekçiliğinden daha
/// önemlidir (MCI/PlaySound yolu tek sesli çalar ve her seferinde bir öncekini keser).
/// </summary>
internal sealed class ZooGameControl : Control, IGameModule
{
    /// <summary>Hayvan sesi ana gürlük çarpanı (davul ve balonla dengeli seviyede).</summary>
    private const float VoiceGain = 1.15f;

    private readonly WaveMixer _mixer;
    private readonly Random _random = new();
    private readonly AnimalShuffleBag _bag;
    private readonly ZooStageView _stage;

    /// <summary>Basılı tutulan tuşlar (auto-repeat'te yeni hayvan çağırmamak için).</summary>
    private readonly HashSet<int> _pressedKeys = new();

    private bool _disposedResources;

    public ZooGameControl(WaveMixer mixer)
    {
        _mixer = mixer;
        _bag = new AnimalShuffleBag(_random);

        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;

        _stage = new ZooStageView();
        _stage.AnimalRequested += OnAnimalRequested;
        _stage.StageClicked += OnStageClicked;
        Controls.Add(_stage);
    }

    public string MenuIcon => "🦁";

    public string MenuTitle => "Hayvanlar";

    public Color MenuColor => Theme.ColorFromHsv(38.0, 0.85, 0.95);

    public Control View => this;

    /// <summary>Efekt motorunun ürettiği toplam nesne sayısı (selftest raporu için).</summary>
    internal int TotalEffectsSpawned => _stage.Engine.TotalSpawned;

    /// <summary>Selftest: sahnedeki hayvan sayısı.</summary>
    internal int AnimalCount => _stage.ActorCount;

    public void Start()
    {
        _stage.Start();

        // Oyun açılır açılmaz karşılama hayvanı gelir: sahne boş başlamaz (kural 8).
        SummonNext();
    }

    public void Stop()
    {
        _stage.Stop();
        _pressedKeys.Clear();

        // Uzun hayvan sesleri sonraki oyuna taşmasın.
        _mixer.StopAll();
    }

    // ---------------- Tuş akışı ----------------

    /// <summary>
    /// Kancadan gelen her tuş basımı bir hayvan çağırır. HİÇBİR TUŞ MUAF DEĞİLDİR:
    /// Esc, Windows tuşu, Alt, Tab, Ctrl, Shift, CapsLock ve medya tuşları da çağırır.
    /// </summary>
    public void HandleKeyDown(int vkCode)
    {
        if (_pressedKeys.Contains(vkCode))
        {
            // Auto-repeat: yeni hayvan gelmez ama tepki verilir (neşe zıplaması).
            _stage.Cheer();
            return;
        }

        _pressedKeys.Add(vkCode);
        SummonNext();
    }

    public void HandleKeyUp(int vkCode)
    {
        _pressedKeys.Remove(vkCode);
    }

    private void OnStageClicked()
    {
        // Fareyle sahneye dokunmak da gerçek bir çağrıdır (ebeveyn deneyebilsin).
        SummonNext();
    }

    private void OnAnimalRequested()
    {
        // Sahne uzun süre boş kaldı: davet hayvanı gelsin.
        SummonNext();
    }

    /// <summary>
    /// Torbadan sıradaki hayvanı alır, sesini ANINDA çalar ve sahneye çıkarır.
    /// Hayvanın sahnede kalma süresi sesin uzunluğuna göre belirlenir; böylece ses
    /// hayvan gittikten sonra devam etmez.
    /// </summary>
    private void SummonNext()
    {
        AnimalKind kind = _bag.Take();

        short[] sample = AnimalSoundSynth.GetMixerSample(kind);
        float soundSeconds = sample.Length / (float)SampleUtil.SampleRate;

        // Ses aygıtı yoksa oyun sessiz ama tam işlevli çalışmaya devam eder.
        _mixer.Play(sample, VoiceGain);

        _stage.Summon(kind, soundSeconds);
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

        // Sahne tüm alanı kaplar (alt panel yok: orman hissi).
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
    /// Efekt tetiklendiyse true döner.
    /// </summary>
    internal bool SelfTestFeedKey(int vkCode)
    {
        int spawnedBefore = _stage.Engine.TotalSpawned;

        HandleKeyDown(vkCode);
        bool reacted = _stage.Engine.TotalSpawned > spawnedBefore;
        HandleKeyUp(vkCode);

        return reacted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;

            _stage.AnimalRequested -= OnAnimalRequested;
            _stage.StageClicked -= OnStageClicked;

            // Mikser Program'a aittir; burada kapatılmaz.
        }

        base.Dispose(disposing);
    }
}
