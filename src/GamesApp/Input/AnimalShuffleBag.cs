using GamesApp.Audio;

namespace GamesApp.Input;

/// <summary>
/// Hayvanları "torba" (shuffle bag) yöntemiyle sırayla verir: tüm hayvanlar karıştırılıp
/// bir kez tüketilir, torba boşalınca yeniden karıştırılır.
///
/// NEDEN SAF RASTGELE DEĞİL: Her seferinde rastgele seçim yapılsa aynı hayvan üst üste
/// birkaç kez gelebilirdi. Çocuk için sürprizin değeri "bu sefer BAŞKA bir şey geldi"
/// hissindedir; torba bunu garanti eder ve tüm hayvanların görülmesini sağlar.
///
/// Hem hayvan sürprizi (<see cref="AnimalDirector"/>) hem Hayvanat Bahçesi oyunu
/// bu sınıfı kullanır. Yalnızca UI thread'inden kullanılır; kilit yoktur.
/// </summary>
internal sealed class AnimalShuffleBag
{
    private readonly Random _random;
    private readonly List<AnimalKind> _bag = new(AnimalInfo.All.Length);

    public AnimalShuffleBag(Random random)
    {
        _random = random;
        Refill();
    }

    /// <summary>Torbadan sıradaki hayvanı alır; torba boşsa yeniden karıştırır.</summary>
    public AnimalKind Take()
    {
        if (_bag.Count == 0)
        {
            Refill();
        }

        int lastIndex = _bag.Count - 1;
        AnimalKind kind = _bag[lastIndex];
        _bag.RemoveAt(lastIndex);
        return kind;
    }

    /// <summary>Torbayı tüm hayvanlarla doldurup Fisher-Yates ile karıştırır.</summary>
    public void Refill()
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
