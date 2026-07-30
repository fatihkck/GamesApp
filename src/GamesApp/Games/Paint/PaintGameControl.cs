using System.Drawing;
using GamesApp.Audio;
using GamesApp.UI;

namespace GamesApp.Games.Paint;

/// <summary>
/// Sihirli Fırça (Boyama) oyunu: ekran koyu bir tuval olarak başlar ve KLAVYEDEKİ
/// HERHANGİ BİR TUŞA basıldığında tuşun bölgesine büyük, parlak bir boya lekesi
/// vurulur (ana daire + fırça izi + saçılan damlacıklar) ve ıslak bir "şlop" sesi
/// duyulur. Tuşlara bastıkça ekran rengarenk bir tabloya dönüşür; tablo yeterince
/// dolunca (%80) konfeti yağar, zafer fanfarı çalar ve tuval beyaz bir parlama
/// içinde temizlenip boyama BAŞTAN başlar.
///
/// TASARIM KURALLARI (bkz. docs/TASARIM-KURALLARI.md):
///  - Omni-input: her tuş bir leke vurur, muaf tuş yoktur (kural 1).
///  - Ses ve görsel aynı karede başlar: tuş anında "şlop" çalar ve leke belirir
///    (kural 5).
///  - Sesler ortak <see cref="WaveMixer"/> üzerinden, tam ölçeğe normalize edilmiş
///    örneklerle çalar (kural 6); diğer oyunlarla dengeli gürlükte duyulur.
///  - Auto-repeat'te yeni leke vurulmaz; tuşun bölgesine küçük damlacıklar serpilir
///    (kural 7: basılı tutan çocuk "püskürtme" yapmış olur, tepki hiç kesilmez).
///  - Sahne asla boş kalmaz (kural 8): oyuna girişte karşılama lekesi düşer; tuvale
///    4 saniye dokunulmazsa kendiliğinden bir leke gelip çocuğu basmaya davet eder.
///    Oyun değişse bile RESİM KORUNUR: çocuk dönünce tablosunu yerinde bulur.
///
/// ESNETİLEN KURAL 9 (hayvan sürprizi): Bu oyunda hayvan sürprizi YOKTUR — sahnenin
/// kendisi çocuğun biriken eseridir; üstüne çıkan bir hayvan tabloyu kapatır ve
/// "benim yaptığım resim" hissini bölerdi. Ödül, tablonun dolması ve tamamlanınca
/// gelen konfetili fanfar kutlamasıdır (Balon oyunuyla aynı gerekçe ailesi).
///
/// KURAL 4 GEREĞİ arka plan müziği bu oyunda BAŞLATILMAZ: "şlop" sesleri ve fanfar
/// çocuğun kendi eylemine aittir, müzik onları bastırırdı.
/// </summary>
internal sealed class PaintGameControl : Control, IGameModule
{
    /// <summary>"Şlop" sesi ana gürlük çarpanı (davul ve balonla dengeli seviyede).</summary>
    private const float SplatGain = 1.10f;

    /// <summary>Fanfar gürlük çarpanı (kutlama belirgin ama kulak tırmalamaz).</summary>
    private const float FanfareGain = 1.0f;

    private readonly WaveMixer _mixer;
    private readonly Random _random = new();
    private readonly PaintCanvasView _canvas;

    /// <summary>"Şlop" ses torbası: varyantlar karıştırılıp sırayla tüketilir.</summary>
    private readonly int[] _soundOrder = new int[SplatSoundSynth.VariantCount];
    private int _soundIndex;

    /// <summary>Basılı tutulan tuşlar (auto-repeat'te yeni leke vurmamak için).</summary>
    private readonly HashSet<int> _pressedKeys = new();

    private bool _disposedResources;

    public PaintGameControl(WaveMixer mixer)
    {
        _mixer = mixer;

        for (int i = 0; i < _soundOrder.Length; i++)
        {
            _soundOrder[i] = i;
        }

        ShuffleSounds();

        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.BackgroundDeep;

        _canvas = new PaintCanvasView();
        _canvas.SplatRequested += OnSplatRequested;
        _canvas.CanvasCompleted += OnCanvasCompleted;
        _canvas.StageClicked += OnStageClicked;
        Controls.Add(_canvas);
    }

    public string MenuIcon => "🎨";

    public string MenuTitle => "Boyama";

    public Color MenuColor => Theme.ColorFromHsv(160.0, 0.80, 0.90);

    public Control View => this;

    /// <summary>Efekt motorunun ürettiği toplam nesne sayısı (selftest raporu için).</summary>
    internal int TotalEffectsSpawned => _canvas.Engine.TotalSpawned;

    /// <summary>Selftest: tuvalin dolu hücre oranı.</summary>
    internal float CoverageRatio => _canvas.CoverageRatio;

    /// <summary>Selftest: tablo kaç kez tamamlanıp sıfırlandı.</summary>
    internal int ResetCount => _canvas.ResetCount;

    public void Start()
    {
        _canvas.Start();

        // Oyun açılır açılmaz karşılama lekesi düşer: sahne boş başlamaz (kural 8).
        SplatRandom();
    }

    public void Stop()
    {
        _canvas.Stop();
        _pressedKeys.Clear();

        // Uzun fanfar sonraki oyuna taşmasın.
        _mixer.StopAll();
    }

    // ---------------- Tuş akışı ----------------

    /// <summary>
    /// Kancadan gelen her tuş basımı bir leke vurur. HİÇBİR TUŞ MUAF DEĞİLDİR:
    /// Esc, Windows tuşu, Alt, Tab, Ctrl, Shift, CapsLock ve medya tuşları da vurur.
    /// </summary>
    public void HandleKeyDown(int vkCode)
    {
        if (_pressedKeys.Contains(vkCode))
        {
            // Auto-repeat: yeni leke yok ama damlacık serpintisi sürer (püskürtme).
            _canvas.Sprinkle(vkCode);
            return;
        }

        _pressedKeys.Add(vkCode);
        SplatAt(vkCode);
    }

    public void HandleKeyUp(int vkCode)
    {
        _pressedKeys.Remove(vkCode);
    }

    private void OnStageClicked()
    {
        // Fareyle tuvale dokunmak da gerçek bir vuruştur (ebeveyn deneyebilsin).
        SplatRandom();
    }

    private void OnSplatRequested()
    {
        // Tuvale uzun süre dokunulmadı: davet lekesi gelsin.
        SplatRandom();
    }

    private void OnCanvasCompleted()
    {
        // Tablo tamamlandı: zafer fanfarı (konfetiyi tuval kendisi yağdırır).
        _mixer.Play(SplatSoundSynth.GetFanfareSample(), FanfareGain);
    }

    /// <summary>"Şlop" sesini ANINDA çalar ve tuşun bölgesine lekeyi vurur.</summary>
    private void SplatAt(int vkCode)
    {
        // Ses aygıtı yoksa oyun sessiz ama tam işlevli çalışmaya devam eder.
        _mixer.Play(SplatSoundSynth.GetMixerSample(NextSoundVariant()), SplatGain);

        _canvas.Splat(vkCode);
    }

    /// <summary>Rastgele bir tuşun bölgesine leke vurur (karşılama ve davet lekeleri).</summary>
    private void SplatRandom()
    {
        SplatAt(_random.Next(256));
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

        // Tuval tüm alanı kaplar (kenarlıksız resim kağıdı hissi).
        _canvas.SetBounds(0, 0, width, height);
    }

    // ---------------- Selftest yardımcıları ----------------

    /// <summary>Selftest/stres: bir kareyi elle ilerletir (zamanlayıcı çalışmadan).</summary>
    internal void SelfTestAdvance(float deltaSeconds)
    {
        _canvas.Advance(deltaSeconds);
    }

    /// <summary>
    /// Selftest: bir tuşu GERÇEK tuş işleme yolundan geçirir (kanca kurulmadan).
    /// Efekt tetiklendiyse true döner.
    /// </summary>
    internal bool SelfTestFeedKey(int vkCode)
    {
        int spawnedBefore = _canvas.Engine.TotalSpawned;

        HandleKeyDown(vkCode);
        bool reacted = _canvas.Engine.TotalSpawned > spawnedBefore;
        HandleKeyUp(vkCode);

        return reacted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;

            _canvas.SplatRequested -= OnSplatRequested;
            _canvas.CanvasCompleted -= OnCanvasCompleted;
            _canvas.StageClicked -= OnStageClicked;

            // Mikser Program'a aittir; burada kapatılmaz.
        }

        base.Dispose(disposing);
    }
}
