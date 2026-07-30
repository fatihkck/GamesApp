using System.Runtime.InteropServices;
using GamesApp.Interop;

namespace GamesApp.Audio;

/// <summary>
/// Hayvan seslerini çalar. Üç yol vardır ve bu sırayla denenir:
///
/// 1. <b>Hazır ses dosyası (WAV)</b> - <c>Assets\Sounds</c> klasöründe adı eşleşen bir
///    <c>.wav</c> varsa <c>PlaySound</c> ile dosya yolundan asenkron çalınır.
/// 2. <b>Hazır ses dosyası (MP3/OGG/M4A/WMA/AAC)</b> - <c>PlaySound</c> yalnızca WAV
///    çaldığı için sıkıştırılmış biçimler <c>winmm</c> MCI komutlarıyla çalınır
///    (Pixabay ses efektleri ağırlıklı MP3 verdiği için bu şarttır).
/// 3. <b>Prosedürel sentez (yedek)</b> - dosya yoksa veya çalma başarısızsa,
///    <see cref="AnimalSoundSynth"/> tarafından üretilen karikatür ses bellekten çalınır.
///
/// NEDEN PlaySound / MCI (System.Media.SoundPlayer değil): winmm zaten MIDI için projede
/// kullanılıyor, ek assembly bağımlılığı doğmuyor, asenkron çalma UI'ı bloklamıyor ve
/// MIDI piyano ile aynı anda sorunsuz çalışıyor (farklı çıkış yolları).
///
/// KRİTİK BELLEK NOTU (sentez yolu): <c>SND_ASYNC</c> ile çalma, çağrı döndükten sonra da
/// sürer; winmm tamponu okumaya devam eder. Bu yüzden WAV byte dizileri kalıcı olarak
/// önbelleğe alınır VE <see cref="GCHandle"/> ile pinlenir. Geçici/taşınabilir bir dizi
/// verilseydi çöp toplayıcı belleği taşır ve gürültü duyulurdu.
/// </summary>
internal sealed class AnimalSoundPlayer : IAnimalSound
{
    /// <summary>MCI oturumuna verilen takma ad.</summary>
    private const string MciAlias = "piyanoAnimal";

    private readonly Dictionary<AnimalKind, GCHandle> _pinned = new();
    private readonly object _gate = new();
    private readonly Random _random = new();
    private readonly AnimalSoundLibrary _library;

    private bool _mciOpen;
    private bool _disposed;

    public AnimalSoundPlayer(AnimalSoundLibrary library)
    {
        _library = library;
    }

    /// <summary>Ses dosyası kütüphanesi (teşhis ve selftest için).</summary>
    public AnimalSoundLibrary Library => _library;

    public bool TryPlay(AnimalKind kind, out int soundDurationMs)
    {
        lock (_gate)
        {
            soundDurationMs = 0;

            if (_disposed)
            {
                return false;
            }

            // 1-2) Hazır dosya varsa onu çal (açılışta doğrulanmış, süresi ölçülmüş).
            SoundFileInfo? file = _library.PickFile(kind, _random);
            if (file != null)
            {
                if (PlayFile(kind, file.Path))
                {
                    soundDurationMs = file.DurationMs;
                    return true;
                }

                // Dosya çalınamadı: sessizce sentezlenene düş.
            }

            // 3) Yedek: prosedürel sentez (süre 0 => sabit sahne süresi kullanılır).
            return PlaySynth(kind);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopUnsafe();
        }
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

            // Önce çalmayı durdur ve MCI oturumunu kapat, SONRA pinleri serbest bırak.
            // Ters sırada yapılırsa winmm serbest kalmış belleği okumaya çalışabilir.
            StopUnsafe();

            foreach (KeyValuePair<AnimalKind, GCHandle> entry in _pinned)
            {
                if (entry.Value.IsAllocated)
                {
                    entry.Value.Free();
                }
            }

            _pinned.Clear();
        }
    }

    /// <summary>Kilit ALINMIŞ hâlde çalmayı durdurur ve MCI takma adını kapatır.</summary>
    private void StopUnsafe()
    {
        NativeMethods.PlaySound(IntPtr.Zero, IntPtr.Zero, NativeMethods.SND_PURGE);
        CloseMciUnsafe();
    }

    /// <summary>Hazır dosyayı uzantısına göre uygun yolla çalar. Kilit ALINMIŞ olmalıdır.</summary>
    private bool PlayFile(AnimalKind kind, string path)
    {
        string extension = Path.GetExtension(path);

        if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            // Önceki sesi kes, WAV'ı dosya yolundan asenkron çal.
            StopUnsafe();

            if (NativeMethods.PlaySound(path, IntPtr.Zero, NativeMethods.SND_FILE_ASYNC))
            {
                return true;
            }

            _library.AppendDiagnostic(
                $"hata -> {AnimalInfo.GetDisplayName(kind)}: PlaySound basarisiz, dosya: {Path.GetFileName(path)}");
            return false;
        }

        return PlayViaMci(kind, path);
    }

    /// <summary>
    /// Sıkıştırılmış ses dosyasını MCI ile çalar.
    /// Dosya yolu boşluk içerebileceği için MUTLAKA çift tırnak içinde verilir.
    /// Kilit ALINMIŞ olmalıdır.
    /// </summary>
    private bool PlayViaMci(AnimalKind kind, string path)
    {
        // Aynı anda tek hayvan sesi: varsa eski oturumu kapat.
        NativeMethods.PlaySound(IntPtr.Zero, IntPtr.Zero, NativeMethods.SND_PURGE);
        CloseMciUnsafe();

        int result = NativeMethods.mciSendString(
            $"open \"{path}\" alias {MciAlias}",
            null,
            0,
            IntPtr.Zero);

        if (result != 0)
        {
            // Uzantıdan tanımadıysa MPEG çözücüyü açıkça belirterek tekrar dene.
            result = NativeMethods.mciSendString(
                $"open \"{path}\" type mpegvideo alias {MciAlias}",
                null,
                0,
                IntPtr.Zero);
        }

        if (result != 0)
        {
            _library.AppendDiagnostic(
                $"hata -> {AnimalInfo.GetDisplayName(kind)}: MCI open hata kodu {result}, dosya: {Path.GetFileName(path)} (sentez kullanilacak)");
            return false;
        }

        _mciOpen = true;

        result = NativeMethods.mciSendString($"play {MciAlias} from 0", null, 0, IntPtr.Zero);
        if (result != 0)
        {
            _library.AppendDiagnostic(
                $"hata -> {AnimalInfo.GetDisplayName(kind)}: MCI play hata kodu {result}, dosya: {Path.GetFileName(path)} (sentez kullanilacak)");
            CloseMciUnsafe();
            return false;
        }

        return true;
    }

    /// <summary>Sentezlenen sesi pinlenmiş bellekten çalar. Kilit ALINMIŞ olmalıdır.</summary>
    private bool PlaySynth(AnimalKind kind)
    {
        IntPtr address = GetPinnedAddress(kind);
        if (address == IntPtr.Zero)
        {
            return false;
        }

        StopUnsafe();
        return NativeMethods.PlaySound(address, IntPtr.Zero, NativeMethods.SND_MEMORY_ASYNC);
    }

    /// <summary>MCI takma adını kapatır (idempotent). Kilit ALINMIŞ olmalıdır.</summary>
    private void CloseMciUnsafe()
    {
        if (!_mciOpen)
        {
            return;
        }

        _mciOpen = false;
        NativeMethods.mciSendString($"stop {MciAlias}", null, 0, IntPtr.Zero);
        NativeMethods.mciSendString($"close {MciAlias}", null, 0, IntPtr.Zero);
    }

    /// <summary>
    /// Sentezlenen WAV verisini pinler ve sabit adresini döndürür.
    /// Kilit ALINMIŞ olmalıdır.
    /// </summary>
    private IntPtr GetPinnedAddress(AnimalKind kind)
    {
        if (_pinned.TryGetValue(kind, out GCHandle existing) && existing.IsAllocated)
        {
            return existing.AddrOfPinnedObject();
        }

        byte[] wav = AnimalSoundSynth.GetWav(kind);
        if (wav.Length == 0)
        {
            return IntPtr.Zero;
        }

        // Kopya alınır: sentez önbelleğindeki dizi pinlenip kilitlenmesin.
        var buffer = new byte[wav.Length];
        Array.Copy(wav, buffer, wav.Length);

        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        _pinned[kind] = handle;
        return handle.AddrOfPinnedObject();
    }
}
