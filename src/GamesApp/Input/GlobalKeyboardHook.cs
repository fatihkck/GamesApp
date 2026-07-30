using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using GamesApp.Interop;

namespace GamesApp.Input;

/// <summary>Kancadan gelen tek bir tuş olayının taşıyıcısı (struct: allocation yok).</summary>
internal readonly struct KeyEventInfo
{
    public KeyEventInfo(int vkCode, KeyEventKind kind)
    {
        VkCode = vkCode;
        Kind = kind;
    }

    public int VkCode { get; }
    public KeyEventKind Kind { get; }
}

/// <summary>Kancadan gelen olayın türü.</summary>
internal enum KeyEventKind : byte
{
    Down = 0,
    Up = 1
}

/// <summary>
/// Düşük seviye (WH_KEYBOARD_LL) global klavye kancası.
///
/// TASARIM: Her tuş <b>bireysel</b> olarak yutulur; tuş kombinasyonu takibi YOKTUR.
/// Bireysel yutma kombinasyonları da kendiliğinden etkisiz kılar: Windows tuşu hiç
/// sisteme ulaşmadığı için Win+D, Alt hiç ulaşmadığı için Alt+Tab / Alt+F4, Esc ve
/// Tab hiç ulaşmadığı için Ctrl+Esc çalışmaz. Bu sayede geri çağırma çok kısa kalır.
///
/// ENGELLENEMEYEN TUŞLAR (bilinçli sınırlar):
///  - <b>Ctrl+Alt+Del</b>: Secure Attention Sequence (SAS). Çekirdek (kernel) seviyesinde
///    işlenir, kullanıcı modundaki hiçbir kanca bunu göremez veya yutamaz.
///  - <b>Win+L</b>: Oturum kilitleme de SAS altyapısı üzerinden yürür; engellenemez.
///  - <b>UIPI sınırı</b>: Yönetici (yükseltilmiş) yetkiyle çalışan bir pencere odağa
///    gelirse, daha düşük bütünlük seviyesindeki bu süreç o pencerenin tuşlarını
///    yutamaz. Kanca o pencere aktifken etkisiz kalır.
///  - Kanca geri çağırması Windows'un <c>LowLevelHooksTimeout</c> (varsayılan 300 ms)
///    süresini aşarsa sistem kancayı sessizce devre dışı bırakır. Bu yüzden
///    <see cref="HookCallback"/> içinde ses/çizim/LINQ/allocation YAPILMAZ.
/// </summary>
internal sealed class GlobalKeyboardHook : IDisposable
{
    /// <summary>
    /// ÇOK ÖNEMLİ: Delegate örneği bir alanda saklanır. Aksi hâlde yalnızca
    /// yerel değişkende tutulan delegate GC tarafından toplanır, Windows geçersiz
    /// bir fonksiyon işaretçisini çağırır ve uygulama rastgele çöker.
    /// </summary>
    private readonly NativeMethods.LowLevelKeyboardProc _proc;

    /// <summary>UI thread'ine geçiş için hedef kontrol (mesaj döngüsü sahibi).</summary>
    private readonly Control _syncTarget;

    /// <summary>Kanca thread'i ile UI thread'i arasındaki kilitsiz kuyruk.</summary>
    private readonly ConcurrentQueue<KeyEventInfo> _queue = new();

    /// <summary>
    /// Önbelleğe alınmış drenaj delegesi. Her olayda yeni delegate/parametre dizisi
    /// oluşturmamak için tek örnek kullanılır.
    /// </summary>
    private readonly Action _drainAction;

    private IntPtr _hookId = IntPtr.Zero;

    /// <summary>Dispose sonrası BeginInvoke denemesini engelleyen bayrak (ObjectDisposedException riski).</summary>
    private volatile bool _disposed;

    /// <summary>UI'a bildirim gönderilebilir mi? Kapanışta false yapılır.</summary>
    private volatile bool _notifyEnabled = true;

    public GlobalKeyboardHook(Control syncTarget)
    {
        _syncTarget = syncTarget ?? throw new ArgumentNullException(nameof(syncTarget));
        _proc = HookCallback;
        _drainAction = DrainQueue;
    }

    /// <summary>Tuş basıldı (UI thread'inde tetiklenir).</summary>
    public event Action<int>? KeyDownReceived;

    /// <summary>Tuş bırakıldı (UI thread'inde tetiklenir).</summary>
    public event Action<int>? KeyUpReceived;

    /// <summary>Tuşların yutulup yutulmayacağı. Kapanış sürecinde false yapılır.</summary>
    public bool SuppressAll { get; set; } = true;

    /// <summary>Kanca kurulu mu?</summary>
    public bool IsInstalled => _hookId != IntPtr.Zero;

    /// <summary>Kanca kurulamadıysa Win32 hata kodu.</summary>
    public int LastError { get; private set; }

    /// <summary>
    /// Kancayı kurar. Mesaj döngüsüne sahip UI thread'inden çağrılmalıdır
    /// (MainForm.OnHandleCreated / Load).
    /// </summary>
    public bool Install()
    {
        if (IsInstalled)
        {
            return true;
        }

        // WH_KEYBOARD_LL global bir kanca olduğu için hMod olarak
        // GetModuleHandle(null) (yani mevcut süreç) yeterlidir.
        IntPtr hModule = NativeMethods.GetModuleHandle(null);
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hModule, 0);

        if (_hookId == IntPtr.Zero)
        {
            LastError = Marshal.GetLastWin32Error();
            return false;
        }

        LastError = 0;
        return true;
    }

    /// <summary>Kancayı kaldırır. Birden çok kez çağrılabilir (idempotent).</summary>
    public void Uninstall()
    {
        _notifyEnabled = false;

        IntPtr handle = _hookId;
        if (handle != IntPtr.Zero)
        {
            _hookId = IntPtr.Zero;
            NativeMethods.UnhookWindowsHookEx(handle);
        }

        while (_queue.TryDequeue(out _))
        {
            // Kalan olaylar atılır.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Uninstall();
    }

    /// <summary>
    /// Kanca geri çağırması. MUTLAKA kısa kalmalıdır: sadece okuma, kuyruğa ekleme
    /// ve asenkron bildirim yapar.
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // KBDLLHOOKSTRUCT alanlarını doğrudan okuyoruz; Marshal.PtrToStructure
        // yerine ham okuma daha hızlıdır ve hiç allocation üretmez.
        // Yerleşim: [0]=vkCode, [4]=scanCode, [8]=flags, [12]=time, [16]=dwExtraInfo
        int vkCode = Marshal.ReadInt32(lParam);

        int message = (int)wParam;
        bool isDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
        bool isUp = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;

        if (isDown || isUp)
        {
            // Tuş kombinasyonu takibi yok: her tuş bireysel olarak bildirilir.
            Notify(new KeyEventInfo(vkCode, isDown ? KeyEventKind.Down : KeyEventKind.Up));
        }

        if (SuppressAll)
        {
            // 1 döndürmek tuşu zincirin geri kalanına iletmez => tuş yutulur.
            return new IntPtr(1);
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Olayı kuyruğa koyar ve UI thread'ini asenkron olarak uyandırır.
    /// BeginInvoke asenkron olduğu için kanca geri çağırmasını BLOKLAMAZ.
    /// </summary>
    private void Notify(in KeyEventInfo info)
    {
        if (_disposed || !_notifyEnabled)
        {
            return;
        }

        _queue.Enqueue(info);

        if (!_syncTarget.IsHandleCreated)
        {
            return;
        }

        // Yarış durumu (handle tam bu anda yok edilirse) InvalidOperationException
        // veya ObjectDisposedException üretebilir; kanca çökmesin diye bastırılır.
        try
        {
            _syncTarget.BeginInvoke(_drainAction);
        }
        catch (InvalidOperationException)
        {
            // ObjectDisposedException da InvalidOperationException'dan türer;
            // tek yakalama her iki durumu da kapsar.
        }
    }

    /// <summary>Kuyruğu UI thread'inde boşaltır ve olayları tetikler.</summary>
    private void DrainQueue()
    {
        while (_queue.TryDequeue(out KeyEventInfo info))
        {
            if (info.Kind == KeyEventKind.Down)
            {
                KeyDownReceived?.Invoke(info.VkCode);
            }
            else
            {
                KeyUpReceived?.Invoke(info.VkCode);
            }
        }
    }
}
