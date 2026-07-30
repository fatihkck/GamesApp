namespace GamesApp.Audio;

/// <summary>
/// Davul seslerini kod içinde sentezler (44.1 kHz, 16 bit, mono). Ses dosyası
/// gerektirmez ve örnekler tam ölçeğe normalize edildiği için MIDI sentezleyiciden
/// ÇOK daha gür duyulur; ses seviyesi tamamen bizim kontrolümüzdedir.
///
/// Ses tasarımı (klasik analog davul sentezi yaklaşımı):
///  - Kick: frekansı hızla düşen sinüs (gümleme) + kısa tık transienti.
///  - Trampet: kısa gövde tonu + yoğun beyaz gürültü patlaması.
///  - Hi-hat: yüksek geçiren süzgeçten geçirilmiş çok kısa gürültü.
///  - Tomlar: perdesi kayarak düşen sinüs (her tom farklı perdede).
///  - Crash/Ride: uzun sönümlü süzgeçli gürültü + metalik çınlama tonları.
/// Tüm sesler yumuşak kırpma (tanh) ile tok ve dolgun hale getirilir.
/// </summary>
internal static class DrumSoundSynth
{
    /// <summary>Örnekleme hızı (Hz). Tek kaynak <see cref="SampleUtil.SampleRate"/>'dir.</summary>
    public const int SampleRate = SampleUtil.SampleRate;

    /// <summary>
    /// GM perküsyon notasına karşılık gelen sentez örneğini üretir.
    /// Tanınmayan notalar trampete düşer (asla sessiz kalınmaz).
    /// </summary>
    public static short[] Render(int gmDrumNote)
    {
        return gmDrumNote switch
        {
            35 or 36 => RenderKick(),
            42 or 44 => RenderHiHat(),
            48 or 50 => RenderTom(235f, 165f, 0.38f),
            45 or 47 => RenderTom(185f, 128f, 0.42f),
            41 or 43 => RenderTom(142f, 92f, 0.50f),
            49 or 57 => RenderCrash(),
            51 or 59 => RenderRide(),
            _ => RenderSnare()
        };
    }

    /// <summary>Kick: 110 Hz'den 44 Hz'e kayan sinüs + tık; kısa, tok ve GÜR.</summary>
    private static short[] RenderKick()
    {
        int length = (int)(SampleRate * 0.42f);
        var data = new float[length];
        var random = new Random(360);

        double phase = 0.0;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;

            // Perde süpürmesi: gümleme hissi frekansın hızla oturmasından gelir.
            double freq = 44.0 + 96.0 * Math.Exp(-t * 17.0);
            phase += 2.0 * Math.PI * freq / SampleRate;

            float body = (float)Math.Sin(phase) * (float)Math.Exp(-t * 7.5);

            // İlk 4 ms: bagetin deriye çarpma tıkı.
            float click = t < 0.004f
                ? ((float)random.NextDouble() * 2f - 1f) * (1f - t / 0.004f) * 0.5f
                : 0f;

            data[i] = body * 1.15f + click;
        }

        return Finalize(data, 0.98f);
    }

    /// <summary>Trampet: 190 Hz gövde + sert gürültü patlaması.</summary>
    private static short[] RenderSnare()
    {
        int length = (int)(SampleRate * 0.30f);
        var data = new float[length];
        var random = new Random(38);

        double phase = 0.0;
        float previousNoise = 0f;
        float highPassed = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;

            double freq = 165.0 + 60.0 * Math.Exp(-t * 40.0);
            phase += 2.0 * Math.PI * freq / SampleRate;
            float body = (float)Math.Sin(phase) * (float)Math.Exp(-t * 24.0) * 0.55f;

            float noise = (float)random.NextDouble() * 2f - 1f;
            highPassed = 0.86f * (highPassed + noise - previousNoise);
            previousNoise = noise;

            float snap = highPassed * (float)Math.Exp(-t * 15.0) * 0.95f;

            data[i] = body + snap;
        }

        return Finalize(data, 0.97f);
    }

    /// <summary>Hi-hat: çok kısa, tiz, süzgeçli gürültü ("tss").</summary>
    private static short[] RenderHiHat()
    {
        int length = (int)(SampleRate * 0.16f);
        var data = new float[length];
        var random = new Random(42);

        float previousNoise = 0f;
        float highPassed = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;

            float noise = (float)random.NextDouble() * 2f - 1f;

            // İki kademeli yüksek geçiren: yalnızca tiz "metal hışırtısı" kalır.
            highPassed = 0.94f * (highPassed + noise - previousNoise);
            previousNoise = noise;

            data[i] = highPassed * (float)Math.Exp(-t * 34.0);
        }

        return Finalize(data, 0.90f);
    }

    /// <summary>Tom: verilen perdeden aşağı kayan sinüs; dolgun "dum" sesi.</summary>
    private static short[] RenderTom(float startHz, float endHz, float seconds)
    {
        int length = (int)(SampleRate * seconds);
        var data = new float[length];
        var random = new Random((int)startHz);

        double phase = 0.0;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float progress = i / (float)length;

            double freq = endHz + (startHz - endHz) * Math.Exp(-progress * 5.0);
            phase += 2.0 * Math.PI * freq / SampleRate;

            float body = (float)Math.Sin(phase) * (float)Math.Exp(-t * 7.0);

            float attackNoise = t < 0.006f
                ? ((float)random.NextDouble() * 2f - 1f) * (1f - t / 0.006f) * 0.30f
                : 0f;

            data[i] = body * 1.05f + attackNoise;
        }

        return Finalize(data, 0.96f);
    }

    /// <summary>Crash: uzun sönümlü parlak gürültü + metalik parıltı ("çannn").</summary>
    private static short[] RenderCrash()
    {
        int length = (int)(SampleRate * 1.4f);
        var data = new float[length];
        var random = new Random(49);

        float previousNoise = 0f;
        float highPassed = 0f;
        double phase1 = 0.0, phase2 = 0.0;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;

            float noise = (float)random.NextDouble() * 2f - 1f;
            highPassed = 0.90f * (highPassed + noise - previousNoise);
            previousNoise = noise;

            float wash = highPassed * (float)Math.Exp(-t * 2.6f) * 0.85f;

            // Metalik kısmi tonlar: uyumsuz iki tiz sinüs, zil çınlaması verir.
            phase1 += 2.0 * Math.PI * 3121.0 / SampleRate;
            phase2 += 2.0 * Math.PI * 4732.0 / SampleRate;
            float shimmer = ((float)Math.Sin(phase1) + (float)Math.Sin(phase2))
                            * (float)Math.Exp(-t * 3.2f) * 0.10f;

            data[i] = wash + shimmer;
        }

        return Finalize(data, 0.95f);
    }

    /// <summary>Ride: çan tonlu, crash'ten kısa ve tıngırtılı ("ting").</summary>
    private static short[] RenderRide()
    {
        int length = (int)(SampleRate * 0.95f);
        var data = new float[length];
        var random = new Random(51);

        float previousNoise = 0f;
        float highPassed = 0f;
        double phase1 = 0.0, phase2 = 0.0;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;

            float noise = (float)random.NextDouble() * 2f - 1f;
            highPassed = 0.92f * (highPassed + noise - previousNoise);
            previousNoise = noise;

            float wash = highPassed * (float)Math.Exp(-t * 4.0f) * 0.45f;

            phase1 += 2.0 * Math.PI * 872.0 / SampleRate;
            phase2 += 2.0 * Math.PI * 1318.0 / SampleRate;
            float ping = (float)Math.Sin(phase1) * (float)Math.Exp(-t * 5.0f) * 0.35f
                         + (float)Math.Sin(phase2) * (float)Math.Exp(-t * 6.5f) * 0.20f;

            data[i] = wash + ping;
        }

        return Finalize(data, 0.93f);
    }

    /// <summary>Son işlem (yumuşak kırpma + normalize) ortak yardımcıya devredilir.</summary>
    private static short[] Finalize(float[] data, float targetPeak) =>
        SampleUtil.Finalize(data, targetPeak);
}
