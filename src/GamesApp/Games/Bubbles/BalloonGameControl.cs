using System.Drawing;
using GamesApp.Audio;
using GamesApp.UI;

namespace GamesApp.Games.Bubbles;

/// <summary>
/// Balon Patlatma oyunu: ekranda yavaşça yukarı süzülen renkli balonlar, KLAVYEDEKİ
/// HERHANGİ BİR TUŞA basıldığında komik bir "pıt!" sesiyle patlar; içinden konfeti
/// ve yıldızlar saçılır.
///
/// HAYVAN SÜRPRİZİ YOKTUR (kullanıcı kararı): bu oyunun ödülü patlama anının
/// kendisidir; sahne sade kalır ve dikkat balonlarda toplanır. Hayvanlar piyano ve
/// davul oyunlarında çıkmaya devam eder (bkz. tasarım kuralı 9'un esnetilmesi).
///
/// TASARIM KURALLARI (bkz. docs/TASARIM-KURALLARI.md):
///  - Omni-input: her tuş bir balon patlatır, muaf tuş yoktur.
///  - Auto-repeat'te balon patlamaz (tarla bir anda boşalmasın); bunun yerine
///    küçük bir parıltı verilir, yani tepki asla kesilmez.
///  - Arka plan müziği kısık çalar ve çal/sus döngüsüyle aralıklı gider; böylece
///    çocuğun kendi eylemine ait ses (patlama) her zaman öne çıkar.
/// </summary>
internal sealed class BalloonGameControl : Control, IGameModule
{
    /// <summary>Patlama sesi ana gürlük çarpanı (davulla dengeli olacak seviyede).</summary>
    private const float PopGain = 1.25f;

    private readonly WaveMixer _mixer;
    private readonly BackgroundMusic _music;
    private readonly Random _random = new();

    private readonly BalloonFieldView _field;

    /// <summary>Önceden sentezlenmiş "pıt!" varyantları.</summary>
    private readonly short[][] _popSamples = new short[PopSoundSynth.VariantCount][];

    /// <summary>Basılı tutulan tuşlar (auto-repeat'te tekrar patlatmamak için).</summary>
    private readonly HashSet<int> _pressedKeys = new();

    /// <summary>Müziğin çal/sus turlarını sayan zamanlayıcı.</summary>
    private readonly System.Windows.Forms.Timer _musicTimer;

    /// <summary>Aynı sesin üst üste gelmemesi için son çalınan varyant.</summary>
    private int _lastPopVariant = -1;

    private int _musicPhaseSeconds;
    private bool _musicPlayPhase = true;
    private bool _disposedResources;

    public BalloonGameControl(WaveMixer mixer, BackgroundMusic music)
    {
        _mixer = mixer;
        _music = music;

        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;

        for (int i = 0; i < _popSamples.Length; i++)
        {
            _popSamples[i] = PopSoundSynth.Render(i);
        }

        _field = new BalloonFieldView();
        _field.BalloonClicked += OnBalloonClicked;
        Controls.Add(_field);

        _musicTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _musicTimer.Tick += OnMusicTimerTick;
    }

    public string MenuIcon => "🎈";

    public string MenuTitle => "Balon";

    public Color MenuColor => Theme.ColorFromHsv(330.0, 0.75, 0.92);

    public Control View => this;

    /// <summary>Efekt motorunun ürettiği toplam nesne sayısı (selftest raporu için).</summary>
    internal int TotalEffectsSpawned => _field.Engine.TotalSpawned;

    /// <summary>Selftest: ekrandaki balon sayısı.</summary>
    internal int BalloonCount => _field.BalloonCount;

    public void Start()
    {
        _field.Start();

        // Oyuna her girişte müzik turu baştan başlar (önce müzikli faz).
        _musicPhaseSeconds = 0;
        _musicPlayPhase = true;
        _music.Resume();
        _musicTimer.Start();
    }

    public void Stop()
    {
        _musicTimer.Stop();
        _music.Pause();

        _field.Stop();
        _pressedKeys.Clear();
    }

    // ---------------- Müzik döngüsü ----------------

    /// <summary>
    /// Müziği <see cref="BackgroundMusic.PlaySeconds"/> kadar çalar, ardından
    /// <see cref="BackgroundMusic.RestSeconds"/> kadar susturur ve tekrar sürdürür.
    /// </summary>
    private void OnMusicTimerTick(object? sender, EventArgs e)
    {
        _musicPhaseSeconds++;

        if (_musicPlayPhase)
        {
            if (_musicPhaseSeconds >= BackgroundMusic.PlaySeconds)
            {
                _music.Pause();
                _musicPlayPhase = false;
                _musicPhaseSeconds = 0;
            }

            return;
        }

        if (_musicPhaseSeconds >= BackgroundMusic.RestSeconds)
        {
            _music.Resume();
            _musicPlayPhase = true;
            _musicPhaseSeconds = 0;
        }
    }

    // ---------------- Tuş akışı ----------------

    /// <summary>
    /// Kancadan gelen her tuş basımı bir balon patlatır. HİÇBİR TUŞ MUAF DEĞİLDİR:
    /// Esc, Windows tuşu, Alt, Tab, Ctrl, Shift, CapsLock ve medya tuşları da patlatır.
    /// </summary>
    public void HandleKeyDown(int vkCode)
    {
        if (_pressedKeys.Contains(vkCode))
        {
            // Auto-repeat: balon patlamaz ama tepki verilir (küçük parıltı).
            _field.Sparkle();
            return;
        }

        _pressedKeys.Add(vkCode);
        PopOne();
    }

    public void HandleKeyUp(int vkCode)
    {
        _pressedKeys.Remove(vkCode);
    }

    private void OnBalloonClicked(PointF center, Color color)
    {
        // Fare ile patlatmak da gerçek bir patlatmadır: sesi de vardır.
        PlayPop();
    }

    /// <summary>En görünür balonu patlatır; balon yoksa bile ses ve efekt verir.</summary>
    private void PopOne()
    {
        if (!_field.PopMostVisible(out _, out _))
        {
            // Tarla bir an boş kaldıysa (nadir) tepki gene de verilir: sessiz kalmayız.
            _field.Sparkle();
        }

        PlayPop();
    }

    /// <summary>Rastgele bir "pıt!" varyantı çalar (üst üste aynı ses gelmez).</summary>
    private void PlayPop()
    {
        if (!_mixer.IsAvailable)
        {
            return;
        }

        int variant = _random.Next(PopSoundSynth.VariantCount);
        if (variant == _lastPopVariant)
        {
            variant = (variant + 1) % PopSoundSynth.VariantCount;
        }

        _lastPopVariant = variant;
        _mixer.Play(_popSamples[variant], PopGain);
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

        // Balon tarlası tüm alanı kaplar (alt panel yok: gökyüzü hissi).
        _field.SetBounds(0, 0, width, height);
    }

    // ---------------- Selftest yardımcıları ----------------

    /// <summary>Selftest/stres: bir kareyi elle ilerletir (zamanlayıcı çalışmadan).</summary>
    internal void SelfTestAdvance(float deltaSeconds)
    {
        _field.Advance(deltaSeconds);
    }

    /// <summary>Selftest: tarlayı balonlarla doldurur (zamanlayıcı başlatmadan).</summary>
    internal void SelfTestFillField()
    {
        _field.Fill();
    }

    /// <summary>
    /// Selftest: bir tuşu GERÇEK tuş işleme yolundan geçirir (kanca kurulmadan).
    /// Efekt tetiklendiyse true döner.
    /// </summary>
    internal bool SelfTestFeedKey(int vkCode)
    {
        int spawnedBefore = _field.Engine.TotalSpawned;

        HandleKeyDown(vkCode);
        bool reacted = _field.Engine.TotalSpawned > spawnedBefore;
        HandleKeyUp(vkCode);

        return reacted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;

            _musicTimer.Tick -= OnMusicTimerTick;
            _musicTimer.Stop();
            _musicTimer.Dispose();

            _field.BalloonClicked -= OnBalloonClicked;

            // Müzik ve mikser Program'a aittir; burada kapatılmaz.
        }

        base.Dispose(disposing);
    }
}
