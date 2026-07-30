using System.Drawing;
using GamesApp.Audio;
using GamesApp.Input;
using GamesApp.UI;

namespace GamesApp.Games.Drums;

/// <summary>
/// Davul (Bateri) oyunu: her tuş bir bateri parçasına vurur; parça parlar,
/// baget hamle yapar, yukarıda renkli efektler patlar ve piyanodaki gibi
/// belirli sayıda vuruştan sonra hayvan sürprizi çıkar.
///
/// Ses, GM perküsyon kanalı (MIDI kanal 10) üzerinden çalınır; ek ses dosyası
/// gerekmez. Perküsyon tek atımlı olduğu için KeyUp'ta susturma yapılmaz.
/// </summary>
internal sealed class DrumGameControl : Control, IGameModule
{
    private readonly IDrumSound _drums;
    private readonly IAnimalSound _animalSound;
    private readonly AnimalDirector _animalDirector = new();
    private readonly Random _random = new();

    private readonly EffectCanvas _canvas;
    private readonly DrumKitView _kit;

    /// <summary>Basılı tutulan tuşlar (auto-repeat'te vuruşu tekrar çalmamak için).</summary>
    private readonly HashSet<int> _pressedKeys = new();

    private bool _disposedResources;

    public DrumGameControl(IDrumSound drums, IAnimalSound animalSound)
    {
        _drums = drums;
        _animalSound = animalSound;

        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;

        _kit = new DrumKitView();
        _kit.PieceHit += OnMousePieceHit;

        _canvas = new EffectCanvas();
        _canvas.FrameUpdated += OnFrameUpdated;
        _canvas.AnimalExiting += OnAnimalExiting;

        Controls.Add(_kit);
        Controls.Add(_canvas);
    }

    public string MenuTitle => "🥁 Davul";

    public Color MenuColor => Theme.ColorFromHsv(15.0, 0.85, 0.90);

    public Control View => this;

    /// <summary>Efekt motorunun ürettiği toplam nesne sayısı (selftest raporu için).</summary>
    internal int TotalEffectsSpawned => _canvas.Engine.TotalSpawned;

    /// <summary>Selftest: sahnede şu anda bir hayvan var mı?</summary>
    internal bool HasActiveAnimal => _canvas.HasActiveAnimal;

    public void Start()
    {
        _canvas.Start();
    }

    public void Stop()
    {
        _canvas.Stop();
        _pressedKeys.Clear();
        _animalSound.Stop();
    }

    // ---------------- Tuş akışı ----------------

    /// <summary>
    /// Kancadan gelen her tuş basımı bir davul vuruşudur; hiçbir tuş muaf değildir
    /// (piyanodaki "sınır yok" kuralının aynısı).
    /// </summary>
    public void HandleKeyDown(int vkCode)
    {
        int piece = DrumKeyMapper.GetPiece(vkCode);

        if (_pressedKeys.Contains(vkCode))
        {
            // Klavye auto-repeat: ses TEKRAR çalınmaz, görsel hafifçe canlanır.
            _kit.Strike(piece, 0.4f);
            _canvas.Spawn(DrumKit.Pieces[piece].ColorNote, GetEffectOrigin(piece), 0.4f);
            return;
        }

        _pressedKeys.Add(vkCode);
        PlayPiece(piece, DrumKeyMapper.GetVelocity(vkCode));
    }

    public void HandleKeyUp(int vkCode)
    {
        // Perküsyon tek atımlıdır; yalnızca auto-repeat takibi için tutuluyordu.
        _pressedKeys.Remove(vkCode);
    }

    private void OnMousePieceHit(int piece, int velocity)
    {
        PlayPiece(piece, velocity);
    }

    /// <summary>Bir parçaya tam vuruş: ses + baget + parlaklık + efekt + hayvan sayacı.</summary>
    private void PlayPiece(int pieceIndex, int velocity)
    {
        DrumPieceInfo piece = DrumKit.Pieces[pieceIndex];

        _drums.Hit(piece.GmNote, velocity);

        // MIDI yedeğinde ana nota + aksan notası birlikte çalınır (katmanlama = gür vuruş).
        if (_drums.NeedsAccentLayer)
        {
            _drums.Hit(piece.AccentNote, Math.Max(1, velocity - 10));
        }

        _kit.Strike(pieceIndex);
        _canvas.Spawn(piece.ColorNote, GetEffectOrigin(pieceIndex));

        RegisterAnimalProgress();
    }

    /// <summary>
    /// Vuruşu hayvan yönetmenine bildirir; eşiğe ulaşıldıysa hayvanı çıkarır.
    /// Ses ve görsel AYNI KAREDE tetiklenir (piyanodaki davranışın aynısı).
    /// </summary>
    private void RegisterAnimalProgress()
    {
        if (_animalDirector.RegisterNotePress(out AnimalKind kind))
        {
            _animalSound.TryPlay(kind, out int soundDurationMs);
            _canvas.ShowAnimal(kind, soundDurationMs);
        }
    }

    private void OnAnimalExiting()
    {
        _animalSound.Stop();
    }

    private void OnFrameUpdated(float deltaSeconds)
    {
        _kit.Advance(deltaSeconds);
    }

    /// <summary>
    /// Efektin doğduğu nokta: vurulan parçanın yatay konumuyla hizalanır
    /// (görsel-ses ilişkisi kurulsun), dikeyde rastgele sapma verilir.
    /// </summary>
    private PointF GetEffectOrigin(int pieceIndex)
    {
        int width = _canvas.Width;
        int height = _canvas.Height;
        if (width <= 0 || height <= 0)
        {
            return PointF.Empty;
        }

        float margin = width * 0.06f;
        float x = margin + DrumKit.Pieces[pieceIndex].X * (width - margin * 2f);
        float y = height * 0.25f + (float)_random.NextDouble() * height * 0.5f;

        return new PointF(x, y);
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

        int kitHeight = Math.Max(160, (int)(height * 0.40));
        _kit.SetBounds(0, height - kitHeight, width, kitHeight);
        _canvas.SetBounds(0, 0, width, height - kitHeight);
    }

    // ---------------- Selftest yardımcıları ----------------

    /// <summary>
    /// Selftest/stres: animasyonları elle ilerletir (EffectCanvas zamanlayıcısı
    /// çalışmadan baget ve parlaklık sönümü işletilebilsin diye).
    /// </summary>
    internal void SelfTestAdvance(float deltaSeconds)
    {
        _kit.Advance(deltaSeconds);
    }

    /// <summary>
    /// Selftest: bir tuşu GERÇEK tuş işleme yolundan geçirir (kanca kurulmadan).
    /// Efekt tetiklendiyse true döner.
    /// </summary>
    internal bool SelfTestFeedKey(int vkCode)
    {
        int spawnedBefore = _canvas.Engine.TotalSpawned;

        HandleKeyDown(vkCode);
        bool audible = _canvas.Engine.TotalSpawned > spawnedBefore;
        HandleKeyUp(vkCode);

        return audible;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;

            _canvas.FrameUpdated -= OnFrameUpdated;
            _canvas.AnimalExiting -= OnAnimalExiting;
            _kit.PieceHit -= OnMousePieceHit;
        }

        base.Dispose(disposing);
    }
}
