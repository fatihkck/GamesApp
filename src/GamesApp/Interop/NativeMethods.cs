using System.Runtime.InteropServices;
using System.Text;

namespace GamesApp.Interop;

/// <summary>
/// Tüm Win32 P/Invoke bildirimleri tek yerde toplanır.
/// DLL yükleme yolu System32 ile sınırlandırılır (DLL hijacking riskine karşı).
/// </summary>
internal static class NativeMethods
{
    // ---------- Kanca (hook) sabitleri ----------

    /// <summary>Düşük seviye klavye kancası tipi.</summary>
    public const int WH_KEYBOARD_LL = 13;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    /// <summary>Olayın yazılımla enjekte edildiğini belirten bayrak.</summary>
    public const uint LLKHF_INJECTED = 0x10;

    // ---------- Pencere konumlandırma sabitleri ----------

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    /// <summary>TopMost yenilemek için kullanılan bayrak birleşimi.</summary>
    public const uint SWP_TOPMOST_REFRESH = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;

    // ---------- MIDI sabitleri ----------

    /// <summary>Windows MIDI Mapper aygıt kimliği (varsayılan yazılım sentezleyici).</summary>
    public const int MIDI_MAPPER = -1;

    public const int MMSYSERR_NOERROR = 0;

    // ---------- PlaySound sabitleri (hayvan sesleri) ----------

    /// <summary>Ses asenkron çalınır; çağrı hemen döner.</summary>
    public const uint SND_ASYNC = 0x0001;

    /// <summary>Ses bulunamazsa varsayılan sistem sesi ÇALINMAZ.</summary>
    public const uint SND_NODEFAULT = 0x0002;

    /// <summary>Kaynak bellekteki bir WAV görüntüsüdür (dosya değil).</summary>
    public const uint SND_MEMORY = 0x0004;

    /// <summary>Kaynak bir dosya yoludur.</summary>
    public const uint SND_FILENAME = 0x00020000;

    /// <summary>Bu sürece ait çalmakta olan tüm sesleri durdurur.</summary>
    public const uint SND_PURGE = 0x0040;

    /// <summary>Bellekten asenkron çalma için kullanılan bayrak birleşimi.</summary>
    public const uint SND_MEMORY_ASYNC = SND_MEMORY | SND_ASYNC | SND_NODEFAULT;

    /// <summary>Dosyadan asenkron çalma için kullanılan bayrak birleşimi.</summary>
    public const uint SND_FILE_ASYNC = SND_FILENAME | SND_ASYNC | SND_NODEFAULT;

    /// <summary>Kanca geri çağırma imzası.</summary>
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>WH_KEYBOARD_LL kancasının lParam ile taşıdığı yapı.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // ---------- user32 ----------

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // ---------- kernel32 ----------

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ---------- winmm (MIDI çıkışı) ----------

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int midiOutOpen(
        out IntPtr lphMidiOut,
        int uDeviceID,
        IntPtr dwCallback,
        IntPtr dwInstance,
        int dwFlags);

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int midiOutClose(IntPtr hMidiOut);

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int midiOutShortMsg(IntPtr hMidiOut, uint dwMsg);

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int midiOutReset(IntPtr hMidiOut);

    /// <summary>
    /// Bellekteki WAV verisini çalar (byte dizisi sürümü).
    /// DİKKAT: <see cref="SND_ASYNC"/> ile çalma sürerken tamponun canlı ve SABİT
    /// kalması gerekir. Bu yüzden uygulamada pinlenmiş (GCHandle) bellek adresi alan
    /// aşağıdaki aşırı yükleme kullanılır; bu sürüm yalnızca senkron/kısa ömürlü
    /// kullanımlar için bildirilmiştir.
    /// </summary>
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PlaySound(byte[]? pszSound, IntPtr hmod, uint fdwSound);

    /// <summary>
    /// Bellekteki WAV verisini çalar (pinlenmiş adres sürümü).
    /// <c>IntPtr.Zero</c> ve bayrak 0 ile çağrıldığında çalan sesi durdurur.
    /// </summary>
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    /// <summary>
    /// WAV dosyasını yoldan çalar (<see cref="SND_FILENAME"/> ile birlikte kullanılır).
    /// </summary>
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    /// <summary>
    /// MCI komut arayüzü. MP3/OGG/M4A/WMA/AAC gibi sıkıştırılmış biçimleri çalmak için
    /// kullanılır (<c>PlaySound</c> yalnızca WAV çalar). 0 dönerse başarılıdır.
    /// </summary>
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int mciSendString(
        string command,
        StringBuilder? returnValue,
        int returnLength,
        IntPtr hwndCallback);

    // ---------- winmm (waveOut: davul sesleri için PCM akışı) ----------

    /// <summary>Varsayılan dalga çıkış aygıtı.</summary>
    public const int WAVE_MAPPER = -1;

    /// <summary>waveOutOpen: dwCallback bir Event tanıtıcısıdır.</summary>
    public const int CALLBACK_EVENT = 0x00050000;

    /// <summary>WAVEHDR bayrağı: tampon sürücü tarafından çalınıp geri verildi.</summary>
    public const int WHDR_DONE = 0x00000001;

    /// <summary>PCM biçim etiketi.</summary>
    public const short WAVE_FORMAT_PCM = 1;

    /// <summary>waveOutOpen biçim tanımı (PCM).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WAVEFORMATEX
    {
        public short wFormatTag;
        public short nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public short nBlockAlign;
        public short wBitsPerSample;
        public short cbSize;
    }

    /// <summary>waveOutWrite tampon başlığı. Kullanım süresince SABİT bellekte durmalıdır.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WAVEHDR
    {
        public IntPtr lpData;
        public int dwBufferLength;
        public int dwBytesRecorded;
        public IntPtr dwUser;
        public int dwFlags;
        public int dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int waveOutOpen(
        out IntPtr phwo,
        int uDeviceID,
        ref WAVEFORMATEX pwfx,
        IntPtr dwCallback,
        IntPtr dwInstance,
        int fdwOpen);

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int waveOutClose(IntPtr hwo);

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, int cbwh);

    [DllImport("winmm.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int waveOutReset(IntPtr hwo);
}
