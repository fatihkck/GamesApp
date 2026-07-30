using System.Drawing;
using GamesApp.Games;

namespace GamesApp.UI;

/// <summary>
/// Üst menünün oyun şeridi: oyun butonlarını yerleştirir ve gerektiğinde SAYFALAR.
///
/// NEDEN SAYFALAMA: Oyun sayısı arttıkça sabit genişlikli butonlar ekrana sığmaz;
/// eskiden butonlar çıkış butonunun altına taşıyordu. Burada üç aşamalı bir strateji
/// uygulanır:
///  1. Tüm oyunlar sığıyorsa hepsi tek sayfada görünür ve boşluğu eşit paylaşır
///     (buton belirli bir genişlikten daha da büyütülmez: 3 oyunla dev butonlar olmaz).
///  2. Sığmıyorsa butonlar en küçük okunur boyutlarına iner ve şerit sayfalanır;
///     iki yanda ◀ ▶ okları ve altta sayfa noktaları çıkar.
///  3. Buton yine dar kalırsa oyun adı gizlenir, büyük simge kalır
///     (bkz. <see cref="GameMenuButton"/>).
///
/// Oyun değiştirildiğinde aktif oyunun bulunduğu sayfaya otomatik geçilir; böylece
/// seçili oyun her zaman görünür olur.
///
/// KLAVYE: Bu şerit yalnızca FARE ile kullanılır. Klavyenin tamamı aktif oyuna aittir
/// (global kanca her tuşu yutar), bu yüzden hiçbir kontrol odak almaz.
/// </summary>
internal sealed class GameMenuStrip : Panel
{
    /// <summary>Şeridin iç kenar boşluğu (piksel).</summary>
    private const int Pad = 6;

    /// <summary>Butonlar arası boşluk (piksel).</summary>
    private const int Gap = 8;

    /// <summary>Sayfa oklarının genişliği (piksel).</summary>
    private const int ArrowWidth = 44;

    /// <summary>Butonun inebileceği en küçük genişlik (bu boyutta yalnızca simge kalır).</summary>
    private const int MinButtonWidth = 96;

    /// <summary>Butonun büyüyebileceği en fazla genişlik (az oyunla dev buton olmasın).</summary>
    private const int MaxButtonWidth = 224;

    /// <summary>Sayfa noktaları için ayrılan yükseklik (piksel).</summary>
    private const int DotsHeight = 14;

    private readonly List<GameMenuButton> _buttons = new();
    private readonly Button _previousPage;
    private readonly Button _nextPage;
    private readonly Font _arrowFont = new("Segoe UI", 16f, FontStyle.Bold);

    private int _page;
    private int _pageCount = 1;
    private int _perPage;
    private int _selectedIndex = -1;
    private bool _disposedResources;

    public GameMenuStrip(IReadOnlyList<IGameModule> games)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
        SetStyle(ControlStyles.Selectable, false);

        TabStop = false;
        BackColor = Theme.BackgroundDeep;

        for (int i = 0; i < games.Count; i++)
        {
            IGameModule game = games[i];
            var button = new GameMenuButton(i, game.MenuIcon, game.MenuTitle, game.MenuColor);
            button.Click += OnGameButtonClick;

            _buttons.Add(button);
            Controls.Add(button);
        }

        _previousPage = CreateArrowButton("◀");
        _previousPage.Click += OnPreviousPageClick;
        Controls.Add(_previousPage);

        _nextPage = CreateArrowButton("▶");
        _nextPage.Click += OnNextPageClick;
        Controls.Add(_nextPage);
    }

    /// <summary>Fareyle bir oyun seçildi (oyunun listedeki sırası).</summary>
    public event Action<int>? GameSelected;

    /// <summary>Şu anda kaç sayfa var? (Selftest ve teşhis için.)</summary>
    internal int PageCount => _pageCount;

    /// <summary>Şu anda görünen sayfa (0 tabanlı).</summary>
    internal int CurrentPage => _page;

    /// <summary>
    /// Aktif oyunu işaretler ve gerekiyorsa o oyunun bulunduğu sayfaya geçer.
    /// </summary>
    public void SetSelectedGame(int index)
    {
        _selectedIndex = index;

        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].Selected = i == index;
        }

        LayoutChildren(snapToSelection: true);
    }

    private Button CreateArrowButton(string glyph)
    {
        var button = new Button
        {
            Text = glyph,
            Font = _arrowFont,
            ForeColor = Color.White,
            BackColor = Theme.Lerp(Theme.BackgroundDeep, Color.White, 0.12f),
            FlatStyle = FlatStyle.Flat,
            TabStop = false,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Visible = false
        };

        button.FlatAppearance.BorderColor = Theme.Lerp(Theme.BackgroundDeep, Color.White, 0.35f);
        button.FlatAppearance.BorderSize = 2;
        button.FlatAppearance.MouseOverBackColor = Theme.Lerp(Theme.BackgroundDeep, Color.White, 0.26f);
        button.FlatAppearance.MouseDownBackColor = Theme.Lerp(Theme.BackgroundDeep, Color.White, 0.40f);

        return button;
    }

    private void OnGameButtonClick(object? sender, EventArgs e)
    {
        if (sender is GameMenuButton button)
        {
            GameSelected?.Invoke(button.GameIndex);
        }
    }

    private void OnPreviousPageClick(object? sender, EventArgs e)
    {
        // Kiosk sadeliği: sayfalar döngüseldir, ok butonu asla "ölü" görünmez.
        _page = (_page - 1 + _pageCount) % _pageCount;
        LayoutChildren(snapToSelection: false);
    }

    private void OnNextPageClick(object? sender, EventArgs e)
    {
        _page = (_page + 1) % _pageCount;
        LayoutChildren(snapToSelection: false);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren(snapToSelection: true);
    }

    /// <summary>
    /// Butonları yerleştirir, sayfa sayısını hesaplar ve yalnızca geçerli sayfadaki
    /// butonları görünür bırakır.
    /// </summary>
    /// <param name="snapToSelection">
    /// true ise aktif oyunun sayfasına geçilir (oyun değişimi / boyut değişimi).
    /// false ise kullanıcının ok ile seçtiği sayfa korunur.
    /// </param>
    private void LayoutChildren(bool snapToSelection)
    {
        int width = ClientSize.Width;
        int height = ClientSize.Height;
        int count = _buttons.Count;

        if (width <= 0 || height <= 0 || count == 0)
        {
            return;
        }

        int available = width - Pad * 2;

        // 1) Hepsi tek sayfaya sığıyor mu? (En küçük okunur genişlikle ölçülür.)
        bool singlePage = count * MinButtonWidth + (count - 1) * Gap <= available;

        int stripWidth = singlePage
            ? available
            : available - 2 * (ArrowWidth + Gap);

        if (singlePage)
        {
            _perPage = count;
            _pageCount = 1;
            _page = 0;
        }
        else
        {
            _perPage = Math.Max(1, (stripWidth + Gap) / (MinButtonWidth + Gap));
            _pageCount = (count + _perPage - 1) / _perPage;

            _page = snapToSelection && _selectedIndex >= 0
                ? _selectedIndex / _perPage
                : Math.Clamp(_page, 0, _pageCount - 1);
        }

        bool paging = _pageCount > 1;
        int buttonHeight = Math.Max(24, height - Pad * 2 - (paging ? DotsHeight : 0));

        // 2) Buton genişliği: boşluk eşit paylaşılır ama alt/üst sınırlar aşılmaz.
        int slots = Math.Max(1, _perPage);
        int shared = (stripWidth - (slots - 1) * Gap) / slots;
        int buttonWidth = Math.Clamp(shared, MinButtonWidth, MaxButtonWidth);

        // 3) Bu sayfadaki butonlar şeridin ortasına hizalanır.
        int firstIndex = _page * _perPage;
        int visibleCount = Math.Clamp(count - firstIndex, 1, _perPage);
        int used = visibleCount * buttonWidth + (visibleCount - 1) * Gap;

        int stripLeft = Pad + (paging ? ArrowWidth + Gap : 0);
        int x = stripLeft + Math.Max(0, (stripWidth - used) / 2);

        for (int i = 0; i < count; i++)
        {
            GameMenuButton button = _buttons[i];
            bool onPage = i >= firstIndex && i < firstIndex + _perPage;

            button.Visible = onPage;
            if (!onPage)
            {
                continue;
            }

            button.SetBounds(x, Pad, buttonWidth, buttonHeight);
            x += buttonWidth + Gap;
        }

        _previousPage.Visible = paging;
        _nextPage.Visible = paging;

        if (paging)
        {
            int arrowHeight = Math.Min(buttonHeight, Math.Max(32, buttonHeight - 8));
            int arrowY = Pad + (buttonHeight - arrowHeight) / 2;

            _previousPage.SetBounds(Pad, arrowY, ArrowWidth, arrowHeight);
            _nextPage.SetBounds(width - Pad - ArrowWidth, arrowY, ArrowWidth, arrowHeight);
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            PaintCore(e.Graphics);
        }
        catch (Exception ex)
        {
            PaintGuard.Report(nameof(GameMenuStrip), ex);
        }
    }

    /// <summary>
    /// Zemin ve (sayfalama varsa) altta sayfa noktaları. Noktalar, "başka oyunlar da
    /// var" bilgisini yazı okumadan verir.
    /// </summary>
    private void PaintCore(Graphics g)
    {
        g.Clear(Theme.BackgroundDeep);

        if (_pageCount <= 1)
        {
            return;
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        float size = 7f;
        float spacing = size * 2.2f;
        float totalWidth = _pageCount * spacing - (spacing - size);
        float x = (ClientSize.Width - totalWidth) * 0.5f;
        float y = ClientSize.Height - DotsHeight * 0.5f - size * 0.5f - Pad * 0.5f;

        using var active = new SolidBrush(Color.FromArgb(255, 236, 238, 248));
        using var idle = new SolidBrush(Color.FromArgb(255, 96, 102, 128));

        for (int i = 0; i < _pageCount; i++)
        {
            g.FillEllipse(i == _page ? active : idle, x + i * spacing, y, size, size);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;

            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].Click -= OnGameButtonClick;
            }

            _previousPage.Click -= OnPreviousPageClick;
            _nextPage.Click -= OnNextPageClick;
            _arrowFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
