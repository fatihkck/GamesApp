namespace GamesApp.Audio;

/// <summary>Sentezde kullanılan temel dalga biçimleri.</summary>
internal enum WaveKind
{
    /// <summary>Sinüs: yumuşak, flüt benzeri.</summary>
    Sine = 0,

    /// <summary>Üçgen: yumuşak ama biraz daha dolgun (miyav, möö).</summary>
    Triangle = 1,

    /// <summary>Testere: sert ve pırtlak (hav, vak, virak).</summary>
    Saw = 2,

    /// <summary>Beyaz gürültü: hışırtı (havlamanın nefes bileşeni).</summary>
    Noise = 3
}

/// <summary>
/// Hayvan sesinin tek bir parçası (hece / patlama / cıvıltı).
/// Frekans başlangıçtan bitişe doğrusal olarak kayar; üzerine vibrato ve
/// genlik modülasyonu binebilir.
/// </summary>
internal sealed class VoiceSegment
{
    /// <summary>Segmentin başlangıç frekansı (Hz).</summary>
    public float StartFrequency { get; init; } = 440f;

    /// <summary>Segmentin bitiş frekansı (Hz).</summary>
    public float EndFrequency { get; init; } = 440f;

    /// <summary>Süre (ms).</summary>
    public int DurationMs { get; init; } = 200;

    /// <summary>Dalga biçimi.</summary>
    public WaveKind Wave { get; init; } = WaveKind.Triangle;

    /// <summary>Gürültü karışım oranı (0 = yok, 1 = tamamen gürültü).</summary>
    public float NoiseMix { get; init; }

    /// <summary>Vibrato hızı (Hz). 0 = vibrato yok.</summary>
    public float VibratoHz { get; init; }

    /// <summary>Vibrato derinliği (frekansın oranı olarak, ör. 0.03 = %3).</summary>
    public float VibratoDepth { get; init; }

    /// <summary>Genlik modülasyonu hızı (Hz). Kurbağanın "vırak" titremesi için.</summary>
    public float AmplitudeModulationHz { get; init; }

    /// <summary>Genlik modülasyonu derinliği (0-1).</summary>
    public float AmplitudeModulationDepth { get; init; }

    /// <summary>Yükseliş süresi (ms). Havlama için çok kısa olmalı.</summary>
    public float AttackMs { get; init; } = 20f;

    /// <summary>Sönüş süresi (ms).</summary>
    public float ReleaseMs { get; init; } = 60f;

    /// <summary>Segment kazancı (0-1).</summary>
    public float Gain { get; init; } = 1f;

    /// <summary>Segmentten sonra eklenecek sessizlik (ms).</summary>
    public int SilenceAfterMs { get; init; }
}

/// <summary>
/// Bir hayvan sesinin tam tarifi. Sesler kod içinde sentezlenir; gerçek hayvan
/// kaydı DEĞİLDİR, kasıtlı olarak karikatür/oyuncak sesi hedeflenmiştir.
/// </summary>
internal sealed class AnimalVoice
{
    private AnimalVoice(AnimalKind kind, params VoiceSegment[] segments)
    {
        Kind = kind;
        Segments = segments;
    }

    public AnimalKind Kind { get; }

    public IReadOnlyList<VoiceSegment> Segments { get; }

    /// <summary>Hayvanın sentez reçetesini döndürür.</summary>
    public static AnimalVoice CreateFor(AnimalKind kind) => kind switch
    {
        // KEDİ - "MİYAV": iki hece. Önce yükselen, sonra düşen üçgen dalga + hafif vibrato.
        AnimalKind.Cat => new AnimalVoice(
            kind,
            new VoiceSegment
            {
                StartFrequency = 700f,
                EndFrequency = 900f,
                DurationMs = 260,
                Wave = WaveKind.Triangle,
                VibratoHz = 6f,
                VibratoDepth = 0.02f,
                AttackMs = 45f,
                ReleaseMs = 40f,
                Gain = 0.85f
            },
            new VoiceSegment
            {
                StartFrequency = 900f,
                EndFrequency = 480f,
                DurationMs = 340,
                Wave = WaveKind.Triangle,
                VibratoHz = 6f,
                VibratoDepth = 0.03f,
                AttackMs = 15f,
                ReleaseMs = 160f,
                Gain = 1f
            }),

        // KÖPEK - "HAV HAV": iki kısa patlama, arada 90 ms sessizlik.
        // %55 gürültü + düşen testere, çok hızlı yükseliş.
        AnimalKind.Dog => new AnimalVoice(
            kind,
            CreateBark(silenceAfterMs: 90),
            CreateBark(silenceAfterMs: 0)),

        // İNEK - "MÖÖÖ": uzun, kalın, yavaş vibratolu.
        AnimalKind.Cow => new AnimalVoice(
            kind,
            new VoiceSegment
            {
                StartFrequency = 170f,
                EndFrequency = 130f,
                DurationMs = 1000,
                Wave = WaveKind.Triangle,
                VibratoHz = 4f,
                VibratoDepth = 0.02f,
                NoiseMix = 0.05f,
                AttackMs = 130f,
                ReleaseMs = 320f,
                Gain = 1f
            }),

        // KOYUN - "MEEE": 430 Hz taban + 11 Hz hızlı vibrato (meleme titremesi).
        AnimalKind.Sheep => new AnimalVoice(
            kind,
            new VoiceSegment
            {
                StartFrequency = 430f,
                EndFrequency = 400f,
                DurationMs = 700,
                Wave = WaveKind.Triangle,
                VibratoHz = 11f,
                VibratoDepth = 0.06f,
                NoiseMix = 0.1f,
                AttackMs = 60f,
                ReleaseMs = 220f,
                Gain = 0.95f
            }),

        // CİVCİV - "CİK CİK": üç kısa, tiz, hızlı yükselen cıvıltı.
        AnimalKind.Chick => new AnimalVoice(
            kind,
            CreateChirp(silenceAfterMs: 70),
            CreateChirp(silenceAfterMs: 70),
            CreateChirp(silenceAfterMs: 0)),

        // ÖRDEK - "VAK VAK": iki patlama, testere + %35 gürültü.
        AnimalKind.Duck => new AnimalVoice(
            kind,
            CreateQuack(silenceAfterMs: 110),
            CreateQuack(silenceAfterMs: 0)),

        // HOROZ - "Ü-ÜRÜ-ÜÜÜ": üç segment, 600 -> 900 -> 700 Hz.
        AnimalKind.Rooster => new AnimalVoice(
            kind,
            new VoiceSegment
            {
                StartFrequency = 600f,
                EndFrequency = 900f,
                DurationMs = 250,
                Wave = WaveKind.Saw,
                AttackMs = 25f,
                ReleaseMs = 30f,
                NoiseMix = 0.08f,
                Gain = 0.8f
            },
            new VoiceSegment
            {
                StartFrequency = 900f,
                EndFrequency = 880f,
                DurationMs = 330,
                Wave = WaveKind.Saw,
                VibratoHz = 7f,
                VibratoDepth = 0.03f,
                AttackMs = 15f,
                ReleaseMs = 40f,
                NoiseMix = 0.08f,
                Gain = 0.9f
            },
            new VoiceSegment
            {
                StartFrequency = 880f,
                EndFrequency = 700f,
                DurationMs = 320,
                Wave = WaveKind.Saw,
                VibratoHz = 5f,
                VibratoDepth = 0.04f,
                AttackMs = 15f,
                ReleaseMs = 180f,
                NoiseMix = 0.1f,
                Gain = 0.85f
            }),

        // KURBAĞA - "VIRAK": 180 Hz testere + güçlü 20 Hz genlik modülasyonu.
        AnimalKind.Frog => new AnimalVoice(
            kind,
            new VoiceSegment
            {
                StartFrequency = 180f,
                EndFrequency = 165f,
                DurationMs = 500,
                Wave = WaveKind.Saw,
                NoiseMix = 0.15f,
                AmplitudeModulationHz = 20f,
                AmplitudeModulationDepth = 0.8f,
                AttackMs = 20f,
                ReleaseMs = 110f,
                Gain = 1f
            }),

        // FİL - "FÜÜÜÜ": borazan. Hızla tırmanan tiz testere + tutulan tepe;
        // hafif gürültü bileşeni hortumdan geçen havayı taklit eder.
        AnimalKind.Elephant => new AnimalVoice(
            kind,
            new VoiceSegment
            {
                StartFrequency = 240f,
                EndFrequency = 620f,
                DurationMs = 260,
                Wave = WaveKind.Saw,
                NoiseMix = 0.12f,
                AttackMs = 25f,
                ReleaseMs = 20f,
                Gain = 0.9f
            },
            new VoiceSegment
            {
                StartFrequency = 620f,
                EndFrequency = 560f,
                DurationMs = 620,
                Wave = WaveKind.Saw,
                VibratoHz = 7f,
                VibratoDepth = 0.03f,
                NoiseMix = 0.14f,
                AttackMs = 10f,
                ReleaseMs = 240f,
                Gain = 1f
            }),

        // ASLAN - "ROAAAR": kalın kükreme. Düşen frekans + bol gürültü + 14 Hz
        // genlik titremesi (kükremenin "gırtlaktan gelen" dalgalanması).
        AnimalKind.Lion => new AnimalVoice(
            kind,
            new VoiceSegment
            {
                StartFrequency = 170f,
                EndFrequency = 130f,
                DurationMs = 280,
                Wave = WaveKind.Saw,
                NoiseMix = 0.30f,
                AttackMs = 30f,
                ReleaseMs = 30f,
                Gain = 0.85f
            },
            new VoiceSegment
            {
                StartFrequency = 130f,
                EndFrequency = 78f,
                DurationMs = 820,
                Wave = WaveKind.Saw,
                NoiseMix = 0.40f,
                VibratoHz = 3f,
                VibratoDepth = 0.03f,
                AmplitudeModulationHz = 14f,
                AmplitudeModulationDepth = 0.35f,
                AttackMs = 20f,
                ReleaseMs = 300f,
                Gain = 1f
            }),

        // MAYMUN - "U-U AAH": dört kısa tiz çığlık; ilk ikisi kısa "u u",
        // son ikisi yükselen "aah". Hızlı tempo maymunun telaşını verir.
        AnimalKind.Monkey => new AnimalVoice(
            kind,
            CreateScreech(900f, 1250f, 90, silenceAfterMs: 60),
            CreateScreech(950f, 1300f, 90, silenceAfterMs: 90),
            CreateScreech(700f, 1500f, 150, silenceAfterMs: 70),
            CreateScreech(1500f, 820f, 190, silenceAfterMs: 0)),

        // PENGUEN - "ORK ORK": iki kısa, boğuk anırma. Testere + %30 gürültü.
        AnimalKind.Penguin => new AnimalVoice(
            kind,
            CreateBray(silenceAfterMs: 120),
            CreateBray(silenceAfterMs: 0)),

        _ => new AnimalVoice(
            kind,
            new VoiceSegment { StartFrequency = 440f, EndFrequency = 440f, DurationMs = 200 })
    };

    private static VoiceSegment CreateBark(int silenceAfterMs) => new()
    {
        StartFrequency = 260f,
        EndFrequency = 150f,
        DurationMs = 110,
        Wave = WaveKind.Saw,
        NoiseMix = 0.55f,
        AttackMs = 3f,
        ReleaseMs = 65f,
        Gain = 1f,
        SilenceAfterMs = silenceAfterMs
    };

    private static VoiceSegment CreateChirp(int silenceAfterMs) => new()
    {
        StartFrequency = 2600f,
        EndFrequency = 4200f,
        DurationMs = 60,
        Wave = WaveKind.Sine,
        AttackMs = 5f,
        ReleaseMs = 30f,
        Gain = 0.9f,
        SilenceAfterMs = silenceAfterMs
    };

    /// <summary>Maymun çığlığı: tiz, kısa ve hızlı kayan tek hece.</summary>
    private static VoiceSegment CreateScreech(
        float startFrequency,
        float endFrequency,
        int durationMs,
        int silenceAfterMs) => new()
    {
        StartFrequency = startFrequency,
        EndFrequency = endFrequency,
        DurationMs = durationMs,
        Wave = WaveKind.Saw,
        NoiseMix = 0.18f,
        VibratoHz = 14f,
        VibratoDepth = 0.04f,
        AttackMs = 8f,
        ReleaseMs = 45f,
        Gain = 0.95f,
        SilenceAfterMs = silenceAfterMs
    };

    /// <summary>Penguen anırması: kısa, boğuk, düşen tek hece.</summary>
    private static VoiceSegment CreateBray(int silenceAfterMs) => new()
    {
        StartFrequency = 500f,
        EndFrequency = 300f,
        DurationMs = 170,
        Wave = WaveKind.Saw,
        NoiseMix = 0.30f,
        AmplitudeModulationHz = 32f,
        AmplitudeModulationDepth = 0.45f,
        AttackMs = 10f,
        ReleaseMs = 70f,
        Gain = 1f,
        SilenceAfterMs = silenceAfterMs
    };

    private static VoiceSegment CreateQuack(int silenceAfterMs) => new()
    {
        StartFrequency = 420f,
        EndFrequency = 300f,
        DurationMs = 140,
        Wave = WaveKind.Saw,
        NoiseMix = 0.35f,
        AttackMs = 8f,
        ReleaseMs = 75f,
        Gain = 1f,
        SilenceAfterMs = silenceAfterMs
    };
}
