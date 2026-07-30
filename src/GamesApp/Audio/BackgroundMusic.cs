using System.Text;
using GamesApp.Interop;

namespace GamesApp.Audio;

/// <summary>
/// Arka plan müziği: <c>Assets\Music</c> klasöründeki bir ses dosyasını MCI ile
/// KISIK sesle çalar ve <b>çal/sus döngüsü</b> uygular.
///
/// NEDEN DÖNGÜ: 1,5 yaş tasarım kuralı gereği sürekli çalan müzik çocuğun kendi
/// eylemlerine ait sesleri (patlama, hayvan) bastırır ve ilgisini dağıtır. Bu yüzden
/// müzik <see cref="PlaySeconds"/> kadar çalar, <see cref="RestSeconds"/> kadar susar
/// ve kaldığı yerden devam eder. Sessizlik aralığında oyunun kendi sesleri öne çıkar.
///
/// SES SEVİYESİ: MCI <c>setaudio ... volume</c> komutu 0-1000 ölçeğinde çalışır;
/// müzik <see cref="MusicVolume"/> ile fısıltı seviyesine indirilir. Komut
/// desteklenmezse müzik hiç çalınmaz (kısılamayan müzik, kısık müzikten kötüdür).
///
/// Dosya yoksa veya MCI açamazsa sessizce devre dışı kalır; uygulama ÇÖKMEZ.
/// </summary>
internal sealed class BackgroundMusic : IDisposable
{
    /// <summary>MCI oturumuna verilen takma ad (hayvan sesinden ayrı).</summary>
    private const string MciAlias = "gamesAppMusic";

    /// <summary>Müzik ses seviyesi (MCI ölçeği 0-1000). Kasıtlı olarak çok kısık.</summary>
    private const int MusicVolume = 140;

    /// <summary>Bir turda müziğin çalacağı süre (saniye).</summary>
    public const int PlaySeconds = 60;

    /// <summary>İki tur arasındaki sessizlik süresi (saniye).</summary>
    public const int RestSeconds = 60;

    /// <summary>Müzik olarak kabul edilen uzantılar.</summary>
    private static readonly string[] Extensions = { ".mp3", ".wav", ".m4a", ".wma", ".ogg", ".aac" };

    private readonly object _gate = new();

    private bool _open;
    private bool _playing;
    private bool _disposed;

    public BackgroundMusic()
    {
        FilePath = FindMusicFile();

        if (FilePath == null)
        {
            Diagnostic = "Assets\\Music klasorunde muzik dosyasi bulunamadi";
            return;
        }

        lock (_gate)
        {
            if (!OpenUnsafe())
            {
                return;
            }

            // Sesi kısmayı BAŞARAMAZSAK müziği hiç çalmayız: tam sesli müzik,
            // çocuğun kendi seslerini bastırdığı için istenmeyen bir sonuçtur.
            int volumeResult = NativeMethods.mciSendString(
                $"setaudio {MciAlias} volume to {MusicVolume}", null, 0, IntPtr.Zero);

            if (volumeResult != 0)
            {
                Diagnostic = $"MCI setaudio volume desteklenmiyor (hata {volumeResult}); muzik kapatildi";
                CloseUnsafe();
                return;
            }

            IsAvailable = true;
            Diagnostic = $"OK ({Path.GetFileName(FilePath)}, ses {MusicVolume}/1000)";
        }
    }

    /// <summary>Müzik çalınabilir durumda mı?</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Bulunan müzik dosyasının tam yolu (yoksa null).</summary>
    public string? FilePath { get; }

    /// <summary>Teşhis metni (selftest raporu için).</summary>
    public string Diagnostic { get; private set; } = "baslatilmadi";

    /// <summary>Şu anda müzik çalıyor mu?</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _playing;
            }
        }
    }

    /// <summary>
    /// Müziği kaldığı yerden sürdürür. Parça bittiyse baştan başlatır.
    /// Zaten çalıyorsa hiçbir şey yapmaz.
    /// </summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (!IsAvailable || _disposed || _playing)
            {
                return;
            }

            // Parça sonuna geldiyse baştan çal; aksi hâlde kaldığı yerden devam et.
            bool finished = IsStoppedUnsafe();
            string command = finished ? $"play {MciAlias} from 0" : $"play {MciAlias}";

            if (NativeMethods.mciSendString(command, null, 0, IntPtr.Zero) == 0)
            {
                _playing = true;
            }
        }
    }

    /// <summary>Müziği duraklatır (konum korunur).</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (!IsAvailable || _disposed || !_playing)
            {
                return;
            }

            NativeMethods.mciSendString($"pause {MciAlias}", null, 0, IntPtr.Zero);
            _playing = false;
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
            IsAvailable = false;
            CloseUnsafe();
        }
    }

    /// <summary>MCI oturumunu açar. Kilit ALINMIŞ olmalıdır.</summary>
    private bool OpenUnsafe()
    {
        int result = NativeMethods.mciSendString(
            $"open \"{FilePath}\" type mpegvideo alias {MciAlias}", null, 0, IntPtr.Zero);

        if (result != 0)
        {
            // Uzantıdan tanısın diye tür belirtmeden tekrar dene (WAV vb. için).
            result = NativeMethods.mciSendString(
                $"open \"{FilePath}\" alias {MciAlias}", null, 0, IntPtr.Zero);
        }

        if (result != 0)
        {
            Diagnostic = $"MCI open hata {result} ({Path.GetFileName(FilePath)})";
            return false;
        }

        _open = true;
        return true;
    }

    /// <summary>MCI oturumunu kapatır (idempotent). Kilit ALINMIŞ olmalıdır.</summary>
    private void CloseUnsafe()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        _playing = false;
        NativeMethods.mciSendString($"stop {MciAlias}", null, 0, IntPtr.Zero);
        NativeMethods.mciSendString($"close {MciAlias}", null, 0, IntPtr.Zero);
    }

    /// <summary>Parça bitmiş (veya hiç başlamamış) mı? Kilit ALINMIŞ olmalıdır.</summary>
    private bool IsStoppedUnsafe()
    {
        var buffer = new StringBuilder(64);
        if (NativeMethods.mciSendString($"status {MciAlias} mode", buffer, buffer.Capacity, IntPtr.Zero) != 0)
        {
            return false;
        }

        return buffer.ToString().Trim().Equals("stopped", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>Assets\Music</c> klasöründeki ilk ses dosyasını bulur (ada göre sıralı).
    /// Kullanıcı klasöre birden fazla parça koyabilir; şu an ilki çalınır.
    /// </summary>
    private static string? FindMusicFile()
    {
        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Music");
            if (!Directory.Exists(directory))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(directory)
                .Where(path => Extensions.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            // Erişim/IO hatası: müzik yok sayılır, oyun sessiz çalışır.
            return null;
        }
    }
}
