namespace GamesApp.Audio;

/// <summary>
/// Davul sesi motoru: <see cref="DrumSoundSynth"/> ile sentezlenen örnekleri
/// paylaşılan <see cref="WaveMixer"/> üzerinden TAM SES seviyesinde çalar.
/// Mikserin sahibi bu sınıf DEĞİLDİR (Program açar ve kapatır); bu yüzden
/// <see cref="Dispose"/> yalnızca kendi önbelleğini bırakır.
/// </summary>
internal sealed class WaveDrumSound : IDrumSound
{
    /// <summary>
    /// Ana gürlük çarpanı. 1'in üzerindeki değerler tek vuruşu tam ölçeğin üstüne
    /// iter; mikserdeki kırpma (clamp) tepe noktaları törpüler. Davulda bu hafif
    /// doygunluk sert/punchy algılanır ve ses belirgin biçimde yükselir.
    /// </summary>
    private const float MasterGain = 1.4f;

    private readonly WaveMixer _mixer;

    /// <summary>GM notası -> sentezlenmiş örnek (bir kez üretilir, tekrar kullanılır).</summary>
    private readonly Dictionary<int, short[]> _samples = new();

    private readonly object _gate = new();

    private bool _disposed;

    public WaveDrumSound(WaveMixer mixer)
    {
        _mixer = mixer;

        // Sık kullanılan örnekler baştan sentezlenir ki ilk vuruşta takılma olmasın.
        int[] commonNotes = { 36, 38, 42, 48, 45, 41, 49, 51 };
        for (int i = 0; i < commonNotes.Length; i++)
        {
            _samples[commonNotes[i]] = DrumSoundSynth.Render(commonNotes[i]);
        }
    }

    public bool IsAvailable => _mixer.IsAvailable && !_disposed;

    /// <summary>Örnekler zaten tam gürlükte olduğundan aksan katmanı gerekmez.</summary>
    public bool NeedsAccentLayer => false;

    public void Hit(int gmDrumNote, int velocity)
    {
        if (!IsAvailable)
        {
            return;
        }

        int vel = Math.Clamp(velocity, 1, 127);

        // Velocity 0,75-1,0 arası kazanca eşlenir (her vuruş gür kalır ama tuşa
        // göre küçük canlılık farkı duyulur) ve ana gürlük çarpanıyla yükseltilir.
        float gain = (0.75f + 0.25f * (vel / 127f)) * MasterGain;

        short[] sample;
        lock (_gate)
        {
            if (!_samples.TryGetValue(gmDrumNote, out short[]? cached))
            {
                cached = DrumSoundSynth.Render(gmDrumNote);
                _samples[gmDrumNote] = cached;
            }

            sample = cached;
        }

        _mixer.Play(sample, gain);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _samples.Clear();
        }
    }
}
