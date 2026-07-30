namespace GamesApp.Audio;

/// <summary>Hayvan sesi çalma sözleşmesi.</summary>
internal interface IAnimalSound : IDisposable
{
    /// <summary>
    /// Hayvanın sesini asenkron olarak çalar.
    /// Ses aygıtı yoksa veya çalma başarısızsa false döner (uygulama çökmez).
    /// </summary>
    /// <param name="kind">Çalınacak hayvan.</param>
    /// <param name="soundDurationMs">
    /// Çalınan sesin ölçülmüş süresi (ms). Hazır dosya çalındıysa gerçek süre,
    /// sentezlenen ses çalındıysa 0 döner (sentez için sabit sahne süresi kullanılır).
    /// </param>
    bool TryPlay(AnimalKind kind, out int soundDurationMs);

    /// <summary>Çalmakta olan hayvan sesini durdurur.</summary>
    void Stop();
}
