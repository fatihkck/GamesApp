using System.Drawing;
using System.Drawing.Drawing2D;
using GamesApp.UI;

namespace GamesApp.Games.Drums;

/// <summary>
/// Ekranın altında çizilen bateri seti. Gerçek bir set gibi görünür: davullar
/// silindir gövdeli (kasnak, vida ve sehpalarıyla), ziller eğik metalik tabaklar,
/// kick önden beyaz derili ve pedallıdır. Vurulan parça parlar ve hafifçe büyür,
/// ziller sallanır; iki AHŞAP BAGET vurulan parçaya süzülür, arkasında hareket izi
/// (swoosh) bırakır ve temas anında ışık patlaması çıkar.
/// Fare ile parçaya tıklamak da vuruş sayılır.
///
/// Çizim tamamen GDI+ ile yapılır; görsel dosya gerekmez. Parlaklık sönümü ve
/// baget animasyonu, EffectCanvas'ın kare olayından beslenen <see cref="Advance"/>
/// ile ilerletilir (kendi zamanlayıcısı yoktur).
/// </summary>
internal sealed class DrumKitView : Control
{
    /// <summary>Parlaklığın saniyedeki sönümlenme oranı.</summary>
    private const float GlowDecayPerSecond = 2.6f;

    /// <summary>Baget hamlesinin saniyedeki geri çekilme oranı.</summary>
    private const float StrikeDecayPerSecond = 5.0f;

    // Metal ve ahşap tonları (sehpalar, kasnaklar, bagetler).
    private static readonly Color MetalLight = Color.FromArgb(255, 200, 205, 218);
    private static readonly Color MetalDark = Color.FromArgb(255, 120, 126, 142);
    private static readonly Color WoodLight = Color.FromArgb(255, 233, 196, 142);
    private static readonly Color WoodDark = Color.FromArgb(255, 176, 132, 78);
    private static readonly Color SkinColor = Color.FromArgb(255, 248, 246, 240);

    /// <summary>Parça başına parlaklık (0-1).</summary>
    private readonly float[] _glow = new float[DrumKit.Pieces.Length];

    /// <summary>Çizim sırası: arkadakiler önce (ziller ve tomlar arkada, kick/trampet önde).</summary>
    private static readonly int[] DrawOrder =
    {
        DrumKit.CrashIndex,
        DrumKit.RideIndex,
        DrumKit.HiHatIndex,
        DrumKit.TomHighIndex,
        DrumKit.TomMidIndex,
        DrumKit.TomFloorIndex,
        DrumKit.KickIndex,
        DrumKit.SnareIndex
    };

    /// <summary>Tek bir bagetin animasyon durumu.</summary>
    private struct StickState
    {
        /// <summary>Hedef parça indeksi (-1 = hedef yok, dinlenme konumunda).</summary>
        public int TargetPiece;

        /// <summary>Hamle miktarı: 1 = parçaya değiyor, 0 = dinlenmede.</summary>
        public float Strike;
    }

    /// <summary>İki baget: [0] sol, [1] sağ.</summary>
    private readonly StickState[] _sticks = new StickState[2];

    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White, 2f);

    /// <summary>Baget gövdesi için tekrar kullanılan tampon (6 köşe).</summary>
    private readonly PointF[] _stickBuffer = new PointF[6];

    private bool _disposedResources;

    public DrumKitView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);

        TabStop = false;
        BackColor = Theme.BackgroundDeep;

        _sticks[0].TargetPiece = -1;
        _sticks[1].TargetPiece = -1;
    }

    /// <summary>Fare ile bir parçaya vuruldu (parça indeksi, velocity).</summary>
    public event Action<int, int>? PieceHit;

    /// <summary>Bir parçaya vuruş animasyonu başlatır (ses çalmaz; sesi oyun modülü çalar).</summary>
    public void Strike(int pieceIndex, float intensity = 1f)
    {
        if (pieceIndex < 0 || pieceIndex >= DrumKit.Pieces.Length)
        {
            return;
        }

        _glow[pieceIndex] = Math.Clamp(Math.Max(_glow[pieceIndex], intensity), 0f, 1f);

        // Sol yarıdaki parçalara sol baget, sağ yarıdakilere sağ baget hamle yapar.
        int stick = DrumKit.Pieces[pieceIndex].X < 0.5f ? 0 : 1;
        _sticks[stick].TargetPiece = pieceIndex;
        _sticks[stick].Strike = 1f;

        Invalidate();
    }

    /// <summary>Parlaklık ve baget animasyonlarını ilerletir. Her karede oyun modülü çağırır.</summary>
    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return;
        }

        bool changed = false;
        float glowDecay = GlowDecayPerSecond * deltaSeconds;

        for (int i = 0; i < _glow.Length; i++)
        {
            if (_glow[i] > 0f)
            {
                _glow[i] = Math.Max(0f, _glow[i] - glowDecay);
                changed = true;
            }
        }

        float strikeDecay = StrikeDecayPerSecond * deltaSeconds;
        for (int i = 0; i < _sticks.Length; i++)
        {
            if (_sticks[i].Strike > 0f)
            {
                _sticks[i].Strike = Math.Max(0f, _sticks[i].Strike - strikeDecay);
                changed = true;
            }
        }

        if (changed)
        {
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // İstisna OnPaint'ten kaçarsa WinForms kontrolü kalıcı "kırmızı çarpı"
        // moduna sokar ve bateri görseli bir daha çizilmez; bu yüzden kare
        // bazında yutulur ve hata %TEMP%\gamesapp-paint.log dosyasına yazılır.
        try
        {
            PaintCore(e.Graphics);
        }
        catch (Exception ex)
        {
            PaintGuard.Report(nameof(DrumKitView), ex);
        }
    }

    private void PaintCore(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        _brush.Color = Theme.BackgroundDeep;
        g.FillRectangle(_brush, bounds);

        // Zemin çizgisi: setin "sahnede durduğu" hissini verir.
        float floorY = FloorY(bounds);
        using (var floorPen = new Pen(Color.FromArgb(70, 255, 255, 255), 2f))
        {
            g.DrawLine(floorPen, bounds.Width * 0.02f, floorY, bounds.Width * 0.98f, floorY);
        }

        for (int i = 0; i < DrawOrder.Length; i++)
        {
            DrawPiece(g, DrawOrder[i], bounds);
        }

        DrawStick(g, 0, bounds);
        DrawStick(g, 1, bounds);

        // Üst kenara ince bir vurgu çizgisi (piyano görünümüyle tutarlı).
        using var edgePen = new Pen(Color.FromArgb(90, 255, 255, 255), 2f);
        g.DrawLine(edgePen, 0f, 1f, bounds.Width, 1f);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Arka plan OnPaint içinde çiziliyor.
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        int piece = HitTest(e.Location);
        if (piece >= 0)
        {
            PieceHit?.Invoke(piece, 124);
        }
    }

    // ---------------- Geometri ----------------

    private static float FloorY(Rectangle bounds) => bounds.Height * 0.96f;

    /// <summary>Parçanın merkez ve yarıçapını piksele çevirir (yarıçap taşmaya karşı sınırlı).</summary>
    private static void GetPieceGeometry(int index, Rectangle bounds, out float cx, out float cy, out float rx)
    {
        DrumPieceInfo piece = DrumKit.Pieces[index];

        cx = piece.X * bounds.Width;
        cy = piece.Y * bounds.Height;

        float baseRadius = piece.Radius * bounds.Height;
        float maxRadius = bounds.Width * 0.11f;
        rx = Math.Min(baseRadius, maxRadius);
    }

    /// <summary>Silindir gövde derinliği (yarıçapa oranla). Trampet sığ, yer tomu derindir.</summary>
    private static float ShellDepthRatio(int index) => index switch
    {
        DrumKit.SnareIndex => 0.55f,
        DrumKit.TomFloorIndex => 1.00f,
        DrumKit.TomHighIndex or DrumKit.TomMidIndex => 0.85f,
        _ => 0f
    };

    /// <summary>Zil eğimi (derece). Gerçek settekiler gibi hafif yatıktır.</summary>
    private static float CymbalTilt(int index) => index switch
    {
        DrumKit.CrashIndex => -10f,
        DrumKit.RideIndex => 9f,
        _ => -4f
    };

    /// <summary>Bagetin vuracağı nokta (parçanın derisi/tabağı üzerinde).</summary>
    private static PointF HitPoint(int index, Rectangle bounds)
    {
        GetPieceGeometry(index, bounds, out float cx, out float cy, out float rx);

        if (index == DrumKit.KickIndex)
        {
            return new PointF(cx, cy - rx * 0.25f);
        }

        return DrumKit.Pieces[index].IsCymbal
            ? new PointF(cx + rx * 0.35f, cy - rx * 0.05f)
            : new PointF(cx, cy + rx * 0.05f);
    }

    /// <summary>
    /// Verilen noktadaki parçayı bulur; ÖNDE çizilenler önceliklidir (ters çizim sırası).
    /// Küçük parmaklar/fare için vuruş alanı görünenden geniştir. Bulunamazsa -1.
    /// </summary>
    private int HitTest(Point location)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return -1;
        }

        for (int i = DrawOrder.Length - 1; i >= 0; i--)
        {
            int index = DrawOrder[i];
            DrumPieceInfo piece = DrumKit.Pieces[index];
            GetPieceGeometry(index, bounds, out float cx, out float cy, out float rx);

            float centerY = piece.IsCymbal ? cy : cy + rx * 0.25f;
            float ry = piece.IsCymbal ? rx * 0.50f : rx * 1.05f;

            float tolerance = 1.20f;
            float dx = (location.X - cx) / (rx * tolerance);
            float dy = (location.Y - centerY) / (ry * tolerance);
            if (dx * dx + dy * dy <= 1f)
            {
                return index;
            }
        }

        return -1;
    }

    // ---------------- Parça çizimi ----------------

    private void DrawPiece(Graphics g, int index, Rectangle bounds)
    {
        DrumPieceInfo piece = DrumKit.Pieces[index];
        float glow = _glow[index];

        GetPieceGeometry(index, bounds, out float cx, out float cy, out float rx);

        // Vuruşta parça hafifçe büyür.
        rx *= 1f + glow * 0.05f;

        if (index == DrumKit.KickIndex)
        {
            DrawKick(g, piece, glow, cx, cy, rx, bounds);
        }
        else if (piece.IsCymbal)
        {
            DrawCymbal(g, index, piece, glow, cx, cy, rx, bounds);
        }
        else
        {
            DrawShellDrum(g, index, piece, glow, cx, cy, rx, bounds);
        }
    }

    /// <summary>
    /// Parçanın arkasında gövde rengiyle uyumlu, yumuşak bir ışıma (aura) çizer:
    /// iki katman yarı saydam dolgu + ince parlak kenar. Kalın halka yerine bu
    /// kullanılır; blob gibi görünmez, vuruş "ışık saçıyor" hissi verir.
    /// </summary>
    private void DrawGlowAura(Graphics g, DrumPieceInfo piece, float glow, RectangleF area)
    {
        if (glow <= 0.01f)
        {
            return;
        }

        Color color = Theme.Lerp(piece.BodyColor, Color.White, 0.30f);

        RectangleF outer = area;
        outer.Inflate(10f + glow * 22f, 10f + glow * 22f);
        _brush.Color = Theme.WithAlpha(color, glow * 0.14f);
        g.FillEllipse(_brush, outer);

        RectangleF inner = area;
        inner.Inflate(4f + glow * 9f, 4f + glow * 9f);
        _brush.Color = Theme.WithAlpha(color, glow * 0.20f);
        g.FillEllipse(_brush, inner);

        _pen.Color = Theme.WithAlpha(color, glow * 0.85f);
        _pen.Width = 3f;
        g.DrawEllipse(_pen, inner);
    }

    /// <summary>Metalik dikey boru çizer (sehpa gövdesi).</summary>
    private void DrawPole(Graphics g, float x, float topY, float bottomY, float width)
    {
        _pen.Color = MetalDark;
        _pen.Width = width;
        g.DrawLine(_pen, x, topY, x, bottomY);

        _pen.Color = MetalLight;
        _pen.Width = Math.Max(1f, width * 0.4f);
        g.DrawLine(_pen, x - width * 0.18f, topY, x - width * 0.18f, bottomY);
    }

    /// <summary>Sehpanın zemine oturan üç ayaklı tabanını çizer.</summary>
    private void DrawTripod(Graphics g, float x, float bottomY, float spread, float legHeight)
    {
        _pen.Color = MetalDark;
        _pen.Width = Math.Max(2.5f, spread * 0.10f);
        g.DrawLine(_pen, x, bottomY - legHeight, x - spread, bottomY);
        g.DrawLine(_pen, x, bottomY - legHeight, x + spread, bottomY);
        g.DrawLine(_pen, x, bottomY - legHeight, x, bottomY);
    }

    /// <summary>Trampet, tomlar ve yer tomu: silindir gövde + kasnak + vidalar + beyaz deri.</summary>
    private void DrawShellDrum(
        Graphics g,
        int index,
        DrumPieceInfo piece,
        float glow,
        float cx,
        float cy,
        float rx,
        Rectangle bounds)
    {
        float skinRy = rx * 0.32f;
        float shellDepth = rx * ShellDepthRatio(index);
        float floorY = FloorY(bounds);

        // --- Ayak/sehpa (gövdeden ÖNCE çizilir ki arkada kalsın) ---
        if (index == DrumKit.SnareIndex)
        {
            DrawPole(g, cx, cy + shellDepth, floorY, Math.Max(3f, rx * 0.07f));
            DrawTripod(g, cx, floorY, rx * 0.75f, rx * 0.45f);
        }
        else if (index == DrumKit.TomFloorIndex)
        {
            // Yer tomunun üç kısa bacağı.
            _pen.Color = MetalDark;
            _pen.Width = Math.Max(3f, rx * 0.06f);
            g.DrawLine(_pen, cx - rx * 0.7f, cy + shellDepth, cx - rx * 0.85f, floorY);
            g.DrawLine(_pen, cx + rx * 0.7f, cy + shellDepth, cx + rx * 0.85f, floorY);
            g.DrawLine(_pen, cx, cy + shellDepth + skinRy * 0.8f, cx, floorY);
        }
        else
        {
            // Tomlar kick üzerine monte: kısa bağlantı borusu.
            DrawPole(g, cx, cy + shellDepth, cy + shellDepth + rx * 0.55f, Math.Max(3f, rx * 0.08f));
        }

        // --- Yumuşak ışıma ---
        DrawGlowAura(g, piece, glow, new RectangleF(
            cx - rx,
            cy - skinRy,
            rx * 2f,
            shellDepth + skinRy * 2f));

        // --- Silindir gövde: yatay gradyanla (kenarlar koyu, orta parlak) ---
        Color bodyLight = Theme.Lerp(piece.BodyColor, Color.White, 0.42f + glow * 0.25f);
        Color bodyDark = Theme.Lerp(piece.BodyColor, Color.Black, 0.42f);

        var shellRect = new RectangleF(cx - rx, cy, rx * 2f, shellDepth + skinRy);
        if (shellRect.Height > 0f)
        {
            using var shellBrush = new LinearGradientBrush(
                new RectangleF(shellRect.X - 1f, shellRect.Y - 1f, shellRect.Width + 2f, shellRect.Height + 2f),
                bodyDark,
                bodyDark,
                LinearGradientMode.Horizontal);

            var blend = new ColorBlend(3)
            {
                Colors = new[] { bodyDark, bodyLight, bodyDark },
                Positions = new[] { 0f, 0.42f, 1f }
            };
            shellBrush.InterpolationColors = blend;

            g.FillRectangle(shellBrush, cx - rx, cy, rx * 2f, shellDepth);
            g.FillEllipse(shellBrush, cx - rx, cy + shellDepth - skinRy, rx * 2f, skinRy * 2f);
        }

        // --- Alt kasnak ---
        _pen.Color = MetalLight;
        _pen.Width = Math.Max(2.5f, rx * 0.06f);
        g.DrawArc(_pen, cx - rx, cy + shellDepth - skinRy, rx * 2f, skinRy * 2f, 0f, 180f);

        // --- Vidalar (lug): gövdenin önünde küçük metal çubuklar ---
        float lugWidth = Math.Max(3f, rx * 0.09f);
        float lugHeight = Math.Max(6f, shellDepth * 0.55f);
        float[] lugOffsets = { -0.68f, -0.24f, 0.24f, 0.68f };
        for (int i = 0; i < lugOffsets.Length; i++)
        {
            float lx = cx + rx * lugOffsets[i] - lugWidth / 2f;
            float ly = cy + shellDepth * 0.22f;

            _brush.Color = MetalLight;
            g.FillRectangle(_brush, lx, ly, lugWidth, lugHeight);
            _pen.Color = MetalDark;
            _pen.Width = 1f;
            g.DrawRectangle(_pen, lx, ly, lugWidth, lugHeight);
        }

        // --- Üst kasnak + deri ---
        _pen.Color = MetalLight;
        _pen.Width = Math.Max(3f, rx * 0.09f);
        g.DrawEllipse(_pen, cx - rx, cy - skinRy, rx * 2f, skinRy * 2f);

        float skinScale = 0.92f;
        var skinRect = new RectangleF(
            cx - rx * skinScale,
            cy - skinRy * skinScale,
            rx * 2f * skinScale,
            skinRy * 2f * skinScale);

        _brush.Color = Theme.Lerp(SkinColor, Theme.GetNoteColor(piece.ColorNote), glow * 0.55f);
        g.FillEllipse(_brush, skinRect);

        // Derinin alt kenarına hafif gölge: hafif üç boyut hissi.
        _pen.Color = Color.FromArgb(60, 60, 60, 80);
        _pen.Width = 1.5f;
        g.DrawEllipse(_pen, skinRect);
    }

    /// <summary>Kick davulu: önden görünüm — renkli çember kasnak, beyaz ön deri, ayaklar ve pedal.</summary>
    private void DrawKick(
        Graphics g,
        DrumPieceInfo piece,
        float glow,
        float cx,
        float cy,
        float rx,
        Rectangle bounds)
    {
        float floorY = FloorY(bounds);

        // --- Ayaklar ---
        _pen.Color = MetalDark;
        _pen.Width = Math.Max(3.5f, rx * 0.05f);
        g.DrawLine(_pen, cx - rx * 0.80f, cy + rx * 0.35f, cx - rx * 1.02f, floorY);
        g.DrawLine(_pen, cx + rx * 0.80f, cy + rx * 0.35f, cx + rx * 1.02f, floorY);

        // --- Yumuşak ışıma ---
        DrawGlowAura(g, piece, glow, new RectangleF(cx - rx, cy - rx, rx * 2f, rx * 2f));

        // --- Renkli kasnak (dış çember) ---
        var hoop = new RectangleF(cx - rx, cy - rx, rx * 2f, rx * 2f);
        Color hoopLight = Theme.Lerp(piece.BodyColor, Color.White, 0.40f + glow * 0.25f);
        Color hoopDark = Theme.Lerp(piece.BodyColor, Color.Black, 0.35f);
        using (var hoopBrush = new LinearGradientBrush(hoop, hoopLight, hoopDark, LinearGradientMode.Vertical))
        {
            g.FillEllipse(hoopBrush, hoop);
        }

        // --- Beyaz ön deri ---
        float headScale = 0.80f;
        var head = new RectangleF(
            cx - rx * headScale,
            cy - rx * headScale,
            rx * 2f * headScale,
            rx * 2f * headScale);

        _brush.Color = Theme.Lerp(SkinColor, Theme.GetNoteColor(piece.ColorNote), glow * 0.45f);
        g.FillEllipse(_brush, head);

        _pen.Color = Color.FromArgb(80, 70, 70, 90);
        _pen.Width = 2f;
        g.DrawEllipse(_pen, head);

        // Derinin ortasında küçük renkli rozet (gerçek setlerdeki logo gibi).
        float badge = rx * 0.14f;
        _brush.Color = Theme.WithAlpha(piece.BodyColor, 0.85f);
        g.FillEllipse(_brush, cx - badge, cy - badge, badge * 2f, badge * 2f);

        // --- Kasnak vidaları: çember üzerinde 8 metal çivi ---
        float studRadius = Math.Max(2.5f, rx * 0.045f);
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4.0 + Math.PI / 8.0;
            float sx = cx + (float)Math.Cos(angle) * rx * 0.90f;
            float sy = cy + (float)Math.Sin(angle) * rx * 0.90f;

            _brush.Color = MetalLight;
            g.FillEllipse(_brush, sx - studRadius, sy - studRadius, studRadius * 2f, studRadius * 2f);
        }

        // --- Pedal ve tokmak ---
        float pedalWidth = rx * 0.30f;
        _brush.Color = MetalDark;
        g.FillRectangle(_brush, cx - pedalWidth / 2f, floorY - rx * 0.06f, pedalWidth, rx * 0.06f);

        _pen.Color = MetalLight;
        _pen.Width = Math.Max(2.5f, rx * 0.035f);
        g.DrawLine(_pen, cx, floorY - rx * 0.05f, cx, cy + rx * 0.45f);

        float beater = rx * 0.09f;
        _brush.Color = Color.FromArgb(255, 230, 225, 215);
        g.FillEllipse(_brush, cx - beater, cy + rx * 0.45f - beater, beater * 2f, beater * 2f);
    }

    /// <summary>Ziller: eğik metalik tabak, oluk çizgileri, göbek ve sehpa. Vuruşta sallanır.</summary>
    private void DrawCymbal(
        Graphics g,
        int index,
        DrumPieceInfo piece,
        float glow,
        float cx,
        float cy,
        float rx,
        Rectangle bounds)
    {
        float ry = rx * 0.22f;
        float floorY = FloorY(bounds);
        bool isHiHat = index == DrumKit.HiHatIndex;

        // --- Sehpa ---
        DrawPole(g, cx, cy, floorY, Math.Max(3f, rx * 0.05f));
        DrawTripod(g, cx, floorY, rx * 0.55f, rx * 0.35f);

        if (isHiHat)
        {
            // Hi-hat pedalı.
            _brush.Color = MetalDark;
            g.FillRectangle(_brush, cx - rx * 0.16f, floorY - rx * 0.07f, rx * 0.32f, rx * 0.07f);
        }

        // Vuruşta zil sallanır: parlaklıkla sönen küçük bir açı salınımı.
        float wobble = (float)Math.Sin(glow * Math.PI * 3.0) * glow * 8f;

        // Hi-hat çift tabaklıdır: alttaki sabit tabak önce çizilir.
        if (isHiHat)
        {
            DrawCymbalPlate(g, piece, 0f, 0f, cx, cy + ry * 0.9f, rx * 0.97f, ry, flip: true);
        }

        // --- Yumuşak ışıma ---
        DrawGlowAura(g, piece, glow, new RectangleF(cx - rx, cy - ry * 2.2f, rx * 2f, ry * 4.4f));

        DrawCymbalPlate(g, piece, CymbalTilt(index), wobble, cx, cy, rx, ry, flip: false);
    }

    /// <summary>Tek bir zil tabağını (gradyan + oluklar + göbek) verilen eğimle çizer.</summary>
    private void DrawCymbalPlate(
        Graphics g,
        DrumPieceInfo piece,
        float tilt,
        float wobble,
        float cx,
        float cy,
        float rx,
        float ry,
        bool flip)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(tilt + wobble);

        var plate = new RectangleF(-rx, -ry, rx * 2f, ry * 2f);

        Color light = Theme.Lerp(piece.BodyColor, Color.White, flip ? 0.30f : 0.50f);
        Color dark = Theme.Lerp(piece.BodyColor, Color.FromArgb(255, 130, 90, 25), 0.55f);
        using (var plateBrush = new LinearGradientBrush(plate, light, dark, LinearGradientMode.Vertical))
        {
            g.FillEllipse(plateBrush, plate);
        }

        // Oluk çizgileri: iç içe iki soluk elips, tornalanmış metal hissi verir.
        _pen.Color = Color.FromArgb(70, 120, 85, 20);
        _pen.Width = 1.2f;
        g.DrawEllipse(_pen, -rx * 0.72f, -ry * 0.72f, rx * 1.44f, ry * 1.44f);
        g.DrawEllipse(_pen, -rx * 0.45f, -ry * 0.45f, rx * 0.90f, ry * 0.90f);

        // Kenar çizgisi.
        _pen.Color = Color.FromArgb(210, 255, 240, 190);
        _pen.Width = 2f;
        g.DrawEllipse(_pen, plate);

        if (!flip)
        {
            // Zil göbeği (bell): parlak tepe.
            float bell = rx * 0.20f;
            _brush.Color = Theme.Lerp(piece.BodyColor, Color.White, 0.62f);
            g.FillEllipse(_brush, -bell, -bell * (ry / rx) - ry * 0.15f, bell * 2f, bell * 2f * (ry / rx));
        }

        g.Restore(state);
    }

    // ---------------- Bagetler ----------------

    /// <summary>
    /// Gösterişli ahşap baget: kalın tutamaçtan ince boyna doğru sivrilen gövde,
    /// meşe palamudu biçimli uç. Vuruş sırasında arkasında yarı saydam bir hareket
    /// izi (swoosh) kalır; temas anında uçta ışık patlaması çizilir.
    /// </summary>
    private void DrawStick(Graphics g, int stickIndex, Rectangle bounds)
    {
        StickState stick = _sticks[stickIndex];

        // Tutamaç (omuz) noktaları: setin önünde, alt kenarın hemen dışında.
        var butt = new PointF(
            bounds.Width * (stickIndex == 0 ? 0.38f : 0.62f),
            bounds.Height * 1.03f);

        // Dinlenme ucu: iki baget setin ortasında çapraz durur (vitrindeki gibi).
        var restTip = new PointF(
            bounds.Width * (stickIndex == 0 ? 0.545f : 0.455f),
            bounds.Height * 0.42f);

        PointF tip = restTip;
        float strike = stick.Strike;

        if (stick.TargetPiece >= 0 && strike > 0f)
        {
            PointF hit = HitPoint(stick.TargetPiece, bounds);
            tip = Lerp(restTip, hit, strike);

            // Hareket izi: ucun HEMEN ARKASINDA kısa bir kuyruk (tüm yol değil);
            // uzun bir tel gibi görünmesin, kuyruklu yıldız gibi süzülsün.
            DrawSwoosh(g, Lerp(restTip, tip, 0.55f), tip, strike, bounds);
        }

        DrawStickBody(g, butt, tip, bounds);

        // Temas parıltısı: baget hedefe değdiği anda uçta yıldız patlaması.
        if (stick.TargetPiece >= 0 && strike > 0.62f)
        {
            float flash = (strike - 0.62f) / 0.38f;
            DrawImpactFlash(g, tip, flash, stick.TargetPiece, bounds);
        }
    }

    /// <summary>Bagetin sivrilen ahşap gövdesini ve palamut ucunu çizer.</summary>
    private void DrawStickBody(Graphics g, PointF butt, PointF tip, Rectangle bounds)
    {
        float dx = tip.X - butt.X;
        float dy = tip.Y - butt.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < 1f)
        {
            return;
        }

        // Birim yön ve dikme vektörü.
        float ux = dx / length;
        float uy = dy / length;
        float px = -uy;
        float py = ux;

        // Genişlikler: tutamaç kalın, boyun ince (gerçek baget profili).
        float buttWidth = Math.Max(7f, bounds.Height * 0.030f);
        float midWidth = buttWidth * 0.72f;
        float neckWidth = buttWidth * 0.42f;

        var mid = new PointF(butt.X + dx * 0.68f, butt.Y + dy * 0.68f);
        var neck = new PointF(butt.X + dx * 0.94f, butt.Y + dy * 0.94f);

        _stickBuffer[0] = new PointF(butt.X + px * buttWidth, butt.Y + py * buttWidth);
        _stickBuffer[1] = new PointF(mid.X + px * midWidth, mid.Y + py * midWidth);
        _stickBuffer[2] = new PointF(neck.X + px * neckWidth, neck.Y + py * neckWidth);
        _stickBuffer[3] = new PointF(neck.X - px * neckWidth, neck.Y - py * neckWidth);
        _stickBuffer[4] = new PointF(mid.X - px * midWidth, mid.Y - py * midWidth);
        _stickBuffer[5] = new PointF(butt.X - px * buttWidth, butt.Y - py * buttWidth);

        // Ahşap gradyanı: tutamaçtan uca doğru açılan ton.
        using (var woodBrush = new LinearGradientBrush(butt, tip, WoodDark, WoodLight))
        {
            g.FillPolygon(woodBrush, _stickBuffer);
        }

        _pen.Color = Color.FromArgb(150, 120, 84, 46);
        _pen.Width = 1.2f;
        g.DrawPolygon(_pen, _stickBuffer);

        // Palamut uç: hafif damla biçimli parlak topuz.
        float tipRadius = neckWidth * 1.9f;
        _brush.Color = WoodLight;
        g.FillEllipse(_brush, tip.X - tipRadius, tip.Y - tipRadius, tipRadius * 2f, tipRadius * 2f);

        // Uçtaki minik parlama: cilalı ahşap hissi.
        float shine = tipRadius * 0.45f;
        _brush.Color = Color.FromArgb(190, 255, 244, 220);
        g.FillEllipse(
            _brush,
            tip.X - tipRadius * 0.35f - shine / 2f,
            tip.Y - tipRadius * 0.35f - shine / 2f,
            shine,
            shine);
    }

    /// <summary>Vuruş yolunu gösteren kavisli, sönümlü hareket izi.</summary>
    private void DrawSwoosh(Graphics g, PointF from, PointF to, float strike, Rectangle bounds)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < 4f)
        {
            return;
        }

        // Kontrol noktası: yolun ortasından hafif sapma; iz küçük bir yay çizer.
        var control = new PointF(
            from.X + dx * 0.5f - dy * 0.10f,
            from.Y + dy * 0.5f + dx * 0.10f);

        // İki katman: geniş soluk + dar parlak. Alfa hamleyle birlikte söner.
        int alphaWide = (int)(50f * strike);
        int alphaCore = (int)(100f * strike);

        using var path = new GraphicsPath();
        path.AddBezier(
            from,
            control,
            new PointF(control.X + dx * 0.25f, control.Y + dy * 0.25f),
            to);

        using (var widePen = new Pen(Color.FromArgb(alphaWide, 255, 255, 255), Math.Max(6f, bounds.Height * 0.020f)))
        {
            widePen.StartCap = LineCap.Round;
            widePen.EndCap = LineCap.Round;
            g.DrawPath(widePen, path);
        }

        using (var corePen = new Pen(Color.FromArgb(alphaCore, 255, 250, 230), Math.Max(2.5f, bounds.Height * 0.008f)))
        {
            corePen.StartCap = LineCap.Round;
            corePen.EndCap = LineCap.Round;
            g.DrawPath(corePen, path);
        }
    }

    /// <summary>Temas anında baget ucunda parlayan yıldız patlaması.</summary>
    private void DrawImpactFlash(Graphics g, PointF tip, float flash, int pieceIndex, Rectangle bounds)
    {
        float reach = Math.Max(14f, bounds.Height * 0.055f) * flash;

        // GDI+ TUHAFLIĞI (gerçek hata, log ile bulundu): sıfıra yakın boyutlu bir
        // elips çizmek sahte bir OutOfMemoryException ("out of memory" durum kodu)
        // fırlatır ve kontrol kalıcı olarak çizilemez hale gelirdi. Parlama zaten
        // görünmeyecek kadar küçükse hiç çizilmez.
        if (reach < 2f)
        {
            return;
        }

        Color color = Theme.GetNoteColor(DrumKit.Pieces[pieceIndex].ColorNote);

        _pen.Color = Theme.WithAlpha(Color.White, flash * 0.95f);
        _pen.Width = 2.5f;

        for (int i = 0; i < 6; i++)
        {
            double angle = i * Math.PI / 3.0 + 0.35;
            float ex = tip.X + (float)Math.Cos(angle) * reach;
            float ey = tip.Y + (float)Math.Sin(angle) * reach;
            g.DrawLine(_pen, tip.X, tip.Y, ex, ey);
        }

        // Renkli iç halka: patlamanın çekirdeği (alt sınırla asla sıfır boyuta düşmez).
        using var ringPen = new Pen(Theme.WithAlpha(color, flash * 0.8f), 3.5f);
        float ring = Math.Max(1.5f, reach * 0.55f);
        g.DrawEllipse(ringPen, tip.X - ring, tip.Y - ring, ring * 2f, ring * 2f);
    }

    private static PointF Lerp(PointF a, PointF b, float t)
    {
        return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;
            _brush.Dispose();
            _pen.Dispose();
        }

        base.Dispose(disposing);
    }
}
