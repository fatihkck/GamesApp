using GamesApp.Audio;

namespace GamesApp.Input;

/// <summary>
/// Hayvanların ne zaman ve hangi sırayla sahneye çıkacağına karar verir.
///
/// TETİKLEME: Her <b>8 ile 14 arası rastgele</b> nota basımından sonra bir hayvan
/// çıkar; ardından eşik yeniden rastgele belirlenir. Böylece tahmin edilemez ama
/// sık gerçekleşir.
///
/// SEÇİM (shuffle bag): Tüm hayvanlar karıştırılır ve sırayla tüketilir; torba boşalınca
/// yeniden karıştırılır (bkz. <see cref="AnimalShuffleBag"/>). Böylece aynı hayvan üst
/// üste gelmez ve "önce kedi, birkaç tuş sonra köpek" akışı doğal olarak oluşur.
///
/// Yalnızca gerçek nota basımları sayılır; klavye auto-repeat tekrarları
/// <see cref="UI.MainForm"/> tarafındaki basılı tuş takibi sayesinde buraya hiç gelmez.
/// </summary>
internal sealed class AnimalDirector
{
    /// <summary>Eşiğin alt sınırı (dâhil).</summary>
    private const int MinThreshold = 8;

    /// <summary>Eşiğin üst sınırı (dâhil).</summary>
    private const int MaxThreshold = 14;

    private readonly Random _random;
    private readonly AnimalShuffleBag _bag;

    private int _pressCount;

    public AnimalDirector(Random? random = null)
    {
        _random = random ?? new Random();
        _bag = new AnimalShuffleBag(_random);
        Threshold = PickThreshold();
    }

    /// <summary>Bir sonraki hayvan için gereken nota basımı sayısı.</summary>
    public int Threshold { get; private set; }

    /// <summary>Eşiğe kalan basım sayısı (bilgi amaçlı).</summary>
    public int PressesRemaining => Math.Max(0, Threshold - _pressCount);

    /// <summary>
    /// Bir nota basımını kaydeder. Eşiğe ulaşıldıysa true döner ve sahneye çıkacak
    /// hayvanı verir.
    /// </summary>
    public bool RegisterNotePress(out AnimalKind kind)
    {
        _pressCount++;

        if (_pressCount < Threshold)
        {
            kind = AnimalKind.Cat;
            return false;
        }

        _pressCount = 0;
        Threshold = PickThreshold();
        kind = _bag.Take();
        return true;
    }

    /// <summary>Sayacı sıfırlar (yeni oyun / kapanış sonrası).</summary>
    public void Reset()
    {
        _pressCount = 0;
        Threshold = PickThreshold();
        _bag.Refill();
    }

    private int PickThreshold() => _random.Next(MinThreshold, MaxThreshold + 1);
}
