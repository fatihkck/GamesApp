using System.Drawing;
using System.Text;
using GamesApp.Audio;
using GamesApp.Games;
using GamesApp.Games.Bubbles;
using GamesApp.Games.Drums;
using GamesApp.Games.Paint;
using GamesApp.Games.Peekaboo;
using GamesApp.Games.Piano;
using GamesApp.Games.Zoo;
using GamesApp.Input;
using GamesApp.Interop;
using GamesApp.UI;
using GamesApp.UI.Effects;

namespace GamesApp;

/// <summary>
/// Uygulama giriş noktası ve GÜVENLİK AĞI.
///
/// Neden güvenlik ağı: Global klavye kancası kuruluyken beklenmedik bir istisna
/// oluşursa kullanıcının klavyesi kilitli kalabilir. Bu yüzden hem
/// <see cref="Application.ThreadException"/>, hem
/// <see cref="AppDomain.UnhandledException"/>, hem
/// <see cref="Application.ApplicationExit"/> olaylarında, ayrıca
/// <c>try/finally</c> ile kanca ve MIDI her yolda serbest bırakılır.
/// </summary>
internal static class Program
{
    private static GlobalKeyboardHook? _hook;
    private static MidiSynth? _synth;
    private static WaveMixer? _mixer;
    private static WaveDrumSound? _waveDrums;
    private static BackgroundMusic? _music;
    private static IAnimalSound? _animalSound;

    /// <summary>Shutdown'ın yalnızca bir kez çalışmasını sağlayan bayrak (0 = çalışmadı).</summary>
    private static int _shutdownDone;

    [STAThread]
    private static int Main(string[] args)
    {
        bool selfTest = args.Any(a =>
            string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));

        bool snapshot = args.Any(a =>
            string.Equals(a, "--snapshot", StringComparison.OrdinalIgnoreCase));

        bool stress = args.Any(a =>
            string.Equals(a, "--stress", StringComparison.OrdinalIgnoreCase));

        Application.ThreadException += (_, e) =>
        {
            Shutdown();
            WriteCrashLog("ThreadException", e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Shutdown();
            WriteCrashLog("UnhandledException", e.ExceptionObject as Exception);
        };

        Application.ApplicationExit += (_, _) => Shutdown();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            if (selfTest)
            {
                return RunSelfTest();
            }

            if (stress)
            {
                return RunStress();
            }

            return snapshot ? RunSnapshot() : RunNormal();
        }
        finally
        {
            // Hangi yolla çıkılırsa çıkılsın klavye ve ses serbest bırakılır.
            Shutdown();
        }
    }

    /// <summary>Normal kiosk çalışması.</summary>
    private static int RunNormal()
    {
        var synth = new MidiSynth();
        _synth = synth;

        // Ortak PCM mikseri: davul vuruşları ve balon patlamaları TAM SEVİYEDE
        // buradan çalar (tek waveOut aygıtı tüm oyunlarca paylaşılır).
        var mixer = new WaveMixer();
        _mixer = mixer;

        // Davul sesi: birincil motor mikserdir; ses aygıtı açılamazsa MIDI perküsyona düşülür.
        var waveDrums = new WaveDrumSound(mixer);
        _waveDrums = waveDrums;
        IDrumSound drums = waveDrums.IsAvailable ? waveDrums : synth;

        // Arka plan müziği: Assets\Music klasöründeki parça, kısık ve aralıklı çalar.
        var music = new BackgroundMusic();
        _music = music;

        // Hayvan sesleri: klasör bir kez taranır, teşhis logu yazılır.
        var animalSound = new AnimalSoundPlayer(new AnimalSoundLibrary());
        _animalSound = animalSound;

        // Oyun listesi: menüde bu sırayla görünür. Yeni oyun eklemek = buraya modül eklemek.
        var games = new IGameModule[]
        {
            new PianoGameControl(synth, animalSound),
            new DrumGameControl(drums, animalSound),
            new BalloonGameControl(mixer, music),
            new ZooGameControl(mixer),
            new PeekabooGameControl(mixer),
            new PaintGameControl(mixer)
        };

        using var form = new ShellForm(games, synth.IsAvailable, selfTestMode: false);
        _hook = form.Hook;

        Application.Run(form);
        return 0;
    }

    /// <summary>
    /// --selftest modu: klavye kancası KURULMAZ (test makinesinin klavyesi kilitlenmesin),
    /// MIDI açılır, her iki oyun da gerçek tuş işleme yolundan denenir, pencere birkaç
    /// saniye gösterilip temiz kapatılır. Sonuç %TEMP%\gamesapp-selftest.log dosyasına yazılır.
    /// </summary>
    private static int RunSelfTest()
    {
        var lines = new List<string>();
        bool success = true;
        string exitState = "unknown";

        try
        {
            var synth = new MidiSynth();
            _synth = synth;

            lines.Add(synth.IsAvailable ? "MIDI: OK" : "MIDI: FAIL");
            if (!synth.IsAvailable)
            {
                success = false;
            }

            lines.Add("Hook: skipped");

            // "Sınır yok" kuralı (piyano): 0-255 arası HER vkCode nota üretmeli.
            int mappedKeys = 0;
            for (int vk = 0; vk < 256; vk++)
            {
                if (KeyNoteMapper.TryGetNote(vk, out int mappedNote) && mappedNote is >= 36 and <= 108)
                {
                    mappedKeys++;
                }
            }

            lines.Add($"Mapper: {mappedKeys}/256 keys mapped");
            if (mappedKeys != 256)
            {
                success = false;
            }

            // "Sınır yok" kuralı (davul): 0-255 arası HER vkCode geçerli bir parçaya düşmeli.
            int mappedDrumKeys = 0;
            for (int vk = 0; vk < 256; vk++)
            {
                int piece = DrumKeyMapper.GetPiece(vk);
                if (piece >= 0 && piece < DrumKit.Pieces.Length)
                {
                    mappedDrumKeys++;
                }
            }

            lines.Add($"DrumMapper: {mappedDrumKeys}/256 keys mapped");
            if (mappedDrumKeys != 256)
            {
                success = false;
            }

            // Favori tuşlar doğru parçalara sabitlenmiş mi?
            bool favoritesOk =
                DrumKeyMapper.GetPiece(0x20) == DrumKit.KickIndex &&
                DrumKeyMapper.GetPiece(0x0D) == DrumKit.CrashIndex;

            lines.Add(favoritesOk ? "DrumFavorites: OK" : "DrumFavorites: FAIL");
            if (!favoritesOk)
            {
                success = false;
            }

            // --- Davul sentezi: her parçanın örneği üretiliyor ve GÜR mü? ---
            int drumsRendered = 0;
            for (int i = 0; i < DrumKit.Pieces.Length; i++)
            {
                short[] sample = DrumSoundSynth.Render(DrumKit.Pieces[i].GmNote);

                int peak = 0;
                for (int j = 0; j < sample.Length; j++)
                {
                    int magnitude = Math.Abs((int)sample[j]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }

                // En az 2000 örnek (~45 ms) ve tepe değeri tam ölçeğin %60'ı üzerinde olmalı.
                if (sample.Length >= 2000 && peak >= (int)(short.MaxValue * 0.6f))
                {
                    drumsRendered++;
                }
                else
                {
                    lines.Add($"DrumSynthFail: {DrumKit.Pieces[i].Name} " +
                              $"(len {sample.Length}, peak {peak})");
                }
            }

            lines.Add($"DrumSynth: {drumsRendered}/{DrumKit.Pieces.Length} rendered");
            if (drumsRendered != DrumKit.Pieces.Length)
            {
                success = false;
            }

            // --- Balon patlama sesleri: her varyant üretiliyor ve GÜR mü? ---
            int popsRendered = 0;
            for (int i = 0; i < PopSoundSynth.VariantCount; i++)
            {
                short[] sample = PopSoundSynth.Render(i);

                int peak = 0;
                for (int j = 0; j < sample.Length; j++)
                {
                    int magnitude = Math.Abs((int)sample[j]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }

                // En az 1500 örnek (~34 ms) ve tepe değeri tam ölçeğin %60'ı üzerinde olmalı.
                if (sample.Length >= 1500 && peak >= (int)(short.MaxValue * 0.6f))
                {
                    popsRendered++;
                }
                else
                {
                    lines.Add($"PopSynthFail: varyant {i} (len {sample.Length}, peak {peak})");
                }
            }

            lines.Add($"PopSynth: {popsRendered}/{PopSoundSynth.VariantCount} rendered");
            if (popsRendered != PopSoundSynth.VariantCount)
            {
                success = false;
            }

            // --- "Cee-e!" komik sesleri: her varyant üretiliyor ve GÜR mü? ---
            int cheersRendered = 0;
            for (int i = 0; i < CheerSoundSynth.VariantCount; i++)
            {
                short[] sample = CheerSoundSynth.Render(i);

                int peak = 0;
                for (int j = 0; j < sample.Length; j++)
                {
                    int magnitude = Math.Abs((int)sample[j]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }

                // En az 8820 örnek (~200 ms) ve tepe değeri tam ölçeğin %60'ı üzerinde.
                if (sample.Length >= 8820 && peak >= (int)(short.MaxValue * 0.6f))
                {
                    cheersRendered++;
                }
                else
                {
                    lines.Add($"CheerSynthFail: varyant {i} (len {sample.Length}, peak {peak})");
                }
            }

            lines.Add($"CheerSynth: {cheersRendered}/{CheerSoundSynth.VariantCount} rendered");
            if (cheersRendered != CheerSoundSynth.VariantCount)
            {
                success = false;
            }

            // --- Boya "şlop" sesleri: her varyant üretiliyor ve GÜR mü? ---
            int splatsRendered = 0;
            for (int i = 0; i < SplatSoundSynth.VariantCount; i++)
            {
                short[] sample = SplatSoundSynth.Render(i);

                int peak = 0;
                for (int j = 0; j < sample.Length; j++)
                {
                    int magnitude = Math.Abs((int)sample[j]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }

                // En az 4410 örnek (~100 ms) ve tepe değeri tam ölçeğin %60'ı üzerinde.
                if (sample.Length >= 4410 && peak >= (int)(short.MaxValue * 0.6f))
                {
                    splatsRendered++;
                }
                else
                {
                    lines.Add($"SplatSynthFail: varyant {i} (len {sample.Length}, peak {peak})");
                }
            }

            lines.Add($"SplatSynth: {splatsRendered}/{SplatSoundSynth.VariantCount} rendered");
            if (splatsRendered != SplatSoundSynth.VariantCount)
            {
                success = false;
            }

            // --- Tablo tamamlanma fanfarı: yeterince uzun ve GÜR mü? ---
            {
                short[] fanfare = SplatSoundSynth.GetFanfareSample();

                int peak = 0;
                for (int j = 0; j < fanfare.Length; j++)
                {
                    int magnitude = Math.Abs((int)fanfare[j]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }

                // En az 22050 örnek (~500 ms): fanfar bir kutlamadır, kısa bip olamaz.
                bool fanfareOk = fanfare.Length >= 22050 && peak >= (int)(short.MaxValue * 0.6f);
                lines.Add(fanfareOk
                    ? "PaintFanfare: OK"
                    : $"PaintFanfare: FAIL (len {fanfare.Length}, peak {peak})");
                if (!fanfareOk)
                {
                    success = false;
                }
            }

            // --- Ortak mikser: aygıt açılıyor mu? (Aygıt yoksa SKIP; düşürmez.) ---
            var mixer = new WaveMixer();
            _mixer = mixer;
            lines.Add(mixer.IsAvailable ? "Mixer: OK" : "Mixer: SKIP");

            var waveDrums = new WaveDrumSound(mixer);
            _waveDrums = waveDrums;
            lines.Add(waveDrums.IsAvailable ? "DrumWave: OK" : "DrumWave: SKIP");
            if (waveDrums.IsAvailable)
            {
                waveDrums.Hit(36, 127); // duyulabilir tek "güm": gerçek çalma yolu denenir
            }

            IDrumSound drums = waveDrums.IsAvailable ? waveDrums : synth;

            // --- Arka plan müziği: dosya bulunup KISIK sesle açılabiliyor mu? ---
            var music = new BackgroundMusic();
            _music = music;
            lines.Add($"Music: {music.Diagnostic}");

            // Çal/duraklat yolu gerçekten çalışıyor mu? (Müzik yoksa test atlanır.)
            if (music.IsAvailable)
            {
                music.Resume();
                bool resumed = music.IsPlaying;
                music.Pause();
                bool paused = !music.IsPlaying;

                lines.Add(resumed && paused ? "MusicCycle: OK" : "MusicCycle: FAIL");
                if (!resumed || !paused)
                {
                    success = false;
                }
            }
            else
            {
                lines.Add("MusicCycle: SKIP");
            }

            // --- Hayvan sesleri: sentez doğrulaması ---
            int synthesized = 0;
            for (int i = 0; i < AnimalInfo.All.Length; i++)
            {
                if (IsValidWav(AnimalSoundSynth.GetWav(AnimalInfo.All[i])))
                {
                    synthesized++;
                }
            }

            lines.Add($"Animals: {synthesized}/{AnimalInfo.All.Length} synthesized");
            if (synthesized != AnimalInfo.All.Length)
            {
                success = false;
            }

            // --- Hayvan sesleri: mikser yolu (Hayvanat Bahçesi) gür ve dolu mu? ---
            // Bu oyunda ses tuş başına tetiklendiği için MIDI/PlaySound değil ortak
            // mikser kullanılır; her örnek tam ölçeğe yakın olmalıdır (kural 6).
            int mixerVoices = 0;
            for (int i = 0; i < AnimalInfo.All.Length; i++)
            {
                short[] sample = AnimalSoundSynth.GetMixerSample(AnimalInfo.All[i]);

                int peak = 0;
                for (int j = 0; j < sample.Length; j++)
                {
                    int magnitude = Math.Abs((int)sample[j]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }

                // En az 4410 örnek (~100 ms) ve tepe değeri tam ölçeğin %60'ı üzerinde.
                if (sample.Length >= 4410 && peak >= (int)(short.MaxValue * 0.6f))
                {
                    mixerVoices++;
                }
                else
                {
                    lines.Add($"AnimalMixerFail: {AnimalInfo.GetDisplayName(AnimalInfo.All[i])} " +
                              $"(len {sample.Length}, peak {peak})");
                }
            }

            lines.Add($"AnimalMixer: {mixerVoices}/{AnimalInfo.All.Length} rendered");
            if (mixerVoices != AnimalInfo.All.Length)
            {
                success = false;
            }

            // --- Hayvan görselleri: her biri ekran dışı bitmap'e çizilebiliyor mu? ---
            int drawn = 0;
            for (int i = 0; i < AnimalInfo.All.Length; i++)
            {
                if (CanDrawAnimal(AnimalInfo.All[i]))
                {
                    drawn++;
                }
            }

            lines.Add($"AnimalArt: {drawn}/{AnimalInfo.All.Length} drawn");
            if (drawn != AnimalInfo.All.Length)
            {
                success = false;
            }

            // --- Dosya adı eşleştirici: Pixabay tarzı örnek adlar (dosya sistemi kullanılmaz) ---
            (string Name, AnimalKind Expected)[] nameSamples =
            {
                ("cat-meow-8-fx-306184.mp3", AnimalKind.Cat),
                ("dog-barking-70051.mp3", AnimalKind.Dog),
                ("cow-moo-99231.wav", AnimalKind.Cow),
                ("sheep-bleating-141425.mp3", AnimalKind.Sheep),
                ("bird-chirp-16455.mp3", AnimalKind.Chick),
                ("duck-quack-112941.mp3", AnimalKind.Duck),
                ("rooster-crowing-2.mp3", AnimalKind.Rooster),
                ("frog-croaking-26073.mp3", AnimalKind.Frog)
            };

            int nameMatches = 0;
            for (int i = 0; i < nameSamples.Length; i++)
            {
                if (AnimalSoundLibrary.MatchAnimal(nameSamples[i].Name) == nameSamples[i].Expected)
                {
                    nameMatches++;
                }
                else
                {
                    lines.Add($"NameMatchFail: {nameSamples[i].Name} -> " +
                              $"{AnimalSoundLibrary.MatchAnimal(nameSamples[i].Name)?.ToString() ?? "null"} " +
                              $"(beklenen {nameSamples[i].Expected})");
                }
            }

            lines.Add($"NameMatch: {nameMatches}/{nameSamples.Length}");
            if (nameMatches != nameSamples.Length)
            {
                success = false;
            }

            // Zor durumlar: Türkçe karakterler, büyük harf, örtüşen anahtarlar
            // (cattle -> inek, hen -> horoz) ve alakasız dosya (eşleşme yok).
            (string Name, AnimalKind? Expected)[] edgeSamples =
            {
                ("cattle-lowing-2.mp3", AnimalKind.Cow),
                ("hen house sounds.wav", AnimalKind.Rooster),
                ("kedi_miyavlama.mp3", AnimalKind.Cat),
                ("KUZU-melemesi.WAV", AnimalKind.Sheep),
                ("ördek-vak-vak.mp3", AnimalKind.Duck),
                ("kurbağa-vırak-sesi.ogg", AnimalKind.Frog),
                ("kuş-cıvıltısı.m4a", AnimalKind.Chick),
                ("random-music-123.mp3", null),

                // Hayvanat Bahçesi hayvanları
                ("elephant-trumpet-3.mp3", AnimalKind.Elephant),
                ("fil sesi.wav", AnimalKind.Elephant),
                ("lion-roar-8821.mp3", AnimalKind.Lion),
                ("maymun-çığlığı.mp3", AnimalKind.Monkey),
                ("penguin-call.ogg", AnimalKind.Penguin),

                // Kısa anahtarlar TAM SÖZCÜK kuralı: "file" içindeki "fil" sayılmaz,
                // yoksa alakasız her dosya file adı yüzünden fil sesi olurdu.
                ("sound-file-01.mp3", null)
            };

            int edgeMatches = 0;
            for (int i = 0; i < edgeSamples.Length; i++)
            {
                AnimalKind? actual = AnimalSoundLibrary.MatchAnimal(edgeSamples[i].Name);
                if (actual == edgeSamples[i].Expected)
                {
                    edgeMatches++;
                }
                else
                {
                    lines.Add($"NameMatchEdgeFail: {edgeSamples[i].Name} -> " +
                              $"{actual?.ToString() ?? "null"} (beklenen {edgeSamples[i].Expected?.ToString() ?? "null"})");
                }
            }

            lines.Add($"NameMatchEdge: {edgeMatches}/{edgeSamples.Length}");
            if (edgeMatches != edgeSamples.Length)
            {
                success = false;
            }

            // --- Hazır ses dosyaları (0 olabilir; başarısızlık değil) ---
            var library = new AnimalSoundLibrary();
            var animalSound = new AnimalSoundPlayer(library);
            _animalSound = animalSound;
            lines.Add($"SoundFiles: {library.FoundFileCount} found");

            // Açılış doğrulamasının sonucu ve ÖLÇÜLEN maliyeti.
            lines.Add($"SoundProbe: {library.ValidFileCount}/{library.ProbedFileCount} ok " +
                      $"({library.ProbeElapsedMs} ms)");

            // Geçerli her dosyanın süresi 0 < süre <= 6 sn aralığında olmalı.
            bool durationsOk = true;
            for (int i = 0; i < library.AllValidFiles.Count; i++)
            {
                SoundFileInfo info = library.AllValidFiles[i];
                if (info.DurationMs <= 0 || info.DurationMs > AnimalSoundLibrary.MaxDurationMs)
                {
                    durationsOk = false;
                    lines.Add($"SoundDurationFail: {info.FileName} -> {info.DurationMs} ms");
                }
            }

            lines.Add(durationsOk ? "SoundDurations: OK" : "SoundDurations: FAIL");
            if (!durationsOk)
            {
                success = false;
            }

            // Sahne süresi hesabı (saf fonksiyon) sınır durumlarında doğru mu?
            bool cueSyncOk =
                Math.Abs(AnimalCue.ComputeSceneSeconds(0.5f) - 1.8f) < 0.001f &&
                Math.Abs(AnimalCue.ComputeSceneSeconds(2.5f) - 2.9f) < 0.001f &&
                Math.Abs(AnimalCue.ComputeSceneSeconds(10f) - 4.0f) < 0.001f;

            lines.Add(cueSyncOk ? "CueSync: OK" : "CueSync: FAIL");
            if (!cueSyncOk)
            {
                success = false;
            }

            // MCI yolu (MP3 vb. için kullanılan yol) gerçek bir dosyayla denenir.
            // Bilgi amaçlıdır: başarısız olsa bile sentez yedeği devrede olduğu için
            // uygulama çalışmaya devam eder, bu yüzden testi düşürmez.
            lines.Add($"MciPath: {TestMciPath()}");

            // --- Kabuk + altı oyunun uçtan uca denemesi ---
            var pianoGame = new PianoGameControl(synth, animalSound);
            var drumGame = new DrumGameControl(drums, animalSound);
            var balloonGame = new BalloonGameControl(mixer, music);
            var zooGame = new ZooGameControl(mixer);
            var peekabooGame = new PeekabooGameControl(mixer);
            var paintGame = new PaintGameControl(mixer);
            var games = new IGameModule[]
            {
                pianoGame, drumGame, balloonGame, zooGame, peekabooGame, paintGame
            };

            // --- Menü sözleşmesi: her oyunun simgesi ve adı olmalı ---
            // Simge kritik: menü daraldığında ad gizlenir, geriye yalnızca simge kalır.
            int labelled = 0;
            for (int i = 0; i < games.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(games[i].MenuIcon) &&
                    !string.IsNullOrWhiteSpace(games[i].MenuTitle))
                {
                    labelled++;
                }
            }

            lines.Add($"MenuLabels: {labelled}/{games.Length}");
            if (labelled != games.Length)
            {
                success = false;
            }

            // --- Menü sığdırma: geniş ekranda tek sayfa, dar ekranda sayfalanır ---
            // Menüye çok oyun geldiğinde butonların taşmaması bu hesaba bağlıdır.
            int widePages;
            int narrowPages;
            using (var strip = new GameMenuStrip(games))
            {
                strip.Size = new Size(1400, 96);
                widePages = strip.PageCount;

                strip.Size = new Size(360, 96);
                narrowPages = strip.PageCount;
            }

            lines.Add($"MenuPaging: wide {widePages} page(s), narrow {narrowPages} page(s)");
            if (widePages != 1 || narrowPages < 2)
            {
                success = false;
            }

            using var form = new ShellForm(games, synth.IsAvailable, selfTestMode: true);
            _hook = form.Hook; // selftest'te null olmalı
            lines.Add(form.Hook == null ? "HookInstance: none" : "HookInstance: UNEXPECTED");
            if (form.Hook != null)
            {
                success = false;
            }

            // C majör pentatonik'ten 5 nota.
            int[] demoNotes = { 60, 62, 64, 67, 69 };
            int noteIndex = 0;
            int elapsedMs = 0;
            int audiblePianoSpecialKeys = 0;
            int audibleDrumKeys = 0;
            int audibleBalloonKeys = 0;
            int audibleZooKeys = 0;
            bool pianoAnimalTriggered = false;
            bool drumAnimalTriggered = false;
            bool animalAudioPlayed = false;
            bool switchedToDrums = false;
            bool switchedToBalloons = false;
            bool switchedToZoo = false;
            bool switchedToPeekaboo = false;
            bool startedOnPiano = false;
            int balloonsOnField = 0;
            int balloonsAfterPlay = 0;
            int zooWelcomeAnimals = 0;
            int zooAnimalsAfterPlay = 0;
            int audiblePeekabooKeys = 0;
            bool peekabooWelcome = false;
            bool peekabooAfterPlay = false;
            bool switchedToPaint = false;
            int audiblePaintKeys = 0;
            bool paintWelcome = false;
            int paintResets = 0;
            float paintCoverage = 0f;

            // Bireysel olarak yutulan özel tuşlar: hiçbiri sessiz kalmamalı.
            // Esc, LWin, RWin, Alt, Tab, Ctrl, Shift, CapsLock
            int[] specialKeys = { 0x1B, 0x5B, 0x5C, 0x12, 0x09, 0x11, 0x10, 0x14 };

            using var scheduler = new System.Windows.Forms.Timer { Interval = 300 };
            scheduler.Tick += (_, _) =>
            {
                elapsedMs += scheduler.Interval;

                if (noteIndex < demoNotes.Length)
                {
                    pianoGame.SelfTestPlay(demoNotes[noteIndex]);
                    noteIndex++;
                }

                if (elapsedMs >= 3000)
                {
                    scheduler.Stop();

                    startedOnPiano = ReferenceEquals(form.ActiveGame, pianoGame);

                    // --- Piyano: özel tuşlar GERÇEK tuş işleme yolundan geçirilir ---
                    for (int i = 0; i < specialKeys.Length; i++)
                    {
                        if (pianoGame.SelfTestFeedKey(specialKeys[i]))
                        {
                            audiblePianoSpecialKeys++;
                        }
                    }

                    // Hayvan yönetmeninin eşiğine ulaşacak kadar sanal tuş besle
                    // (eşik en fazla 14; 40 deneme fazlasıyla yeter).
                    for (int i = 0; i < 40 && !pianoGame.HasActiveAnimal; i++)
                    {
                        pianoGame.SelfTestFeedKey(0x41 + (i % 20));
                    }

                    pianoAnimalTriggered = pianoGame.HasActiveAnimal;

                    // Hayvan sesi gerçekten çalınabiliyor mu?
                    animalAudioPlayed = animalSound.TryPlay(AnimalKind.Cat, out _);

                    // --- Davula geç ve aynı yoldan doğrula ---
                    form.SwitchToGame(1);
                    switchedToDrums = ReferenceEquals(form.ActiveGame, drumGame);

                    for (int i = 0; i < specialKeys.Length; i++)
                    {
                        if (drumGame.SelfTestFeedKey(specialKeys[i]))
                        {
                            audibleDrumKeys++;
                        }
                    }

                    for (int i = 0; i < 40 && !drumGame.HasActiveAnimal; i++)
                    {
                        drumGame.SelfTestFeedKey(0x41 + (i % 20));
                    }

                    drumAnimalTriggered = drumGame.HasActiveAnimal;

                    // --- Balona geç ve aynı yoldan doğrula ---
                    form.SwitchToGame(2);
                    switchedToBalloons = ReferenceEquals(form.ActiveGame, balloonGame);

                    // Tarla balonlarla doldu mu? (Ekran boş başlamamalı.)
                    balloonsOnField = balloonGame.BalloonCount;

                    for (int i = 0; i < specialKeys.Length; i++)
                    {
                        if (balloonGame.SelfTestFeedKey(specialKeys[i]))
                        {
                            audibleBalloonKeys++;
                        }
                    }

                    // Uzun oynayış: patlat + kare ilerlet döngüsünde tarla balonsuz
                    // kalmamalı (sahne asla boş kalmaz kuralı).
                    for (int i = 0; i < 60; i++)
                    {
                        balloonGame.SelfTestFeedKey(0x41 + (i % 20));
                        balloonGame.SelfTestAdvance(0.5f);
                    }

                    balloonsAfterPlay = balloonGame.BalloonCount;

                    // --- Hayvanat Bahçesine geç ve aynı yoldan doğrula ---
                    form.SwitchToGame(3);
                    switchedToZoo = ReferenceEquals(form.ActiveGame, zooGame);

                    // Oyuna girişte karşılama hayvanı geldi mi? (Sahne boş başlamaz.)
                    zooWelcomeAnimals = zooGame.AnimalCount;

                    for (int i = 0; i < specialKeys.Length; i++)
                    {
                        if (zooGame.SelfTestFeedKey(specialKeys[i]))
                        {
                            audibleZooKeys++;
                        }
                    }

                    // Uzun oynayış: hayvan çağır + kare ilerlet döngüsünde sahne
                    // ne boş kalmalı ne de sınırsız kalabalıklaşmalı.
                    for (int i = 0; i < 60; i++)
                    {
                        zooGame.SelfTestFeedKey(0x41 + (i % 20));
                        zooGame.SelfTestAdvance(0.25f);
                    }

                    zooAnimalsAfterPlay = zooGame.AnimalCount;

                    // --- "Cee-e!" oyununa geç ve aynı yoldan doğrula ---
                    form.SwitchToGame(4);
                    switchedToPeekaboo = ReferenceEquals(form.ActiveGame, peekabooGame);

                    // Oyuna girişte ilk karakter fırladı mı? (Sahne boş başlamaz.)
                    peekabooWelcome = peekabooGame.HasCharacter;

                    for (int i = 0; i < specialKeys.Length; i++)
                    {
                        if (peekabooGame.SelfTestFeedKey(specialKeys[i]))
                        {
                            audiblePeekabooKeys++;
                        }
                    }

                    // Uzun oynayış: "Cee-e!" tetikle + kare ilerlet döngüsünde sahnede
                    // hep bir karakter olmalı (her basış yenisini fırlatır).
                    for (int i = 0; i < 60; i++)
                    {
                        peekabooGame.SelfTestFeedKey(0x41 + (i % 20));
                        peekabooGame.SelfTestAdvance(0.25f);
                    }

                    peekabooAfterPlay = peekabooGame.HasCharacter;

                    // --- Boyama oyununa geç ve aynı yoldan doğrula ---
                    form.SwitchToGame(5);
                    switchedToPaint = ReferenceEquals(form.ActiveGame, paintGame);

                    // Oyuna girişte karşılama lekesi düştü mü? (Tuval boş başlamaz.)
                    paintWelcome = paintGame.CoverageRatio > 0f;

                    for (int i = 0; i < specialKeys.Length; i++)
                    {
                        if (paintGame.SelfTestFeedKey(specialKeys[i]))
                        {
                            audiblePaintKeys++;
                        }
                    }

                    // Uzun oynayış: 220 leke tuvali doldurmalı ve EN AZ BİR kez
                    // kutlamalı sıfırlama tetiklenmeli (tablo tamamlanma döngüsü).
                    for (int i = 0; i < 220; i++)
                    {
                        paintGame.SelfTestFeedKey(i % 256);
                        paintGame.SelfTestAdvance(0.05f);
                    }

                    paintResets = paintGame.ResetCount;
                    paintCoverage = paintGame.CoverageRatio;

                    form.SelfTestClose();
                }
            };

            form.Shown += (_, _) => scheduler.Start();

            Application.Run(form);

            lines.Add(startedOnPiano ? "StartGame: piano" : "StartGame: FAIL");
            if (!startedOnPiano)
            {
                success = false;
            }

            lines.Add($"SpecialKeys(Piano): {audiblePianoSpecialKeys}/{specialKeys.Length} audible");
            if (audiblePianoSpecialKeys != specialKeys.Length)
            {
                success = false;
            }

            lines.Add(pianoAnimalTriggered ? "AnimalCue(Piano): triggered" : "AnimalCue(Piano): FAIL");
            if (!pianoAnimalTriggered)
            {
                success = false;
            }

            lines.Add(switchedToDrums ? "GameSwitch: OK" : "GameSwitch: FAIL");
            if (!switchedToDrums)
            {
                success = false;
            }

            lines.Add($"SpecialKeys(Drums): {audibleDrumKeys}/{specialKeys.Length} audible");
            if (audibleDrumKeys != specialKeys.Length)
            {
                success = false;
            }

            lines.Add(drumAnimalTriggered ? "AnimalCue(Drums): triggered" : "AnimalCue(Drums): FAIL");
            if (!drumAnimalTriggered)
            {
                success = false;
            }

            lines.Add(switchedToBalloons ? "GameSwitch(Balloons): OK" : "GameSwitch(Balloons): FAIL");
            if (!switchedToBalloons)
            {
                success = false;
            }

            lines.Add($"BalloonField: {balloonsOnField} balloons");
            if (balloonsOnField <= 0)
            {
                success = false;
            }

            lines.Add($"SpecialKeys(Balloons): {audibleBalloonKeys}/{specialKeys.Length} reacted");
            if (audibleBalloonKeys != specialKeys.Length)
            {
                success = false;
            }

            // 60 patlatmadan sonra tarla hâlâ dolu olmalı: "sahne asla boş kalmaz"
            // kuralı, hızlı patlatan çocukta da geçerli kalıyor mu?
            lines.Add($"BalloonAfterPlay: {balloonsAfterPlay} balloons");
            if (balloonsAfterPlay <= 0)
            {
                success = false;
            }

            lines.Add(switchedToZoo ? "GameSwitch(Zoo): OK" : "GameSwitch(Zoo): FAIL");
            if (!switchedToZoo)
            {
                success = false;
            }

            lines.Add($"ZooWelcome: {zooWelcomeAnimals} animal(s)");
            if (zooWelcomeAnimals <= 0)
            {
                success = false;
            }

            lines.Add($"SpecialKeys(Zoo): {audibleZooKeys}/{specialKeys.Length} reacted");
            if (audibleZooKeys != specialKeys.Length)
            {
                success = false;
            }

            // 60 çağrıdan sonra sahnede hayvan olmalı ama üst sınır aşılmamalı:
            // en yaşlı hayvan çıkışa zorlanarak yerini yenisine bırakır, fazlalık
            // birikirse doğrudan kaldırılır (sahne okunur kalsın).
            lines.Add($"ZooAfterPlay: {zooAnimalsAfterPlay} animal(s), " +
                      $"limit {ZooStageView.MaxActors}");
            if (zooAnimalsAfterPlay <= 0 || zooAnimalsAfterPlay > ZooStageView.MaxActors)
            {
                success = false;
            }

            lines.Add(switchedToPeekaboo ? "GameSwitch(Peekaboo): OK" : "GameSwitch(Peekaboo): FAIL");
            if (!switchedToPeekaboo)
            {
                success = false;
            }

            lines.Add(peekabooWelcome ? "PeekabooWelcome: OK" : "PeekabooWelcome: FAIL");
            if (!peekabooWelcome)
            {
                success = false;
            }

            lines.Add($"SpecialKeys(Peekaboo): {audiblePeekabooKeys}/{specialKeys.Length} reacted");
            if (audiblePeekabooKeys != specialKeys.Length)
            {
                success = false;
            }

            // 60 tetiklemeden sonra sahnede hâlâ bir karakter olmalı: her basış
            // yenisini fırlatır, sahne hiçbir aralıkta karaktersiz kalmaz.
            lines.Add(peekabooAfterPlay ? "PeekabooAfterPlay: OK" : "PeekabooAfterPlay: FAIL");
            if (!peekabooAfterPlay)
            {
                success = false;
            }

            lines.Add(switchedToPaint ? "GameSwitch(Paint): OK" : "GameSwitch(Paint): FAIL");
            if (!switchedToPaint)
            {
                success = false;
            }

            lines.Add(paintWelcome ? "PaintWelcome: OK" : "PaintWelcome: FAIL");
            if (!paintWelcome)
            {
                success = false;
            }

            lines.Add($"SpecialKeys(Paint): {audiblePaintKeys}/{specialKeys.Length} reacted");
            if (audiblePaintKeys != specialKeys.Length)
            {
                success = false;
            }

            // 220 lekeden sonra tablo en az bir kez tamamlanıp sıfırlanmış olmalı:
            // "komple resim olunca baştan başlar" döngüsü gerçekten dönüyor mu?
            lines.Add($"PaintReset: {paintResets} reset(s), coverage {paintCoverage:P0}");
            if (paintResets < 1)
            {
                success = false;
            }

            // Ses aygıtı yoksa SKIP yazılır ve bu başarısızlık SAYILMAZ.
            lines.Add(animalAudioPlayed ? "AnimalAudio: OK" : "AnimalAudio: SKIP");

            lines.Add($"Effects(Piano): {pianoGame.TotalEffectsSpawned} spawned");
            lines.Add($"Effects(Drums): {drumGame.TotalEffectsSpawned} spawned");
            lines.Add($"Effects(Balloons): {balloonGame.TotalEffectsSpawned} spawned");
            lines.Add($"Effects(Zoo): {zooGame.TotalEffectsSpawned} spawned");
            lines.Add($"Effects(Peekaboo): {peekabooGame.TotalEffectsSpawned} spawned");
            lines.Add($"Effects(Paint): {paintGame.TotalEffectsSpawned} spawned");
            if (pianoGame.TotalEffectsSpawned <= 0 ||
                drumGame.TotalEffectsSpawned <= 0 ||
                balloonGame.TotalEffectsSpawned <= 0 ||
                zooGame.TotalEffectsSpawned <= 0 ||
                peekabooGame.TotalEffectsSpawned <= 0 ||
                paintGame.TotalEffectsSpawned <= 0 ||
                noteIndex != demoNotes.Length)
            {
                success = false;
            }

            exitState = "clean";
        }
        catch (Exception ex)
        {
            success = false;
            exitState = "error";
            lines.Add($"Exception: {ex.GetType().Name}: {ex.Message}");
            lines.Add($"StackTrace: {ex.StackTrace}");
        }
        finally
        {
            Shutdown();
        }

        lines.Add($"Exit: {exitState}");
        lines.Add($"Result: {(success ? "PASS" : "FAIL")}");

        WriteSelfTestLog(lines);

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        return success ? 0 : 1;
    }

    /// <summary>
    /// --snapshot modu (geliştirici aracı): pencere açmadan her iki oyunu ekran dışı
    /// bitmap'e çizer ve %TEMP% altına PNG kaydeder. Görsel değişiklikleri uygulamayı
    /// başlatmadan denetlemek için kullanılır.
    /// </summary>
    private static int RunSnapshot()
    {
        var synth = new MidiSynth();
        _synth = synth;

        var animalSound = new AnimalSoundPlayer(new AnimalSoundLibrary());
        _animalSound = animalSound;

        using (var drums = new DrumGameControl(synth, animalSound))
        {
            SaveSnapshot(drums, () =>
            {
                drums.HandleKeyDown(0x0D); // Enter -> crash (sol baget hamlede)
                drums.HandleKeyDown(0xA1); // Sağ Shift -> yer tomu (sağ baget hamlede)
            }, "gamesapp-snapshot-drums.png");
        }

        using (var piano = new PianoGameControl(synth, animalSound))
        {
            SaveSnapshot(piano, () =>
            {
                piano.SelfTestPlay(60);
                piano.SelfTestPlay(67);
            }, "gamesapp-snapshot-piano.png");
        }

        using (var mixer = new WaveMixer())
        using (var balloons = new BalloonGameControl(mixer, new BackgroundMusic()))
        {
            SaveSnapshot(balloons, () =>
            {
                balloons.SelfTestFillField();
                balloons.HandleKeyDown(0x41); // tek patlama: konfeti de görünsün
                balloons.SelfTestAdvance(0.12f);
            }, "gamesapp-snapshot-balloons.png");
        }

        using (var mixer = new WaveMixer())
        using (var zoo = new ZooGameControl(mixer))
        {
            SaveSnapshot(zoo, () =>
            {
                // Üç hayvan çağrılır ve hepsi yerine yerleşene kadar ilerletilir:
                // orman, hayvanlar ve konuşma balonları birlikte görünür.
                zoo.Start();
                zoo.HandleKeyDown(0x41);
                zoo.HandleKeyDown(0x42);
                zoo.SelfTestAdvance(0.95f);
            }, "gamesapp-snapshot-zoo.png");
        }

        using (var mixer = new WaveMixer())
        using (var peekaboo = new PeekabooGameControl(mixer))
        {
            SaveSnapshot(peekaboo, () =>
            {
                // Karşılama karakteri fırlatılır ve tam görünür olana kadar
                // ilerletilir: açık perde, karakter ve "CEE-E!" balonu birlikte görünür.
                peekaboo.Start();
                peekaboo.HandleKeyDown(0x41);
                peekaboo.SelfTestAdvance(0.55f);
            }, "gamesapp-snapshot-peekaboo.png");
        }

        using (var mixer = new WaveMixer())
        using (var paint = new PaintGameControl(mixer))
        {
            SaveSnapshot(paint, () =>
            {
                // Bir düzine leke vurulur: tuvalin farklı köşelerinde büyük renkli
                // lekeler, fırça izleri ve damlacıklar birlikte görünür.
                paint.Start();
                for (int i = 0; i < 12; i++)
                {
                    int vk = 0x30 + i * 17;
                    paint.HandleKeyDown(vk);
                    paint.HandleKeyUp(vk);
                    paint.SelfTestAdvance(0.1f);
                }
            }, "gamesapp-snapshot-paint.png");
        }

        SaveAnimalSheet();
        SaveMenuSheets();

        return 0;
    }

    /// <summary>
    /// Menü şeridinde yalnızca ad/simge/renk taşıyan sahte oyun (snapshot aracı için).
    /// Oynanabilir değildir; menünün YERLEŞİMİNİ gerçek oyunları kurmadan denetlemeye
    /// yarar.
    /// </summary>
    private sealed class MenuPreviewGame : IGameModule
    {
        private readonly Control _view = new();

        public MenuPreviewGame(string icon, string title, double hue)
        {
            MenuIcon = icon;
            MenuTitle = title;
            MenuColor = Theme.ColorFromHsv(hue, 0.82, 0.92);
        }

        public string MenuIcon { get; }

        public string MenuTitle { get; }

        public Color MenuColor { get; }

        public Control View => _view;

        public void Start() { }

        public void Stop() { }

        public void HandleKeyDown(int vkCode) { }

        public void HandleKeyUp(int vkCode) { }

        public void Dispose() => _view.Dispose();
    }

    /// <summary>
    /// --snapshot (geliştirici aracı): Menü şeridini BUGÜNKÜ dört oyunla ve ileride
    /// oyun sayısı artmış hâliyle (dokuz oyun) çizer. Menünün taşmadığı, daralınca
    /// sayfalandığı ve simgelerin okunur kaldığı böyle denetlenir.
    /// </summary>
    private static void SaveMenuSheets()
    {
        // İlk altısı gerçek oyunlar; kalanı menünün büyümesini göstermek için örnek.
        (string Icon, string Title, double Hue)[] catalog =
        {
            ("🎹", "Piyano", 205.0),
            ("🥁", "Davul", 15.0),
            ("🎈", "Balon", 330.0),
            ("🦁", "Hayvanlar", 38.0),
            ("🙈", "Cee-e", 285.0),
            ("🎨", "Boyama", 160.0),
            ("🚗", "Arabalar", 250.0),
            ("🔤", "Harfler", 120.0),
            ("⭐", "Şekiller", 55.0),
            ("🍎", "Meyveler", 0.0)
        };

        // Gerçek kabuktaki şerit genişliği: ekran eksi çıkış butonu ve kenar boşlukları.
        RenderMenuSheet(catalog, 6, 1420, 96, "gamesapp-snapshot-menu-6.png");
        RenderMenuSheet(catalog, catalog.Length, 1420, 96, "gamesapp-snapshot-menu-10.png");
    }

    private static void RenderMenuSheet(
        (string Icon, string Title, double Hue)[] catalog,
        int count,
        int width,
        int height,
        string fileName)
    {
        var games = new IGameModule[count];
        for (int i = 0; i < count; i++)
        {
            games[i] = new MenuPreviewGame(catalog[i].Icon, catalog[i].Title, catalog[i].Hue);
        }

        try
        {
            using var strip = new GameMenuStrip(games);
            strip.Size = new Size(width, height);
            strip.CreateControl();
            strip.SetSelectedGame(count - 1);

            using var bitmap = new Bitmap(width, height);
            strip.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
            bitmap.Save(
                Path.Combine(Path.GetTempPath(), fileName),
                System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            for (int i = 0; i < games.Length; i++)
            {
                games[i].Dispose();
            }
        }
    }

    /// <summary>
    /// --snapshot (geliştirici aracı): TÜM hayvanları ızgara hâlinde tek PNG'ye çizer.
    /// Selftest yalnızca "çizilebiliyor ve boş değil" der; hayvanın gerçekten kediye ya
    /// da file benzeyip benzemediği ancak göze bakılarak denetlenebilir.
    /// </summary>
    private static void SaveAnimalSheet()
    {
        const int columns = 4;
        const int cell = 260;

        int rows = (AnimalInfo.All.Length + columns - 1) / columns;

        using var bitmap = new Bitmap(columns * cell, rows * cell);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Theme.Background);

            using var font = new Font("Segoe UI", 15f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);

            for (int i = 0; i < AnimalInfo.All.Length; i++)
            {
                AnimalKind kind = AnimalInfo.All[i];
                int x = i % columns * cell;
                int y = i / columns * cell;

                AnimalArtist.Draw(
                    g,
                    kind,
                    new RectangleF(x + 30f, y + 14f, cell - 60f, cell - 70f),
                    1f);

                g.DrawString(
                    $"{AnimalInfo.GetDisplayName(kind)} — {AnimalInfo.GetSoundText(kind)}",
                    font,
                    brush,
                    x + 12f,
                    y + cell - 46f);
            }
        }

        bitmap.Save(
            Path.Combine(Path.GetTempPath(), "gamesapp-snapshot-animals.png"),
            System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>Stres testi için sessiz davul motoru (ses aygıtına dokunmaz).</summary>
    private sealed class NullDrumSound : IDrumSound
    {
        public bool IsAvailable => true;
        public bool NeedsAccentLayer => false;
        public void Hit(int gmDrumNote, int velocity) { }
        public void Dispose() { }
    }

    /// <summary>Stres testi için sessiz hayvan sesi motoru.</summary>
    private sealed class NullAnimalSound : IAnimalSound
    {
        public bool TryPlay(AnimalKind kind, out int soundDurationMs)
        {
            soundDurationMs = 0;
            return false;
        }

        public void Stop() { }
        public void Dispose() { }
    }

    /// <summary>
    /// --stress modu (geliştirici aracı): davul oyununu binlerce RASTGELE tuşla
    /// besler ve sık sık ekran dışına çizdirir. Amaç, yalnızca uzun oynayışta ortaya
    /// çıkan çizim hatalarını (kalıcı "kırmızı çarpı" vakası gibi) yakalamaktır.
    /// Çizim istisnaları PaintGuard tarafından %TEMP%\gamesapp-paint.log dosyasına
    /// düşer; test sonunda bu dosya varsa FAIL döner.
    /// </summary>
    private static int RunStress()
    {
        string paintLog = Path.Combine(Path.GetTempPath(), "gamesapp-paint.log");
        try
        {
            File.Delete(paintLog);
        }
        catch (IOException)
        {
        }

        var lines = new List<string>();

        using var bitmap = new Bitmap(1600, 900);

        using (var drums = new DrumGameControl(new NullDrumSound(), new NullAnimalSound()))
        {
            drums.Size = new Size(1600, 900);
            drums.CreateControl();

            var random = new Random(12345);

            for (int i = 0; i < 8000; i++)
            {
                int vk = random.Next(0, 256);
                drums.HandleKeyDown(vk);

                // Tuşların bir kısmı basılı bırakılır: auto-repeat yolu da çalışsın.
                if (random.Next(2) == 0)
                {
                    drums.HandleKeyUp(random.Next(0, 256));
                }

                drums.SelfTestAdvance(0.016f);

                if (i % 20 == 0)
                {
                    drums.DrawToBitmap(bitmap, new Rectangle(Point.Empty, drums.Size));
                }
            }

            lines.Add("Stress(Drums): 8000 keys, 400 frames drawn");
        }

        // Balon oyunu: gradyan dolgular ve küçülen şekiller GDI+ için hassas olduğu
        // için ayrıca stres edilir (balonlar doğar, yükselir, patlar).
        using (var mixer = new WaveMixer())
        using (var balloons = new BalloonGameControl(mixer, new BackgroundMusic()))
        {
            balloons.Size = new Size(1600, 900);
            balloons.CreateControl();
            balloons.SelfTestFillField();

            var random = new Random(999);

            for (int i = 0; i < 6000; i++)
            {
                balloons.HandleKeyDown(random.Next(0, 256));

                if (random.Next(2) == 0)
                {
                    balloons.HandleKeyUp(random.Next(0, 256));
                }

                // Kare süresi değişken: balonlar hem doğar hem ekranı terk eder.
                balloons.SelfTestAdvance(random.Next(2) == 0 ? 0.016f : 0.4f);

                if (i % 20 == 0)
                {
                    balloons.DrawToBitmap(bitmap, new Rectangle(Point.Empty, balloons.Size));
                }
            }

            lines.Add("Stress(Balloons): 6000 keys, 300 frames drawn");
        }

        // Hayvanat Bahçesi: döndürülen (takla atan) hayvanlar, gölge elipsleri ve
        // konuşma balonları GDI+ dönüşümleriyle çizildiği için ayrıca stres edilir.
        using (var mixer = new WaveMixer())
        using (var zoo = new ZooGameControl(mixer))
        {
            zoo.Size = new Size(1600, 900);
            zoo.CreateControl();

            var random = new Random(777);

            for (int i = 0; i < 6000; i++)
            {
                zoo.HandleKeyDown(random.Next(0, 256));

                if (random.Next(2) == 0)
                {
                    zoo.HandleKeyUp(random.Next(0, 256));
                }

                // Kare süresi değişken: hayvanlar hem girer hem sahneyi terk eder.
                zoo.SelfTestAdvance(random.Next(2) == 0 ? 0.016f : 0.35f);

                if (i % 20 == 0)
                {
                    zoo.DrawToBitmap(bitmap, new Rectangle(Point.Empty, zoo.Size));
                }
            }

            lines.Add("Stress(Zoo): 6000 keys, 300 frames drawn");
        }

        // "Cee-e!": kırpılmış karakter çizimi, PathGradient spot ışığı ve toplanan
        // perdenin küçülen şekilleri GDI+ için hassas olduğu için ayrıca stres edilir.
        using (var mixer = new WaveMixer())
        using (var peekaboo = new PeekabooGameControl(mixer))
        {
            peekaboo.Size = new Size(1600, 900);
            peekaboo.CreateControl();

            var random = new Random(555);

            for (int i = 0; i < 6000; i++)
            {
                peekaboo.HandleKeyDown(random.Next(0, 256));

                if (random.Next(2) == 0)
                {
                    peekaboo.HandleKeyUp(random.Next(0, 256));
                }

                // Kare süresi değişken: karakter hem fırlar hem saklanır, perde
                // hem açılır hem kapanır.
                peekaboo.SelfTestAdvance(random.Next(2) == 0 ? 0.016f : 0.35f);

                if (i % 20 == 0)
                {
                    peekaboo.DrawToBitmap(bitmap, new Rectangle(Point.Empty, peekaboo.Size));
                }
            }

            lines.Add("Stress(Peekaboo): 6000 keys, 300 frames drawn");
        }

        // Boyama: bitmap'e sürekli birikimli çizim, döndürülmüş fırça izleri ve
        // tekrarlanan kutlamalı sıfırlama döngüsü ayrıca stres edilir.
        using (var mixer = new WaveMixer())
        using (var paint = new PaintGameControl(mixer))
        {
            paint.Size = new Size(1600, 900);
            paint.CreateControl();

            var random = new Random(333);

            for (int i = 0; i < 6000; i++)
            {
                paint.HandleKeyDown(random.Next(0, 256));

                if (random.Next(2) == 0)
                {
                    paint.HandleKeyUp(random.Next(0, 256));
                }

                // Kare süresi değişken: sıfırlama animasyonu hem başlar hem biter.
                paint.SelfTestAdvance(random.Next(2) == 0 ? 0.016f : 0.35f);

                if (i % 20 == 0)
                {
                    paint.DrawToBitmap(bitmap, new Rectangle(Point.Empty, paint.Size));
                }
            }

            lines.Add($"Stress(Paint): 6000 keys, 300 frames drawn, {paint.ResetCount} reset(s)");
        }

        bool paintFailed = File.Exists(paintLog);
        if (paintFailed)
        {
            lines.Add("PaintErrors:");
            try
            {
                lines.AddRange(File.ReadAllLines(paintLog));
            }
            catch (IOException)
            {
            }
        }

        lines.Add($"Result: {(paintFailed ? "FAIL" : "PASS")}");

        try
        {
            File.WriteAllLines(
                Path.Combine(Path.GetTempPath(), "gamesapp-stress.log"),
                lines,
                new UTF8Encoding(false));
        }
        catch (IOException)
        {
        }

        return paintFailed ? 1 : 0;
    }

    /// <summary>Snapshot: kontrolü boyutlandırır, verilen etkileşimi uygular ve PNG kaydeder.</summary>
    private static void SaveSnapshot(Control control, Action interact, string fileName)
    {
        control.Size = new Size(1600, 900);
        control.CreateControl();
        interact();

        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        bitmap.Save(
            Path.Combine(Path.GetTempPath(), fileName),
            System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>
    /// Klavye kancasını ve MIDI'yi serbest bırakır. İdempotenttir; birden çok
    /// kez ve farklı thread'lerden çağrılabilir.
    /// </summary>
    internal static void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownDone, 1) == 1)
        {
            return;
        }

        GlobalKeyboardHook? hook = _hook;
        _hook = null;
        if (hook != null)
        {
            try
            {
                hook.SuppressAll = false;
                hook.Dispose();
            }
            catch (Exception)
            {
                // Kapanış sırasındaki hatalar yutulur; amaç klavyeyi kesinlikle bırakmak.
            }
        }

        MidiSynth? synth = _synth;
        _synth = null;
        if (synth != null)
        {
            try
            {
                synth.AllNotesOff();
                synth.Dispose();
            }
            catch (Exception)
            {
                // Ses kapanış hatası yok sayılır.
            }
        }

        WaveDrumSound? waveDrums = _waveDrums;
        _waveDrums = null;
        if (waveDrums != null)
        {
            try
            {
                waveDrums.Dispose();
            }
            catch (Exception)
            {
                // Davul motoru kapanış hatası yok sayılır.
            }
        }

        BackgroundMusic? music = _music;
        _music = null;
        if (music != null)
        {
            try
            {
                // Müziği durdurur ve MCI oturumunu kapatır.
                music.Dispose();
            }
            catch (Exception)
            {
                // Müzik kapanış hatası yok sayılır.
            }
        }

        WaveMixer? mixer = _mixer;
        _mixer = null;
        if (mixer != null)
        {
            try
            {
                // Mikser iş parçacığını durdurur ve waveOut aygıtını kapatır.
                mixer.Dispose();
            }
            catch (Exception)
            {
                // Mikser kapanış hatası yok sayılır.
            }
        }

        IAnimalSound? animalSound = _animalSound;
        _animalSound = null;
        if (animalSound != null)
        {
            try
            {
                // Çalan hayvan sesini durdurur (PlaySound(null, ...)) ve MCI oturumunu kapatır.
                animalSound.Stop();
                animalSound.Dispose();
            }
            catch (Exception)
            {
                // Hayvan sesi kapanış hatası yok sayılır.
            }
        }
    }

    /// <summary>
    /// Selftest: MCI çalma yolunu (open/play/stop/close + boşluk içeren yolun çift
    /// tırnaklanması) gerçek bir dosyayla dener.
    ///
    /// DÜRÜST SINIR: Elimizde MP3 dosyası olmadığı için burada <c>waveaudio</c> aygıtı
    /// kullanılır. Bu, komut dizilimini ve alias yönetimini doğrular; MP3 çözücünün
    /// kendisi (<c>type mpegvideo</c>) bu testte doğrulanmaz.
    /// </summary>
    private static string TestMciPath()
    {
        const string alias = "gamesAppMciTest";
        string directory = Path.Combine(Path.GetTempPath(), "gamesapp mci testi");
        string file = Path.Combine(directory, "cat meow test.wav");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(file, AnimalSoundSynth.GetWav(AnimalKind.Cat));

            int open = NativeMethods.mciSendString(
                $"open \"{file}\" type waveaudio alias {alias}", null, 0, IntPtr.Zero);

            if (open != 0)
            {
                return $"FAIL (open {open})";
            }

            int play = NativeMethods.mciSendString($"play {alias} from 0", null, 0, IntPtr.Zero);
            NativeMethods.mciSendString($"stop {alias}", null, 0, IntPtr.Zero);
            NativeMethods.mciSendString($"close {alias}", null, 0, IntPtr.Zero);

            return play == 0 ? "OK (waveaudio, bosluklu yol)" : $"FAIL (play {play})";
        }
        catch (Exception ex)
        {
            return $"FAIL ({ex.GetType().Name})";
        }
        finally
        {
            try
            {
                File.Delete(file);
                Directory.Delete(directory);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Selftest: WAV verisi en az 1000 bayt ve geçerli RIFF/WAVE başlığı taşıyor mu?</summary>
    private static bool IsValidWav(byte[] wav)
    {
        if (wav.Length < 1000)
        {
            return false;
        }

        return wav[0] == (byte)'R' && wav[1] == (byte)'I' && wav[2] == (byte)'F' && wav[3] == (byte)'F' &&
               wav[8] == (byte)'W' && wav[9] == (byte)'A' && wav[10] == (byte)'V' && wav[11] == (byte)'E';
    }

    /// <summary>
    /// Selftest: hayvan ekran dışı bir bitmap'e istisnasız çizilebiliyor mu ve
    /// sonuç tamamen saydam/boş değil mi? (Piksel örneklemesi yapılır.)
    /// </summary>
    private static bool CanDrawAnimal(AnimalKind kind)
    {
        try
        {
            using var bitmap = new Bitmap(200, 200);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                AnimalArtist.Draw(g, kind, new RectangleF(10f, 10f, 180f, 180f), 1f);
            }

            int visible = 0;
            for (int y = 0; y < bitmap.Height; y += 4)
            {
                for (int x = 0; x < bitmap.Width; x += 4)
                {
                    if (bitmap.GetPixel(x, y).A > 16)
                    {
                        visible++;
                    }
                }
            }

            // Anlamlı bir çizim en az birkaç yüz örneklenmiş pikseli doldurmalı.
            return visible >= 200;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void WriteSelfTestLog(IEnumerable<string> lines)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "gamesapp-selftest.log");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteCrashLog(string kind, Exception? exception)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "gamesapp-crash.log");
            string text = $"[{DateTime.Now:O}] {kind}: {exception}{Environment.NewLine}";
            File.AppendAllText(path, text, new UTF8Encoding(false));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
