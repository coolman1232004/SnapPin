using System.Diagnostics;

namespace SnapAnchor.Setup;

internal sealed class SetupWizardForm : Form
{
    private readonly Panel[] _pages;
    private readonly Label _stepLabel;
    private readonly Label _headerTitle;
    private readonly Label _headerSubtitle;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Button _cancelButton;
    private readonly CheckBox _acceptBox;
    private readonly TextBox _destinationBox;
    private readonly CheckBox _desktopShortcutBox;
    private readonly CheckBox _startMenuShortcutBox;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressStatus;
    private readonly CheckBox _launchBox;
    private readonly CheckBox _readmeBox;
    private int _step;
    private bool _installing;
    private string _installedDirectory = string.Empty;

    internal SetupWizardForm(string suggestedDirectory)
    {
        Text = "SnapAnchor Setup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 500);
        MinimumSize = new Size(680, 470);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(247, 249, 252);
        Font = new Font("Segoe UI", 9F);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(28, 18, 28, 12) };
        root.Controls.Add(header, 0, 0);
        _stepLabel = new Label { AutoSize = true, Text = "Step 1 of 4", ForeColor = Color.FromArgb(47, 128, 237), Font = new Font("Segoe UI Semibold", 9F), Location = new Point(29, 15) };
        _headerTitle = new Label { AutoSize = false, Text = "Welcome to SnapAnchor Setup", ForeColor = Color.FromArgb(17, 24, 39), Font = new Font("Segoe UI Semibold", 18F), Location = new Point(27, 35), Size = new Size(660, 34) };
        _headerSubtitle = new Label { AutoSize = false, Text = "Review the information below before continuing.", ForeColor = Color.FromArgb(91, 101, 116), Font = new Font("Segoe UI", 9.5F), Location = new Point(30, 70), Size = new Size(650, 23) };
        header.Controls.AddRange([_stepLabel, _headerTitle, _headerSubtitle]);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 18) };
        root.Controls.Add(content, 0, 1);

        _acceptBox = new CheckBox { AutoSize = true, Text = "I understand and accept these terms.", Font = new Font("Segoe UI Semibold", 9.5F), Margin = new Padding(0, 10, 0, 0) };
        var welcomePage = CreateWelcomePage(_acceptBox);

        _destinationBox = new TextBox { Text = suggestedDirectory, Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Segoe UI", 10F), Margin = new Padding(0, 6, 8, 0) };
        _desktopShortcutBox = new CheckBox { AutoSize = true, Text = "Create a desktop shortcut", Checked = true, Margin = new Padding(0, 8, 0, 4) };
        _startMenuShortcutBox = new CheckBox { AutoSize = true, Text = "Create a Start menu shortcut", Checked = true, Margin = new Padding(0, 4, 0, 0) };
        var destinationPage = CreateDestinationPage();

        _progressBar = new ProgressBar { Dock = DockStyle.Top, Height = 24, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 18, 0, 0) };
        _progressStatus = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 46, Text = "Preparing installation...", ForeColor = Color.FromArgb(75, 85, 99), Padding = new Padding(0, 12, 0, 0) };
        var progressPage = CreateProgressPage();

        _launchBox = new CheckBox { AutoSize = true, Text = "Launch SnapAnchor", Checked = true, Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0, 12, 0, 4) };
        _readmeBox = new CheckBox { AutoSize = true, Text = "Open README", Checked = false, Font = new Font("Segoe UI", 10F), Margin = new Padding(0, 5, 0, 0) };
        var completePage = CreateCompletePage();

        _pages = [welcomePage, destinationPage, progressPage, completePage];
        foreach (var page in _pages) { page.Dock = DockStyle.Fill; page.Visible = false; content.Controls.Add(page); }

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(28, 14, 28, 14) };
        root.Controls.Add(footer, 0, 2);
        _cancelButton = CreateButton("Cancel", 92, false);
        _cancelButton.Dock = DockStyle.Right;
        _cancelButton.Click += Cancel_Click;
        _nextButton = CreateButton("Next  ›", 106, true);
        _nextButton.Dock = DockStyle.Right;
        _nextButton.Margin = new Padding(8, 0, 0, 0);
        _nextButton.Click += Next_Click;
        _acceptBox.CheckedChanged += (_, _) => _nextButton.Enabled = _acceptBox.Checked;
        _backButton = CreateButton("‹  Back", 96, false);
        _backButton.Dock = DockStyle.Right;
        _backButton.Click += (_, _) => ShowStep(Math.Max(0, _step - 1));
        footer.Controls.Add(_cancelButton);
        footer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        footer.Controls.Add(_nextButton);
        footer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        footer.Controls.Add(_backButton);

        FormClosing += (_, e) => { if (_installing) e.Cancel = true; };
        ShowStep(0);
    }

    private Panel CreateWelcomePage(CheckBox acceptBox)
    {
        var page = new Panel();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var intro = new Label
        {
            AutoSize = false, Dock = DockStyle.Fill,
            Text = "SnapAnchor is a local-first screenshot and floating-reference utility for Windows.",
            ForeColor = Color.FromArgb(52, 64, 84), Font = new Font("Segoe UI", 10F)
        };
        var terms = new RichTextBox
        {
            ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White,
            Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F), TabStop = false,
            WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            Text = "IMPORTANT INFORMATION\n\n" +
                   "SnapAnchor is provided as-is, without warranty. You are responsible for ensuring you have permission to capture, store, and share screen content.\n\n" +
                   "SnapAnchor stores settings and capture history locally on this computer. OCR is processed locally. The installer creates only the shortcuts you select and registers an uninstaller in Windows.\n\n" +
                   "Close any older SnapAnchor instance before installation; Setup will also attempt to close the installed copy automatically."
        };
        acceptBox.Anchor = AnchorStyles.Left;
        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(terms, 0, 1);
        layout.Controls.Add(acceptBox, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private Panel CreateDestinationPage()
    {
        var page = new Panel();
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 265, ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        var title = new Label { AutoSize = true, Text = "Install SnapAnchor to:", Font = new Font("Segoe UI Semibold", 11F), ForeColor = Color.FromArgb(17, 24, 39), Anchor = AnchorStyles.Left };
        layout.Controls.Add(title, 0, 0); layout.SetColumnSpan(title, 2);
        layout.Controls.Add(_destinationBox, 0, 1);
        var browse = CreateButton("Browse...", 96, false); browse.Anchor = AnchorStyles.Right; browse.Click += Browse_Click; layout.Controls.Add(browse, 1, 1);
        var options = new Label { AutoSize = true, Text = "Additional options", Font = new Font("Segoe UI Semibold", 10F), ForeColor = Color.FromArgb(17, 24, 39), Anchor = AnchorStyles.Left };
        layout.Controls.Add(options, 0, 2); layout.SetColumnSpan(options, 2);
        layout.Controls.Add(_desktopShortcutBox, 0, 3); layout.SetColumnSpan(_desktopShortcutBox, 2);
        layout.Controls.Add(_startMenuShortcutBox, 0, 4); layout.SetColumnSpan(_startMenuShortcutBox, 2);
        var note = new Label { AutoSize = false, Dock = DockStyle.Fill, BackColor = Color.FromArgb(235, 244, 255), ForeColor = Color.FromArgb(52, 64, 84), Padding = new Padding(12, 10, 12, 8), Text = "A per-user installation is used, so administrator permission is normally not required. Your existing settings and history will be preserved." };
        layout.Controls.Add(note, 0, 5); layout.SetColumnSpan(note, 2);
        page.Controls.Add(layout);
        return page;
    }

    private Panel CreateProgressPage()
    {
        var page = new Panel();
        var title = new Label { AutoSize = true, Dock = DockStyle.Top, Text = "Installing SnapAnchor", Font = new Font("Segoe UI Semibold", 16F), ForeColor = Color.FromArgb(17, 24, 39), Padding = new Padding(0, 5, 0, 12) };
        var message = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 58, Text = "Please wait while Setup installs SnapAnchor. This may take a moment because the portable runtime and local OCR models are included.", ForeColor = Color.FromArgb(75, 85, 99) };
        var note = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 58, Text = "Do not close this window while files are being installed.", ForeColor = Color.FromArgb(102, 112, 133), Padding = new Padding(0, 18, 0, 0) };
        page.Controls.Add(note);
        page.Controls.Add(_progressStatus);
        page.Controls.Add(_progressBar);
        page.Controls.Add(message);
        page.Controls.Add(title);
        return page;
    }

    private Panel CreateCompletePage()
    {
        var page = new Panel();
        var title = new Label { AutoSize = true, Dock = DockStyle.Top, Text = "SnapAnchor is ready", Font = new Font("Segoe UI Semibold", 18F), ForeColor = Color.FromArgb(17, 24, 39), Padding = new Padding(0, 8, 0, 10) };
        var message = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 72, Text = "Setup has installed SnapAnchor successfully. Click Finish to close Setup and perform the selected actions.", ForeColor = Color.FromArgb(75, 85, 99), Font = new Font("Segoe UI", 10F) };
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 80, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        options.Controls.Add(_launchBox); options.Controls.Add(_readmeBox);
        var privacy = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 70, BackColor = Color.FromArgb(236, 253, 245), ForeColor = Color.FromArgb(22, 101, 74), Padding = new Padding(12), Text = "Local-first by design: SnapAnchor does not require an account or cloud connection." };
        page.Controls.Add(privacy);
        page.Controls.Add(options);
        page.Controls.Add(message);
        page.Controls.Add(title);
        return page;
    }

    private static Button CreateButton(string text, int width, bool primary)
    {
        var button = new Button
        {
            Text = text, Width = width, Height = 36, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(88, 224, 170) : Color.FromArgb(234, 238, 244),
            ForeColor = Color.FromArgb(17, 24, 39), Font = new Font("Segoe UI Semibold", 9F),
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 3);
        for (var index = 0; index < _pages.Length; index++) _pages[index].Visible = index == _step;
        _pages[_step].BringToFront();
        _stepLabel.Text = $"Step {_step + 1} of 4";
        (_headerTitle.Text, _headerSubtitle.Text) = _step switch
        {
            0 => ("Welcome to SnapAnchor Setup", "Review the information below before continuing."),
            1 => ("Choose installation options", "Select where SnapAnchor will be installed."),
            2 => ("Installing SnapAnchor", "Setup is copying files and configuring shortcuts."),
            _ => ("Installation complete", "SnapAnchor was installed successfully.")
        };

        _backButton.Visible = _step == 1;
        _cancelButton.Visible = _step < 3;
        _cancelButton.Enabled = !_installing;
        _nextButton.Visible = _step != 2;
        _nextButton.Text = _step switch { 1 => "Install", 3 => "Finish", _ => "Next  ›" };
        _nextButton.Enabled = _step != 0 || _acceptBox.Checked;
        AcceptButton = _nextButton.Visible && _nextButton.Enabled ? _nextButton : null;
        CancelButton = _cancelButton.Visible && _cancelButton.Enabled ? _cancelButton : null;
    }

    private async void Next_Click(object? sender, EventArgs e)
    {
        if (_step == 0) { ShowStep(1); return; }
        if (_step == 1) { await BeginInstallationAsync(); return; }
        if (_step == 3) Finish();
    }

    private async Task BeginInstallationAsync()
    {
        try
        {
            _installedDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_destinationBox.Text.Trim()));
            _installing = true;
            ShowStep(2);
            _progressBar.Value = 0;
            var progress = new Progress<InstallProgress>(update =>
            {
                _progressBar.Value = Math.Clamp(update.Percentage, _progressBar.Minimum, _progressBar.Maximum);
                _progressStatus.Text = update.Message;
            });
            await Program.InstallAsync(_installedDirectory, _desktopShortcutBox.Checked, _startMenuShortcutBox.Checked, progress, CancellationToken.None);
            _installing = false;
            ShowStep(3);
        }
        catch (Exception ex)
        {
            _installing = false;
            MessageBox.Show(this, ex.Message, "SnapAnchor Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ShowStep(1);
        }
    }

    private void Browse_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the folder where SnapAnchor will be installed", UseDescriptionForTitle = true, ShowNewFolderButton = true, SelectedPath = Directory.Exists(_destinationBox.Text) ? _destinationBox.Text : Program.DefaultInstallDirectory };
        if (dialog.ShowDialog(this) == DialogResult.OK) _destinationBox.Text = dialog.SelectedPath;
    }

    private void Finish()
    {
        Hide();
        try
        {
            if (_readmeBox.Checked) Program.OpenReadme(_installedDirectory);
            if (_launchBox.Checked) Program.LaunchInstalledApp(_installedDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SnapAnchor Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Close();
    }

    private void Cancel_Click(object? sender, EventArgs e)
    {
        if (_installing) return;
        if (MessageBox.Show(this, "Exit SnapAnchor Setup?", "SnapAnchor Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Close();
    }

    internal void RenderPreviews(string directory)
    {
        for (var step = 0; step < 4; step++)
        {
            ShowStep(step);
            PerformLayout();
            Application.DoEvents();
            using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
            bitmap.Save(Path.Combine(directory, $"setup-step-{step + 1}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
