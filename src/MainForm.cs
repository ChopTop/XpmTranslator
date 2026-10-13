using System.Diagnostics;
using XpmTranslator.Core;

namespace XpmTranslator;

sealed class MainForm : Form
{
    Engine? _engine;
    readonly Label _appLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    readonly TableLayoutPanel _list = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 3, Padding = new Padding(4) };
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
    readonly Button _apply = new() { Text = "Apply", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
    readonly Button _restore = new() { Text = "Restore originals", AutoSize = true };
    readonly Button _browse = new() { Text = "Change folder…", AutoSize = true };
    readonly Button _backup = new() { Text = "Open backup folder", AutoSize = true };
    readonly Dictionary<string, (CheckBox Box, Label State)> _rows = new();

    public MainForm()
    {
        Text = "Xiaomi PC Manager — English";
        Width = 860;
        Height = 760;
        MinimumSize = new Size(700, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
        top.Controls.Add(_appLabel);
        top.Controls.Add(_browse);

        _list.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddHeader("Translation");
        foreach (var c in Component.All.Where(c => !c.Fix)) AddRow(c);
        AddHeader("Fixes");
        foreach (var c in Component.All.Where(c => c.Fix)) AddRow(c);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange(new Control[] { _apply, _restore, _backup });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 470 };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_log);
        split.Panel2.Padding = new Padding(8, 0, 8, 0);

        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(top);

        _apply.Click += async (_, _) => await Run(e => e.Apply(_rows.Where(r => r.Value.Box.Checked).Select(r => r.Key).ToList()));
        _restore.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, "Put all original Xiaomi files back?", Text, MessageBoxButtons.OKCancel) == DialogResult.OK)
                await Run(e => e.RestoreAll());
        };
        _browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Folder with XiaomiPcManager.exe (e.g. ...\\XiaomiPCManager\\5.3.2.407)" };
            if (d.ShowDialog(this) == DialogResult.OK && File.Exists(Path.Combine(d.SelectedPath, "XiaomiPcManager.exe")))
                LoadInstall(Install.FromDir(d.SelectedPath));
        };
        _backup.Click += (_, _) =>
        {
            if (_engine == null) return;
            Directory.CreateDirectory(_engine.Store.Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", _engine.Store.Dir) { UseShellExecute = true });
        };
        Shown += (_, _) =>
        {
            split.SplitterDistance = Math.Max(200, split.Height - 150); // list gets the room, log keeps ~150px
            LoadInstall(Install.Detect());
        };
    }

    void AddHeader(string text)
    {
        var l = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 8, 0, 2) };
        _list.Controls.Add(l);
        _list.SetColumnSpan(l, 3);
    }

    void AddRow(Component c)
    {
        var box = new CheckBox { Text = c.Title, AutoSize = true, UseMnemonic = false, Checked = true, Padding = new Padding(0, 2, 8, 0) };
        var state = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        var desc = new Label { Text = c.Description, AutoSize = true, ForeColor = SystemColors.GrayText, UseMnemonic = false, MaximumSize = new Size(480, 0), Padding = new Padding(0, 6, 0, 6) };
        _list.Controls.Add(box);
        _list.Controls.Add(state);
        _list.Controls.Add(desc);
        _rows[c.Id] = (box, state);
    }

    void LoadInstall(Install? app)
    {
        if (app == null)
        {
            _appLabel.Text = "Xiaomi PC Manager was not found. Choose its folder:";
            _apply.Enabled = _restore.Enabled = false;
            return;
        }
        _engine = new Engine(app, Log);
        _appLabel.Text = $"Xiaomi PC Manager {app.Version}   {app.Dir}" +
                         (_engine.Originals.IsKnownVersion ? "" : "   — unknown version, made for 5.3.2.407");
        _apply.Enabled = _restore.Enabled = true;
        RefreshStatus();
    }

    void RefreshStatus()
    {
        if (_engine == null) return;
        foreach (var c in Component.All)
        {
            var st = _engine.StatusOf(c);
            var (box, label) = _rows[c.Id];
            label.Text = st switch
            {
                State.Applied => "applied",
                State.NotApplied => "not applied",
                State.Partial => "partly applied",
                _ => "modified",
            };
            label.ForeColor = st == State.Applied ? Color.SeaGreen : st == State.NotApplied ? SystemColors.ControlText : Color.DarkOrange;
        }
    }

    async Task Run(Action<Engine> op)
    {
        if (_engine == null) return;
        _apply.Enabled = _restore.Enabled = _browse.Enabled = false;
        _log.Clear();
        try { await Task.Run(() => op(_engine)); }
        catch (Exception e) { Log("Error: " + e.Message); }
        finally
        {
            _apply.Enabled = _restore.Enabled = _browse.Enabled = true;
            LoadInstall(_engine.App);
        }
    }

    void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(s)); return; }
        _log.AppendText(s + Environment.NewLine);
    }
}
