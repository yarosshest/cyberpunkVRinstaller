using System.Diagnostics;
using System.Reflection;

namespace CyberpunkVRInstaller;

internal sealed class MainForm : Form
{
    private const string BuildMarker = "VR PORT 0.2.2";
    private readonly InstallerEngine _engine = new();
    private readonly ComponentInstaller _componentInstaller = new();
    private readonly ModSafeModeManager _safeModeManager = new();
    private readonly TextBox _pathBox = new();
    private readonly CyberButton _browseButton = new();
    private readonly CyberButton _installButton = new();
    private readonly CyberButton _removeButton = new();
    private readonly CyberButton _refreshButton = new();
    private readonly CyberButton _componentsButton = new();
    private readonly CyberButton _archivesButton = new();
    private readonly CyberButton _toggleVrButton = new();
    private readonly CyberButton _safeModeButton = new();
    private readonly CyberButton _languageButton = new();
    private readonly CyberButton _closeButton = new();
    private readonly ListView _dependencies = new();
    private readonly Label _status = new();
    private readonly Label _pathHint = new();
    private readonly Label _subtitle = new();
    private readonly Label _gameSectionLabel = new();
    private readonly Label _dependenciesSectionLabel = new();
    private readonly CyberProgressBar _progress = new();
    private readonly LinkLabel _projectLink = new();
    private CancellationTokenSource? _refreshCts;
    private string? _shownVersionWarningKey;

    private static readonly Color Background = Color.FromArgb(10, 13, 18);
    private static readonly Color Panel = Color.FromArgb(19, 24, 32);
    private static readonly Color PanelLight = Color.FromArgb(25, 32, 42);
    private static readonly Color Cyan = Color.FromArgb(0, 236, 255);
    private static readonly Color Yellow = Color.FromArgb(255, 224, 0);
    private static readonly Color Muted = Color.FromArgb(158, 170, 184);

    public MainForm()
    {
        ClientSize = new Size(1040, 740);
        MinimumSize = new Size(920, 680);
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        BuildUi();
        ApplyLanguage();
        Shown += async (_, _) => await AutoDetectAsync();
        FormClosed += (_, _) => _refreshCts?.Cancel();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Panel, Padding = new Padding(34, 20, 34, 14), Margin = Padding.Empty };
        header.Paint += (_, e) =>
        {
            using var cyanPen = new Pen(Cyan, 2);
            using var yellowPen = new Pen(Yellow, 2);
            e.Graphics.DrawLine(yellowPen, 0, header.Height - 2, header.Width * 0.38f, header.Height - 2);
            e.Graphics.DrawLine(cyanPen, header.Width * 0.38f, header.Height - 2, header.Width, header.Height - 2);
        };
        var title = new Label
        {
            Text = "CYBERPUNK 2077  /  VR PORT",
            Font = new Font("Segoe UI Semibold", 24F),
            ForeColor = Yellow,
            AutoSize = true,
            Location = new Point(34, 20),
            BackColor = Color.Transparent
        };
        _subtitle.AutoSize = true;
        _subtitle.Font = new Font("Segoe UI", 9.5F);
        _subtitle.ForeColor = Muted;
        _subtitle.Location = new Point(37, 75);
        _subtitle.BackColor = Color.Transparent;
        var version = new Label
        {
            Text = $"VR {InstallerEngine.ModVersion}  •  INST {InstallerEngine.InstallerVersion}",
            Font = new Font("Consolas", 10F, FontStyle.Bold),
            ForeColor = Cyan,
            BackColor = PanelLight,
            AutoSize = false,
            Size = new Size(230, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(header.Width - 190, 27)
        };
        StyleButton(_languageButton, "EN", false);
        _languageButton.Size = new Size(62, 30);
        _languageButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _languageButton.Click += async (_, _) =>
        {
            L.Toggle();
            ApplyLanguage();
            _shownVersionWarningKey = null;
            await RefreshStateAsync();
        };
        header.Resize += (_, _) =>
        {
            version.Left = header.ClientSize.Width - version.Width - 34;
            _languageButton.Left = version.Left - _languageButton.Width - 10;
        };
        header.Controls.Add(title);
        header.Controls.Add(_subtitle);
        header.Controls.Add(version);
        header.Controls.Add(_languageButton);
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(34, 18, 34, 18),
            RowCount = 8,
            ColumnCount = 1,
            Margin = Padding.Empty,
            BackColor = Background
        };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.Controls.Add(body, 0, 1);

        ConfigureSectionLabel(_gameSectionLabel);
        body.Controls.Add(_gameSectionLabel, 0, 0);

        var pathPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        pathPanel.Margin = Padding.Empty;
        StyleTextBox(_pathBox);
        _pathBox.Dock = DockStyle.Fill;
        _pathBox.Margin = new Padding(0, 4, 8, 4);
        _pathBox.TextChanged += async (_, _) => await RefreshStateAsync();
        StyleButton(_browseButton, "ОБЗОР…", false);
        _browseButton.Dock = DockStyle.Fill;
        _browseButton.Margin = new Padding(0, 3, 0, 3);
        _browseButton.Click += Browse;
        pathPanel.Controls.Add(_pathBox, 0, 0);
        pathPanel.Controls.Add(_browseButton, 1, 0);
        body.Controls.Add(pathPanel, 0, 1);

        _pathHint.Dock = DockStyle.Fill;
        _pathHint.ForeColor = Muted;
        _pathHint.Font = new Font("Segoe UI", 9F);
        _pathHint.TextAlign = ContentAlignment.MiddleLeft;
        _pathHint.Padding = new Padding(2, 0, 0, 0);
        body.Controls.Add(_pathHint, 0, 2);

        var depHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        depHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        depHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        depHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        depHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        depHeader.Margin = Padding.Empty;
        ConfigureSectionLabel(_dependenciesSectionLabel);
        depHeader.Controls.Add(_dependenciesSectionLabel, 0, 0);
        StyleButton(_safeModeButton, "SAFE MODE", false);
        _safeModeButton.Dock = DockStyle.Fill;
        _safeModeButton.Margin = new Padding(0, 4, 8, 4);
        _safeModeButton.Click += async (_, _) => await ToggleSafeModeAsync();
        depHeader.Controls.Add(_safeModeButton, 1, 0);
        StyleButton(_archivesButton, "УСТАНОВИТЬ АРХИВ…", false);
        _archivesButton.Dock = DockStyle.Fill;
        _archivesButton.Margin = new Padding(0, 4, 8, 4);
        _archivesButton.Click += async (_, _) => await InstallLocalArchivesAsync();
        depHeader.Controls.Add(_archivesButton, 2, 0);
        StyleButton(_refreshButton, "ПРОВЕРИТЬ", false);
        _refreshButton.Dock = DockStyle.Fill;
        _refreshButton.Margin = new Padding(0, 4, 0, 4);
        _refreshButton.Click += async (_, _) => await RefreshStateAsync();
        depHeader.Controls.Add(_refreshButton, 3, 0);
        body.Controls.Add(depHeader, 0, 3);

        _dependencies.Dock = DockStyle.Fill;
        _dependencies.View = View.Details;
        _dependencies.FullRowSelect = true;
        _dependencies.GridLines = true;
        _dependencies.BackColor = Panel;
        _dependencies.ForeColor = Color.White;
        _dependencies.BorderStyle = BorderStyle.None;
        _dependencies.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _dependencies.HideSelection = false;
        _dependencies.OwnerDraw = true;
        _dependencies.Font = new Font("Segoe UI", 9.5F);
        _dependencies.SmallImageList = new ImageList { ImageSize = new Size(1, 28), ColorDepth = ColorDepth.Depth32Bit };
        _dependencies.Columns.Add("", 46);
        _dependencies.Columns.Add("КОМПОНЕНТ", 390);
        _dependencies.Columns.Add("МИНИМУМ", 128);
        _dependencies.Columns.Add("СОСТОЯНИЕ", 340);
        ConfigureDependencyDrawing();
        _dependencies.Resize += (_, _) => ResizeDependencyColumns();
        body.Controls.Add(_dependencies, 0, 4);

        _status.Text = "Выберите папку игры";
        _status.ForeColor = Muted;
        _status.Dock = DockStyle.Fill;
        _status.Font = new Font("Segoe UI Semibold", 9.5F);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(2, 0, 0, 0);
        body.Controls.Add(_status, 0, 5);
        _progress.Dock = DockStyle.Fill;
        _progress.Margin = new Padding(2, 2, 2, 2);
        body.Controls.Add(_progress, 0, 6);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        actions.Margin = Padding.Empty;
        _projectLink.Text = "GitHub проекта  •  лицензия MIT";
        _projectLink.LinkColor = Cyan;
        _projectLink.ActiveLinkColor = Yellow;
        _projectLink.Dock = DockStyle.Fill;
        _projectLink.TextAlign = ContentAlignment.MiddleLeft;
        _projectLink.Font = new Font("Segoe UI", 9F);
        _projectLink.Margin = new Padding(2, 0, 8, 0);
        _projectLink.AutoEllipsis = true;
        _projectLink.LinkClicked += (_, e) =>
        {
            if (e.Link?.LinkData as string == "license") ShowLicense();
            else Process.Start(new ProcessStartInfo("https://github.com/dariulone/cyberpunk-vr-port") { UseShellExecute = true });
        };
        _projectLink.Links.Clear();
        _projectLink.Links.Add(0, "GitHub проекта".Length, "project");
        _projectLink.Links.Add("GitHub проекта  •  ".Length, "лицензия MIT".Length, "license");
        StyleButton(_componentsButton, "УСТАНОВИТЬ КОМПОНЕНТЫ", false);
        _componentsButton.Dock = DockStyle.Fill;
        _componentsButton.Click += async (_, _) => await InstallComponentsAsync();
        StyleButton(_toggleVrButton, "ОТКЛЮЧИТЬ VR", false);
        _toggleVrButton.Dock = DockStyle.Fill;
        _toggleVrButton.Click += async (_, _) => await ToggleVrAsync();
        StyleButton(_removeButton, "УДАЛИТЬ МОД", false);
        _removeButton.Dock = DockStyle.Fill;
        _removeButton.Click += async (_, _) => await RemoveAsync();
        StyleButton(_installButton, "УСТАНОВИТЬ", true);
        _installButton.Dock = DockStyle.Fill;
        _installButton.Click += async (_, _) => await InstallAsync();
        StyleButton(_closeButton, "ЗАКРЫТЬ", false);
        _closeButton.Dock = DockStyle.Fill;
        _closeButton.Click += (_, _) => Close();
        actions.Controls.Add(_projectLink, 0, 0);
        actions.Controls.Add(_componentsButton, 1, 0);
        actions.Controls.Add(_toggleVrButton, 2, 0);
        actions.Controls.Add(_removeButton, 3, 0);
        actions.Controls.Add(_closeButton, 4, 0);
        actions.Controls.Add(_installButton, 5, 0);
        body.Controls.Add(actions, 0, 7);
    }

    private static void ConfigureSectionLabel(Label label)
    {
        label.ForeColor = Cyan;
        label.Font = new Font("Segoe UI Semibold", 8.5F);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.BottomLeft;
        label.Padding = new Padding(2, 0, 0, 2);
    }

    private void ApplyLanguage()
    {
        Text = L.T(
            $"Cyberpunk VR Port — Установщик {InstallerEngine.InstallerVersion} — {BuildMarker}",
            $"Cyberpunk VR Port — Installer {InstallerEngine.InstallerVersion} — {BuildMarker}");
        _subtitle.Text = L.T("АВТОНОМНЫЙ WINDOWS-ИНСТАЛЛЯТОР  •  CYBERPUNK 2077 2.5",
            "STANDALONE WINDOWS INSTALLER  •  CYBERPUNK 2077 2.5");
        _languageButton.Text = L.IsRussian ? "EN" : "RU";
        _gameSectionLabel.Text = L.T("01  /  ПАПКА ИГРЫ", "01  /  GAME FOLDER");
        _dependenciesSectionLabel.Text = L.T("02  /  ЗАВИСИМОСТИ", "02  /  DEPENDENCIES");
        _browseButton.Text = L.T("ОБЗОР…", "BROWSE…");
        _archivesButton.Text = L.T("АРХИВ МОДА…", "MOD ARCHIVE…");
        _refreshButton.Text = L.T("ПРОВЕРИТЬ", "CHECK");
        _componentsButton.Text = L.T("КОМПОНЕНТЫ", "COMPONENTS");
        _removeButton.Text = L.T("УДАЛИТЬ МОД", "REMOVE MOD");
        _closeButton.Text = L.T("ЗАКРЫТЬ", "CLOSE");
        _dependencies.Columns[1].Text = L.T("КОМПОНЕНТ", "COMPONENT");
        _dependencies.Columns[2].Text = L.T("МИНИМУМ", "MINIMUM");
        _dependencies.Columns[3].Text = L.T("СОСТОЯНИЕ", "STATUS");
        _status.Text = L.T("Выберите папку игры", "Select the game folder");
        UpdateProjectLink();
        ApplyDynamicButtonText();
    }

    private void UpdateProjectLink()
    {
        var projectText = "GitHub";
        var licenseText = "MIT";
        _projectLink.Text = projectText + "  •  " + licenseText;
        _projectLink.Links.Clear();
        _projectLink.Links.Add(0, projectText.Length, "project");
        _projectLink.Links.Add(projectText.Length + 5, licenseText.Length, "license");
    }

    private void ApplyDynamicButtonText()
    {
        var root = _pathBox.Text.Trim();
        var installed = GameLocator.IsGameRoot(root) ? _engine.GetInstalledVersion(root) : null;
        var safeMode = GameLocator.IsGameRoot(root) ? _safeModeManager.GetStatus(root) : new SafeModeResult(false, true, []);
        _installButton.Text = installed is null ? L.T("УСТАНОВИТЬ", "INSTALL") : L.T("ПЕРЕУСТАНОВИТЬ", "REINSTALL");
        _toggleVrButton.Text = _engine.IsDisabled(root) ? L.T("ВКЛЮЧИТЬ VR", "ENABLE VR") : L.T("ОТКЛЮЧИТЬ VR", "DISABLE VR");
        _safeModeButton.Text = safeMode.IsActive
            ? (safeMode.Verified ? L.T("ВКЛЮЧИТЬ МОДЫ", "ENABLE MODS") : L.T("ПОЧИНИТЬ SAFE MODE", "REPAIR SAFE MODE"))
            : "SAFE MODE";
    }

    private void ResizeDependencyColumns()
    {
        if (_dependencies.Columns.Count < 4) return;
        var available = Math.Max(400, _dependencies.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        _dependencies.Columns[0].Width = 46;
        _dependencies.Columns[2].Width = 128;
        _dependencies.Columns[1].Width = Math.Max(300, (int)(available * 0.42));
        _dependencies.Columns[3].Width = Math.Max(220, available - _dependencies.Columns[0].Width -
            _dependencies.Columns[1].Width - _dependencies.Columns[2].Width);
    }

    private void ConfigureDependencyDrawing()
    {
        _dependencies.DrawColumnHeader += (_, e) =>
        {
            using var background = new SolidBrush(PanelLight);
            using var border = new Pen(Color.FromArgb(54, 65, 78));
            e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            var bounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            using var headerFont = new Font("Segoe UI Semibold", 8.5F);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", headerFont, bounds,
                Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        _dependencies.DrawItem += (_, _) => { };
        _dependencies.DrawSubItem += (_, e) =>
        {
            if (e.Item is null || e.SubItem is null) return;
            var selected = e.Item.Selected;
            var rowColor = selected ? Color.FromArgb(29, 58, 66) :
                (e.ItemIndex % 2 == 0 ? Panel : Color.FromArgb(16, 21, 28));
            using var background = new SolidBrush(rowColor);
            using var border = new Pen(Color.FromArgb(48, 58, 70));
            e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var bounds = new Rectangle(e.Bounds.X + (e.ColumnIndex == 0 ? 0 : 8), e.Bounds.Y,
                e.Bounds.Width - (e.ColumnIndex == 0 ? 0 : 12), e.Bounds.Height);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                (e.ColumnIndex == 0 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, bounds, e.Item.ForeColor, flags);
        };
    }

    private async Task AutoDetectAsync()
    {
        _status.Text = L.T("Поиск установленной игры…", "Searching for an installed game…");
        var installs = await Task.Run(GameLocator.FindInstallations);
        if (installs.Count > 0) _pathBox.Text = installs[0];
        else await RefreshStateAsync();
    }

    private async Task RefreshStateAsync()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;
        var root = _pathBox.Text.Trim();
        var valid = GameLocator.IsGameRoot(root);
        var gameVersion = valid ? GameVersionChecker.Check(root) : null;
        var compatibleVersion = gameVersion?.State == GameVersionState.Compatible;
        _pathHint.Text = !valid
            ? L.T("Укажите корень игры, содержащий папки bin, r6 и red4ext",
                "Select the game root containing the bin, r6, and red4ext folders")
            : compatibleVersion
                ? $"✓ {gameVersion!.Details}"
                : $"⚠ {gameVersion!.Details}";
        _pathHint.ForeColor = !valid ? Muted : compatibleVersion ? Color.FromArgb(77, 230, 138) : Yellow;
        _installButton.Enabled = valid && compatibleVersion;
        _componentsButton.Enabled = valid && compatibleVersion;
        _archivesButton.Enabled = valid && compatibleVersion;
        _removeButton.Enabled = valid && _engine.IsInstalled(root);
        _toggleVrButton.Enabled = valid && _engine.IsInstalled(root);
        _safeModeButton.Enabled = valid;
        _dependencies.Items.Clear();
        if (!valid)
        {
            _status.Text = L.T("Игра не найдена", "Game not found");
            _status.ForeColor = Muted;
            return;
        }

        _status.Text = L.T("Проверка зависимостей…", "Checking dependencies…");
        try
        {
            var checks = await Task.Run(() => DependencyChecker.Check(root), token);
            if (token.IsCancellationRequested) return;
            var gameItem = new ListViewItem(compatibleVersion ? "✓" : "!")
            {
                ForeColor = compatibleVersion ? Color.FromArgb(77, 230, 138) : Yellow
            };
            gameItem.SubItems.Add("Cyberpunk 2077");
            gameItem.SubItems.Add(GameVersionChecker.SupportedGameVersion);
            gameItem.SubItems.Add(gameVersion!.Details);
            _dependencies.Items.Add(gameItem);
            foreach (var dep in checks)
            {
                var item = new ListViewItem(dep.Found ? "✓" : "!") { ForeColor = dep.Found ? Color.FromArgb(77, 230, 138) : Yellow };
                item.SubItems.Add(dep.Name);
                item.SubItems.Add(dep.MinimumVersion);
                item.SubItems.Add(dep.Details);
                _dependencies.Items.Add(item);
            }

            var installed = _engine.GetInstalledVersion(root);
            var modState = _engine.GetModState(root);
            var safeMode = _safeModeManager.GetStatus(root);
            _installButton.Text = installed is null ? L.T("УСТАНОВИТЬ", "INSTALL") : L.T("ПЕРЕУСТАНОВИТЬ", "REINSTALL");
            _toggleVrButton.Text = _engine.IsDisabled(root) ? L.T("ВКЛЮЧИТЬ VR", "ENABLE VR") : L.T("ОТКЛЮЧИТЬ VR", "DISABLE VR");
            _safeModeButton.Text = safeMode.IsActive
                ? (safeMode.Verified ? L.T("ВКЛЮЧИТЬ МОДЫ", "ENABLE MODS") : L.T("ПОЧИНИТЬ SAFE MODE", "REPAIR SAFE MODE"))
                : "SAFE MODE";
            _safeModeButton.Primary = safeMode.IsActive;
            _removeButton.Enabled = installed is not null && !safeMode.IsActive;
            _toggleVrButton.Enabled = installed is not null && !safeMode.IsActive;
            _installButton.Enabled = compatibleVersion && !safeMode.IsActive;
            _componentsButton.Enabled = compatibleVersion && !safeMode.IsActive;
            _archivesButton.Enabled = compatibleVersion && !safeMode.IsActive;
            if (safeMode.IsActive)
            {
                _status.Text = safeMode.Verified
                    ? L.T("SAFE MODE: все пути загрузки модов отключены — проверка пройдена",
                        "SAFE MODE: all mod loading paths are disabled — verification passed")
                    : L.T($"SAFE MODE неполный: проблем {safeMode.Problems.Count}",
                        $"SAFE MODE is incomplete: {safeMode.Problems.Count} problem(s)");
                _status.ForeColor = safeMode.Verified ? Color.FromArgb(77, 230, 138) : Yellow;
            }
            else if (modState.State == VrModState.Disabled)
            {
                _status.Text = L.T("VR-мод отключён — активные файлы отсутствуют, проверка пройдена",
                    "VR mod is disabled — no active files found, verification passed");
                _status.ForeColor = Color.FromArgb(77, 230, 138);
            }
            else if (modState.State == VrModState.Incomplete)
            {
                _status.Text = L.T($"Состояние VR-мода неполное: проблем {modState.Problems.Count}",
                    $"VR mod state is incomplete: {modState.Problems.Count} problem(s)");
                _status.ForeColor = Yellow;
            }
            else
            {
                _status.Text = installed is null
                    ? L.T("Готово к установке", "Ready to install")
                    : L.T($"Установлена и активна версия {installed}", $"Version {installed} is installed and active");
                _status.ForeColor = Muted;
            }

            if (!compatibleVersion)
            {
                var warningKey = root + "|" + gameVersion.DetectedVersion + "|" + gameVersion.State;
                if (!string.Equals(_shownVersionWarningKey, warningKey, StringComparison.OrdinalIgnoreCase))
                {
                    _shownVersionWarningKey = warningKey;
                    MessageBox.Show(this,
                        L.T("Установка заблокирована из-за несовместимой версии игры.\n\n",
                            "Installation is blocked because the game version is incompatible.\n\n") + gameVersion.Details +
                        L.T($"\n\nТребуется Cyberpunk 2077 {GameVersionChecker.SupportedGameVersion}. " +
                            "Отключение VR, Safe mode и удаление уже установленных модов остаются доступными.",
                            $"\n\nCyberpunk 2077 {GameVersionChecker.SupportedGameVersion} is required. " +
                            "Disabling VR, Safe Mode, and removing installed mods remain available."),
                        L.T("Проверка версии Cyberpunk 2077", "Cyberpunk 2077 version check"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = L.T($"Ошибка проверки: {ex.Message}", $"Check failed: {ex.Message}"); }
    }

    private void Browse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.T("Выберите корневую папку Cyberpunk 2077", "Select the Cyberpunk 2077 root folder"),
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_pathBox.Text) ? _pathBox.Text : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dialog.SelectedPath;
    }

    private async Task InstallAsync()
    {
        var root = _pathBox.Text.Trim();
        GameVersionChecker.EnsureCompatible(root);
        var checks = await Task.Run(() => DependencyChecker.Check(root));
        var missing = checks.Where(d => d.Essential && !d.Found).Select(d => d.Name).ToArray();
        var conflicts = _engine.FindConflicts(root);
        var warnings = new List<string>();
        if (missing.Length > 0) warnings.Add(L.T("Не найдены зависимости:\n• ", "Missing dependencies:\n• ") + string.Join("\n• ", missing));
        if (conflicts.Count > 0) warnings.Add(L.T("Возможные конфликты:\n• ", "Possible conflicts:\n• ") + string.Join("\n• ", conflicts));
        if (warnings.Count > 0 && MessageBox.Show(this,
                string.Join("\n\n", warnings) + L.T("\n\nПродолжить установку?", "\n\nContinue installation?"),
                L.T("Требуется внимание", "Attention required"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        await RunOperationAsync(() => _engine.Install(root, CreateProgress()),
            L.T("Установка завершена. Запустите OpenXR/SteamVR, затем игру.",
                "Installation complete. Start OpenXR/SteamVR, then launch the game."));
    }

    private async Task InstallComponentsAsync()
    {
        var root = _pathBox.Text.Trim();
        GameVersionChecker.EnsureCompatible(root);
        if (MessageBox.Show(this,
                L.T("Будет загружена и установлена последняя официальная версия RED4ext с GitHub.\n\n" +
                    "Cyberpunk VR Port 0.2.2 больше не требует CET, redscript, ArchiveXL, TweakXL, " +
                    "Codeware, Equipment-EX, Input Loader или моды Nexus. Они останутся на месте, если нужны другим модам.\n\n" +
                    "Продолжить?",
                    "The latest official RED4ext version will be downloaded and installed from GitHub.\n\n" +
                    "Cyberpunk VR Port 0.2.2 no longer requires CET, redscript, ArchiveXL, TweakXL, " +
                    "Codeware, Equipment-EX, Input Loader, or Nexus mods. They will remain in place if other mods need them.\n\n" +
                    "Continue?"), L.T("Установка компонентов", "Install components"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var succeeded = await RunOperationAsync(async () =>
        {
            await _componentInstaller.InstallGitHubComponentsAsync(root, CreateProgress());
        }, L.T("Компоненты GitHub установлены.", "GitHub components installed."));
        if (!succeeded) return;

    }

    private async Task InstallLocalArchivesAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = L.T("Выберите архивы модов или компонентов", "Select mod or component archives"),
            Filter = L.T("Архивы модов", "Mod archives") + " (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|ZIP (*.zip)|*.zip|RAR (*.rar)|*.rar|7z (*.7z)|*.7z",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var root = _pathBox.Text.Trim();
        GameVersionChecker.EnsureCompatible(root);
        var missingBefore = (await Task.Run(() => DependencyChecker.Check(root)))
            .Where(d => !d.Found).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await RunOperationAsync(() =>
        {
            _componentInstaller.InstallLocalArchives(root, dialog.FileNames, CreateProgress());
            if (missingBefore.Count > 0)
            {
                var missingAfter = DependencyChecker.Check(root).Where(d => !d.Found)
                    .Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (missingBefore.SetEquals(missingAfter))
                    throw new InstallerException(L.T("Архив распакован, но отсутствующий компонент не обнаружен.",
                        "The archive was extracted, but the missing component was not detected."));
            }
        },
            L.T("Выбранные компоненты установлены.", "Selected components installed."));
    }

    private async Task RemoveAsync()
    {
        if (MessageBox.Show(this, L.T("Удалить Cyberpunk VR Port и восстановить заменённые файлы?",
                "Remove Cyberpunk VR Port and restore replaced files?"), L.T("Удаление", "Removal"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        UninstallResult? result = null;
        await RunOperationAsync(() => result = _engine.Uninstall(_pathBox.Text.Trim(), CreateProgress()),
            L.T("Мод удалён.", "Mod removed."));
        if (result?.PreservedModifiedFiles.Count > 0)
            MessageBox.Show(this, L.T("Изменённые после установки файлы оставлены на месте:\n\n",
                "Files changed after installation were left in place:\n\n") +
                string.Join("\n", result.PreservedModifiedFiles), L.T("Файлы сохранены", "Files preserved"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ToggleVrAsync()
    {
        var root = _pathBox.Text.Trim();
        var disabled = _engine.IsDisabled(root);
        var question = disabled
            ? L.T("Включить VR-мод и вернуть его файлы в папку игры?",
                "Enable the VR mod and return its files to the game folder?")
            : L.T("Отключить только Cyberpunk VR Port?\n\nФайлы будут перемещены в защищённое хранилище и проверены. Общие компоненты останутся активны.",
                "Disable only Cyberpunk VR Port?\n\nIts files will be moved to protected storage and verified. Shared components will remain active.");
        if (MessageBox.Show(this, question, disabled ? L.T("Включение VR", "Enable VR") : L.T("Отключение VR", "Disable VR"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (disabled)
            await RunOperationAsync(() => _engine.Enable(root, CreateProgress()),
                L.T("VR-мод включён и проверен.", "VR mod enabled and verified."));
        else
            await RunOperationAsync(() => _engine.Disable(root, CreateProgress()),
                L.T("VR-мод отключён. Проверка подтвердила, что игра не видит его активные файлы.",
                    "VR mod disabled. Verification confirmed that the game cannot see its active files."));
    }

    private async Task ToggleSafeModeAsync()
    {
        var root = _pathBox.Text.Trim();
        var status = _safeModeManager.GetStatus(root);
        if (status.IsActive)
        {
            if (!status.Verified)
            {
                if (MessageBox.Show(this,
                        L.T("Safe mode включён не полностью. Дозаписать новые точки загрузки модов в защищённое хранилище и повторить проверку?",
                            "Safe Mode is incomplete. Move newly detected mod loading paths to protected storage and verify again?"),
                        L.T("Ремонт Safe mode", "Repair Safe Mode"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                await RunOperationAsync(() => _safeModeManager.Repair(root, CreateProgress()),
                    L.T("Safe mode отремонтирован. Проверка подтвердила полное отключение известных загрузчиков модов.",
                        "Safe Mode repaired. Verification confirmed that all known mod loaders are disabled."));
                return;
            }
            if (MessageBox.Show(this, L.T("Выйти из Safe mode и восстановить все отключённые моды?",
                    "Exit Safe Mode and restore all disabled mods?"), L.T("Включение модов", "Enable mods"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunOperationAsync(() => _safeModeManager.Deactivate(root, CreateProgress()),
                L.T("Все файлы модов восстановлены и проверены.", "All mod files restored and verified."));
        }
        else
        {
            if (MessageBox.Show(this,
                    L.T("Включить Safe mode для диагностики вылетов?\n\nБудут временно отключены RED4ext, CET, redscript-моды, ArchiveXL-моды и остальные файлы модификаций. Все данные можно восстановить этой же кнопкой.",
                        "Enable Safe Mode to diagnose crashes?\n\nRED4ext, CET, redscript mods, ArchiveXL mods, and other modification files will be temporarily disabled. Everything can be restored with the same button."),
                    "Safe mode", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunOperationAsync(() => _safeModeManager.Activate(root, CreateProgress()),
                L.T("Safe mode включён. Проверка подтвердила, что пути загрузки модов недоступны игре.",
                    "Safe Mode enabled. Verification confirmed that mod loading paths are unavailable to the game."));
        }
    }

    private IProgress<InstallProgress> CreateProgress() => new Progress<InstallProgress>(p =>
    {
        _progress.Value = Math.Clamp(p.Percent, 0, 100);
        _status.Text = p.Message;
    });

    private async Task RunOperationAsync(Action action, string successMessage)
    {
        SetBusy(true);
        try
        {
            await Task.Run(action);
            MessageBox.Show(this, successMessage, "Cyberpunk VR Port", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L.T("Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshStateAsync();
        }
    }

    private async Task<bool> RunOperationAsync(Func<Task> action, string successMessage)
    {
        SetBusy(true);
        var succeeded = false;
        try
        {
            await action();
            succeeded = true;
            MessageBox.Show(this, successMessage, "Cyberpunk VR Port", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L.T("Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshStateAsync();
        }
        return succeeded;
    }

    private void SetBusy(bool busy)
    {
        _pathBox.Enabled = !busy;
        _browseButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _componentsButton.Enabled = !busy;
        _archivesButton.Enabled = !busy;
        _installButton.Enabled = !busy;
        _removeButton.Enabled = !busy;
        _toggleVrButton.Enabled = !busy;
        _safeModeButton.Enabled = !busy;
        _languageButton.Enabled = !busy;
        UseWaitCursor = busy;
        if (!busy) _progress.Value = 0;
    }

    private static void StyleTextBox(TextBox box)
    {
        box.BackColor = Color.FromArgb(27, 34, 44);
        box.ForeColor = Color.White;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Consolas", 10F);
        box.AutoSize = false;
    }

    private static void StyleButton(CyberButton button, string text, bool primary)
    {
        button.Text = text;
        button.Primary = primary;
        button.Font = new Font("Segoe UI Semibold", 9F);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(5, 3, 0, 3);
    }

    private void ShowLicense()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ThirdPartyNotices.txt");
        using var reader = stream is null ? null : new StreamReader(stream);
        var notices = reader?.ReadToEnd() ?? L.T("Текст лицензии не найден.", "License text was not found.");
        using var sevenZipStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SevenZipLicense.txt");
        using var sevenZipReader = sevenZipStream is null ? null : new StreamReader(sevenZipStream);
        if (sevenZipReader is not null) notices += "\n\n---------------- 7-Zip ----------------\n\n" + sevenZipReader.ReadToEnd();
        MessageBox.Show(this, notices,
            L.T("Лицензия CyberpunkVR Port", "CyberpunkVR Port license"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

internal sealed class CyberButton : Button
{
    private bool _hovered;
    private bool _primary;

    public bool Primary
    {
        get => _primary;
        set { _primary = value; Invalidate(); }
    }

    public CyberButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.Black);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill;
        Color border;
        Color text;

        if (!Enabled)
        {
            fill = Color.FromArgb(22, 27, 34);
            border = Color.FromArgb(55, 63, 72);
            text = Color.FromArgb(101, 110, 120);
        }
        else if (Primary)
        {
            fill = _hovered ? Color.FromArgb(255, 235, 72) : Color.FromArgb(255, 224, 0);
            border = fill;
            text = Color.FromArgb(9, 12, 16);
        }
        else
        {
            fill = _hovered ? Color.FromArgb(25, 47, 56) : Color.FromArgb(18, 24, 31);
            border = Color.FromArgb(0, 222, 239);
            text = Color.White;
        }

        using var fillBrush = new SolidBrush(fill);
        using var borderPen = new Pen(border);
        e.Graphics.FillRectangle(fillBrush, bounds);
        e.Graphics.DrawRectangle(borderPen, bounds);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && Enabled)
        {
            var focus = Rectangle.Inflate(bounds, -4, -4);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, text, fill);
        }
    }
}

internal sealed class CyberProgressBar : Control
{
    private int _value;

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    public CyberProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 8;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var background = new SolidBrush(Color.FromArgb(28, 35, 44));
        using var border = new Pen(Color.FromArgb(49, 60, 72));
        e.Graphics.FillRectangle(background, bounds);
        e.Graphics.DrawRectangle(border, bounds);
        if (Value > 0)
        {
            var fillWidth = Math.Max(1, (int)((Width - 2) * Value / 100d));
            using var fill = new SolidBrush(Color.FromArgb(0, 225, 241));
            e.Graphics.FillRectangle(fill, 1, 1, fillWidth, Math.Max(1, Height - 2));
        }
    }
}

