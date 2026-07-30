using System.Text;

namespace GamesApp.UI;

/// <summary>
/// Çizim istisnalarına karşı güvenlik ağı.
///
/// NEDEN GEREKLİ: WinForms'ta OnPaint içinden kaçan bir istisna, kontrolü KALICI
/// olarak "kırmızı çarpı" (hata) moduna sokar; kontrol bir daha asla çizilmez ve
/// oyun görseli ekrandan kaybolur. Bu sınıfla istisna OnPaint içinde yakalanır:
/// o kare atlanır, bir sonraki karede çizim normal devam eder ve hata teşhis için
/// %TEMP%\gamesapp-paint.log dosyasına yazılır (taşmayı önlemek için en fazla 20 kayıt).
/// </summary>
internal static class PaintGuard
{
    private const int MaxLoggedErrors = 20;

    private static int _loggedCount;

    /// <summary>Yakalanan çizim istisnasını loglar. Kendisi asla istisna fırlatmaz.</summary>
    public static void Report(string source, Exception exception)
    {
        if (Interlocked.Increment(ref _loggedCount) > MaxLoggedErrors)
        {
            return;
        }

        try
        {
            string path = Path.Combine(Path.GetTempPath(), "gamesapp-paint.log");
            string text = $"[{DateTime.Now:O}] {source}: {exception}{Environment.NewLine}";
            File.AppendAllText(path, text, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // Log yazılamıyorsa sessizce vazgeç; çizim döngüsü asla kesilmemeli.
        }
    }
}
