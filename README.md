# GamesApp - Çocuklar için Oyun Kutusu

1,5 yaş ve üzeri çocuklar için tam ekran (kiosk) oyun uygulaması. Üstteki menüden
**fare ile** oyun değiştirilir; klavyenin tamamı aktif oyuna aittir (her tuş yutulur,
çocuk sisteme zarar veremez). Çıkış yalnızca fare ile sağ üstteki **ÇIKIŞ** butonundandır.

Tüm oyunların uyduğu ortak tasarım sözleşmesi: **[docs/TASARIM-KURALLARI.md](docs/TASARIM-KURALLARI.md)**

## Oyunlar

| Oyun | Açıklama |
|------|----------|
| 🎹 Piyano | Her tuş uyumlu bir nota çalar (C majör pentatonik - yanlış nota yoktur). Renkli halkalar, yıldızlar ve arada bir hayvan sürprizi çıkar. Ekrandaki piyanoya fareyle de basılabilir. |
| 🥁 Davul | Her tuş bateri setinin bir parçasına vurur (kick, trampet, hi-hat, tomlar, ziller). Ahşap bagetler vurulan parçaya süzülür, parçalar parlar, konfeti patlar ve hayvan sürprizleri çıkar. Boşluk = kick, Enter = crash zili. Parçalara fareyle de vurulabilir. |
| 🎈 Balon | Ekranda yavaşça yukarı süzülen renkli balonlar; her tuşa basılınca en görünür balon komik bir "pıt!" sesiyle patlar, içinden konfeti ve yıldızlar saçılır. Bu oyunda hayvan sürprizi yoktur (sahne sade kalsın diye); arka planda kısık ve aralıklı müzik çalar. Balonlara fareyle de tıklanabilir. |

## Sesler

- **Piyano:** Windows GS Wavetable Synth (MIDI kanal 0).
- **Davul ve balon:** MIDI perküsyon kısık kaldığı için sesler kod içinde sentezlenir
  (`DrumSoundSynth`, `PopSoundSynth`), tam ölçeğe normalize edilir ve ortak
  `WaveMixer` (waveOut, 16 sesli polifoni, ~12-35 ms gecikme) üzerinden çalınır.
- **Hayvan sesleri:** `Assets\Sounds` içindeki dosyalar; dosya yoksa sentez yedeği devrede.
- **Arka plan müziği:** `Assets\Music` klasöründeki ilk parça, MCI ile **kısık**
  (140/1000) ve **aralıklı** çalar (60 sn çalar, 60 sn susar). Klasöre kendi parçanızı
  koyabilirsiniz. Ses kısılamıyorsa müzik hiç çalınmaz (bkz. tasarım kuralı 4).

## Teknik

- .NET 8 WinForms, harici NuGet paketi yok (yalnızca BCL + Win32 P/Invoke).
- Global klavye kancası (WH_KEYBOARD_LL) her tuşu yutar; Ctrl+Alt+Del ve Win+L
  engellenemez (bilinçli sınır).
- Her çıkış yolunda kanca, MIDI, mikser ve müzik serbest bırakılır (`Program.Shutdown`).
- Tüm çizim kontrolleri `PaintGuard` ile korunur; bir çizim hatası görselin kalıcı
  kaybolmasına yol açamaz (hata `%TEMP%\gamesapp-paint.log`'a yazılır).

## Derleme ve çalıştırma

```powershell
dotnet build GamesApp.slnx -c Release
& "src\GamesApp\bin\Release\net8.0-windows\GamesApp.exe"
```

## Geliştirici modları

| Komut | Ne yapar |
|-------|----------|
| `GamesApp.exe --selftest` | Klavye kancası kurulmadan tüm alt sistemleri doğrular (MIDI, tuş eşleyiciler, ses sentezi, müzik, hayvanlar, efektler, oyun değiştirme). Sonuç: `%TEMP%\gamesapp-selftest.log` |
| `GamesApp.exe --stress` | Binlerce rastgele tuş ve yüzlerce kare çizerek çizim hatalarını avlar. Sonuç: `%TEMP%\gamesapp-stress.log` |
| `GamesApp.exe --snapshot` | Pencere açmadan her oyunun PNG ekran görüntüsünü `%TEMP%` altına kaydeder (görsel değişiklikleri denetlemek için). |

## Yeni oyun ekleme

1. `src/GamesApp/Games/<OyunAdı>/` altında `IGameModule` uygulayan bir `Control` yaz
   (örnek: `PianoGameControl`, `DrumGameControl`, `BalloonGameControl`).
2. `Program.RunNormal` içindeki oyun listesine ekle - menü butonu otomatik oluşur.
3. [docs/TASARIM-KURALLARI.md](docs/TASARIM-KURALLARI.md) sonundaki kontrol listesini uygula.
