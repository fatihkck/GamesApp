namespace GamesApp.Audio;

/// <summary>
/// Balon patlama ("pıt!") seslerini kod içinde sentezler. Ses dosyası gerekmez.
///
/// SES TASARIMI: Klasik çizgi film patlaması iki bileşenden oluşur:
///  1) çok kısa bir <b>tık</b> transienti (lastiğin yarılması),
///  2) frekansı hızla YUKARI doğru süpüren kısa bir rezonans ("pıt/blop" hissi).
/// Bazı varyantlara sonda küçük bir <b>parıltı</b> (yüksek tiz ping) eklenir; bu,
/// 1,5 yaş için "ödül" niteliği taşıyan neşeli bir bitiş verir.
///
/// ÇEŞİTLİLİK: 6 varyant üretilir ve her patlamada rastgele biri seçilir. Aynı ses
/// üst üste duyulmadığı için tekrar sıkıcı olmaz, komik etki korunur.
/// </summary>
internal static class PopSoundSynth
{
    /// <summary>Üretilen varyant sayısı.</summary>
    public const int VariantCount = 6;

    /// <summary>
    /// Varyant örneğini üretir. Varyant numarası aralık dışındaysa güvenle sarmalanır
    /// (sessiz kalma durumu yoktur).
    /// </summary>
    public static short[] Render(int variant)
    {
        int index = ((variant % VariantCount) + VariantCount) % VariantCount;

        return index switch
        {
            // baseHz, sweep, saniye, gürültü, parıltı Hz (0 = yok)
            0 => RenderPop(430f, 3.1f, 0.115f, 0.55f, 2600f),
            1 => RenderPop(320f, 3.6f, 0.135f, 0.45f, 0f),
            2 => RenderPop(560f, 2.6f, 0.100f, 0.60f, 3300f),
            3 => RenderPop(250f, 4.2f, 0.150f, 0.40f, 0f),
            4 => RenderPop(680f, 2.2f, 0.090f, 0.70f, 3900f),
            _ => RenderPop(380f, 3.4f, 0.125f, 0.50f, 2100f)
        };
    }

    /// <summary>
    /// Tek bir patlama sesi üretir.
    /// </summary>
    /// <param name="baseHz">Rezonansın başlangıç frekansı.</param>
    /// <param name="sweep">Frekansın kaç katına çıkacağı (yukarı süpürme oranı).</param>
    /// <param name="seconds">Toplam süre (kısa tutulur: 90-150 ms).</param>
    /// <param name="noiseAmount">Tık transientinin şiddeti (0-1).</param>
    /// <param name="sparkleHz">Bitişteki tiz parıltı frekansı; 0 ise parıltı yok.</param>
    private static short[] RenderPop(
        float baseHz,
        float sweep,
        float seconds,
        float noiseAmount,
        float sparkleHz)
    {
        int length = (int)(SampleUtil.SampleRate * seconds);
        var data = new float[length];

        // Tohum parametrelerden türetilir: aynı varyant her zaman aynı sesi verir.
        var random = new Random((int)(baseHz * 7f) + (int)(sweep * 100f));

        double phase = 0.0;
        double sparklePhase = 0.0;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleUtil.SampleRate;
            float progress = i / (float)length;

            // Frekans süpürmesi: ilk yarıda hızla tırmanır ("pııt" yükselen his).
            double freq = baseHz * (1.0 + (sweep - 1.0) * Math.Pow(progress, 0.55));
            phase += 2.0 * Math.PI * freq / SampleUtil.SampleRate;

            // Gövde: hızlı sönümlü sinüs; üçüncü harmonik hafif "plastik" tat katar.
            float envelope = (float)Math.Exp(-t * 26.0);
            float body = ((float)Math.Sin(phase) + 0.22f * (float)Math.Sin(phase * 3.0)) * envelope;

            // Tık: ilk 4 ms'de gürültü patlaması (lastiğin yarılma sesi).
            float click = 0f;
            if (t < 0.004f)
            {
                float clickEnvelope = 1f - t / 0.004f;
                click = ((float)random.NextDouble() * 2f - 1f) * clickEnvelope * noiseAmount;
            }

            // Parıltı: sonda beliren ve hemen sönen tiz ping (neşeli bitiş).
            float sparkle = 0f;
            if (sparkleHz > 0f && t > 0.020f)
            {
                sparklePhase += 2.0 * Math.PI * sparkleHz / SampleUtil.SampleRate;
                sparkle = (float)Math.Sin(sparklePhase) * (float)Math.Exp(-(t - 0.020f) * 34.0) * 0.28f;
            }

            data[i] = body + click + sparkle;
        }

        // Hafif sürüş: patlama tok ve "yakın" duyulur.
        return SampleUtil.Finalize(data, 0.97f, 1.9f);
    }
}
