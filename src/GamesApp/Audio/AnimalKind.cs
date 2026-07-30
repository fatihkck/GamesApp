namespace GamesApp.Audio;

/// <summary>
/// Sahneye çıkabilen hayvanlar.
///
/// İlk sekizi çiftlik hayvanıdır (piyano ve davul oyunlarının hayvan sürprizi bunlarla
/// başlamıştı); sondaki dördü Hayvanat Bahçesi oyunu için eklendi ama sürpriz havuzu da
/// onları kullanır (çeşitlilik arttıkça sürpriz etkisi güçlenir).
///
/// Sayısal değerler kalıcıdır: yeni hayvan SONA eklenir, aradaki numaralar değişmez.
/// </summary>
internal enum AnimalKind
{
    Cat = 0,
    Dog = 1,
    Cow = 2,
    Sheep = 3,
    Chick = 4,
    Duck = 5,
    Rooster = 6,
    Frog = 7,
    Elephant = 8,
    Lion = 9,
    Monkey = 10,
    Penguin = 11
}

/// <summary>Hayvanlara ait metin bilgileri (Türkçe sesler, dosya adları).</summary>
internal static class AnimalInfo
{
    /// <summary>Tüm hayvanlar, sabit sırada.</summary>
    public static readonly AnimalKind[] All =
    {
        AnimalKind.Cat,
        AnimalKind.Dog,
        AnimalKind.Cow,
        AnimalKind.Sheep,
        AnimalKind.Chick,
        AnimalKind.Duck,
        AnimalKind.Rooster,
        AnimalKind.Frog,
        AnimalKind.Elephant,
        AnimalKind.Lion,
        AnimalKind.Monkey,
        AnimalKind.Penguin
    };

    /// <summary>Konuşma balonunda gösterilen Türkçe ses metni.</summary>
    public static string GetSoundText(AnimalKind kind) => kind switch
    {
        AnimalKind.Cat => "MİYAV!",
        AnimalKind.Dog => "HAV HAV!",
        AnimalKind.Cow => "MÖÖÖ!",
        AnimalKind.Sheep => "MEEE!",
        AnimalKind.Chick => "CİK CİK!",
        AnimalKind.Duck => "VAK VAK!",
        AnimalKind.Rooster => "Ü-ÜRÜ-ÜÜÜ!",
        AnimalKind.Frog => "VIRAK!",
        AnimalKind.Elephant => "FÜÜÜÜ!",
        AnimalKind.Lion => "ROAAAR!",
        AnimalKind.Monkey => "U-U AAH!",
        AnimalKind.Penguin => "ORK ORK!",
        _ => "?"
    };

    /// <summary>Hayvanın Türkçe adı.</summary>
    public static string GetDisplayName(AnimalKind kind) => kind switch
    {
        AnimalKind.Cat => "Kedi",
        AnimalKind.Dog => "Köpek",
        AnimalKind.Cow => "İnek",
        AnimalKind.Sheep => "Koyun",
        AnimalKind.Chick => "Civciv",
        AnimalKind.Duck => "Ördek",
        AnimalKind.Rooster => "Horoz",
        AnimalKind.Frog => "Kurbağa",
        AnimalKind.Elephant => "Fil",
        AnimalKind.Lion => "Aslan",
        AnimalKind.Monkey => "Maymun",
        AnimalKind.Penguin => "Penguen",
        _ => "?"
    };

    /// <summary>
    /// <c>Assets\Sounds</c> klasöründe aranacak dosya adı (uzantısız).
    /// Kullanıcı buraya kendi WAV dosyasını koyarsa sentez yerine o çalınır.
    /// </summary>
    public static string GetAssetName(AnimalKind kind) => kind switch
    {
        AnimalKind.Cat => "cat",
        AnimalKind.Dog => "dog",
        AnimalKind.Cow => "cow",
        AnimalKind.Sheep => "sheep",
        AnimalKind.Chick => "chick",
        AnimalKind.Duck => "duck",
        AnimalKind.Rooster => "rooster",
        AnimalKind.Frog => "frog",
        AnimalKind.Elephant => "elephant",
        AnimalKind.Lion => "lion",
        AnimalKind.Monkey => "monkey",
        AnimalKind.Penguin => "penguin",
        _ => "unknown"
    };
}
