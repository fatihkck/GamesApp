using System.Diagnostics;
using System.Text;
using GamesApp.Interop;

namespace GamesApp.Audio;

/// <summary>Doğrulamadan geçmiş bir ses dosyası ve ölçülen süresi.</summary>
internal sealed class SoundFileInfo
{
    public SoundFileInfo(string path, int durationMs)
    {
        Path = path;
        DurationMs = durationMs;
    }

    /// <summary>Dosyanın tam yolu.</summary>
    public string Path { get; }

    /// <summary>Ölçülen süre (ms). WAV'da başlıktan, sıkıştırılmışlarda MCI ile ölçülür.</summary>
    public int DurationMs { get; }

    /// <summary>Yalnızca dosya adı (log için).</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// <c>Assets\Sounds</c> klasöründeki hazır ses dosyalarını bulur, dosya adına göre
/// hangi hayvana ait olduğunu tahmin eder ve <b>her dosyayı açılışta bir kez
/// doğrular</b> (süresini ölçer).
///
/// AMAÇ: Kullanıcı Pixabay gibi bir kaynaktan indirdiği dosyayı klasöre <b>olduğu gibi</b>
/// kopyalasın; yeniden adlandırmak zorunda kalmasın. Dosya adında hayvanın Türkçe ya da
/// İngilizce adı (veya sesi) geçmesi yeterlidir.
///
/// NEDEN AÇILIŞTA DOĞRULAMA: Bazı MP3 dosyaları Windows MCI ile hiç açılamıyor
/// (kodlayıcıya bağlı), bazıları da bu uygulama için fazla uzun. Bunları çalma anında
/// keşfetmek kullanıcıya sessizlik ya da hayvan gittikten sonra devam eden ses olarak
/// yansırdı. Açılışta eleyip ölçülen süreyi saklıyoruz; süre hayvanın sahne süresini belirler.
///
/// Klasör taraması ve doğrulama yalnızca uygulama başlangıcında BİR KEZ yapılır;
/// sonrasında liste bellekte tutulur (tuş başına dosya sistemi/MCI erişimi olmaz).
/// </summary>
internal sealed class AnimalSoundLibrary
{
    /// <summary>Desteklenen ses uzantıları.</summary>
    public static readonly string[] SupportedExtensions =
    {
        ".wav", ".mp3", ".ogg", ".m4a", ".wma", ".aac"
    };

    /// <summary>
    /// Kabul edilen en uzun ses (ms). Daha uzun kayıtlar bu uygulamaya uygun değildir:
    /// hayvan sahneden gitmeden ses bitmez ve sonraki hayvana taşar.
    /// </summary>
    public const int MaxDurationMs = 6000;

    /// <summary>Teşhis logunun adı.</summary>
    private const string DiagnosticFileName = "piyano-sesler.log";

    /// <summary>Aranan alt klasör.</summary>
    private const string AssetsSubPath = @"Assets\Sounds";

    /// <summary>MCI doğrulaması için kullanılan takma ad.</summary>
    private const string ProbeAlias = "piyanoProbe";

    /// <summary>
    /// Hayvan başına anahtar kelimeler. Hem Türkçe hem İngilizce; karşılaştırma
    /// sadeleştirilmiş (küçük harf, Türkçe karakterler ASCII'ye indirgenmiş) metinde yapılır.
    /// </summary>
    private static readonly (AnimalKind Kind, string[] Keywords)[] KeywordTable =
    {
        (AnimalKind.Cat, new[] { "cat", "kedi", "meow", "miyav", "kitten", "yavru kedi" }),
        (AnimalKind.Dog, new[] { "dog", "kopek", "bark", "hav", "puppy", "woof" }),
        (AnimalKind.Cow, new[] { "cow", "inek", "moo", "cattle", "boga" }),
        (AnimalKind.Sheep, new[] { "sheep", "koyun", "lamb", "kuzu", "baa", "bleat" }),
        (AnimalKind.Chick, new[] { "chick", "civciv", "bird", "kus", "chirp", "tweet", "sparrow" }),
        (AnimalKind.Duck, new[] { "duck", "ordek", "quack", "vak" }),
        (AnimalKind.Rooster, new[] { "rooster", "horoz", "cock", "crow", "tavuk", "hen", "chicken" }),
        (AnimalKind.Frog, new[] { "frog", "kurbaga", "croak", "toad", "virak" })
    };

    private readonly Dictionary<AnimalKind, List<SoundFileInfo>> _filesByAnimal = new();
    private readonly List<SoundFileInfo> _allValidFiles = new();
    private readonly List<string> _skippedFiles = new();
    private readonly List<string> _rejectedFiles = new();
    private readonly object _logGate = new();

    public AnimalSoundLibrary()
    {
        SoundsFolder = Path.Combine(AppContext.BaseDirectory, AssetsSubPath);
        DiagnosticLogPath = Path.Combine(Path.GetTempPath(), DiagnosticFileName);

        var stopwatch = Stopwatch.StartNew();
        Scan();
        stopwatch.Stop();
        ProbeElapsedMs = stopwatch.ElapsedMilliseconds;

        WriteDiagnostics();
    }

    /// <summary>Taranan klasörün tam yolu.</summary>
    public string SoundsFolder { get; }

    /// <summary>Teşhis logunun tam yolu.</summary>
    public string DiagnosticLogPath { get; }

    /// <summary>Klasörde bulunan desteklenen ses dosyası sayısı.</summary>
    public int FoundFileCount { get; private set; }

    /// <summary>Adı bir hayvana eşleştiği için doğrulamaya sokulan dosya sayısı.</summary>
    public int ProbedFileCount { get; private set; }

    /// <summary>Doğrulamadan geçen (kullanılabilir) dosya sayısı.</summary>
    public int ValidFileCount => _allValidFiles.Count;

    /// <summary>Tarama + doğrulamanın toplam süresi (ms). Açılış maliyeti ölçümü.</summary>
    public long ProbeElapsedMs { get; }

    /// <summary>Doğrulamadan geçen tüm dosyalar (selftest için).</summary>
    public IReadOnlyList<SoundFileInfo> AllValidFiles => _allValidFiles;

    /// <summary>Bir hayvana eşleşen geçerli dosya var mı?</summary>
    public bool HasFilesFor(AnimalKind kind) =>
        _filesByAnimal.TryGetValue(kind, out List<SoundFileInfo>? list) && list.Count > 0;

    /// <summary>
    /// Hayvan için bir dosya seçer. Birden fazla eşleşme varsa her çağrıda
    /// RASTGELE biri seçilir (aynı ses tekrar etmesin). Dosya yoksa null döner.
    /// </summary>
    public SoundFileInfo? PickFile(AnimalKind kind, Random random)
    {
        if (!_filesByAnimal.TryGetValue(kind, out List<SoundFileInfo>? list) || list.Count == 0)
        {
            return null;
        }

        return list.Count == 1 ? list[0] : list[random.Next(list.Count)];
    }

    /// <summary>Teşhis loguna satır ekler (çalma hataları için).</summary>
    public void AppendDiagnostic(string line)
    {
        lock (_logGate)
        {
            try
            {
                File.AppendAllText(
                    DiagnosticLogPath,
                    line + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// SAF FONKSİYON: Dosya adından hayvanı tahmin eder. Dosya sistemine erişmez,
    /// bu yüzden doğrudan test edilebilir (selftest bunu kullanır).
    ///
    /// Kurallar:
    ///  - Dosya adında <b>daha erken</b> geçen anahtar kelime kazanır.
    ///  - Aynı konumda birden fazla anahtar varsa <b>daha uzun</b> olan kazanır
    ///    (ör. "cattle" -> inek, "cat" değil; "chicken" -> horoz, "chick" değil).
    ///  - Yine eşitse tabloda ilk tanımlı hayvan kazanır.
    ///  - Hiçbir anahtara uymazsa null döner (dosya yok sayılır).
    /// </summary>
    public static AnimalKind? MatchAnimal(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string normalized = Normalize(Path.GetFileNameWithoutExtension(fileName));
        if (normalized.Length == 0)
        {
            return null;
        }

        AnimalKind? best = null;
        int bestIndex = int.MaxValue;
        int bestLength = 0;

        for (int i = 0; i < KeywordTable.Length; i++)
        {
            (AnimalKind kind, string[] keywords) = KeywordTable[i];

            for (int k = 0; k < keywords.Length; k++)
            {
                string keyword = keywords[k];
                int index = normalized.IndexOf(keyword, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                bool better = index < bestIndex ||
                              (index == bestIndex && keyword.Length > bestLength);

                if (better)
                {
                    best = kind;
                    bestIndex = index;
                    bestLength = keyword.Length;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Karşılaştırma için metni sadeleştirir: küçük harf, Türkçe karakterler ASCII'ye
    /// indirgenir (ö→o, ü→u, ı→i, ş→s, ç→c, ğ→g), noktalama/tire/alt çizgi/rakam
    /// ayırıcı olarak boşluğa çevrilir.
    /// </summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            char mapped = c switch
            {
                'ö' or 'Ö' => 'o',
                'ü' or 'Ü' => 'u',
                'ı' or 'I' => 'i',
                'İ' or 'i' => 'i',
                'ş' or 'Ş' => 's',
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'â' or 'Â' => 'a',
                'î' or 'Î' => 'i',
                'û' or 'Û' => 'u',
                _ => char.ToLowerInvariant(c)
            };

            if (mapped is >= 'a' and <= 'z')
            {
                builder.Append(mapped);
            }
            else
            {
                // Rakam, tire, alt çizgi, boşluk vb. hepsi ayırıcı sayılır.
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// SAF FONKSİYON: Dosyanın süresine göre hayvanın sahnede kalma süresini hesaplar.
    /// <c>clamp(ses + 0,4 sn; 1,8 sn; 4,0 sn)</c> - kısa seslerde hayvan en az 1,8 sn
    /// görünür kalır, uzun seslerde 4 sn'yi geçmez.
    /// </summary>
    public static float ComputeSceneSeconds(float soundSeconds) =>
        Math.Clamp(soundSeconds + 0.4f, 1.8f, 4.0f);

    /// <summary>Klasörü tarar, dosyaları doğrular ve hayvanlara dağıtır.</summary>
    private void Scan()
    {
        try
        {
            if (!Directory.Exists(SoundsFolder))
            {
                return;
            }

            foreach (string path in Directory.EnumerateFiles(SoundsFolder))
            {
                string extension = Path.GetExtension(path);
                bool supported = false;

                for (int i = 0; i < SupportedExtensions.Length; i++)
                {
                    if (string.Equals(extension, SupportedExtensions[i], StringComparison.OrdinalIgnoreCase))
                    {
                        supported = true;
                        break;
                    }
                }

                if (!supported)
                {
                    continue;
                }

                FoundFileCount++;

                AnimalKind? kind = MatchAnimal(Path.GetFileName(path));
                if (kind == null)
                {
                    _skippedFiles.Add(Path.GetFileName(path));
                    continue;
                }

                ProbedFileCount++;

                // --- DOĞRULAMA: dosya gerçekten açılabiliyor mu, süresi ne? ---
                (int durationMs, string? failure) = ProbeDuration(path);

                if (failure != null)
                {
                    _rejectedFiles.Add($"{Path.GetFileName(path)} -> {failure}");
                    continue;
                }

                if (durationMs > MaxDurationMs)
                {
                    _rejectedFiles.Add(
                        $"{Path.GetFileName(path)} -> COK UZUN ({durationMs / 1000.0:0.0} sn, ust sinir {MaxDurationMs / 1000.0:0.0} sn)");
                    continue;
                }

                var info = new SoundFileInfo(path, durationMs);

                if (!_filesByAnimal.TryGetValue(kind.Value, out List<SoundFileInfo>? list))
                {
                    list = new List<SoundFileInfo>();
                    _filesByAnimal[kind.Value] = list;
                }

                list.Add(info);
                _allValidFiles.Add(info);
            }
        }
        catch (IOException)
        {
            // Klasör okunamazsa sentezlenen seslerle devam edilir.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Dosyanın süresini ölçer. WAV için başlık okunur (MCI'ye gerek yok, çok hızlı);
    /// sıkıştırılmış biçimler için MCI ile açılıp <c>status length</c> sorulur.
    /// Hata durumunda ikinci değer eleme gerekçesini taşır.
    /// </summary>
    private static (int DurationMs, string? Failure) ProbeDuration(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return ProbeWavDuration(path);
        }

        return ProbeMciDuration(path);
    }

    /// <summary>WAV süresini RIFF başlığındaki fmt/data chunk'larından hesaplar.</summary>
    private static (int DurationMs, string? Failure) ProbeWavDuration(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);

            if (data.Length < 44 ||
                data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F' ||
                data[8] != (byte)'W' || data[9] != (byte)'A' || data[10] != (byte)'V' || data[11] != (byte)'E')
            {
                return (0, "GECERSIZ (RIFF/WAVE basligi yok)");
            }

            int byteRate = 0;
            int sampleRate = 0;
            int channels = 0;
            int bitsPerSample = 0;
            long dataBytes = -1;

            // Chunk'ları sırayla gez: 12. bayttan sonra [id(4)][size(4)][veri...]
            int offset = 12;
            while (offset + 8 <= data.Length)
            {
                string chunkId = Encoding.ASCII.GetString(data, offset, 4);
                int chunkSize = BitConverter.ToInt32(data, offset + 4);
                if (chunkSize < 0)
                {
                    break;
                }

                int body = offset + 8;

                if (chunkId == "fmt " && body + 16 <= data.Length)
                {
                    channels = BitConverter.ToInt16(data, body + 2);
                    sampleRate = BitConverter.ToInt32(data, body + 4);
                    byteRate = BitConverter.ToInt32(data, body + 8);
                    bitsPerSample = BitConverter.ToInt16(data, body + 14);
                }
                else if (chunkId == "data")
                {
                    // Bildirilen boyut dosyadan büyükse gerçek kalan boyut kullanılır.
                    dataBytes = Math.Min(chunkSize, data.Length - body);
                    break;
                }

                offset = body + chunkSize + (chunkSize % 2); // chunk'lar çift hizalıdır
            }

            if (dataBytes <= 0)
            {
                return (0, "GECERSIZ (data chunk bulunamadi)");
            }

            if (byteRate <= 0)
            {
                byteRate = sampleRate * channels * (bitsPerSample / 8);
            }

            if (byteRate <= 0)
            {
                return (0, "GECERSIZ (fmt chunk okunamadi)");
            }

            int durationMs = (int)(dataBytes * 1000L / byteRate);
            return durationMs <= 0
                ? (0, "GECERSIZ (sure sifir)")
                : (durationMs, null);
        }
        catch (IOException ex)
        {
            return (0, $"OKUNAMADI ({ex.GetType().Name})");
        }
        catch (UnauthorizedAccessException)
        {
            return (0, "OKUNAMADI (erisim engellendi)");
        }
    }

    /// <summary>
    /// Sıkıştırılmış dosyanın süresini MCI ile ölçer.
    /// Açılamayan dosyalar (bazı MP3 kodlayıcıları Windows MCI ile uyumsuzdur)
    /// hata koduyla birlikte elenir.
    /// </summary>
    private static (int DurationMs, string? Failure) ProbeMciDuration(string path)
    {
        // Önceki oturum sızmışsa kapat (idempotent, hata yok sayılır).
        NativeMethods.mciSendString($"close {ProbeAlias}", null, 0, IntPtr.Zero);

        int open = NativeMethods.mciSendString(
            $"open \"{path}\" alias {ProbeAlias}", null, 0, IntPtr.Zero);

        if (open != 0)
        {
            // Uzantıdan tanımadıysa MPEG çözücüyü açıkça belirterek tekrar dene.
            open = NativeMethods.mciSendString(
                $"open \"{path}\" type mpegvideo alias {ProbeAlias}", null, 0, IntPtr.Zero);
        }

        if (open != 0)
        {
            return (0, $"ACILAMADI (MCI hata {open})");
        }

        try
        {
            NativeMethods.mciSendString(
                $"set {ProbeAlias} time format milliseconds", null, 0, IntPtr.Zero);

            var buffer = new StringBuilder(128);
            int status = NativeMethods.mciSendString(
                $"status {ProbeAlias} length", buffer, buffer.Capacity, IntPtr.Zero);

            if (status != 0)
            {
                return (0, $"SURE OLCULEMEDI (MCI hata {status})");
            }

            if (!int.TryParse(buffer.ToString().Trim(), out int durationMs) || durationMs <= 0)
            {
                return (0, "SURE OLCULEMEDI (gecersiz deger)");
            }

            return (durationMs, null);
        }
        finally
        {
            NativeMethods.mciSendString($"close {ProbeAlias}", null, 0, IntPtr.Zero);
        }
    }

    /// <summary>Hangi hayvanın sesinin nereden geldiğini gösteren teşhis logunu yazar.</summary>
    private void WriteDiagnostics()
    {
        var lines = new List<string>
        {
            "# Piyano Kiosk - hayvan sesi kaynaklari",
            $"# Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"# Klasor: {SoundsFolder}",
            $"# Bulunan desteklenen dosya: {FoundFileCount}",
            $"# Adi eslesip dogrulamaya giren: {ProbedFileCount}",
            $"# Dogrulamadan gecen: {ValidFileCount}",
            $"# Tarama + dogrulama suresi: {ProbeElapsedMs} ms",
            $"# Kabul edilen en uzun ses: {MaxDurationMs / 1000.0:0.0} sn",
            string.Empty
        };

        for (int i = 0; i < AnimalInfo.All.Length; i++)
        {
            AnimalKind kind = AnimalInfo.All[i];
            string label = AnimalInfo.GetDisplayName(kind);

            if (_filesByAnimal.TryGetValue(kind, out List<SoundFileInfo>? list) && list.Count > 0)
            {
                for (int f = 0; f < list.Count; f++)
                {
                    lines.Add($"{label} -> DOSYA: {list[f].FileName} ({list[f].DurationMs / 1000.0:0.00} sn)");
                }

                if (list.Count > 1)
                {
                    lines.Add($"{label} -> ({list.Count} dosya, her seferinde rastgele biri calar)");
                }
            }
            else
            {
                lines.Add($"{label} -> SENTEZ (uygulamanin kendi urettigi karikatur ses)");
            }
        }

        lines.Add(string.Empty);

        if (_rejectedFiles.Count == 0)
        {
            lines.Add("# Elenen dosya yok.");
        }
        else
        {
            lines.Add("# ELENEN DOSYALAR (dogrulamayi gecemedi; o hayvan icin digerleri ya da sentez kullanilir):");
            for (int i = 0; i < _rejectedFiles.Count; i++)
            {
                lines.Add($"elendi -> {_rejectedFiles[i]}");
            }
        }

        lines.Add(string.Empty);

        if (_skippedFiles.Count == 0)
        {
            lines.Add("# Adi eslesmeyen dosya yok.");
        }
        else
        {
            lines.Add("# ATLANAN DOSYALAR (adindan hangi hayvan oldugu anlasilamadi):");
            for (int i = 0; i < _skippedFiles.Count; i++)
            {
                lines.Add($"atlandi -> {_skippedFiles[i]} -> ad eslesmedi");
            }
        }

        lock (_logGate)
        {
            try
            {
                File.WriteAllLines(DiagnosticLogPath, lines, new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
