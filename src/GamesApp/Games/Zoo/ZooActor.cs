using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.Audio;
using GamesApp.UI;
using GamesApp.UI.Effects;

namespace GamesApp.Games.Zoo;

/// <summary>Hayvanın sahneye giriş (ve çıkış) biçimi.</summary>
internal enum ZooEntrance
{
    /// <summary>Yandan ağır ağır yürüyerek girer, adım adım hoplar (fil, inek, koyun).</summary>
    Walk = 0,

    /// <summary>Yandan zıplayarak girer (kurbağa, civciv, ördek).</summary>
    Hop = 1,

    /// <summary>Takla atarak (kendi ekseninde dönerek) girer (maymun).</summary>
    Flip = 2,

    /// <summary>Karnının üstünde eğik biçimde kayarak girer (penguen).</summary>
    Slide = 3,

    /// <summary>Kenardan hızla atılır ve ani durur (aslan, köpek).</summary>
    Pounce = 4,

    /// <summary>Yukarıdan düşer, yere değince iki kez seker (kedi, horoz).</summary>
    Drop = 5
}

/// <summary>
/// Sahnedeki tek bir hayvanın ömrü: girer → durup sesini verir → gider.
///
/// TASARIM: Her hayvanın giriş biçimi (<see cref="ZooEntrance"/>) sabittir; çocuk
/// "kurbağa zıplayarak gelir, penguen kayarak gelir" ilişkisini öğrenir. Buna karşın
/// <b>her geliş farklıdır</b>: giriş yönü, hız, zıplama yüksekliği ve duruş yeri
/// (slot) her seferinde yeniden seçilir.
///
/// Hayvanın çizimi <see cref="AnimalArtist"/> ile vektör olarak yapılır, ses metni
/// <see cref="SpeechBubble"/> ile gösterilir; ikisi de diğer oyunlarla ortaktır.
/// </summary>
internal sealed class ZooActor
{
    /// <summary>Çıkış (sahneden ayrılma) süresi (saniye).</summary>
    private const float ExitSeconds = 0.55f;

    /// <summary>Konuşma balonu girişin bu oranından sonra görünür olur.</summary>
    private const float BubbleAppearAt = 0.45f;

    private readonly float _enterSeconds;
    private readonly float _holdSeconds;

    /// <summary>Hayvanın durduğu noktanın merkez X'i (piksel).</summary>
    private readonly float _targetX;

    /// <summary>Girişin başladığı ekran dışı X (piksel).</summary>
    private readonly float _startX;

    /// <summary>Çıkışın bittiği ekran dışı X (piksel).</summary>
    private readonly float _exitX;

    /// <summary>Hayvanın ayağının bastığı Y (piksel): kutunun ALT kenarı.</summary>
    private readonly float _groundY;

    /// <summary>Kutunun kenar uzunluğu (piksel).</summary>
    private readonly float _side;

    /// <summary>Sahneye girdiği/çıktığı yön: +1 soldan sağa, -1 sağdan sola.</summary>
    private readonly int _direction;

    /// <summary>Girişte kaç zıplama/adım yapılacağı (aynı hayvan hep aynı görünmesin).</summary>
    private readonly float _bounces;

    /// <summary>Salınım fazı: bekleme sırasındaki nefes alma hareketi kaydırılır.</summary>
    private readonly float _wobblePhase;

    private float _age;

    /// <summary>Kalan neşe zıplaması (auto-repeat tepkisi); 1'den 0'a iner.</summary>
    private float _cheer;

    /// <param name="kind">Sahneye çıkan hayvan.</param>
    /// <param name="slot">Duracağı yer (sahnedeki sabit noktalardan biri).</param>
    /// <param name="soundSeconds">Çalınan sesin süresi; bekleme süresi buna göre ayarlanır.</param>
    /// <param name="stageSize">Sahnenin (oyun alanının) piksel boyutu.</param>
    /// <param name="random">Yön/hız çeşitliliği için rastgelelik kaynağı.</param>
    public ZooActor(AnimalKind kind, ZooSlot slot, float soundSeconds, SizeF stageSize, Random random)
    {
        Kind = kind;
        Slot = slot;
        Style = GetEntrance(kind);
        Text = AnimalInfo.GetSoundText(kind);

        float width = Math.Max(1f, stageSize.Width);
        float height = Math.Max(1f, stageSize.Height);

        // Uzaktaki (derinliği düşük) hayvan küçük ve yukarıda, yakındaki büyük ve aşağıda
        // durur: sahne düz bir şerit gibi görünmez, derinlik hissi oluşur.
        float baseSide = Math.Min(width * 0.21f, height * 0.36f);
        _side = baseSide * GetSizeFactor(kind) * (0.80f + slot.Depth * 0.32f);
        _targetX = width * slot.X;
        _groundY = height * (0.70f + slot.Depth * 0.17f);

        // Ekranın hangi kenarından gireceği: durduğu yere UZAK olan kenar seçilir,
        // böylece hayvan sahnede gözle takip edilebilecek kadar yol alır.
        _direction = slot.X <= 0.5f ? +1 : -1;
        if (Style == ZooEntrance.Drop)
        {
            // Yukarıdan düşen hayvanın yönü yalnızca eğilme/takla yönünü belirler.
            _direction = random.Next(2) == 0 ? +1 : -1;
        }

        // Ekran dışı giriş/çıkış noktaları: hayvan tamamen görünmez olacak kadar
        // dışarıda başlar ve orada biter (kenarda yarım hayvan takılı kalmaz).
        float margin = _side * 0.85f;
        if (Style == ZooEntrance.Drop)
        {
            // Yukarıdan gelen hayvan yatayda hiç yol almaz.
            _startX = _targetX;
            _exitX = _targetX;
        }
        else
        {
            _startX = _direction > 0 ? -margin : width + margin;
            _exitX = _direction > 0 ? width + margin : -margin;
        }

        _enterSeconds = GetEnterSeconds(Style) * (0.85f + (float)random.NextDouble() * 0.30f);
        _bounces = GetBounces(Style) + (random.Next(2) == 0 ? 0f : 1f);
        _wobblePhase = (float)(random.NextDouble() * Math.PI * 2.0);

        // Bekleme: ses bitene kadar hayvan sahnede kalır (en az 1,1 sn, en çok 2,8 sn).
        _holdSeconds = Math.Clamp(soundSeconds + 0.45f, 1.1f, 2.8f);
    }

    public AnimalKind Kind { get; }

    /// <summary>Durduğu yer; hayvan gidince slot boşalır.</summary>
    public ZooSlot Slot { get; }

    public ZooEntrance Style { get; }

    /// <summary>Konuşma balonundaki Türkçe ses metni.</summary>
    public string Text { get; }

    /// <summary>Toplam sahne süresi (saniye).</summary>
    public float TotalSeconds => _enterSeconds + _holdSeconds + ExitSeconds;

    public bool IsAlive => _age < TotalSeconds;

    /// <summary>Sahnede ne kadar süredir bulunuyor (en yaşlıyı bulmak için).</summary>
    public float Age => _age;

    /// <summary>Hayvan hedefine ulaştı mı? (Giriş animasyonu bitti.)</summary>
    public bool HasArrived => _age >= _enterSeconds;

    /// <summary>Hayvanın o anki merkez noktası (efektlerin doğduğu yer).</summary>
    public PointF Center
    {
        get
        {
            GetPose(out PointF center, out _, out _, out _);
            return center;
        }
    }

    /// <summary>Hayvanın kutu kenarı (piksel).</summary>
    public float Side => _side;

    public void Update(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return;
        }

        _age += deltaSeconds;

        if (_cheer > 0f)
        {
            // Neşe zıplaması ~0,4 saniyede söner.
            _cheer = Math.Max(0f, _cheer - deltaSeconds * 2.5f);
        }
    }

    /// <summary>
    /// Auto-repeat tepkisi: hayvan yeni gelmez ama sahnedeki hayvan neşeyle zıplar.
    /// Tasarım kuralı 7: basılı tutmak tepkiyi kesmez, sahneyi de boşaltmaz.
    /// </summary>
    public void Cheer()
    {
        _cheer = 1f;
    }

    /// <summary>
    /// Hayvanı hemen çıkış aşamasına geçirir (sahne dolduğunda en yaşlısına uygulanır).
    /// Böylece her tuş basımı yeni bir hayvan getirebilir; neden-sonuç bozulmaz.
    /// </summary>
    public void ForceExit()
    {
        float exitStart = _enterSeconds + _holdSeconds;
        if (_age < exitStart)
        {
            _age = exitStart;
        }
    }

    /// <summary>
    /// Hayvanı ve gölgesini çizer. Konuşma balonu AYRI çizilir
    /// (bkz. <see cref="DrawBubble"/>): tüm hayvanlar çizildikten sonra balonlar en
    /// üste gelsin, yandaki hayvan balonu kapatmasın.
    /// </summary>
    public void Draw(Graphics g, RectangleF area, Font speechFont)
    {
        if (!IsAlive)
        {
            return;
        }

        GetPose(out PointF center, out float scale, out float rotation, out float alpha);

        float drawSide = _side * scale;
        if (drawSide < 12f || alpha <= 0.02f)
        {
            // GDI+ TUZAĞI: sıfıra yakın boyutlu şekiller sahte OutOfMemoryException
            // fırlatır; çok küçülen hayvan hiç çizilmez.
            return;
        }

        var box = new RectangleF(
            area.X + center.X - drawSide * 0.5f,
            area.Y + center.Y - drawSide * 0.5f,
            drawSide,
            drawSide);

        DrawShadow(g, area, center, drawSide, alpha);

        if (Math.Abs(rotation) < 0.5f)
        {
            AnimalArtist.Draw(g, Kind, box, alpha);
        }
        else
        {
            // Takla ve kayma eğimi: hayvan kendi merkezi etrafında döndürülür.
            GraphicsState state = g.Save();
            try
            {
                g.TranslateTransform(box.X + box.Width * 0.5f, box.Y + box.Height * 0.5f);
                g.RotateTransform(rotation);
                g.TranslateTransform(-(box.X + box.Width * 0.5f), -(box.Y + box.Height * 0.5f));
                AnimalArtist.Draw(g, Kind, box, alpha);
            }
            finally
            {
                g.Restore(state);
            }
        }
    }

    /// <summary>
    /// Konuşma balonunu çizer. Balon hayvanla birlikte DÖNMEZ (yazı her zaman düz ve
    /// okunabilir kalır) ve girişin sonuna doğru belirir.
    /// </summary>
    public void DrawBubble(Graphics g, RectangleF area, Font speechFont)
    {
        if (!IsAlive)
        {
            return;
        }

        GetPose(out PointF center, out float scale, out _, out float alpha);

        float drawSide = _side * scale;
        float bubbleAlpha = alpha * GetBubbleFade();

        if (drawSide < 12f || bubbleAlpha <= 0.02f)
        {
            return;
        }

        var box = new RectangleF(
            area.X + center.X - drawSide * 0.5f,
            area.Y + center.Y - drawSide * 0.5f,
            drawSide,
            drawSide);

        // Sahnede yan yana birkaç hayvan olabildiği için balon hayvanın ÜSTÜNE konur:
        // yana konan balon komşu hayvanın yüzüne biniyordu.
        SpeechBubble.Draw(g, area, box, Text, speechFont, bubbleAlpha, preferAbove: true);
    }

    /// <summary>Hayvanın altındaki yumuşak gölge: yerden ne kadar yükseldiğini gösterir.</summary>
    private void DrawShadow(Graphics g, RectangleF area, PointF center, float drawSide, float alpha)
    {
        float feetY = center.Y + drawSide * 0.5f;
        float lift = Math.Max(0f, _groundY - feetY);

        // Yükseldikçe gölge küçülür ve soluklaşır (zıplama hissini güçlendirir).
        float shrink = Math.Clamp(1f - lift / (drawSide * 1.2f), 0.35f, 1f);
        float shadowWidth = drawSide * 0.62f * shrink;
        float shadowHeight = shadowWidth * 0.26f;

        if (shadowWidth < 8f || shadowHeight < 3f)
        {
            return;
        }

        using var brush = new SolidBrush(Color.FromArgb(
            (int)Math.Clamp(90f * shrink * alpha, 0f, 255f),
            4,
            10,
            6));

        g.FillEllipse(
            brush,
            area.X + center.X - shadowWidth * 0.5f,
            area.Y + _groundY - shadowHeight * 0.5f,
            shadowWidth,
            shadowHeight);
    }

    /// <summary>Konuşma balonunun görünürlük çarpanı (girişte belirir, çıkışta kaybolur).</summary>
    private float GetBubbleFade()
    {
        if (_age >= _enterSeconds)
        {
            return 1f;
        }

        float p = _age / _enterSeconds;
        return p <= BubbleAppearAt
            ? 0f
            : (p - BubbleAppearAt) / (1f - BubbleAppearAt);
    }

    /// <summary>
    /// Hayvanın o andaki duruşunu hesaplar: merkez konumu, ölçek, dönme açısı ve
    /// saydamlık. Giriş biçimine göre tamamen farklı yollar izlenir.
    /// </summary>
    private void GetPose(out PointF center, out float scale, out float rotation, out float alpha)
    {
        float restY = _groundY - _side * 0.5f;

        scale = 1f;
        rotation = 0f;
        alpha = 1f;

        if (_age < _enterSeconds)
        {
            GetEnterPose(_age / _enterSeconds, restY, out center, out scale, out rotation);
        }
        else if (_age < _enterSeconds + _holdSeconds)
        {
            GetHoldPose(_age - _enterSeconds, restY, out center, out scale, out rotation);
        }
        else
        {
            float p = Math.Clamp((_age - _enterSeconds - _holdSeconds) / ExitSeconds, 0f, 1f);
            GetExitPose(p, restY, out center, out scale, out rotation, out alpha);
        }

        // Neşe zıplaması (auto-repeat): her fazın üstüne binen kısa hoplama.
        if (_cheer > 0f)
        {
            center = new PointF(
                center.X,
                center.Y - (float)Math.Sin(_cheer * Math.PI) * _side * 0.16f);
        }
    }

    private void GetEnterPose(
        float p,
        float restY,
        out PointF center,
        out float scale,
        out float rotation)
    {
        p = Math.Clamp(p, 0f, 1f);
        scale = 1f;
        rotation = 0f;

        // Yumuşak yavaşlama: hedefe varırken hız düşer (ani duruş sarsıcı olur).
        float easedOut = 1f - (1f - p) * (1f - p) * (1f - p);
        float x = _startX + (_targetX - _startX) * easedOut;
        float y = restY;

        switch (Style)
        {
            case ZooEntrance.Walk:
                // Ağır ağır: her adımda gövde hafifçe yukarı kalkar.
                y = restY - (float)Math.Abs(Math.Sin(p * Math.PI * _bounces)) * _side * 0.06f;
                x = _startX + (_targetX - _startX) * p; // sabit hız: ağırlık hissi
                break;

            case ZooEntrance.Hop:
                // Yüksek parabolik zıplamalar.
                y = restY - (float)Math.Abs(Math.Sin(p * Math.PI * _bounces)) * _side * 0.45f;
                break;

            case ZooEntrance.Flip:
                // Takla: tek bir yüksek kavis + iki tam tur dönüş.
                y = restY - (float)Math.Sin(p * Math.PI) * _side * 0.55f;
                rotation = p * 720f * _direction;
                break;

            case ZooEntrance.Slide:
                // Kayma: hızlı başlar, sürtünerek durur; gövde eğik ve yere yakın.
                x = _startX + (_targetX - _startX) * (1f - (1f - p) * (1f - p));
                y = restY + _side * 0.06f * (1f - p);
                rotation = -16f * _direction * (1f - p * 0.35f);
                break;

            case ZooEntrance.Pounce:
                // Atılış: uzun bir hamle ve hedefte küçük bir "yerine oturma".
                y = restY - (float)Math.Sin(p * Math.PI) * _side * 0.30f;
                scale = 0.92f + 0.08f * easedOut + (float)Math.Sin(p * Math.PI) * 0.06f;
                break;

            case ZooEntrance.Drop:
                // Yukarıdan düşüş: hızlanarak iner, yere değince iki kez seker.
                x = _targetX;
                y = GetDropY(p, restY);
                rotation = 8f * _direction * (1f - p);
                break;
        }

        center = new PointF(x, y);
    }

    /// <summary>
    /// Düşüş eğrisi: ilk %65'te yerçekimiyle hızlanarak iner, kalan sürede
    /// giderek küçülen iki sekme yapar.
    /// </summary>
    private float GetDropY(float p, float restY)
    {
        float startY = -_side * 0.6f;

        if (p < 0.65f)
        {
            float fall = p / 0.65f;
            return startY + (restY - startY) * fall * fall;
        }

        float bouncePhase = (p - 0.65f) / 0.35f;
        float damping = 1f - bouncePhase;
        float lift = (float)Math.Abs(Math.Sin(bouncePhase * Math.PI * 2.0)) * _side * 0.22f * damping;
        return restY - lift;
    }

    private void GetHoldPose(
        float t,
        float restY,
        out PointF center,
        out float scale,
        out float rotation)
    {
        // Bekleme: nefes alma + hafif hoplama. Hayvan asla donuk durmaz.
        float breath = (float)Math.Sin(t * 6.4 + _wobblePhase);
        float bob = (float)Math.Abs(Math.Sin(t * 3.1 + _wobblePhase));

        scale = 1f + breath * 0.025f;
        rotation = Style == ZooEntrance.Slide ? -6f * _direction : breath * 2.5f;

        float x = _targetX;
        float y = restY - bob * _side * 0.04f;

        // Kükreyen/borazan çalan hayvanlar ilk yarım saniyede sarsılır: sesin
        // gücü görsel olarak da hissedilir (aslan, fil).
        if (t < 0.5f && (Kind == AnimalKind.Lion || Kind == AnimalKind.Elephant))
        {
            float shake = (1f - t / 0.5f) * _side * 0.02f;
            x += (float)Math.Sin(t * 60.0) * shake;
            scale += (1f - t / 0.5f) * 0.05f;
        }

        center = new PointF(x, y);
    }

    private void GetExitPose(
        float p,
        float restY,
        out PointF center,
        out float scale,
        out float rotation,
        out float alpha)
    {
        // Çıkış: geldiği yöne devam ederek ekrandan ayrılır; son anda saydamlaşır.
        float eased = p * p;
        float x = _targetX + (_exitX - _targetX) * eased;
        float y = restY;

        scale = 1f;
        rotation = 0f;
        alpha = 1f - Math.Max(0f, (p - 0.6f) / 0.4f);

        switch (Style)
        {
            case ZooEntrance.Hop:
                y = restY - (float)Math.Abs(Math.Sin(p * Math.PI * 2.0)) * _side * 0.38f;
                break;

            case ZooEntrance.Flip:
                y = restY - (float)Math.Sin(p * Math.PI) * _side * 0.40f;
                rotation = p * 360f * _direction;
                break;

            case ZooEntrance.Slide:
                y = restY + _side * 0.06f * p;
                rotation = -16f * _direction;
                break;

            case ZooEntrance.Drop:
                // Geldiği gibi gider: yukarı doğru süzülerek kaybolur.
                x = _targetX;
                y = restY - _side * 1.4f * eased;
                scale = 1f - 0.25f * p;
                alpha = 1f - p;
                break;

            case ZooEntrance.Pounce:
                y = restY - (float)Math.Sin(p * Math.PI) * _side * 0.22f;
                break;
        }

        center = new PointF(x, y);
    }

    // ---------------- Hayvan başına sabitler ----------------

    /// <summary>
    /// Hayvanın giriş biçimi. Sabittir: çocuk "penguen kayarak gelir" ilişkisini
    /// tekrar ederek öğrenir ve bir sonraki gelişi tahmin etmeye çalışır.
    /// </summary>
    public static ZooEntrance GetEntrance(AnimalKind kind) => kind switch
    {
        AnimalKind.Elephant => ZooEntrance.Walk,
        AnimalKind.Cow => ZooEntrance.Walk,
        AnimalKind.Sheep => ZooEntrance.Walk,
        AnimalKind.Frog => ZooEntrance.Hop,
        AnimalKind.Chick => ZooEntrance.Hop,
        AnimalKind.Duck => ZooEntrance.Hop,
        AnimalKind.Monkey => ZooEntrance.Flip,
        AnimalKind.Penguin => ZooEntrance.Slide,
        AnimalKind.Lion => ZooEntrance.Pounce,
        AnimalKind.Dog => ZooEntrance.Pounce,
        AnimalKind.Cat => ZooEntrance.Drop,
        AnimalKind.Rooster => ZooEntrance.Drop,
        _ => ZooEntrance.Hop
    };

    /// <summary>Hayvanın boy çarpanı: fil koca, civciv minik olmalı.</summary>
    private static float GetSizeFactor(AnimalKind kind) => kind switch
    {
        AnimalKind.Elephant => 1.30f,
        AnimalKind.Cow => 1.15f,
        AnimalKind.Lion => 1.12f,
        AnimalKind.Sheep => 1.05f,
        AnimalKind.Monkey => 1.00f,
        AnimalKind.Penguin => 0.95f,
        AnimalKind.Dog => 0.95f,
        AnimalKind.Cat => 0.90f,
        AnimalKind.Rooster => 0.90f,
        AnimalKind.Duck => 0.88f,
        AnimalKind.Frog => 0.85f,
        AnimalKind.Chick => 0.78f,
        _ => 1f
    };

    /// <summary>Giriş süresi: atılan hayvan hızlı, yürüyen hayvan ağır gelir.</summary>
    private static float GetEnterSeconds(ZooEntrance style) => style switch
    {
        ZooEntrance.Walk => 0.90f,
        ZooEntrance.Hop => 0.70f,
        ZooEntrance.Flip => 0.75f,
        ZooEntrance.Slide => 0.55f,
        ZooEntrance.Pounce => 0.38f,
        ZooEntrance.Drop => 0.60f,
        _ => 0.70f
    };

    /// <summary>Girişteki adım/zıplama sayısının tabanı.</summary>
    private static float GetBounces(ZooEntrance style) => style switch
    {
        ZooEntrance.Walk => 4f,
        ZooEntrance.Hop => 3f,
        _ => 1f
    };
}
