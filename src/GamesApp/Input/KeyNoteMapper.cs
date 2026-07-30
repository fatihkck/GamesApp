namespace GamesApp.Input;

/// <summary>
/// Sanal tuş kodlarını MIDI notalarına çevirir.
///
/// Gam: C majör pentatonik (perde sınıfları 0, 2, 4, 7, 9). Pentatonik seçilmiştir
/// çünkü rastgele basılan tuşlar bile birbiriyle uyumlu duyulur - çocuk için
/// "yanlış nota" yoktur. Aralık yaklaşık MIDI 48 (C3) - 96 (C7), 4 oktav.
///
/// PERDE YÖNÜ (seçim): Klavyenin ALT satırı en kalın, ÜST (F) satırı en tiz olacak
/// şekilde satırlar aşağıdan yukarı doğru yükselir; ayrıca HER SATIRIN İÇİNDE de
/// soldan sağa perde artar. Yani sol-alt köşe en kalın, sağ-üst köşe en tiz sestir.
/// Satırlar birbiriyle kısmen örtüşür (her satır gamın farklı bir noktasından başlar).
///
/// SINIR YOK KURALI: Tabloda olmayan HERHANGİ bir vkCode (0-255; medya tuşları,
/// numpad, IME, OEM tuşları dâhil) için deterministik bir yedek nota üretilir.
/// Dolayısıyla klavyenin en köşedeki tuşu bile mutlaka ses çıkarır.
/// </summary>
internal static class KeyNoteMapper
{
    /// <summary>C majör pentatonik notaları, kalından tize sıralı (MIDI 48-96).</summary>
    private static readonly int[] ScaleNotes = BuildScale();

    /// <summary>vkCode -> ScaleNotes indeksi. -1 = tabloda yok (yedek kural uygulanır).</summary>
    private static readonly int[] NoteIndexByVk = BuildLookup();

    /// <summary>Gamdaki en kalın nota (görsel piyano için).</summary>
    public static int LowestNote => ScaleNotes[0];

    /// <summary>Gamdaki en tiz nota (görsel piyano için).</summary>
    public static int HighestNote => ScaleNotes[^1];

    /// <summary>Gamdaki nota sayısı.</summary>
    public static int ScaleLength => ScaleNotes.Length;

    /// <summary>Gamdaki i. notayı verir.</summary>
    public static int GetScaleNote(int index) => ScaleNotes[Math.Clamp(index, 0, ScaleNotes.Length - 1)];

    /// <summary>
    /// Verilen sanal tuş kodu için notayı döndürür. HER ZAMAN true döner;
    /// "nota bulunamadı" durumu yoktur.
    /// </summary>
    public static bool TryGetNote(int vkCode, out int midiNote)
    {
        if (vkCode is >= 0 and < 256)
        {
            int index = NoteIndexByVk[vkCode];
            if (index >= 0)
            {
                midiNote = ScaleNotes[index];
                return true;
            }

            // Yedek kural: tablo dışı tuşlar için deterministik dağıtım.
            midiNote = ScaleNotes[vkCode % ScaleNotes.Length];
            return true;
        }

        // 0-255 aralığı dışındaki beklenmeyen kodlar için de ses üret.
        int safe = Math.Abs(vkCode) % ScaleNotes.Length;
        midiNote = ScaleNotes[safe];
        return true;
    }

    /// <summary>
    /// Tuşa göre 95-120 arası hafif değişen vuruş şiddeti (velocity).
    /// Sabit şiddet mekanik duyulduğu için tuş koduna bağlı deterministik varyasyon uygulanır.
    /// </summary>
    public static int GetVelocity(int vkCode)
    {
        int variation = Math.Abs(vkCode * 7) % 26; // 0-25
        return 95 + variation;
    }

    /// <summary>Notanın gam içindeki normalize konumu (0 = en kalın, 1 = en tiz).</summary>
    public static float GetPitchPosition(int midiNote)
    {
        int low = LowestNote;
        int high = HighestNote;
        if (high <= low)
        {
            return 0.5f;
        }

        return Math.Clamp((midiNote - low) / (float)(high - low), 0f, 1f);
    }

    private static int[] BuildScale()
    {
        // Pentatonik perde sınıfları: C, D, E, G, A
        int[] pitchClasses = { 0, 2, 4, 7, 9 };
        var notes = new List<int>(24);

        for (int note = 48; note <= 96; note++)
        {
            int pc = note % 12;
            for (int i = 0; i < pitchClasses.Length; i++)
            {
                if (pc == pitchClasses[i])
                {
                    notes.Add(note);
                    break;
                }
            }
        }

        return notes.ToArray();
    }

    private static int[] BuildLookup()
    {
        var lookup = new int[256];
        for (int i = 0; i < lookup.Length; i++)
        {
            lookup[i] = -1;
        }

        // --- Satır tanımları: soldan sağa fiziksel sıra ---
        // Alt satır (Ctrl, Win, Alt, Space, AltGr, Win, Menü, Ctrl)
        int[] rowBottom = { 0xA2, 0x5B, 0xA4, 0x20, 0xA5, 0x5C, 0x5D, 0xA3 };

        // ZXCV satırı (Shift, <, Z..M, virgül, nokta, eğik çizgi, Shift)
        int[] rowZ =
        {
            0xA0, 0xE2, 0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D,
            0xBC, 0xBE, 0xBF, 0xA1
        };

        // ASDF satırı (CapsLock, A..L, noktalı virgül, kesme, ters eğik çizgi, Enter)
        int[] rowA =
        {
            0x14, 0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C,
            0xBA, 0xDE, 0xDC, 0x0D
        };

        // QWERTY satırı (Tab, Q..P, köşeli parantezler)
        int[] rowQ =
        {
            0x09, 0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50,
            0xDB, 0xDD
        };

        // Sayı satırı (ters vurgu, 1..0, eksi, artı, Backspace)
        int[] rowNumbers =
        {
            0xC0, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30,
            0xBD, 0xBB, 0x08
        };

        // Fonksiyon satırı (Esc, F1..F12)
        int[] rowFunction =
        {
            0x1B, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B
        };

        // Her satırın gamdaki başlangıç indeksi: aşağıdan yukarı doğru artar.
        AssignRow(lookup, rowBottom, 0);
        AssignRow(lookup, rowZ, 2);
        AssignRow(lookup, rowA, 4);
        AssignRow(lookup, rowQ, 6);
        AssignRow(lookup, rowNumbers, 7);
        AssignRow(lookup, rowFunction, 8);

        return lookup;
    }

    /// <summary>Bir satırın tuşlarını, başlangıç indeksinden itibaren soldan sağa artan notalara bağlar.</summary>
    private static void AssignRow(int[] lookup, int[] row, int startIndex)
    {
        int maxIndex = ScaleNotes.Length - 1;
        for (int i = 0; i < row.Length; i++)
        {
            int vk = row[i];
            if (vk is < 0 or > 255)
            {
                continue;
            }

            lookup[vk] = Math.Min(startIndex + i, maxIndex);
        }
    }
}
