using GamesApp.Audio;

namespace GamesApp.Input;

/// <summary>
/// Hayvanların ne zaman ve hangi sırayla sahneye çıkacağına karar verir.
///
/// TETİKLEME: Her <b>8 ile 14 arası rastgele</b> nota basımından sonra bir hayvan
/// çıkar; ardından eşik yeniden rastgele belirlenir. Böylece tahmin edilemez ama
/// sık gerçekleşir.
///
/// SEÇİM (shuffle bag): 8 hayvan karıştırılır ve sırayla tüketilir; torba boşalınca
/// yeniden karıştırılır. Böylece aynı hayvan üst üste gelmez ve "önce kedi, birkaç
/// tuş sonra köpek" akışı doğal olarak oluşur.
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
    private readonly List<AnimalKind> _bag = new(AnimalInfo.All.Length);

    private int _pressCount;

    public AnimalDirector(Random? random = null)
    {
        _random = random ?? new Random();
        Threshold = PickThreshold();
        RefillBag();
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
        kind = TakeNext();
        return true;
    }

    /// <summary>Sayacı sıfırlar (yeni oyun / kapanış sonrası).</summary>
    public void Reset()
    {
        _pressCount = 0;
        Threshold = PickThreshold();
        RefillBag();
    }

    private int PickThreshold() => _random.Next(MinThreshold, MaxThreshold + 1);

    /// <summary>Torbadan sıradaki hayvanı alır; torba boşsa yeniden karıştırır.</summary>
    private AnimalKind TakeNext()
    {
        if (_bag.Count == 0)
        {
            RefillBag();
        }

        int lastIndex = _bag.Count - 1;
        AnimalKind kind = _bag[lastIndex];
        _bag.RemoveAt(lastIndex);
        return kind;
    }

    /// <summary>Torbayı tüm hayvanlarla doldurup Fisher-Yates ile karıştırır.</summary>
    private void RefillBag()
    {
        _bag.Clear();
        _bag.AddRange(AnimalInfo.All);

        for (int i = _bag.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
        }
    }
}
