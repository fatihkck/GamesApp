using System.Runtime.InteropServices;
using GamesApp.Interop;

namespace GamesApp.Audio;

/// <summary>
/// Sentezlenmiş PCM örneklerini TAM SES seviyesinde çalan, ÇOK SESLİ (polifonik)
/// mini mikser. Tüm oyunlar tek örneği paylaşır (tek waveOut aygıtı açılır).
///
/// NEDEN MIDI DEĞİL: GS Wavetable sentezleyicinin perküsyon örnekleri, velocity ve
/// kanal sesi (CC7) tavana çekilse bile piyanodan belirgin kısık kalır. Burada ise
/// örnekler tam ölçeğe normalize edilir ve doğrudan ses aygıtına yazılır; gürlük
/// tamamen bizim kontrolümüzdedir.
///
/// ÇALIŞMA ŞEKLİ: Aygıta sürekli küçük tamponlar (3 × ~12 ms) yazılır. Sürücü bir
/// tamponu bitirince Event tetiklenir; arka plan iş parçacığı aktif sesleri (en fazla
/// 16) o tampona miksleyip geri yazar. Ses yokken sessizlik akar; işlemci maliyeti
/// ihmal edilebilir. Gecikme ~12-35 ms'dir (algılanamaz).
///
/// Ses aygıtı yoksa <see cref="IsAvailable"/> false olur ve uygulama ÇÖKMEZ;
/// çağıran taraf yedeğine (ör. MIDI) düşer.
/// </summary>
internal sealed class WaveMixer : IDisposable
{
    /// <summary>Tampon boyutu (örnek sayısı): 512 örnek ≈ 11,6 ms.</summary>
    private const int BufferSamples = 512;

    /// <summary>Sırada bekleyen tampon sayısı (toplam kuyruk ≈ 35 ms).</summary>
    private const int BufferCount = 3;

    /// <summary>Aynı anda çalabilen en fazla ses sayısı.</summary>
    private const int MaxVoices = 16;

    /// <summary>Çalmakta olan tek bir ses.</summary>
    private struct Voice
    {
        public short[]? Data;
        public int Position;
        public float Gain;
    }

    private static readonly int HeaderSize = Marshal.SizeOf<NativeMethods.WAVEHDR>();

    private static readonly int FlagsOffset =
        (int)Marshal.OffsetOf<NativeMethods.WAVEHDR>(nameof(NativeMethods.WAVEHDR.dwFlags));

    /// <summary>Voice listesi bu kilitle korunur.</summary>
    private readonly object _gate = new();

    private readonly Voice[] _voices = new Voice[MaxVoices];

    /// <summary>Sürücünün "tampon bitti" sinyali.</summary>
    private readonly AutoResetEvent _bufferDone = new(false);

    // Tampon başlıkları ve ses verileri yönetilmeyen bellekte tutulur:
    // sürücü bunlara bizim kontrolümüz dışında eriştiği için GC taşımamalıdır.
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _bufferMemory = new IntPtr[BufferCount];

    // Miksleme için tekrar kullanılan ara tamponlar (her karede allocation yok).
    private readonly float[] _mixBuffer = new float[BufferSamples];
    private readonly short[] _outBuffer = new short[BufferSamples];

    private IntPtr _handle;
    private Thread? _pump;
    private volatile bool _running;
    private bool _disposed;

    public WaveMixer()
    {
        var format = new NativeMethods.WAVEFORMATEX
        {
            wFormatTag = NativeMethods.WAVE_FORMAT_PCM,
            nChannels = 1,
            nSamplesPerSec = DrumSoundSynth.SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = DrumSoundSynth.SampleRate * 2,
            cbSize = 0
        };

        int result = NativeMethods.waveOutOpen(
            out IntPtr handle,
            NativeMethods.WAVE_MAPPER,
            ref format,
            _bufferDone.SafeWaitHandle.DangerousGetHandle(),
            IntPtr.Zero,
            NativeMethods.CALLBACK_EVENT);

        if (result != NativeMethods.MMSYSERR_NOERROR || handle == IntPtr.Zero)
        {
            // Ses aygıtı yoksa sessizce devre dışı kal; çağıran yedeğine düşer.
            _handle = IntPtr.Zero;
            IsAvailable = false;
            return;
        }

        _handle = handle;

        // Tamponlar hazırlanır, sessizlikle doldurulup kuyruğa yazılır.
        for (int i = 0; i < BufferCount; i++)
        {
            _bufferMemory[i] = Marshal.AllocHGlobal(BufferSamples * 2);
            Marshal.Copy(_outBuffer, 0, _bufferMemory[i], BufferSamples);

            var header = new NativeMethods.WAVEHDR
            {
                lpData = _bufferMemory[i],
                dwBufferLength = BufferSamples * 2
            };

            _headers[i] = Marshal.AllocHGlobal(HeaderSize);
            Marshal.StructureToPtr(header, _headers[i], false);

            NativeMethods.waveOutPrepareHeader(_handle, _headers[i], HeaderSize);
            NativeMethods.waveOutWrite(_handle, _headers[i], HeaderSize);
        }

        _running = true;
        _pump = new Thread(PumpLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "WaveMixerPump"
        };
        _pump.Start();

        IsAvailable = true;
    }

    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Örneği çalmaya başlar. Boş ses kanalı yoksa bitmeye en yakın olan kesilir
    /// (voice çalma). Kazanç 1'in üzerinde olabilir; mikserdeki kırpma tepe
    /// noktaları törpüler ve bu davulda "punch" olarak duyulur.
    /// </summary>
    public void Play(short[] sample, float gain)
    {
        if (!IsAvailable || sample.Length == 0)
        {
            return;
        }

        float safeGain = Math.Clamp(gain, 0f, 4f);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            int slot = -1;
            int stealCandidate = 0;
            int longestPosition = -1;

            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Data == null)
                {
                    slot = i;
                    break;
                }

                if (_voices[i].Position > longestPosition)
                {
                    longestPosition = _voices[i].Position;
                    stealCandidate = i;
                }
            }

            if (slot < 0)
            {
                slot = stealCandidate;
            }

            _voices[slot].Data = sample;
            _voices[slot].Position = 0;
            _voices[slot].Gain = safeGain;
        }
    }

    /// <summary>Çalmakta olan tüm sesleri anında susturur.</summary>
    public void StopAll()
    {
        lock (_gate)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i].Data = null;
            }
        }
    }

    /// <summary>
    /// Mikser döngüsü: sürücünün bitirdiği her tamponu aktif seslerle doldurup
    /// kuyruğa geri yazar. Kısa ve allocation'sız kalmalıdır.
    /// </summary>
    private void PumpLoop()
    {
        while (_running)
        {
            // 100 ms zaman aşımı: kapanış sinyali kaçarsa bile döngü kilitlenmez.
            _bufferDone.WaitOne(100);

            if (!_running)
            {
                return;
            }

            for (int i = 0; i < BufferCount; i++)
            {
                int flags = Marshal.ReadInt32(_headers[i] + FlagsOffset);
                if ((flags & NativeMethods.WHDR_DONE) == 0)
                {
                    continue;
                }

                MixInto(_bufferMemory[i]);

                // DONE biti temizlenir (PREPARED korunur) ve tampon yeniden kuyruğa girer.
                Marshal.WriteInt32(_headers[i] + FlagsOffset, flags & ~NativeMethods.WHDR_DONE);
                NativeMethods.waveOutWrite(_handle, _headers[i], HeaderSize);
            }
        }
    }

    /// <summary>Aktif sesleri toplar, kırpar ve tampona yazar.</summary>
    private void MixInto(IntPtr buffer)
    {
        Array.Clear(_mixBuffer);

        lock (_gate)
        {
            for (int v = 0; v < _voices.Length; v++)
            {
                short[]? data = _voices[v].Data;
                if (data == null)
                {
                    continue;
                }

                int position = _voices[v].Position;
                float gain = _voices[v].Gain * (1f / 32768f);
                int count = Math.Min(BufferSamples, data.Length - position);

                for (int j = 0; j < count; j++)
                {
                    _mixBuffer[j] += data[position + j] * gain;
                }

                position += count;
                if (position >= data.Length)
                {
                    _voices[v].Data = null;
                }
                else
                {
                    _voices[v].Position = position;
                }
            }
        }

        for (int j = 0; j < BufferSamples; j++)
        {
            float sample = Math.Clamp(_mixBuffer[j], -1f, 1f);
            _outBuffer[j] = (short)(sample * 32000f);
        }

        Marshal.Copy(_outBuffer, 0, buffer, BufferSamples);
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
        }

        _running = false;
        _bufferDone.Set();
        _pump?.Join(500);

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.waveOutReset(_handle);

            for (int i = 0; i < BufferCount; i++)
            {
                if (_headers[i] != IntPtr.Zero)
                {
                    NativeMethods.waveOutUnprepareHeader(_handle, _headers[i], HeaderSize);
                    Marshal.FreeHGlobal(_headers[i]);
                    _headers[i] = IntPtr.Zero;
                }

                if (_bufferMemory[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_bufferMemory[i]);
                    _bufferMemory[i] = IntPtr.Zero;
                }
            }

            NativeMethods.waveOutClose(_handle);
            _handle = IntPtr.Zero;
        }

        _bufferDone.Dispose();
    }
}
