using System.Text;

namespace GamesApp.Audio;

/// <summary>
/// Hayvan seslerini prosedürel olarak sentezler ve bellekte 16-bit PCM WAV
/// (22050 Hz, mono) byte dizisi üretir. Harici ses dosyası veya NuGet paketi gerekmez.
///
/// Üretilen sesler GERÇEK HAYVAN KAYDI DEĞİLDİR; kasıtlı olarak karikatür /
/// oyuncak sesi karakterindedir.
///
/// İKİ ÇIKIŞ YOLU VARDIR:
///  - <see cref="GetWav"/>: 22050 Hz WAV baytları. <c>PlaySound</c> ile bellekten çalınır
///    (piyano/davul oyunlarındaki hayvan sürprizinin yedek yolu).
///  - <see cref="GetMixerSample"/>: <see cref="SampleUtil.SampleRate"/> hızında 16-bit PCM
///    örnek dizisi. Ortak <see cref="WaveMixer"/> üzerinden ÇOK SESLİ ve TAM SEVİYEDE
///    çalınır (Hayvanat Bahçesi oyunu bunu kullanır; tasarım kuralı 6).
/// </summary>
internal static class AnimalSoundSynth
{
    /// <summary>WAV çıkışının örnekleme hızı (Hz).</summary>
    public const int SampleRate = 22050;

    /// <summary>Klik sesini önlemek için baştaki/sondaki yumuşatma süresi (ms).</summary>
    private const float EdgeFadeMs = 5f;

    /// <summary>Normalizasyon hedefi (0-1). 1.0 kırpma riski taşır.</summary>
    private const float PeakTarget = 0.9f;

    /// <summary>Mikser örneklerinin hedef tepe seviyesi (davul/balonla dengeli olsun).</summary>
    private const float MixerPeakTarget = 0.95f;

    /// <summary>
    /// Mikser örneklerine uygulanan sürüş. Davulda 1.9 kullanılıyor; hayvan sesleri
    /// ton ağırlıklı olduğu için daha düşük tutulur: gür olur ama boğuklaşmaz.
    /// </summary>
    private const float MixerDrive = 1.25f;

    private static readonly Dictionary<AnimalKind, byte[]> Cache = new();
    private static readonly Dictionary<AnimalKind, short[]> MixerCache = new();
    private static readonly object Gate = new();

    /// <summary>
    /// Hayvanın WAV verisini döndürür. İlk çağrıda sentezlenir, sonrasında
    /// önbellekten verilir. Dönen dizi ASLA değiştirilmemelidir.
    /// </summary>
    public static byte[] GetWav(AnimalKind kind)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(kind, out byte[]? cached))
            {
                return cached;
            }

            byte[] wav = Render(AnimalVoice.CreateFor(kind));
            Cache[kind] = wav;
            return wav;
        }
    }

    /// <summary>
    /// Hayvanın ortak <see cref="WaveMixer"/> ile çalınacak PCM örneğini döndürür
    /// (<see cref="SampleUtil.SampleRate"/> hızında, tam ölçeğe normalize).
    /// İlk çağrıda sentezlenir, sonrasında önbellekten verilir.
    /// Dönen dizi ASLA değiştirilmemelidir (mikser bunu doğrudan okur).
    /// </summary>
    public static short[] GetMixerSample(AnimalKind kind)
    {
        lock (Gate)
        {
            if (MixerCache.TryGetValue(kind, out short[]? cached))
            {
                return cached;
            }

            short[] sample = RenderMixerSample(AnimalVoice.CreateFor(kind));
            MixerCache[kind] = sample;
            return sample;
        }
    }

    /// <summary>
    /// Bir sesi mikser hızında sentezler ve tam ölçeğe getirir (önbelleğe bakmadan).
    /// Selftest bunu doğrudan çağırır.
    /// </summary>
    public static short[] RenderMixerSample(AnimalVoice voice)
    {
        float[] samples = RenderSamples(voice, SampleUtil.SampleRate);
        ApplyEdgeFade(samples, SampleUtil.SampleRate);
        return SampleUtil.Finalize(samples, MixerPeakTarget, MixerDrive);
    }

    /// <summary>Bir sesi baştan sona sentezler (önbelleğe bakmadan).</summary>
    public static byte[] Render(AnimalVoice voice)
    {
        float[] samples = RenderSamples(voice, SampleRate);
        Normalize(samples);
        ApplyEdgeFade(samples, SampleRate);
        return BuildWav(samples);
    }

    /// <summary>
    /// Sesin ham örneklerini (yaklaşık -1..1) verilen örnekleme hızında üretir.
    /// Normalizasyon ve son işlem çağırana bırakılır: WAV yolu ile mikser yolu
    /// farklı gürlük hedefleri kullanır.
    /// </summary>
    private static float[] RenderSamples(AnimalVoice voice, int sampleRate)
    {
        // Gürültü bileşeni deterministik olsun ki her çalışmada aynı ses duyulsun.
        var noise = new Random(20260729);

        int totalSamples = 0;
        for (int i = 0; i < voice.Segments.Count; i++)
        {
            VoiceSegment segment = voice.Segments[i];
            totalSamples += MsToSamples(segment.DurationMs, sampleRate) +
                            MsToSamples(segment.SilenceAfterMs, sampleRate);
        }

        if (totalSamples <= 0)
        {
            totalSamples = MsToSamples(100, sampleRate);
        }

        var samples = new float[totalSamples];
        int writeIndex = 0;

        for (int s = 0; s < voice.Segments.Count; s++)
        {
            VoiceSegment segment = voice.Segments[s];
            int length = MsToSamples(segment.DurationMs, sampleRate);
            double phase = 0.0;

            for (int i = 0; i < length && writeIndex < samples.Length; i++, writeIndex++)
            {
                float progress = length <= 1 ? 0f : i / (float)(length - 1);
                float timeSeconds = i / (float)sampleRate;

                // Frekans kayması + vibrato
                float frequency = segment.StartFrequency +
                                  (segment.EndFrequency - segment.StartFrequency) * progress;

                if (segment.VibratoHz > 0f && segment.VibratoDepth > 0f)
                {
                    double vibrato = Math.Sin(2.0 * Math.PI * segment.VibratoHz * timeSeconds);
                    frequency *= 1f + segment.VibratoDepth * (float)vibrato;
                }

                // Faz biriktirme: frekans değişse bile dalga sürekliliği bozulmaz.
                phase += 2.0 * Math.PI * frequency / sampleRate;
                if (phase > 2.0 * Math.PI)
                {
                    phase -= 2.0 * Math.PI;
                }

                float tone = Oscillate(segment.Wave, phase, noise);

                if (segment.NoiseMix > 0f)
                {
                    float rawNoise = (float)(noise.NextDouble() * 2.0 - 1.0);
                    tone = tone * (1f - segment.NoiseMix) + rawNoise * segment.NoiseMix;
                }

                // Zarf: yükseliş / sönüş
                float elapsedMs = i * 1000f / sampleRate;
                float remainingMs = (length - i) * 1000f / sampleRate;
                float envelope = 1f;

                if (segment.AttackMs > 0f)
                {
                    envelope *= Math.Min(1f, elapsedMs / segment.AttackMs);
                }

                if (segment.ReleaseMs > 0f)
                {
                    envelope *= Math.Min(1f, remainingMs / segment.ReleaseMs);
                }

                // Genlik modülasyonu (kurbağanın titremesi)
                if (segment.AmplitudeModulationHz > 0f && segment.AmplitudeModulationDepth > 0f)
                {
                    double am = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * segment.AmplitudeModulationHz * timeSeconds);
                    envelope *= 1f - segment.AmplitudeModulationDepth + segment.AmplitudeModulationDepth * (float)am;
                }

                samples[writeIndex] = tone * envelope * segment.Gain;
            }

            // Segment sonrası sessizlik (dizi zaten 0 ile dolu)
            writeIndex += MsToSamples(segment.SilenceAfterMs, sampleRate);
            if (writeIndex > samples.Length)
            {
                writeIndex = samples.Length;
            }
        }

        return samples;
    }

    /// <summary>Verilen dalga biçiminden anlık örnek üretir.</summary>
    private static float Oscillate(WaveKind wave, double phase, Random noise)
    {
        switch (wave)
        {
            case WaveKind.Sine:
                return (float)Math.Sin(phase);

            case WaveKind.Triangle:
                // Üçgen: sinüsün arcsin'i ile yumuşak üçgen elde edilir.
                return (float)(2.0 / Math.PI * Math.Asin(Math.Sin(phase)));

            case WaveKind.Saw:
                return (float)(2.0 * (phase / (2.0 * Math.PI)) - 1.0);

            case WaveKind.Noise:
                return (float)(noise.NextDouble() * 2.0 - 1.0);

            default:
                return 0f;
        }
    }

    /// <summary>Tepe değeri hedefe çeker (kırpma / clipping olmaması için).</summary>
    private static void Normalize(float[] samples)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Math.Abs(samples[i]);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        if (peak <= 0.0001f)
        {
            return;
        }

        float scale = PeakTarget / peak;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Math.Clamp(samples[i] * scale, -1f, 1f);
        }
    }

    /// <summary>Baş ve sona 5 ms yumuşatma uygular (klik sesini engeller).</summary>
    private static void ApplyEdgeFade(float[] samples, int sampleRate)
    {
        int fade = MsToSamples((int)EdgeFadeMs, sampleRate);
        if (fade <= 1 || samples.Length < fade * 2)
        {
            return;
        }

        for (int i = 0; i < fade; i++)
        {
            float factor = i / (float)fade;
            samples[i] *= factor;
            samples[samples.Length - 1 - i] *= factor;
        }
    }

    /// <summary>float örnekleri 44 baytlık RIFF başlığı ile 16-bit PCM WAV'a çevirir.</summary>
    private static byte[] BuildWav(float[] samples)
    {
        int dataBytes = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // RIFF başlığı
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt bölümü
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                            // bölüm boyutu
        writer.Write((short)1);                      // PCM
        writer.Write((short)1);                      // mono
        writer.Write(SampleRate);                    // örnekleme hızı
        writer.Write(SampleRate * 2);                // bayt/saniye
        writer.Write((short)2);                      // blok hizalaması
        writer.Write((short)16);                     // bit/örnek

        // data bölümü
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            writer.Write(value);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static int MsToSamples(int milliseconds, int sampleRate) =>
        milliseconds <= 0 ? 0 : (int)(milliseconds * (long)sampleRate / 1000);
}
