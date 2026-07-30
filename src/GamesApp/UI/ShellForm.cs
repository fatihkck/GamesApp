using System.Drawing;
using GamesApp.Games;
using GamesApp.Input;
using GamesApp.Interop;

namespace GamesApp.UI;

/// <summary>
/// GamesApp ana penceresi (kabuk): tam ekran, çerçevesiz, en üstte.
/// Üstte FARE ile kullanılan oyun menüsü bulunur; klavyenin tamamı aktif oyuna
/// aittir (global kanca her tuşu yutar), bu yüzden menü yalnızca fareyle çalışır.
///
/// GÖREV ÇUBUĞU KARARI: Görev çubuğunu Shell API (ABM_SETSTATE) ile GİZLEMİYORUZ.
/// Uygulama beklenmedik bir şekilde çökerse kullanıcının görev çubuğu kalıcı olarak
/// kaybolmuş kalırdı. TopMost + ekranın tamamını kaplayan pencere kiosk etkisi için
/// yeterlidir ve geri döndürülemez bir sistem değişikliği yapmaz.
/// </summary>
internal sealed class ShellForm : Form
{
    /// <summary>Odak sigortasının periyodu (ms).</summary>
    private const int FocusTimerIntervalMs = 1000;

    private readonly IReadOnlyList<IGameModule> _games;
    private readonly bool _selfTestMode;

    private readonly Panel _menuBar;
    private readonly Panel _gameHost;
    private readonly List<Button> _gameButtons = new();
    private readonly Button _exitButton;
    private readonly Label _statusLabel;
    private readonly System.Windows.Forms.Timer _focusTimer;

    private readonly Font _menuFont = new("Segoe UI", 20f, FontStyle.Bold);
    private readonly Font _exitFont = new("Segoe UI", 18f, FontStyle.Bold);
    private readonly Font _statusFont = new("Segoe UI", 10f, FontStyle.Regular);

    private IGameModule? _activeGame;
    private bool _shuttingDown;
    private bool _exitApproved;
    private bool _disposedResources;

    /// <summary>
    /// Yerleşim hesabı yapılabilir mi? Kurucu içinde <c>Bounds</c> atandığı anda
    /// OnResize tetiklenir; o noktada alt kontroller henüz oluşturulmamıştır.
    /// </summary>
    private bool _layoutReady;

    public ShellForm(IReadOnlyList<IGameModule> games, bool soundAvailable, bool selfTestMode)
    {
        if (games.Count == 0)
        {
            throw new ArgumentException("En az bir oyun modülü gerekir.", nameof(games));
        }

        _games = games;
        _selfTestMode = selfTestMode;

        // --- Pencere temel ayarları ---
        Text = "GamesApp";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.Background;
        DoubleBuffered = false; // Çizim oyun modüllerinde çift tamponlu yapılıyor.
        TopMost = !selfTestMode;
        Cursor = Cursors.Default;

        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
        Bounds = selfTestMode
            ? new Rectangle(screen.X, screen.Y, Math.Min(1024, screen.Width), Math.Min(640, screen.Height))
            : screen;

        // --- Üst menü çubuğu ---
        _menuBar = new Panel
        {
            BackColor = Theme.BackgroundDeep,
            TabStop = false
        };

        // --- Oyun butonları (fare ile tek oyun değiştirme yolu) ---
        for (int i = 0; i < games.Count; i++)
        {
            IGameModule game = games[i];
            var button = new Button
            {
                Text = game.MenuTitle,
                Font = _menuFont,
                ForeColor = Color.White,
                BackColor = Theme.Lerp(game.MenuColor, Color.Black, 0.55f),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Tag = i
            };
            button.FlatAppearance.BorderColor = Theme.Lerp(game.MenuColor, Color.White, 0.2f);
            button.FlatAppearance.BorderSize = 2;
            button.FlatAppearance.MouseOverBackColor = Theme.Lerp(game.MenuColor, Color.Black, 0.35f);
            button.FlatAppearance.MouseDownBackColor = game.MenuColor;
            button.Click += OnGameButtonClick;

            _gameButtons.Add(button);
            _menuBar.Controls.Add(button);
        }

        // --- Çıkış butonu (fare ile tek çıkış yolu) ---
        _exitButton = new Button
        {
            Text = "✕  ÇIKIŞ",
            Font = _exitFont,
            ForeColor = Color.White,
            BackColor = Theme.ExitButton,
            FlatStyle = FlatStyle.Flat,
            TabStop = false,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _exitButton.FlatAppearance.BorderColor = Color.White;
        _exitButton.FlatAppearance.BorderSize = 2;
        _exitButton.FlatAppearance.MouseOverBackColor = Theme.ExitButtonHover;
        _exitButton.FlatAppearance.MouseDownBackColor = Theme.ExitButtonHover;
        _exitButton.Click += OnExitButtonClick;
        _menuBar.Controls.Add(_exitButton);

        // --- Durum yazısı (ses uyarısı / ipucu) ---
        _statusLabel = new Label
        {
            Text = soundAvailable
                ? "Oyun değiştirmek ve çıkmak için fareyi kullanın"
                : "UYARI: ses aygıtı bulunamadı, sessiz çalışıyor",
            Font = _statusFont,
            ForeColor = soundAvailable ? Theme.Hint : Theme.Warning,
            BackColor = Color.Transparent,
            AutoSize = true
        };
        _menuBar.Controls.Add(_statusLabel);

        // --- Oyun alanı ---
        _gameHost = new Panel
        {
            BackColor = Theme.Background,
            TabStop = false
        };

        Controls.Add(_menuBar);
        Controls.Add(_gameHost);

        // --- Klavye kancası (selftest modunda KURULMAZ) ---
        if (!selfTestMode)
        {
            Hook = new GlobalKeyboardHook(this);
            Hook.KeyDownReceived += OnHookKeyDown;
            Hook.KeyUpReceived += OnHookKeyUp;
        }

        // --- Odak sigortası ---
        _focusTimer = new System.Windows.Forms.Timer { Interval = FocusTimerIntervalMs };
        _focusTimer.Tick += OnFocusTimerTick;

        Deactivate += OnFormDeactivate;

        _layoutReady = true;
        LayoutChildren();
    }

    /// <summary>Klavye kancası (selftest modunda null).</summary>
    public GlobalKeyboardHook? Hook { get; }

    /// <summary>Şu anda aktif olan oyun modülü.</summary>
    internal IGameModule? ActiveGame => _activeGame;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (!_selfTestMode)
        {
            Rectangle screen = Screen.PrimaryScreen?.Bounds ?? Bounds;
            Bounds = screen;
            EnforceTopMost();
        }

        LayoutChildren();

        // İlk oyun açılışta başlar.
        SwitchToGame(0);

        // Kanca, mesaj döngüsüne sahip UI thread'inde kurulur.
        if (Hook != null && !Hook.Install())
        {
            _statusLabel.Text = $"UYARI: klavye kancası kurulamadı, hata {Hook.LastError}";
            _statusLabel.ForeColor = Theme.Warning;
            LayoutChildren();
        }

        if (!_selfTestMode)
        {
            _focusTimer.Start();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (!_selfTestMode)
        {
            EnforceTopMost();
            Activate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // İkinci savunma hattı: Alt+F4 bir şekilde kancadan kaçarsa kapanmayı engelle.
        if (!_exitApproved)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    // ---------------- Oyun değiştirme ----------------

    /// <summary>
    /// Aktif oyunu değiştirir: önce eski oyun durdurulur (sesi susturur),
    /// sonra yenisi yerleştirilip başlatılır. Aynı oyuna geçiş istenirse hiçbir şey yapılmaz.
    /// </summary>
    public void SwitchToGame(int index)
    {
        if (_shuttingDown || index < 0 || index >= _games.Count)
        {
            return;
        }

        IGameModule next = _games[index];
        if (ReferenceEquals(next, _activeGame))
        {
            return;
        }

        if (_activeGame != null)
        {
            _activeGame.Stop();
            _gameHost.Controls.Remove(_activeGame.View);
        }

        _activeGame = next;
        next.View.Dock = DockStyle.Fill;
        _gameHost.Controls.Add(next.View);
        next.Start();

        UpdateMenuSelection(index);
    }

    private void OnGameButtonClick(object? sender, EventArgs e)
    {
        if (sender is Button { Tag: int index })
        {
            SwitchToGame(index);
        }
    }

    /// <summary>Seçili oyunun butonu parlak, diğerleri sönük gösterilir.</summary>
    private void UpdateMenuSelection(int selectedIndex)
    {
        for (int i = 0; i < _gameButtons.Count; i++)
        {
            Button button = _gameButtons[i];
            Color accent = _games[i].MenuColor;
            bool selected = i == selectedIndex;

            button.BackColor = selected
                ? Theme.Lerp(accent, Color.Black, 0.15f)
                : Theme.Lerp(accent, Color.Black, 0.60f);
            button.FlatAppearance.BorderColor = selected
                ? Color.White
                : Theme.Lerp(accent, Color.White, 0.2f);
            button.FlatAppearance.BorderSize = selected ? 3 : 2;
        }
    }

    // ---------------- Yerleşim ----------------

    /// <summary>Çocuk kontrolleri elle yerleştirir (Designer/Dock kullanılmıyor).</summary>
    private void LayoutChildren()
    {
        if (!_layoutReady)
        {
            return;
        }

        int width = ClientSize.Width;
        int height = ClientSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int menuHeight = Math.Max(84, (int)(height * 0.10));
        _menuBar.SetBounds(0, 0, width, menuHeight);
        _gameHost.SetBounds(0, menuHeight, width, height - menuHeight);

        int margin = 12;
        int buttonHeight = menuHeight - margin * 2;
        int buttonWidth = Math.Max(210, (int)(width * 0.14));

        int x = margin;
        for (int i = 0; i < _gameButtons.Count; i++)
        {
            _gameButtons[i].SetBounds(x, margin, buttonWidth, buttonHeight);
            x += buttonWidth + margin;
        }

        int exitWidth = Math.Max(170, (int)(width * 0.10));
        _exitButton.SetBounds(width - exitWidth - margin, margin, exitWidth, buttonHeight);

        _statusLabel.Location = new Point(
            width - exitWidth - margin * 2 - _statusLabel.PreferredWidth,
            (menuHeight - _statusLabel.PreferredHeight) / 2);
    }

    // ---------------- Kiosk davranışı ----------------

    /// <summary>Pencereyi en üste taşır ve odağı geri alır.</summary>
    private void EnforceTopMost()
    {
        if (_shuttingDown || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        TopMost = true;
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_TOPMOST_REFRESH);
        NativeMethods.SetForegroundWindow(Handle);
    }

    private void OnFocusTimerTick(object? sender, EventArgs e)
    {
        if (_shuttingDown)
        {
            return;
        }

        EnforceTopMost();
        if (!ContainsFocus)
        {
            Activate();
        }
    }

    private void OnFormDeactivate(object? sender, EventArgs e)
    {
        if (_shuttingDown || _selfTestMode)
        {
            return;
        }

        EnforceTopMost();
        Activate();
    }

    // ---------------- Tuş yönlendirme ----------------

    private void OnHookKeyDown(int vkCode)
    {
        if (_shuttingDown)
        {
            return;
        }

        _activeGame?.HandleKeyDown(vkCode);
    }

    private void OnHookKeyUp(int vkCode)
    {
        if (_shuttingDown)
        {
            return;
        }

        _activeGame?.HandleKeyUp(vkCode);
    }

    // ---------------- Çıkış ----------------

    private void OnExitButtonClick(object? sender, EventArgs e)
    {
        PerformExit();
    }

    /// <summary>
    /// Kontrollü kapanış: önce klavye serbest bırakılır, sonra oyun ve zamanlayıcılar
    /// durdurulur, en son pencere kapatılır. Birden çok kez çağrılabilir.
    /// Ses motorlarının Dispose'u Program.Shutdown'dadır (her çıkış yolunda çalışır).
    /// </summary>
    public void PerformExit()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;

        // 1) Tuşları serbest bırak, 2) kancayı kaldır.
        if (Hook != null)
        {
            Hook.SuppressAll = false;
            Hook.Dispose();
        }

        // 3) Aktif oyunu durdur (çalan sesleri de susturur).
        _activeGame?.Stop();
        _activeGame = null;

        // 4) Zamanlayıcıları durdur.
        _focusTimer.Stop();

        // 5) Kapanışa izin ver.
        _exitApproved = true;
        TopMost = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposedResources)
        {
            _disposedResources = true;

            Deactivate -= OnFormDeactivate;
            _focusTimer.Tick -= OnFocusTimerTick;
            _focusTimer.Stop();
            _focusTimer.Dispose();

            foreach (Button button in _gameButtons)
            {
                button.Click -= OnGameButtonClick;
            }

            _exitButton.Click -= OnExitButtonClick;

            if (Hook != null)
            {
                Hook.KeyDownReceived -= OnHookKeyDown;
                Hook.KeyUpReceived -= OnHookKeyUp;
                Hook.Dispose();
            }

            // Aktif olmayan oyun görselleri Controls ağacında olmadığı için form
            // tarafından otomatik dispose edilmez; hepsi burada elle bırakılır.
            foreach (IGameModule game in _games)
            {
                game.Dispose();
            }

            _menuFont.Dispose();
            _exitFont.Dispose();
            _statusFont.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---------------- Selftest yardımcıları ----------------

    /// <summary>Selftest: kapanışa izin verip pencereyi kapatır.</summary>
    internal void SelfTestClose()
    {
        PerformExit();
    }
}
