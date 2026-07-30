namespace GamesApp.Audio;

/// <summary>
/// "Cee-e!" oyununun komik ödül seslerini kod içinde sentezler. Ses dosyası gerekmez.
///
/// SES TASARIMI: Saklambaç oyununda karakterin perdeden fırlaması bir ŞAKADIR; sesin de
/// şaka gibi duyulması gerekir. Bu yüzden her varyant iki bölümden oluşur:
///  1) kısa, YUKARI süpüren bir "cee!" işareti (karakterin fırlama anı),
///  2) komik bir gövde: kıkırdama, kahkaha, alkış, zil parıltısı, parti borusu
///     veya kaydırmalı düdük.
///
/// ÇEŞİTLİLİK: 6 varyant vardır ve oyun bunları torba yöntemiyle sırayla kullanır;
/// aynı ses üst üste gelmez, "bu sefer hangi ses çıkacak?" merakı canlı kalır.
///
/// Tüm örnekler diğer oyunlarla dengeli gürlükte olması için tam ölçeğe normalize
/// edilir ve ortak <see cref="WaveMixer"/> üzerinden çalınır (tasarım kuralı 6).
/// </summary>
internal static class CheerSoundSynth
{
    /// <summary>Üretilen varyant sayısı.</summary>
    public const int VariantCount = 6;

    /// <summary>Örnekler bir kez üretilip saklanır (tuş başına yeniden sentez yapılmaz).</summary>
    private static readonly short[]?[] Cache = new short[VariantCount][];

    /// <summary>
    /// Varyantın mikser örneğini verir (önbellekten). Varyant numarası aralık
    /// dışındaysa güvenle sarmalanır; sessiz kalma durumu yoktur.
    /// </summary>
    public static short[] GetMixerSample(int variant)
    {
        int index = ((variant % VariantCount) + VariantCount) % VariantCount;
        return Cache[index] ??= Render(index);
    }

    /// <summary>Varyant örneğini üretir (selftest doğrudan bunu çağırır).</summary>
    public static short[] Render(int variant)
    {
        int index = ((variant % VariantCount) + VariantCount) % VariantCount;

        return index switch
        {
            0 => RenderGiggle(baseHz: 640f, pulses: 6, pulseSeconds: 0.085f, seed: 1234),
            1 => RenderApplause(seconds: 1.15f, seed: 4242),
            2 => RenderGiggle(baseHz: 400f, pulses: 4, pulseSeconds: 0.125f, seed: 777),
            3 => RenderTwinkle(),
            4 => RenderPartyHorn(),
            _ => RenderSlideWhistle()
        };
    }

    // ---------------- Ortak parçalar ----------------

    /// <summary>
    /// "Cee!" işareti: yukarı süpüren kısa bir düdük tonu. Karakterin perdeden
    /// fırladığı anda duyulur; komik gövde bunun hemen ardından gelir.
    /// </summary>
    /// <param name="data">Yazılacak tampon (baştan itibaren yazılır).</param>
    /// <param name="seconds">İşaretin süresi.</param>
    /// <param name="fromHz">Başlangıç frekansı.</param>
    /// <param name="toHz">Bitiş frekansı.</param>
    private static void WriteRiseCue(float[] data, float seconds, float fromHz, float toHz)
    {
        int length = Math.Min(data.Length, (int)(SampleUtil.SampleRate * seconds));
        double phase = 0.0;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleUtil.SampleRate;
            float progress = i / (float)length;

            // Frekans üstel biçimde tırmanır: kulakta doğal bir "cee-eee!" verir.
            double freq = fromHz * Math.Pow(toHz / fromHz, progress);
            phase += 2.0 * Math.PI * freq / SampleUtil.SampleRate;

            // Zarf: hızlı atak, sona doğru yumuşak bırakma.
            float attack = Math.Min(1f, t / 0.015f);
            float release = Math.Min(1f, (length - i) / (SampleUtil.SampleRate * 0.06f));

            data[i] += ((float)Math.Sin(phase) + 0.18f * (float)Math.Sin(phase * 2.0))
                       * attack * release * 0.85f;
        }
    }

    // ---------------- Varyantlar ----------------

    /// <summary>
    /// Kıkırdama/kahkaha: "cee!" işaretinin ardından pes perdeye doğru inen kısa
    /// "hi-hi-hi" darbeleri. Darbelerdeki hızlı vibrato ve ikinci harmonik, tonu
    /// düdükten çok insan sesine yaklaştırır.
    /// </summary>
    /// <param name="baseHz">Kıkırdamanın orta frekansı (düşükse kahkaha gibi duyulur).</param>
    /// <param name="pulses">Darbe ("hi") sayısı.</param>
    /// <param name="pulseSeconds">Tek darbenin süresi.</param>
    /// <param name="seed">Nefes gürültüsünün tohumu (varyant hep aynı duyulsun).</param>
    private static short[] RenderGiggle(float baseHz, int pulses, float pulseSeconds, int seed)
    {
        const float leadSeconds = 0.30f;
        float gapSeconds = pulseSeconds * 0.45f;
        float total = leadSeconds + pulses * (pulseSeconds + gapSeconds) + 0.12f;

        var data = new float[(int)(SampleUtil.SampleRate * total)];
        var random = new Random(seed);

        WriteRiseCue(data, leadSeconds * 0.85f, 380f, baseHz * 2.1f);

        for (int k = 0; k < pulses; k++)
        {
            // Her darbe bir öncekinden biraz daha pes: gülmenin "sönüşü".
            float pulseHz = baseHz * (1.14f - 0.07f * k);
            int start = (int)(SampleUtil.SampleRate * (leadSeconds + k * (pulseSeconds + gapSeconds)));
            int length = (int)(SampleUtil.SampleRate * pulseSeconds);

            double phase = 0.0;
            for (int j = 0; j < length && start + j < data.Length; j++)
            {
                float t = j / (float)SampleUtil.SampleRate;

                // Hızlı vibrato: düz sinüs "bip" gibi kalıyordu, titreşim gülüşe çevirir.
                double vibrato = 1.0 + 0.05 * Math.Sin(t * 2.0 * Math.PI * 24.0);
                phase += 2.0 * Math.PI * pulseHz * vibrato / SampleUtil.SampleRate;

                float attack = Math.Min(1f, t / 0.010f);
                float decay = (float)Math.Exp(-t * 14.0);

                float voice = (float)Math.Sin(phase) + 0.35f * (float)Math.Sin(phase * 2.0);
                float breath = ((float)random.NextDouble() * 2f - 1f) * 0.06f;

                data[start + j] += (voice + breath) * attack * decay;
            }
        }

        return SampleUtil.Finalize(data, 0.95f, 1.7f);
    }

    /// <summary>
    /// Alkış: "cee!" işaretinin ardından iki elin (iki bağımsız vuruş dizisinin)
    /// üst üste bindiği kısa gürültü patlamaları. Aralıklar ve şiddetler hafifçe
    /// rastgeledir; metronom gibi duyulmaz.
    /// </summary>
    private static short[] RenderApplause(float seconds, int seed)
    {
        const float leadSeconds = 0.26f;
        float total = leadSeconds + seconds + 0.1f;

        var data = new float[(int)(SampleUtil.SampleRate * total)];
        var random = new Random(seed);

        WriteRiseCue(data, leadSeconds * 0.85f, 420f, 1050f);

        // İki "el": ikinci dizi yarım vuruş kaymayla başlar, kalabalık hissi verir.
        for (int hand = 0; hand < 2; hand++)
        {
            float t = leadSeconds + hand * 0.045f;

            while (t < total - 0.08f)
            {
                int start = (int)(SampleUtil.SampleRate * t);
                float amplitude = 0.5f + (float)random.NextDouble() * 0.5f;
                int length = (int)(SampleUtil.SampleRate * 0.028f);

                for (int j = 0; j < length && start + j < data.Length; j++)
                {
                    float clap = ((float)random.NextDouble() * 2f - 1f);
                    float envelope = (float)Math.Exp(-j / (float)SampleUtil.SampleRate * 240.0);
                    data[start + j] += clap * envelope * amplitude;
                }

                t += 0.065f + (float)random.NextDouble() * 0.045f;
            }
        }

        return SampleUtil.Finalize(data, 0.95f, 1.6f);
    }

    /// <summary>
    /// Zil parıltısı: majör akor üzerinde yukarı tırmanan çan "ping"leri
    /// ("yaşasın!" hissi). Pingler üst üste biner ve uzun kuyrukla söner.
    /// </summary>
    private static short[] RenderTwinkle()
    {
        // G5 - B5 - D6 - G6 - B6: parlak majör tırmanış.
        float[] bells = { 784f, 988f, 1175f, 1568f, 1976f };
        const float leadSeconds = 0.22f;
        const float stepSeconds = 0.085f;
        const float ringSeconds = 0.55f;

        float total = leadSeconds + bells.Length * stepSeconds + ringSeconds;
        var data = new float[(int)(SampleUtil.SampleRate * total)];

        WriteRiseCue(data, leadSeconds * 0.85f, 500f, 1200f);

        for (int k = 0; k < bells.Length; k++)
        {
            int start = (int)(SampleUtil.SampleRate * (leadSeconds + k * stepSeconds));
            int length = (int)(SampleUtil.SampleRate * ringSeconds);

            double phase = 0.0;
            for (int j = 0; j < length && start + j < data.Length; j++)
            {
                float t = j / (float)SampleUtil.SampleRate;
                phase += 2.0 * Math.PI * bells[k] / SampleUtil.SampleRate;

                // Çan tınısı: temel ton + hafif detone edilmiş üçüncü harmonik.
                float bell = (float)Math.Sin(phase) + 0.20f * (float)Math.Sin(phase * 3.01);
                data[start + j] += bell * (float)Math.Exp(-t * 7.0) * 0.8f;
            }
        }

        return SampleUtil.Finalize(data, 0.95f, 1.5f);
    }

    /// <summary>
    /// Parti borusu: açılırken hafifçe tizleşen, vibratolu, testere dişine yakın
    /// vızıltı. Doğum günü borusunun komik "düt-düüüt" hissini verir.
    /// </summary>
    private static short[] RenderPartyHorn()
    {
        const float total = 0.78f;
        var data = new float[(int)(SampleUtil.SampleRate * total)];

        double phase = 0.0;
        double fifthPhase = 0.0;

        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleUtil.SampleRate;

            // Boru açılırken perde oturur (240 -> 305 Hz), sonra vibratoyla titrer.
            float settle = Math.Min(1f, t / 0.08f);
            double freq = (240.0 + 65.0 * settle) * (1.0 + 0.025 * Math.Sin(t * 2.0 * Math.PI * 7.0));

            phase += 2.0 * Math.PI * freq / SampleUtil.SampleRate;
            fifthPhase += 2.0 * Math.PI * freq * 1.5 / SampleUtil.SampleRate;

            // Testere dişine yakın tını: 1/n genlikli ilk beş harmonik.
            float buzz = 0f;
            for (int h = 1; h <= 5; h++)
            {
                buzz += (float)Math.Sin(phase * h) / h;
            }

            // Beşli aralıktaki ikinci ses "ördek kornası" komikliği katar.
            buzz += 0.22f * (float)Math.Sin(fifthPhase);

            float attack = Math.Min(1f, t / 0.02f);
            float release = t > total - 0.18f
                ? (float)Math.Exp(-(t - (total - 0.18f)) * 16.0)
                : 1f;

            data[i] = buzz * attack * release;
        }

        return SampleUtil.Finalize(data, 0.95f, 1.4f);
    }

    /// <summary>
    /// Kaydırmalı düdük: tepeye tırmanan, tepede titreyip aşağı kayan klasik çizgi
    /// film "boiiing" düdüğü. Bu varyantın "cee!" işareti kendisidir; ayrıca işaret
    /// yazılmaz.
    /// </summary>
    private static short[] RenderSlideWhistle()
    {
        const float upSeconds = 0.42f;
        const float holdSeconds = 0.12f;
        const float downSeconds = 0.22f;
        const float total = upSeconds + holdSeconds + downSeconds + 0.08f;

        var data = new float[(int)(SampleUtil.SampleRate * total)];
        double phase = 0.0;

        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleUtil.SampleRate;

            double freq;
            if (t < upSeconds)
            {
                // Yavaş başlayıp hızlanan tırmanış (kulağa "çekiliyor" gibi gelir).
                float p = t / upSeconds;
                freq = 380.0 * Math.Pow(1500.0 / 380.0, Math.Pow(p, 0.8));
            }
            else if (t < upSeconds + holdSeconds)
            {
                // Tepede vibrato: düdük tepede "titrer".
                freq = 1500.0 * (1.0 + 0.03 * Math.Sin((t - upSeconds) * 2.0 * Math.PI * 18.0));
            }
            else
            {
                float p = Math.Min(1f, (t - upSeconds - holdSeconds) / downSeconds);
                freq = 1500.0 * Math.Pow(820.0 / 1500.0, p);
            }

            phase += 2.0 * Math.PI * freq / SampleUtil.SampleRate;

            float attack = Math.Min(1f, t / 0.03f);
            float release = Math.Min(1f, (data.Length - i) / (SampleUtil.SampleRate * 0.12f));

            data[i] = ((float)Math.Sin(phase) + 0.20f * (float)Math.Sin(phase * 2.0))
                      * attack * release;
        }

        return SampleUtil.Finalize(data, 0.95f, 1.5f);
    }
}
