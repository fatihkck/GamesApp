namespace GamesApp.Audio;

/// <summary>
/// Ses sentezi yardımcıları. Tüm sentezleyiciler (davul, balon patlaması)
/// aynı örnekleme hızını ve aynı son işlem zincirini kullanır.
/// </summary>
internal static class SampleUtil
{
    /// <summary>Örnekleme hızı (Hz). Mikser ve tüm sentezleyiciler bu hızda çalışır.</summary>
    public const int SampleRate = 44100;

    /// <summary>
    /// Yumuşak kırpma (tanh) uygular, tepe değeri hedefe normalize eder ve
    /// 16 bit örneklere çevirir. Böylece her ses tok ve TAM SEVİYEDE çıkar.
    /// </summary>
    /// <param name="data">Ham örnekler (yaklaşık -1..1 aralığı beklenir).</param>
    /// <param name="targetPeak">Hedef tepe seviyesi (0-1).</param>
    /// <param name="drive">Kırpma öncesi sürüş miktarı; büyüdükçe ses doygunlaşır.</param>
    public static short[] Finalize(float[] data, float targetPeak, float drive = 1.6f)
    {
        // Yumuşak kırpma: transientler ezilmeden doygun bir ton verir.
        float softCeiling = (float)Math.Tanh(drive);
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (float)Math.Tanh(data[i] * drive) / softCeiling;
        }

        float peak = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float magnitude = Math.Abs(data[i]);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        float gain = peak > 0.0001f ? targetPeak / peak : 0f;

        var samples = new short[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            samples[i] = (short)Math.Clamp(data[i] * gain * 32767f, short.MinValue, short.MaxValue);
        }

        return samples;
    }
}
