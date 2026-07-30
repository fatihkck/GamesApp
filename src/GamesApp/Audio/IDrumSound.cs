namespace GamesApp.Audio;

/// <summary>Davul (perküsyon) ses motoru sözleşmesi.</summary>
internal interface IDrumSound : IDisposable
{
    /// <summary>Ses motoru kullanılabilir durumda mı? (Ses aygıtı yoksa false.)</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Ana vuruşun yanında aksan notasının da katmanlanması gerekir mi?
    /// MIDI motoru kısık perküsyon örneklerini gürleştirmek için buna ihtiyaç duyar;
    /// tam seviyeli örnek çalan wave motoru duymaz.
    /// </summary>
    bool NeedsAccentLayer { get; }

    /// <summary>
    /// Bir davul parçasına vurur. GM perküsyon notası (ör. 36 = kick, 38 = trampet)
    /// tek atımlıdır; ayrıca bir "bırakma" çağrısı gerekmez.
    /// </summary>
    void Hit(int gmDrumNote, int velocity);
}
