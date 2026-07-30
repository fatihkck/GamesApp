using System.Drawing;
using GamesApp.Audio;
using GamesApp.Input;
using GamesApp.UI;

namespace GamesApp.Games.Peekaboo;

/// <summary>
/// "Cee-e!" (saklambaç) oyunu: ekranda kırmızı perdeli bir kukla tiyatrosu vardır ve
/// KLAVYEDEKİ HERHANGİ BİR TUŞA basıldığında perde aralanır, arkasından sevimli bir
/// karakter "CEE-E!" diyerek fırlar. Her basışta HEM karakter HEM ses değişir:
/// karakterler torbadan (<see cref="AnimalShuffleBag"/>), komik sesler (kıkırdama,
/// kahkaha, alkış, zil, parti borusu, düdük) kendi torbasından sırayla gelir; ikisi de
/// üst üste tekrar etmez. 1,5 yaş için "Cee-e" oyununun kendisi ödüldür: karakterin
/// aniden belirmesi ve komik ses her basışta yeni bir kahkaha üretir.
///
/// TASARIM KURALLARI (bkz. docs/TASARIM-KURALLARI.md):
///  - Omni-input: her tuş bir "Cee-e!" tetikler, muaf tuş yoktur (kural 1).
///  - Ses ve görsel aynı karede başlar: tuş anında ses çalar ve karakter fırlamaya
///    başlar (kural 5). Fırlama bu yüzden çok kısadır (0,3 sn).
///  - Sesler ortak <see cref="WaveMixer"/> üzerinden, tam ölçeğe normalize edilmiş
///    örneklerle çalar (kural 6); diğer oyunlarla dengeli gürlükte duyulur.
///  - Auto-repeat'te yeni "Cee-e!" olmaz; sahnedeki karakter neşeyle zıplar (kural 7).
///  - Sahne asla boş kalmaz (kural 8): perde salınır, ampuller yanıp söner, yıldızlar
///    parıldar; perde uzun süre kapalı kalırsa karakter kendiliğinden "Cee-e!" yapar.
///
/// ESNETİLEN KURAL 9 (hayvan sürprizi): Bu oyunda ayrı bir sürpriz katmanı YOKTUR —
/// perdeden fırlayan karakterin kendisi zaten sürprizdir; üstüne ikinci bir sürpriz
/// eklemek neden-sonuç ilişkisini bulanıklaştırırdı (Hayvanat Bahçesi ile aynı karar).
///
/// KURAL 4 GEREĞİ arka plan müziği bu oyunda BAŞLATILMAZ: oyunun bütün mizahı komik
/// seslerdedir, müzik onları bastırırdı.
/// </summary>
internal sealed class PeekabooGameControl : Control, IGameModule
{
    /// <summary>Komik ses ana gürlük çarpanı (davul ve balonla dengeli seviyede).</summary>
    private const float VoiceGain = 1.10f;

    private readonly WaveMixer _mixer;
    private readonly Random _random = new();
    private readonly AnimalShuffleBag _bag;
    private readonly PeekabooStageView _stage;

    /// <summary>Komik ses torbası: varyantlar karıştırılıp sırayla tüketilir.</summary>
    private readonly int[] _soundOrder = new int[CheerSoundSynth.VariantCount];
    private int _soundIndex;

    /// <summary>Basılı tutulan tuşlar (auto-repeat'te yeni "Cee-e!" tetiklememek için).</summary>
    private readonly HashSet<int> _pressedKeys = new();

    private bool _disposedResources;

    public PeekabooGameControl(WaveMixer mixer)
    {
        _mixer = mixer;
        _bag = new AnimalShuffleBag(_random);

        for (int i = 0; i < _soundOrder.Length; i++)
        {
            _soundOrder[i] = i;
        }

        ShuffleSounds();

        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;

        _stage = new PeekabooStageView();
        _stage.RevealRequested += OnRevealRequested;
        _stage.StageClicked += OnStageClicked;
        Controls.Add(_stage);
    }

    public string MenuIcon => "🙈";

    public string MenuTitle => "Cee-e";

    public Color MenuColor => Theme.ColorFromHsv(285.0, 0.75, 0.95);

    public Control View => this;

    /// <summary>Efekt motorunun ürettiği toplam nesne sayısı (selftest raporu için).</summary>
    internal int TotalEffectsSpawned => _stage.Engine.TotalSpawned;

    /// <summary>Selftest: sahnede görünen bir karakter var mı?</summary>
    internal bool HasCharacter => _stage.HasCharacter;

    public void Start()
    {
        _stage.Start();

        // Oyun açılır açılmaz ilk "Cee-e!" gelir: sahne boş başlamaz (kural 8).
        RevealNext();
    }

    public void Stop()
    {
        _stage.Stop();
        _pressedKeys.Clear();

        // Uzun kahkaha/alkış sesleri sonraki oyuna taşmasın.
        _mixer.StopAll();
    }

    // ---------------- Tuş akışı ----------------

    /// <summary>
    /// Kancadan gelen her tuş basımı bir "Cee-e!" tetikler. HİÇBİR TUŞ MUAF DEĞİLDİR:
    /// Esc, Windows tuşu, Alt, Tab, Ctrl, Shift, CapsLock ve medya tuşları da tetikler.
    /// </summary>
    public void HandleKeyDown(int vkCode)
    {
        if (_pressedKeys.Contains(vkCode))
        {
            // Auto-repeat: yeni "Cee-e!" olmaz ama tepki verilir (neşe zıplaması).
            _stage.Cheer();
            return;
        }

        _pressedKeys.Add(vkCode);
        RevealNext();
    }

    public void HandleKeyUp(int vkCode)
    {
        _pressedKeys.Remove(vkCode);
    }

    private void OnStageClicked()
    {
        // Fareyle sahneye dokunmak da gerçek bir tetiklemedir (ebeveyn deneyebilsin).
        RevealNext();
    }

    private void OnRevealRequested()
    {
        // Perde uzun süre kapalı kaldı: davet "Cee-e!"si gelsin.
        RevealNext();
    }

    /// <summary>
    /// Torbadan sıradaki karakteri ve sıradaki komik sesi alır, sesi ANINDA çalar ve
    /// karakteri perdenin arkasından fırlatır. Karakterin sahnede kalma süresi sesin
    /// uzunluğuna göre belirlenir; ses karakterden sonra sahipsiz kalmaz.
    /// </summary>
    private void RevealNext()
    {
        AnimalKind kind = _bag.Take();

        short[] sample = CheerSoundSynth.GetMixerSample(NextSoundVariant());
        float soundSeconds = sample.Length / (float)SampleUtil.SampleRate;

        // Ses aygıtı yoksa oyun sessiz ama tam işlevli çalışmaya devam eder.
        _mixer.Play(sample, VoiceGain);

        _stage.Reveal(kind, soundSeconds);
    }

    /// <summary>Ses torbasından sıradaki varyantı verir; torba bitince yeniden karıştırır.</summary>
    private int NextSoundVariant()
    {
        if (_soundIndex >= _soundOrder.Length)
        {
            ShuffleSounds();
        }

        return _soundOrder[_soundIndex++];
    }

    /// <summary>Ses torbasını Fisher-Yates ile karıştırır ve başa sarar.</summary>
    private void ShuffleSounds()
    {
        for (int i = _soundOrder.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_soundOrder[i], _soundOrder[j]) = (_soundOrder[j], _soundOrder[i]);
        }

        _soundIndex = 0;
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

        // Sahne tüm alanı kaplar (tiyatro hissi; ayrı panel yok).
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

            _stage.RevealRequested -= OnRevealRequested;
            _stage.StageClicked -= OnStageClicked;

            // Mikser Program'a aittir; burada kapatılmaz.
        }

        base.Dispose(disposing);
    }
}
