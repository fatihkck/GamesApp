using System.Drawing;
using GamesApp.Audio;
using GamesApp.Input;
using GamesApp.UI;

namespace GamesApp.Games.Piano;

/// <summary>
/// Piyano oyunu: her tuş bir nota çalar, efektler ve hayvan sürprizleri üretir.
/// Eski MainForm'un oyun mantığı buraya taşınmıştır; kiosk sorumlulukları
/// (kanca, tam ekran, çıkış) ShellForm'dadır.
/// </summary>
internal sealed class PianoGameControl : Control, IGameModule
{
    private readonly IPianoSound _sound;
    private readonly IAnimalSound _animalSound;
    private readonly AnimalDirector _animalDirector = new();
    private readonly Random _random = new();

    private readonly EffectCanvas _canvas;
    private readonly PianoKeyboardView _piano;

    /// <summary>Basılı tutulan tuşlar (auto-repeat'te notayı tekrar çalmamak için).</summary>
    private readonly HashSet<int> _pressedKeys = new();

    /// <summary>Tuş -> çalınan nota eşlemesi (KeyUp'ta doğru notayı bırakmak için).</summary>
    private readonly Dictionary<int, int> _noteByKey = new();

    private bool _disposedResources;

    public PianoGameControl(IPianoSound sound, IAnimalSound animalSound)
    {
        _sound = sound;
        _animalSound = animalSound;

        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;

        _piano = new PianoKeyboardView();
        _piano.NotePressed += OnPianoNotePressed;
        _piano.NoteReleased += OnPianoNoteReleased;

        _canvas = new EffectCanvas();
        _canvas.FrameUpdated += OnFrameUpdated;
        _canvas.AnimalExiting += OnAnimalExiting;

        Controls.Add(_piano);
        Controls.Add(_canvas);
    }

    public string MenuIcon => "🎹";

    public string MenuTitle => "Piyano";

    public Color MenuColor => Theme.ColorFromHsv(205.0, 0.80, 0.85);

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
        _noteByKey.Clear();

        // Oyundan çıkarken çalan her şeyi sustur; sonraki oyuna ses taşmasın.
        _sound.AllNotesOff();
        _animalSound.Stop();
    }

    // ---------------- Tuş akışı ----------------

    /// <summary>
    /// Kancadan gelen her tuş basımı. HİÇBİR TUŞ MUAF DEĞİLDİR: Esc, sol/sağ Windows
    /// tuşu, Alt, Tab, Ctrl, Shift, CapsLock, PrintScreen ve medya tuşları da nota çalar.
    /// </summary>
    public void HandleKeyDown(int vkCode)
    {
        if (_pressedKeys.Contains(vkCode))
        {
            // Klavye auto-repeat: nota TEKRAR çalınmaz, sadece efekt hafifçe canlanır.
            if (_noteByKey.TryGetValue(vkCode, out int repeatNote))
            {
                _canvas.Spawn(repeatNote, GetEffectOrigin(repeatNote), 0.4f);
                _piano.Highlight(repeatNote);
            }

            return;
        }

        _pressedKeys.Add(vkCode);

        KeyNoteMapper.TryGetNote(vkCode, out int note);
        _noteByKey[vkCode] = note;

        _sound.NoteOn(note, KeyNoteMapper.GetVelocity(vkCode));
        _canvas.Spawn(note, GetEffectOrigin(note));
        _piano.Highlight(note);

        // Birkaç tuştan sonra hayvan sürprizi (piyano çalmaya devam eder).
        RegisterAnimalProgress();
    }

    public void HandleKeyUp(int vkCode)
    {
        if (!_pressedKeys.Remove(vkCode))
        {
            return;
        }

        if (_noteByKey.Remove(vkCode, out int note))
        {
            _sound.NoteOff(note);
        }
    }

    private void OnPianoNotePressed(int note, int velocity)
    {
        _sound.NoteOn(note, velocity);
        _canvas.Spawn(note, GetEffectOrigin(note));

        // Fare ile çalmak da gerçek bir nota basımıdır; hayvan sayacına dâhildir.
        RegisterAnimalProgress();
    }

    private void OnPianoNoteReleased(int note)
    {
        _sound.NoteOff(note);
    }

    /// <summary>
    /// Nota basımını hayvan yönetmenine bildirir; eşiğe ulaşıldıysa hayvanı çıkarır.
    /// Ses ve görsel AYNI KAREDE tetiklenir.
    /// </summary>
    private void RegisterAnimalProgress()
    {
        if (_animalDirector.RegisterNotePress(out AnimalKind kind))
        {
            // Önceki hayvanın sesi hâlâ çalıyorsa TryPlay içinde kesilir (tek ses kuralı).
            _animalSound.TryPlay(kind, out int soundDurationMs);

            // Sahne süresi sesin ölçülmüş süresine göre ayarlanır.
            _canvas.ShowAnimal(kind, soundDurationMs);
        }
    }

    /// <summary>
    /// Hayvan kaybolmaya başladığında çalan sesi keser; böylece ses hayvanla birlikte
    /// biter ve bir sonraki hayvana taşmaz.
    /// </summary>
    private void OnAnimalExiting()
    {
        _animalSound.Stop();
    }

    private void OnFrameUpdated(float deltaSeconds)
    {
        _piano.Advance(deltaSeconds);
    }

    /// <summary>
    /// Efektin doğduğu nokta: notanın perdesi yatay konumu belirler
    /// (kalın nota solda, tiz nota sağda), dikeyde rastgele sapma verilir.
    /// </summary>
    private PointF GetEffectOrigin(int midiNote)
    {
        int width = _canvas.Width;
        int height = _canvas.Height;
        if (width <= 0 || height <= 0)
        {
            return PointF.Empty;
        }

        float margin = width * 0.08f;
        float x = margin + KeyNoteMapper.GetPitchPosition(midiNote) * (width - margin * 2f);
        float y = height * 0.28f + (float)_random.NextDouble() * height * 0.5f;

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

        int pianoHeight = Math.Max(90, (int)(height * 0.24));
        _piano.SetBounds(0, height - pianoHeight, width, pianoHeight);
        _canvas.SetBounds(0, 0, width, height - pianoHeight);
    }

    // ---------------- Selftest yardımcıları ----------------

    /// <summary>Selftest: verilen notayı çalar ve efekt üretir (kanca kurulmadan).</summary>
    internal void SelfTestPlay(int midiNote)
    {
        _sound.NoteOn(midiNote, 110);
        _canvas.Spawn(midiNote, GetEffectOrigin(midiNote));
        _piano.Highlight(midiNote);
    }

    /// <summary>
    /// Selftest: bir tuşu GERÇEK tuş işleme yolundan geçirir (kanca kurulmadan).
    /// Nota üretildi ve efekt tetiklendiyse true döner.
    /// </summary>
    internal bool SelfTestFeedKey(int vkCode)
    {
        int spawnedBefore = _canvas.Engine.TotalSpawned;

        HandleKeyDown(vkCode);

        bool audible = _noteByKey.ContainsKey(vkCode)
                       && _canvas.Engine.TotalSpawned > spawnedBefore;

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
            _piano.NotePressed -= OnPianoNotePressed;
            _piano.NoteReleased -= OnPianoNoteReleased;
        }

        base.Dispose(disposing);
    }
}
