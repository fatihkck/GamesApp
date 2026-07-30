using System.Drawing;

namespace GamesApp.Games;

/// <summary>
/// Kabuk (ShellForm) içinde barındırılan bir oyun modülü sözleşmesi.
///
/// SORUMLULUK AYRIMI: Kiosk davranışı (tam ekran, klavye kancası, odak sigortası,
/// çıkış butonu, oyun menüsü) ShellForm'a aittir. Oyun modülü yalnızca kendi
/// görselini çizer, kancadan yönlendirilen tuşları işler ve sesini üretir.
/// Aynı anda YALNIZCA BİR oyun aktiftir; kabuk oyun değiştirirken önce
/// <see cref="Stop"/>, sonra yeni oyunda <see cref="Start"/> çağırır.
/// </summary>
internal interface IGameModule : IDisposable
{
    /// <summary>
    /// Menü butonundaki simge (ör. "🎹"). Menü daraldığında ad gizlenir ama simge
    /// her zaman görünür kalır; okumayı bilmeyen çocuk oyunları simgeden tanır.
    /// Tek bir emoji ya da kısa bir karakter olmalıdır.
    /// </summary>
    string MenuIcon { get; }

    /// <summary>Menü butonunda görünen ad (ör. "Piyano"). Simge burada TEKRARLANMAZ.</summary>
    string MenuTitle { get; }

    /// <summary>Menü butonunun vurgu rengi.</summary>
    Color MenuColor { get; }

    /// <summary>Oyunun görsel kökü; kabuk bunu oyun alanına yerleştirir.</summary>
    Control View { get; }

    /// <summary>Oyun görünür oldu: animasyon döngüsünü başlat.</summary>
    void Start();

    /// <summary>
    /// Oyun gizlendi: animasyonu durdur, basılı tuş durumunu temizle ve
    /// çalan sesleri sustur (sonraki oyuna ses taşmasın).
    /// </summary>
    void Stop();

    /// <summary>Global kancadan yönlendirilen tuş basımı (UI thread'inde).</summary>
    void HandleKeyDown(int vkCode);

    /// <summary>Global kancadan yönlendirilen tuş bırakma (UI thread'inde).</summary>
    void HandleKeyUp(int vkCode);
}
