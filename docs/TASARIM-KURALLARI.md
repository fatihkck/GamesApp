# GamesApp Tasarım Kuralları (1,5 Yaş Ergonomisi)

Bu kurallar **tüm oyunlar** için geçerli ortak sözleşmedir. Gerektiğinde esnetilebilir
ama esnetme bilinçli olmalı ve nedeni kodda yorum olarak yazılmalıdır.

Hedef kitle: 1,5 yaş ve üzeri çocuklar. Oyun bir "beceri sınavı" değil, **neden-sonuç
oyuncağıdır**: çocuk bir şey yapar, dünya hemen ve keyifli biçimde tepki verir.

---

## 1. Tuş kısıtlaması olmasın (Omni-input)

Çocuk bu yaşta belirli tuşları hedefleyemez. **Klavyenin neresine basarsa bassın**
(Space, Enter, harfler, F tuşları, medya tuşları, Esc, Windows tuşu...) sistem tepki
vermelidir. "Geçersiz tuş" diye bir şey yoktur.

**Uygulamada:** Her oyun modülü 0-255 arası her `vkCode` için anlamlı bir çıktı üretir.
Tuş eşleyicilerde tabloda olmayan kodlar için deterministik yedek kural bulunur
(`KeyNoteMapper`, `DrumKeyMapper`). Selftest bunu `256/256` olarak doğrular; bu sayı
düşerse test kırmızıya döner.

## 2. Sistem tuş kombinasyonları kilitlensin

Alt+F4, Alt+Tab, Windows tuşu, Ctrl+Esc gibi kombinasyonlar oyunun aniden kapanmasına
veya masaüstüne düşmesine yol açar. Bunlar uygulama içinde yakalanıp **yutulur**.

**Uygulamada:** `GlobalKeyboardHook` (WH_KEYBOARD_LL) her tuşu **bireysel** olarak yutar;
kombinasyon takibi yoktur (Windows tuşu hiç sisteme ulaşmadığı için Win+D de çalışmaz).
Çıkış yalnızca fareyle sağ üstteki ÇIKIŞ butonundan yapılır.

**Bilinçli sınırlar:** Ctrl+Alt+Del ve Win+L çekirdek seviyesinde işlendiği için
engellenemez. Görev çubuğu da bilinçli olarak gizlenmez (uygulama çökerse kullanıcının
görev çubuğu kalıcı kaybolmasın).

## 3. Canlı renkler ve yüksek kontrast

Bu yaş grubu parlak, doygun renkleri ve belirgin hatları daha kolay takip eder.

**Uygulamada:** Koyu (neredeyse siyah-lacivert) arka plan üzerine yüksek doygunluklu
renkler; nesnelerin etrafında belirgin dış hat; vuruş/patlama anında renkli ışıma.
Soluk pastel tonlardan ve ince gri çizgilerden kaçınılır.

## 4. Karmaşık müzik olmasın — ses çocuğun eylemine ait olsun

Arka planda sürekli çalan ağır müzik, çocuğun **kendi** eylemlerinin seslerini bastırır
ve ilgisini kısa sürede dağıtır. Öncelik her zaman **anlık geri bildirim sesidir**
(nota, davul vuruşu, "pıt!", hayvan sesi).

**Uygulamada:**
- Arka plan müziği varsayılan olarak **kısık** çalar (MCI ölçeğinde 140/1000).
- Müzik **aralıklıdır**: 60 saniye çalar, 60 saniye susar, kaldığı yerden devam eder
  (`BackgroundMusic.PlaySeconds` / `RestSeconds`).
- Müziğin sesi kısılamıyorsa (MCI komutu desteklenmiyorsa) müzik **hiç çalınmaz**;
  tam sesli müzik, kısık müzikten kötüdür.
- Yalnızca müziğe ihtiyaç duyan oyunlar müziği başlatır; oyun değişince müzik durur.

## 5. Her eylem duyulur ve görünür olmalı (anında geri bildirim)

Tepki gecikmesi neden-sonuç ilişkisini bozar. Ses ve görsel **aynı karede** tetiklenir.

**Uygulamada:** Ses gecikmesi ~12-35 ms'dir (`WaveMixer`). Efekt ve ses aynı olay
işleyicisinde üretilir; hiçbiri kuyruğa alınıp geciktirilmez.

## 6. Sesler dengeli ve yeterince gür olmalı

Bir oyunun sesi diğerinden belirgin kısıksa çocuk o oyunu "bozuk" algılar.

**Uygulamada:** Davul ve balon sesleri MIDI yerine kod içinde sentezlenip tam ölçeğe
normalize edilir ve `WaveMixer` üzerinden çalınır (MIDI perküsyon örnekleri, ayarlar
tavana çekilse bile piyanodan kısık kalıyordu). Selftest her örneğin tepe seviyesini
kontrol eder.

## 7. Basılı tutmak ekranı boşaltmasın (auto-repeat nezaketi)

Çocuk bir tuşa yüklenip saniyede onlarca tekrar üretebilir. Bu, ses çamurlaşmasına veya
sahnenin bir anda boşalmasına yol açmamalıdır — **ama tepki de kesilmemelidir**.

**Uygulamada:** Basılı tuş takibi yapılır; auto-repeat tekrarında ana eylem (nota, vuruş,
balon patlatma) **tekrarlanmaz**, yerine hafif bir görsel canlanma verilir. Balon
oyununda tarla yarıdan boşalırsa iki kat hızlı balon doğar.

## 8. Sahne asla boş kalmasın

Ekranda yapacak bir şey yoksa çocuk ilgisini kaybeder.

**Uygulamada:** Balon oyunu açılışta tarlayı 12 balonla **eşit dağılımlı** doldurur
(rastgele dağılım kümelenme yapıyordu); patlayanların yerine alttan yenileri doğar.
Tarla yarıdan fazla boşalırsa **acil besleme** devreye girer: üretim aralığı üçte bire
düşer, balonlar ikili doğar ve ekranın hemen altından girerler (hızlı patlatan çocuk
anında yeni hedef bulur). Selftest hem açılış doluluğunu (`BalloonField`) hem 60
patlatma sonrasını (`BalloonAfterPlay`) doğrular.

## 9. Sürpriz ödüller (hayvan sürprizi) — oyun bazında opsiyonel

Her 8-14 eylemden sonra sahneye rastgele bir hayvan çıkar (sesiyle birlikte). Tahmin
edilemez ama sık gerçekleşir; şaşırtma etkisi ilgiyi uzun süre canlı tutar.

**Uygulamada:** `AnimalDirector` (shuffle bag: aynı hayvan üst üste gelmez) + `AnimalCue`
piyano ve davul oyunlarında ortaktır.

**ESNETİLDİ — Balon oyunu:** Balon oyununda hayvan **çıkmaz** (kullanıcı kararı). Bu
oyunun ödülü patlama anının kendisidir; sahne sade kalır ve dikkat balonlarda toplanır.
Yani hayvan sürprizi zorunlu değil, oyuna göre tercih edilen bir araçtır: ödül mekaniği
oyunun kendi eyleminde yeterince güçlüyse eklenmez.

## 10. Çökme ve kilitlenme kesinlikle olmayacak

Uygulama tam ekran, klavyeyi yutan bir kiosk olduğu için bir çökme çocuğu ekranda
kilitli bırakabilir veya ebeveynin klavyesini kullanılamaz hale getirebilir.

**Uygulamada:**
- `Program.Shutdown` her çıkış yolunda (normal, `ThreadException`,
  `UnhandledException`, `ApplicationExit`, `finally`) kancayı ve ses aygıtlarını bırakır.
- Tüm çizim kontrolleri `PaintGuard` ile korunur: `OnPaint` içinden kaçan bir istisna
  WinForms'ta kontrolü kalıcı "kırmızı çarpı" moduna sokar ve oyun görseli bir daha
  çizilmez. Hata o kare atlanarak yutulur ve `%TEMP%\gamesapp-paint.log`'a yazılır.
- **GDI+ tuzağı:** Sıfıra yakın boyutlu şekil/gradyan çizmek sahte
  `OutOfMemoryException` fırlatır. Küçülen her efekt için alt sınır kontrolü konur.
- `--stress` modu binlerce rastgele tuş ve yüzlerce kare çizerek bu hataları avlar.

---

## Yeni oyun eklerken kontrol listesi

1. `IGameModule` uygulayan bir `Control` yaz (`Games/<Ad>/` klasörü altında).
2. 0-255 arası **her** tuşa tepki ver; auto-repeat'i ayrıca ele al (kural 1 ve 7).
3. Sesi `WaveMixer` üzerinden, tam ölçeğe normalize edilmiş örneklerle çal (kural 6).
4. `OnPaint`'i `PaintGuard` ile sar; küçülen şekillerde alt sınır koy (kural 10).
5. `AnimalDirector` + hayvan sürprizini bağla (kural 9).
6. `Stop()` içinde kendi seslerini ve zamanlayıcılarını durdur (oyun değişiminde
   ses taşması olmasın).
7. `Program.RunNormal` içindeki oyun listesine ekle — menü butonu otomatik oluşur.
8. Selftest'e (`--selftest`) ve stres testine (`--stress`) yeni oyunu ekle.
