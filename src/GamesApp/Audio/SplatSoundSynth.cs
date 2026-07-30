namespace GamesApp.Audio;

/// <summary>
/// Sihirli Fırça (Boyama) oyununun seslerini kod içinde sentezler. Ses dosyası gerekmez.
///
/// SES TASARIMI: Boya "şlop" sesi iki bileşenden oluşur:
///  1) frekansı hızla AŞAĞI süpüren yumuşak bir rezonans (ıslak "blop" hissi;
///     balonun yukarı süpüren "pıt"ının tersi — patlama değil, yapışma),
///  2) kısa bir sıçrama gürültüsü (boyanın etrafa saçılan damlacıkları).
/// Bazı varyantların sonunda minik bir "plip" (tiz damla) vardır: neşeli bitiş.
///
/// Ayrıca tablo TAMAMLANDIĞINDA çalan bir zafer fanfarı üretir: yukarı tırmanan
/// majör arpej + uzun çınlayan final akoru ("tablon bitti, aferin!" ödülü).
///
/// Tüm örnekler diğer oyunlarla dengeli gürlükte olması için tam ölçeğe normalize
/// edilir ve ortak <see cref="WaveMixer"/> üzerinden çalınır (tasarım kuralı 6).
/// </summary>
internal static class SplatSoundSynth
{
    /// <summary>Üretilen "şlop" varyantı sayısı.</summary>
    public const int VariantCount = 6;

    /// <summary>Örnekler bir kez üretilip saklanır (tuş başına yeniden sentez yapılmaz).</summary>
    private static readonly short[]?[] Cache = new short[VariantCount][];

    private static short[]? _fanfare;

    /// <summary>
    /// Varyantın mikser örneğini verir (önbellekten). Varyant numarası aralık
    /// dışındaysa güvenle sarmalanır; sessiz kalma durumu yoktur.
    /// </summary>
    public static short[] GetMixerSample(int variant)
    {
        int index = ((variant % VariantCount) + VariantCount) % VariantCount;
        return Cache[index] ??= Render(index);
    }

    /// <summary>Tablo tamamlanma fanfarının mikser örneğini verir (önbellekten).</summary>
    public static short[] GetFanfareSample() => _fanfare ??= RenderFanfare();

    /// <summary>Varyant örneğini üretir (selftest doğrudan bunu çağırır).</summary>
    public static short[] Render(int variant)
    {
        int index = ((variant % VariantCount) + VariantCount) % VariantCount;

        return index switch
        {
            // baseHz, düşüş oranı, saniye, sıçrama, plip Hz (0 = yok)
            0 => RenderSplat(520f, 0.28f, 0.20f, 0.55f, 2400f),
            1 => RenderSplat(380f, 0.32f, 0.24f, 0.45f, 0f),
            2 => RenderSplat(640f, 0.24f, 0.17f, 0.65f, 3100f),
            3 => RenderSplat(300f, 0.38f, 0.27f, 0.40f, 1900f),
            4 => RenderSplat(720f, 0.22f, 0.16f, 0.70f, 0f),
            _ => RenderSplat(450f, 0.30f, 0.22f, 0.50f, 2700f)
        };
    }

    /// <summary>
    /// Tek bir "şlop" sesi üretir.
    /// </summary>
    /// <param name="baseHz">Rezonansın başlangıç frekansı.</param>
    /// <param name="dropRatio">Frekansın süpürme sonunda ineceği oran (0-1).</param>
    /// <param name="seconds">Toplam süre (kısa tutulur: 160-270 ms).</param>
    /// <param name="splashAmount">Sıçrama gürültüsünün şiddeti (0-1).</param>
    /// <param name="plipHz">Bitişteki tiz damla frekansı; 0 ise damla yok.</param>
    private static short[] RenderSplat(
        float baseHz,
        float dropRatio,
        float seconds,
        float splashAmount,
        float plipHz)
    {
        int length = (int)(SampleUtil.SampleRate * seconds);
        var data = new float[length];

        // Tohum parametrelerden türetilir: aynı varyant her zaman aynı sesi verir.
        var random = new Random((int)(baseHz * 11f) + (int)(dropRatio * 1000f));

        double phase = 0.0;
        double plipPhase = 0.0;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleUtil.SampleRate;
            float progress = i / (float)length;

            // AŞAĞI süpürme: boya yüzeye "oturur" (yukarı süpüren pıt'ın tersi).
            double freq = baseHz * Math.Pow(dropRatio, Math.Pow(progress, 0.7));
            phase += 2.0 * Math.PI * freq / SampleUtil.SampleRate;

            // Gövde: yumuşak sönümlü sinüs; ikinci harmonik ıslak "dolgunluk" katar.
            float envelope = (float)Math.Exp(-t * 18.0);
            float body = ((float)Math.Sin(phase) + 0.30f * (float)Math.Sin(phase * 2.0)) * envelope;

            // Sıçrama: ilk 30 ms'de hızla sönen gürültü (damlacıkların saçılışı).
            float splash = 0f;
            if (t < 0.030f)
            {
                float splashEnvelope = (float)Math.Exp(-t * 140.0);
                splash = ((float)random.NextDouble() * 2f - 1f) * splashEnvelope * splashAmount;
            }

            // Plip: sona doğru beliren minik tiz damla (neşeli bitiş).
            float plip = 0f;
            if (plipHz > 0f && t > seconds * 0.55f)
            {
                plipPhase += 2.0 * Math.PI * plipHz / SampleUtil.SampleRate;
                plip = (float)Math.Sin(plipPhase)
                       * (float)Math.Exp(-(t - seconds * 0.55f) * 40.0) * 0.30f;
            }

            data[i] = body + splash + plip;
        }

        // Hafif sürüş: şlop tok ve "yakın" duyulur.
        return SampleUtil.Finalize(data, 0.95f, 1.8f);
    }

    /// <summary>
    /// Tablo tamamlanma fanfarı: yukarı tırmanan majör arpej (C6-E6-G6-C7) ve uzun
    /// çınlayan final akoru. Sıfırlama bir kayıp değil KUTLAMADIR; fanfar bunu söyler.
    /// </summary>
    private static short[] RenderFanfare()
    {
        // Arpej notaları ve final akoru (C majör: parlak, kutlayıcı).
        float[] arpeggio = { 1047f, 1319f, 1568f, 2093f };
        const float stepSeconds = 0.14f;
        const float ringSeconds = 1.1f;

        float chordStart = arpeggio.Length * stepSeconds;
        float total = chordStart + ringSeconds;

        var data = new float[(int)(SampleUtil.SampleRate * total)];

        // Arpej: her nota bir öncekinin üstüne çınlayarak biner.
        for (int k = 0; k < arpeggio.Length; k++)
        {
            WriteBell(data, arpeggio[k], k * stepSeconds, 0.6f, 0.85f);
        }

        // Final akoru: kök + majör üçlü + beşli birlikte, uzun kuyrukla.
        WriteBell(data, 1047f, chordStart, ringSeconds, 0.9f);
        WriteBell(data, 1319f, chordStart, ringSeconds, 0.7f);
        WriteBell(data, 1568f, chordStart, ringSeconds, 0.6f);

        return SampleUtil.Finalize(data, 0.95f, 1.5f);
    }

    /// <summary>Tek bir çan tonu yazar (temel + hafif detone üçüncü harmonik).</summary>
    private static void WriteBell(float[] data, float hz, float startSeconds, float ringSeconds, float gain)
    {
        int start = (int)(SampleUtil.SampleRate * startSeconds);
        int length = (int)(SampleUtil.SampleRate * ringSeconds);

        double phase = 0.0;
        for (int i = 0; i < length && start + i < data.Length; i++)
        {
            float t = i / (float)SampleUtil.SampleRate;
            phase += 2.0 * Math.PI * hz / SampleUtil.SampleRate;

            float bell = (float)Math.Sin(phase) + 0.20f * (float)Math.Sin(phase * 3.01);
            data[start + i] += bell * (float)Math.Exp(-t * 4.5) * gain;
        }
    }
}
