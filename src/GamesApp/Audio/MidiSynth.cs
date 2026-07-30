using GamesApp.Interop;

namespace GamesApp.Audio;

/// <summary>
/// Windows MIDI Mapper (GS Wavetable Synth) üzerinden çalan ortak ses motoru.
/// Harici kütüphane veya ses dosyası gerektirmez. Tek MIDI aygıtı iki amaçla
/// kullanılır: kanal 0 piyano (melodik), kanal 9 davul (GM perküsyon kanalı).
///
/// BIRAKIŞ (release) KARARI: Notaların sert kesilmemesi için <b>Control Change 64
/// (sustain pedalı)</b> piyano kanalında açık tutulur. Böylece NoteOff mesajı geldiğinde
/// sentezleyici notayı ani kesmez, piyano örneğinin doğal sönümlenmesine bırakır.
/// Ses çamurlaşmasını önlemek için, biriken nota sayısı eşiği aşınca pedal kısa süre
/// bırakılıp yeniden basılır (bkz. <see cref="RefreshSustainIfNeeded"/>).
///
/// DAVUL KARARI: GM standardında kanal 9 (1 tabanlı 10) her zaman perküsyondur;
/// program değişikliği gerekmez. Perküsyon sesleri tek atımlıdır ve NoteOff'u
/// yok sayar; yine de sentezleyicinin ses kanallarını (voice) serbest bırakması
/// için vuruştan hemen sonra NoteOff gönderilir.
/// </summary>
internal sealed class MidiSynth : IPianoSound, IDrumSound
{
    private const int PianoChannel = 0;
    private const int DrumChannel = 9;

    private const int NoteOnStatus = 0x90;
    private const int NoteOffStatus = 0x80;
    private const int ControlChangeStatus = 0xB0;
    private const int ProgramChangeStatus = 0xC0;

    private const int CcVolume = 7;
    private const int CcExpression = 11;
    private const int CcSustain = 64;
    private const int CcAllNotesOff = 123;
    private const int CcAllSoundOff = 120;

    /// <summary>Pedal tazelemesi öncesi izin verilen birikmiş nota sayısı.</summary>
    private const int SustainRefreshThreshold = 40;

    /// <summary>midiOutShortMsg thread-safe değildir; tüm çağrılar kilit altında yapılır.</summary>
    private readonly object _gate = new();

    private IntPtr _handle;
    private bool _disposed;
    private int _pendingNotes;

    public MidiSynth()
    {
        int result = NativeMethods.midiOutOpen(
            out IntPtr handle,
            NativeMethods.MIDI_MAPPER,
            IntPtr.Zero,
            IntPtr.Zero,
            0);

        if (result != NativeMethods.MMSYSERR_NOERROR || handle == IntPtr.Zero)
        {
            // Ses aygıtı yoksa sessizce devre dışı kal; uygulama ÇÖKMEZ.
            _handle = IntPtr.Zero;
            IsAvailable = false;
            return;
        }

        _handle = handle;
        IsAvailable = true;

        // Program 0 = Acoustic Grand Piano (yalnızca piyano kanalı için).
        SendRaw(BuildMessage(ProgramChangeStatus | PianoChannel, 0, 0));

        // Sustain pedalını bas (hoş bir bırakış için).
        SendRaw(BuildMessage(ControlChangeStatus | PianoChannel, CcSustain, 127));

        // SES DENGESİ: GM'de kanal ana sesi (CC7) varsayılan 100'dür; perküsyon bu
        // yüzden piyanodan kısık duyulur. Davul kanalının ana sesi ve ifadesi (CC11)
        // en yükseğe çekilir ki vuruşlar piyano ile aynı seviyede gürlesin.
        SendRaw(BuildMessage(ControlChangeStatus | DrumChannel, CcVolume, 127));
        SendRaw(BuildMessage(ControlChangeStatus | DrumChannel, CcExpression, 127));
    }

    public bool IsAvailable { get; private set; }

    /// <summary>GS perküsyon örnekleri kısıktır; aksan katmanı gürlük için gereklidir.</summary>
    public bool NeedsAccentLayer => true;

    public void NoteOn(int midiNote, int velocity)
    {
        if (!IsAvailable)
        {
            return;
        }

        int note = Math.Clamp(midiNote, 0, 127);
        int vel = Math.Clamp(velocity, 1, 127);

        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            SendRawUnsafe(BuildMessage(NoteOnStatus | PianoChannel, note, vel));
            _pendingNotes++;
            RefreshSustainIfNeeded();
        }
    }

    public void NoteOff(int midiNote)
    {
        if (!IsAvailable)
        {
            return;
        }

        int note = Math.Clamp(midiNote, 0, 127);

        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            // Sustain açık olduğu için bu mesaj notayı kesmez, sönümlenmeye bırakır.
            SendRawUnsafe(BuildMessage(NoteOffStatus | PianoChannel, note, 0));
        }
    }

    public void Hit(int gmDrumNote, int velocity)
    {
        if (!IsAvailable)
        {
            return;
        }

        // GM perküsyon haritası 27-87 aralığında tanımlıdır.
        int note = Math.Clamp(gmDrumNote, 27, 87);
        int vel = Math.Clamp(velocity, 1, 127);

        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            // SES GÜCÜ: MIDI'de velocity ve CC7 zaten tavanda; kalan tek kaldıraç
            // VOICE YIĞMA'dır. Aynı nota art arda iki kez tetiklenir; GS sentezleyici
            // iki örneği üst üste mikslediği için vuruş belirgin şekilde gürleşir.
            SendRawUnsafe(BuildMessage(NoteOnStatus | DrumChannel, note, vel));
            SendRawUnsafe(BuildMessage(NoteOnStatus | DrumChannel, note, vel));

            // GM perküsyonu tek atımlıdır; NoteOff sesi kesmez, yalnızca voice'u serbest bırakır.
            SendRawUnsafe(BuildMessage(NoteOffStatus | DrumChannel, note, 0));
        }
    }

    public void AllNotesOff()
    {
        if (!IsAvailable)
        {
            return;
        }

        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            SendRawUnsafe(BuildMessage(ControlChangeStatus | PianoChannel, CcSustain, 0));
            SendRawUnsafe(BuildMessage(ControlChangeStatus | PianoChannel, CcAllNotesOff, 0));
            SendRawUnsafe(BuildMessage(ControlChangeStatus | PianoChannel, CcAllSoundOff, 0));
            SendRawUnsafe(BuildMessage(ControlChangeStatus | PianoChannel, CcSustain, 127));

            SendRawUnsafe(BuildMessage(ControlChangeStatus | DrumChannel, CcAllSoundOff, 0));
            _pendingNotes = 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            IsAvailable = false;

            IntPtr handle = _handle;
            _handle = IntPtr.Zero;

            if (handle != IntPtr.Zero)
            {
                NativeMethods.midiOutReset(handle);
                NativeMethods.midiOutClose(handle);
            }
        }
    }

    /// <summary>
    /// Çok fazla nota biriktiyse pedalı kısa süre bırakıp tekrar basar; böylece
    /// eski notalar serbest kalır ve ses çamurlaşmaz. Kilit altında çağrılır.
    /// </summary>
    private void RefreshSustainIfNeeded()
    {
        if (_pendingNotes < SustainRefreshThreshold)
        {
            return;
        }

        _pendingNotes = 0;
        SendRawUnsafe(BuildMessage(ControlChangeStatus | PianoChannel, CcSustain, 0));
        SendRawUnsafe(BuildMessage(ControlChangeStatus | PianoChannel, CcSustain, 127));
    }

    private void SendRaw(uint message)
    {
        lock (_gate)
        {
            if (_handle != IntPtr.Zero)
            {
                SendRawUnsafe(message);
            }
        }
    }

    /// <summary>Kilit ZATEN alınmış olmalıdır.</summary>
    private void SendRawUnsafe(uint message)
    {
        int result = NativeMethods.midiOutShortMsg(_handle, message);
        if (result != NativeMethods.MMSYSERR_NOERROR)
        {
            // Aygıt bir şekilde kayboldu: sessizce devre dışı kal.
            IsAvailable = false;
        }
    }

    /// <summary>MIDI kısa mesajını paketler: status | (data1 &lt;&lt; 8) | (data2 &lt;&lt; 16).</summary>
    private static uint BuildMessage(int status, int data1, int data2)
    {
        return (uint)((status & 0xFF) | ((data1 & 0x7F) << 8) | ((data2 & 0x7F) << 16));
    }
}
