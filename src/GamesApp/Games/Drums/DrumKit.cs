using System.Drawing;
using GamesApp.UI;

namespace GamesApp.Games.Drums;

/// <summary>Bateri setindeki tek bir parçanın tanımı.</summary>
internal readonly struct DrumPieceInfo
{
    public DrumPieceInfo(
        string name,
        int gmNote,
        int accentNote,
        int colorNote,
        float x,
        float y,
        float radius,
        bool isCymbal,
        Color bodyColor)
    {
        Name = name;
        GmNote = gmNote;
        AccentNote = accentNote;
        ColorNote = colorNote;
        X = x;
        Y = y;
        Radius = radius;
        IsCymbal = isCymbal;
        BodyColor = bodyColor;
    }

    /// <summary>Türkçe parça adı (teşhis/selftest için).</summary>
    public string Name { get; }

    /// <summary>GM perküsyon notası (kanal 10'da çalınır).</summary>
    public int GmNote { get; }

    /// <summary>
    /// Ana notayla AYNI ANDA çalınan tamamlayıcı GM notası (ses katmanlama).
    /// MIDI'de kanal sesi ve velocity tavana çekildikten sonra kalan tek güçlendirme
    /// yolu budur: iki örnek üst üste mikslenince vuruş tok ve gür duyulur.
    /// </summary>
    public int AccentNote { get; }

    /// <summary>
    /// Efekt rengini ve halka boyutunu belirleyen sanal nota. Perde sınıfları
    /// bilinçli olarak farklı seçilmiştir ki her parçanın efekt rengi ayrışsın;
    /// kalın notalar (kick) daha büyük halka üretir.
    /// </summary>
    public int ColorNote { get; }

    /// <summary>Göreli merkez X (0-1, kontrol genişliğine göre).</summary>
    public float X { get; }

    /// <summary>Göreli merkez Y (0-1, kontrol yüksekliğine göre).</summary>
    public float Y { get; }

    /// <summary>Göreli yarıçap (kontrol yüksekliğine göre).</summary>
    public float Radius { get; }

    /// <summary>Zil mi? (Ziller basık elips olarak çizilir ve vuruşta sallanır.)</summary>
    public bool IsCymbal { get; }

    /// <summary>Parçanın gövde rengi.</summary>
    public Color BodyColor { get; }
}

/// <summary>
/// Bateri setinin sabit tanımı: 8 parça, soldan sağa gerçek bir set gibi dizilir.
/// Konumlar/yarıçaplar görelidir; DrumKitView bunları piksele çevirir.
/// </summary>
internal static class DrumKit
{
    public const int CrashIndex = 0;
    public const int HiHatIndex = 1;
    public const int SnareIndex = 2;
    public const int TomHighIndex = 3;
    public const int TomMidIndex = 4;
    public const int KickIndex = 5;
    public const int TomFloorIndex = 6;
    public const int RideIndex = 7;

    /// <summary>Setin tüm parçaları. Dizi sırası çizim sırası DEĞİLDİR (bkz. DrumKitView).</summary>
    // Renk notaları, EFEKT renginin (Theme.GetNoteColor: perde sınıfı -> renk tonu)
    // parçanın GÖVDE rengiyle uyuşması için seçilmiştir; ayrıca kalın notalar daha
    // büyük halka ürettiği için kick en kalın renk notasını alır.
    // Aksan notaları: kick = iki bas davul birden (36+35), trampet = akustik +
    // elektronik trampet (38+40), ziller = ikiz zil örnekleri (49+57, 51+59),
    // tomlar = komşu tom (dolgun "çift deri" etkisi), hi-hat = kendisi (voice yığma).
    public static readonly DrumPieceInfo[] Pieces =
    {
        // Ad, GM nota, aksan, renk notası, X, Y, yarıçap, zil mi, gövde rengi
        new("Crash Zili", 49, 57, 38, 0.13f, 0.24f, 0.30f, true, Color.FromArgb(255, 240, 195, 70)),
        new("Hi-Hat", 42, 42, 49, 0.07f, 0.48f, 0.24f, true, Color.FromArgb(255, 235, 205, 95)),
        new("Trampet", 38, 40, 48, 0.28f, 0.56f, 0.27f, false, Color.FromArgb(255, 235, 70, 90)),
        new("İnce Tom", 48, 47, 61, 0.40f, 0.33f, 0.22f, false, Color.FromArgb(255, 255, 140, 40)),
        new("Orta Tom", 45, 47, 66, 0.585f, 0.33f, 0.22f, false, Color.FromArgb(255, 60, 190, 220)),
        new("Kick Davul", 36, 35, 43, 0.49f, 0.58f, 0.37f, false, Color.FromArgb(255, 70, 110, 235)),
        new("Yer Tomu", 41, 43, 52, 0.73f, 0.52f, 0.28f, false, Color.FromArgb(255, 90, 200, 110)),
        new("Ride Zili", 51, 59, 62, 0.88f, 0.26f, 0.32f, true, Color.FromArgb(255, 245, 180, 60))
    };
}

/// <summary>
/// Sanal tuş kodlarını bateri parçalarına çevirir.
///
/// SINIR YOK KURALI (piyanodaki gibi): 0-255 arası HER vkCode mutlaka bir parçaya
/// düşer; tablo dışı tuşlar için deterministik yedek kural uygulanır. Komşu tuşlar
/// farklı parçalara dağıtılır ki rastgele basan çocuk çeşitli sesler duysun.
/// Boşluk çubuğu KICK, Enter CRASH zilidir: en büyük tuşlar en tatmin edici sesleri verir.
/// </summary>
internal static class DrumKeyMapper
{
    /// <summary>vkCode -> parça indeksi.</summary>
    private static readonly int[] PieceByVk = BuildLookup();

    /// <summary>Verilen sanal tuş kodu için parça indeksini döndürür. Her tuş bir parçaya düşer.</summary>
    public static int GetPiece(int vkCode)
    {
        if (vkCode is >= 0 and < 256)
        {
            return PieceByVk[vkCode];
        }

        return Math.Abs(vkCode) % DrumKit.Pieces.Length;
    }

    /// <summary>
    /// Tuşa göre 118-127 arası hafif değişen vuruş şiddeti. Perküsyon örnekleri
    /// piyanodan kısık duyulduğu için bilinçli olarak en üst bant kullanılır;
    /// sabit şiddet mekanik duyulacağından tuşa bağlı küçük varyasyon korunur.
    /// </summary>
    public static int GetVelocity(int vkCode)
    {
        int variation = Math.Abs(vkCode * 7) % 10; // 0-9
        return 118 + variation;
    }

    private static int[] BuildLookup()
    {
        int count = DrumKit.Pieces.Length;
        var lookup = new int[256];

        // Yedek kural: tablo dışı her tuş deterministik dağıtılır. 3 ile çarpmak
        // komşu vk kodlarının aynı parçaya düşmesini engeller (3 ile 8 aralarında asaldır).
        for (int i = 0; i < lookup.Length; i++)
        {
            lookup[i] = (i * 3) % count;
        }

        // Ana yazı satırları: soldan sağa komşu tuşlar farklı parçalara döner.
        // Satır başlangıçları kaydırılır ki alt alta tuşlar da farklı sesler versin.
        int[] rowZ = { 0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D, 0xBC, 0xBE, 0xBF };
        int[] rowA = { 0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, 0xBA, 0xDE };
        int[] rowQ = { 0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50, 0xDB, 0xDD };
        int[] rowNumbers = { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30 };
        int[] rowFunction = { 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B };

        AssignRow(lookup, rowZ, 0);
        AssignRow(lookup, rowA, 2);
        AssignRow(lookup, rowQ, 4);
        AssignRow(lookup, rowNumbers, 6);
        AssignRow(lookup, rowFunction, 1);

        // Büyük ve favori tuşlar: en gösterişli parçalara sabitlenir.
        lookup[0x20] = DrumKit.KickIndex;    // Boşluk -> kick (gümmm!)
        lookup[0x0D] = DrumKit.CrashIndex;   // Enter  -> crash (çannn!)
        lookup[0x08] = DrumKit.RideIndex;    // Backspace -> ride
        lookup[0x09] = DrumKit.HiHatIndex;   // Tab -> hi-hat
        lookup[0xA0] = DrumKit.SnareIndex;   // Sol Shift -> trampet
        lookup[0xA1] = DrumKit.TomFloorIndex; // Sağ Shift -> yer tomu

        return lookup;
    }

    /// <summary>Bir satırın tuşlarını, başlangıç kaydırmasıyla parçalara sırayla dağıtır.</summary>
    private static void AssignRow(int[] lookup, int[] row, int startOffset)
    {
        int count = DrumKit.Pieces.Length;
        for (int i = 0; i < row.Length; i++)
        {
            int vk = row[i];
            if (vk is >= 0 and < 256)
            {
                lookup[vk] = (startOffset + i) % count;
            }
        }
    }
}
