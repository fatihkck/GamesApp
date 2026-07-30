namespace GamesApp.Audio;

/// <summary>
/// Havai Fişek oyununun seslerini kod içinde sentezler. Ses dosyası gerekmez.
///
/// SES TASARIMI: Gerçek bir havai fişek İKİ AYRI SES anıdır ve oyun bunu aynen taklit
/// eder:
///  1) <b>Fırlama</b> (tuşa basıldığı anda): tıslayan gürültü + yukarı süpüren ince
///     ıslık ("vıiiiii..."). Anında çalar; çocuk tuş ile roket arasındaki bağı kurar.
///  2) <b>Patlama</b> (roket tepeye ulaştığında): göğsü titreten pes bir "GÜM" +
///     ardından seyrek çıtırtılar (kıvılcımların sönüşü). Görsel patlamayla aynı
///     karede çalınır.
///
/// ÇEŞİTLİLİK: Fırlama 3, patlama 4 varyanttır; patlamalar torbayla sırayla kullanılır.
///
/// Tüm örnekler diğer oyunlarla dengeli gürlükte olması için tam ölçeğe normalize
/// edilir ve ortak <see cref="WaveMixer"/> üzerinden çalınır (tasarım kuralı 6).
/// </summary>
internal static class FireworkSoundSynth
{
    /// <summary>Fırlama sesi varyant sayısı.</summary>
    public const int LaunchVariantCount = 3;

    /// <summary>Patlama sesi varyant sayısı.</summary>
    public const int BoomVariantCount = 4;

    private static readonly short[]?[] LaunchCache = new short[LaunchVariantCount][];
    private static readonly short[]?[] BoomCache = new short[BoomVariantCount][];

    /// <summary>Fırlama örneğini verir (önbellekten; numara güvenle sarmalanır).</summary>
    public static short[] GetLaunchSample(int variant)
    {
        int index = ((variant % LaunchVariantCount) + LaunchVariantCount) % LaunchVariantCount;
        return LaunchCache[index] ??= RenderLaunch(index);
    }

    /// <summary>Patlama örneğini verir (önbellekten; numara güvenle sarmalanır).</summary>
    public static short[] GetBoomSample(int variant)
    {
        int index = ((variant % BoomVariantCount) + BoomVariantCount) % BoomVariantCount;
        return BoomCache[index] ??= RenderBoom(index);
    }

    /// <summary>Fırlama örneğini üretir (selftest doğrudan bunu çağırır).</summary>
    public static short[] RenderLaunch(int variant)
    {
        int index = ((variant % LaunchVariantCount) + LaunchVariantCount) % LaunchVariantCount;

        return index switch
        {
            // fromHz, toHz, saniye
            0 => RenderWhoosh(650f, 1500f, 0.70f),
            1 => RenderWhoosh(520f, 1250f, 0.80f),
            _ => RenderWhoosh(760f, 1750f, 0.60f)
        };
    }

    /// <summary>Patlama örneğini üretir (selftest doğrudan bunu çağırır).</summary>
    public static short[] RenderBoom(int variant)
    {
        int index = ((variant % BoomVariantCount) + BoomVariantCount) % BoomVariantCount;

        return index switch
        {
            // thumpHz, saniye, çıtırtı yoğunluğu
            0 => RenderBoomCore(170f, 1.20f, 0.9f),
            1 => RenderBoomCore(140f, 1.40f, 0.6f),
            2 => RenderBoomCore(200f, 1.00f, 1.2f),
            _ => RenderBoomCore(155f, 1.30f, 0.8f)
        };
    }

    /// <summary>
    /// Fırlama: tıslayan gürültü + yukarı süpüren ıslık. Gürültü roketin ateşidir,
    /// ıslık yükselişidir; ikisi birlikte "vıiiii" hissini verir.
    /// </summary>
    private static short[] RenderWhoosh(float fromHz, float toHz, float seconds)
    {
        int length = (int)(SampleUtil.SampleRate * seconds);
        var data = new float[length];

        var random = new Random((int)(fromHz * 13f));
        double phase = 0.0;
        float hiss = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleUtil.SampleRate;
            float progress = i / (float)length;

            // Islık: yükseldikçe tizleşir, hafif vibratoyla canlanır.
            double freq = fromHz * Math.Pow(toHz / fromHz, progress)
                          * (1.0 + 0.015 * Math.Sin(t * 2.0 * Math.PI * 9.0));
            phase += 2.0 * Math.PI * freq / SampleUtil.SampleRate;

            // Tıs: alçak geçiren süzgeçten geçmiş gürültü (sert cızırtı olmasın).
            float noise = (float)random.NextDouble() * 2f - 1f;
            hiss += (noise - hiss) * 0.25f;

            // Zarf: hızla yükselir, roket uzaklaştıkça söner.
            float envelope = (float)(Math.Sin(Math.Min(1f, progress * 3f) * Math.PI * 0.5)
                                     * Math.Pow(1f - progress, 0.6));

            data[i] = ((float)Math.Sin(phase) * 0.55f + hiss * 0.75f) * envelope;
        }

        return SampleUtil.Finalize(data, 0.90f, 1.5f);
    }

    /// <summary>
    /// Patlama: pes "GÜM" (aşağı süpüren kalın ton + gümbürtü gürültüsü) ve ardında
    /// seyrekleşen çıtırtı kuyruğu (kıvılcımların tek tek sönüşü).
    /// </summary>
    private static short[] RenderBoomCore(float thumpHz, float seconds, float crackleAmount)
    {
        int length = (int)(SampleUtil.SampleRate * seconds);
        var data = new float[length];

        var random = new Random((int)(thumpHz * 17f));
        double phase = 0.0;
        float rumble = 0f;

        // Çıtırtı anları önceden serpilir: patlamadan sonra başlar, gitgide seyrekleşir.
        int crackleCount = (int)(26 * crackleAmount);
        var crackles = new int[crackleCount];
        for (int k = 0; k < crackleCount; k++)
        {
            // Kareli dağılım: çıtırtılar başta sık, sona doğru seyrek düşer.
            float where = 0.12f + 0.85f * (float)Math.Pow(random.NextDouble(), 1.6);
            crackles[k] = (int)(length * where);
        }

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleUtil.SampleRate;

            // GÜM: frekansı hızla aşağı süpüren kalın ton (göğüs titreten kısım).
            double freq = thumpHz * Math.Pow(0.25, Math.Min(1f, t / 0.35f));
            phase += 2.0 * Math.PI * freq / SampleUtil.SampleRate;
            float thump = (float)Math.Sin(phase) * (float)Math.Exp(-t * 7.0);

            // Gümbürtü: alçak geçiren süzgeçli gürültü, GÜM ile birlikte söner.
            float noise = (float)random.NextDouble() * 2f - 1f;
            rumble += (noise - rumble) * 0.08f;
            float body = rumble * 2.2f * (float)Math.Exp(-t * 5.0);

            data[i] = thump * 1.1f + body;
        }

        // Çıtırtılar: her biri 8 ms'lik minik bir gürültü patlaması.
        int crackleLength = (int)(SampleUtil.SampleRate * 0.008f);
        for (int k = 0; k < crackleCount; k++)
        {
            float amplitude = 0.25f + (float)random.NextDouble() * 0.45f;
            for (int j = 0; j < crackleLength && crackles[k] + j < length; j++)
            {
                float pop = (float)random.NextDouble() * 2f - 1f;
                data[crackles[k] + j] += pop * amplitude * (1f - j / (float)crackleLength);
            }
        }

        return SampleUtil.Finalize(data, 0.95f, 1.9f);
    }
}
