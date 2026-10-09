using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace SenjiDownloader;

public sealed class MainForm : Form
{
    private readonly Preferences preferences;
    private readonly SingleInstance? instance;
    private readonly bool background, preview;
    private readonly QueueEngine engine;
    private readonly UpdateService updater;
    private readonly Panel sidebar = new() { Dock = DockStyle.Left, Width = 202, BackColor = Theme.Sidebar };
    private readonly Panel host = new() { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
    private readonly Dictionary<string, Panel> pages = new();
    private readonly Dictionary<string, SmoothButton> navigation = new();
    private readonly Dictionary<Guid, JobRow> rows = new();
    private readonly FlowLayoutPanel queueList = new() { AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Canvas };
    private readonly Label queueSummary = Label("0 archivos", 9, false, Theme.Muted);
    private readonly Label status = Label("Listo para descargar", 9, false, Theme.Muted);
    private readonly Label updateStatus = Label("", 10, false, Theme.Muted);
    private readonly Label empty = Label("Tu cola está lista.\n\nPega uno o varios enlaces para comenzar.", 13, false, Theme.Muted);
    private readonly SmoothButton pauseQueue = new() { Text = "Pausar", GlyphName = "pause", Quiet = true, Width = 102, Height = 36 };
    private readonly TextField links = new(true, "https://…\r\nPega varios enlaces, uno por línea.");
    private readonly TextField folder = new();
    private readonly DarkCombo quality = new("Mejor", "2160p", "1440p", "1080p", "720p", "480p");
    private readonly DarkCombo format = new("MP4", "MKV", "MP3");
    private readonly DarkCombo parallel = new("1 descarga", "2 descargas", "3 descargas", "4 descargas", "5 descargas", "6 descargas");
    private readonly TextField conversionFiles = new(true, "Selecciona archivos de audio o video.");
    private readonly DarkCombo conversionFormat = new("MP3", "WAV", "MP4");
    private readonly TextField repository = new();
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 160 };
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 2500 };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 60_000 };
    private NotifyIcon? tray;
    private bool quitting;
    private volatile bool dirty = true, saveNeeded;
    private string page = "downloads";
    private string[] mediaFiles = [];
    private readonly Icon appIcon;
    public QueueEngine Engine => engine;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string? PreviewOutput { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string? StartupReport { get; set; }
    public MainForm(Preferences preferences, SingleInstance? instance, bool background = false, bool preview = false)
    {
        this.preferences = preferences; this.instance = instance; this.background = background; this.preview = preview;
        Text = "SenjiDownloader"; BackColor = Theme.Canvas; ForeColor = Theme.Text; Font = Theme.Font();
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(1120, 748); MinimumSize = new Size(960, 690);
        StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true; KeyPreview = true;
        if (background) { Opacity = 0; ShowInTaskbar = false; }
        using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SenjiDownloader.assets.senji.ico");
        appIcon = iconStream != null ? new Icon(iconStream) : SystemIcons.Application; Icon = appIcon;
        engine = new QueueEngine(preferences, Dispatch, !preview); updater = new UpdateService(preferences, engine);
        engine.Changed += () => { dirty = true; saveNeeded = true; };
        engine.Finished += Finished;
        updater.Changed += () => Dispatch(() => { updateStatus.Text = updater.Status; status.Text = updater.PendingFile == null ? "Motor listo · actualizado en segundo plano" : "Actualización lista"; });
        SuspendLayout(); Controls.Add(host); Controls.Add(sidebar);
        BuildSidebar(); BuildDownloads(); BuildConversion(); BuildSettings(); BuildHistory();
        ResumeLayout(true);
        ShowPage("downloads");
        folder.Value = preferences.DownloadFolder; quality.SelectedItem = preferences.Quality; format.SelectedItem = preferences.Format;
        parallel.SelectedIndex = Math.Clamp(preferences.ParallelDownloads, 1, 6) - 1;
        quality.AccessibleName = "Calidad de descarga"; format.AccessibleName = "Formato de descarga"; parallel.AccessibleName = "Descargas simultáneas";
        conversionFormat.AccessibleName = "Formato de conversión";
        parallel.SelectedIndexChanged += (_, _) => { preferences.ParallelDownloads = parallel.SelectedIndex + 1; SavePreferences(); engine.Pump(); };
        format.SelectedIndexChanged += (_, _) => { quality.Enabled = format.Value != "MP3"; };
        links.Editor.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddDownloads(); } };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F6) { ShowPage("downloads"); links.Editor.Focus(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.Enter && page == "downloads") { AddDownloads(); e.Handled = true; }
        };
        AllowDrop = true; DragEnter += Dropping; DragDrop += Drop;
        FormClosing += OnClosing;
        refresh.Tick += (_, _) => { if (dirty) { dirty = false; RefreshQueue(); } };
        saveTimer.Tick += (_, _) => { if (saveNeeded) { saveNeeded = false; engine.Persist(); } };
        updateTimer.Tick += async (_, _) =>
        {
            if (preferences.AutoUpdate && updater.PendingFile != null && updater.TryApply()) Quit();
            else if (preferences.AutoUpdate && !engine.IsBusy && DateTime.UtcNow - preferences.LastEngineUpdate > TimeSpan.FromHours(24)) await updater.Check();
        };
        Shown += OnShown;
    }
    private static Label Label(string text, float size = 10, bool bold = false, Color? color = null) => new()
    {
        Text = text, Font = Theme.Font(size, bold), ForeColor = color ?? Theme.Text, BackColor = Color.Transparent,
        AutoSize = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft
    };
    private void Dispatch(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        else action();
    }
    private void SavePreferences()
    {
        try { Storage.Save("preferences.json", preferences); }
        catch (Exception ex) { AppPaths.Log("Preferencias: " + ex.Message); status.Text = "No se pudieron guardar las preferencias."; }
    }
    private async void OnShown(object? sender, EventArgs e)
    {
        Theme.DarkTitle(this);
        if (preview)
        {
            RefreshQueue();
            if (StartupReport != null)
            {
                var runtimePath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                File.WriteAllText(StartupReport, System.Text.Json.JsonSerializer.Serialize(new { managedReadyMs = Program.Startup.ElapsedMilliseconds, version = UpdateService.CurrentVersion, runtimePath }));
                BeginInvoke(() => { quitting = true; Close(); }); return;
            }
            if (PreviewOutput != null)
            {
                BeginInvoke(() =>
                {
                    using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(PreviewOutput);
                    foreach (var target in new[] { "conversion", "settings", "history" })
                    {
                        ShowPage(target); RefreshQueue(); using var capture = new Bitmap(Width, Height); DrawToBitmap(capture, new Rectangle(0, 0, Width, Height));
                        capture.Save(Path.Combine(Path.GetDirectoryName(PreviewOutput)!, "SenjiDownloader-" + target + ".png"));
                    }
                    quitting = true; Close();
                });
            }
            return;
        }
        CreateTray(); refresh.Start(); saveTimer.Start(); updateTimer.Start();
        if (background) { ShowInTaskbar = false; Hide(); Opacity = 1; }
        if (instance != null) _ = instance.Listen(command => Dispatch(() => { if (command == "EXIT") Quit(); else Restore(); }));
        try { using var ready = EventWaitHandle.OpenExisting(SingleInstance.UpdateReady); ready.Set(); } catch (WaitHandleCannotBeOpenedException) { }
        AppPaths.Log($"Interfaz lista. Versión {UpdateService.CurrentVersion}; proceso {Environment.ProcessId}; inicio administrado {Program.Startup.ElapsedMilliseconds} ms.");
        // No networking, external tools, or system integration are required to paint the first window.
        await Task.Delay(1200);
        if (quitting) return;
        try
        {
            if (!Program.NoIntegration) DesktopIntegration.SetStartup(preferences.StartWithWindows);
            if (!Program.NoIntegration && preferences.IntegratedPath != Environment.ProcessPath)
            {
                DesktopIntegration.CreateShortcut(); preferences.IntegratedPath = Environment.ProcessPath!; SavePreferences();
            }
        }
        catch (Exception ex) { AppPaths.Log("Integración de Windows: " + ex.Message); }
        await updater.Check();
    }
    private void BuildSidebar()
    {
        var brand = new Panel { Location = new Point(18, 28), Size = new Size(164, 70), BackColor = Theme.Sidebar };
        brand.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Theme.FillRound(e.Graphics, Theme.Accent, new RectangleF(4, 9, 40, 40), 10);
            Theme.Glyph(e.Graphics, "download", Theme.AccentText, new RectangleF(12, 17, 24, 24));
            using var font = Theme.Font(14, true); TextRenderer.DrawText(e.Graphics, "Senji", font, new Point(57, 7), Theme.Text);
            using var small = Theme.Font(9); TextRenderer.DrawText(e.Graphics, "Downloader", small, new Point(58, 34), Theme.Muted);
        };
        sidebar.Controls.Add(brand);
        var section = Label("ESPACIO DE TRABAJO", 8, true, Theme.Muted); section.SetBounds(28, 122, 160, 25); sidebar.Controls.Add(section);
        AddNav("downloads", "Descargas", "download", 164);
        AddNav("conversion", "Convertir", "convert", 222);
        AddNav("history", "Historial", "clock", 280);
        AddNav("settings", "Preferencias", "settings", 338);
        var bottom = new Panel { Height = 106, Dock = DockStyle.Bottom, BackColor = Theme.Sidebar };
        var line = new Panel { BackColor = Theme.Line, Height = 1, Dock = DockStyle.Top }; bottom.Controls.Add(line);
        var ready = Label("●  Listo en segundo plano", 9, false, Theme.Accent); ready.SetBounds(22, 20, 174, 24); bottom.Controls.Add(ready);
        var version = Label("v" + UpdateService.CurrentVersion + "  /  WINDOWS", 8, true, Theme.Muted); version.SetBounds(24, 52, 174, 20); bottom.Controls.Add(version);
        var portable = Label("Hecho para ir contigo.", 8, false, Theme.Muted); portable.SetBounds(24, 75, 174, 18); bottom.Controls.Add(portable);
        sidebar.Controls.Add(bottom);
    }
    private void AddNav(string name, string text, string glyph, int top)
    {
        var button = new SmoothButton { Text = text, GlyphName = glyph, AlignLeft = true, Quiet = true };
        button.SetBounds(16, top, 170, 46); button.Click += (_, _) => ShowPage(name);
        sidebar.Controls.Add(button); navigation[name] = button;
    }
    private Panel NewPage(string key, string title, string description)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Visible = false };
        var heading = Label(title, 26, true); heading.SetBounds(28, 23, 690, 46); panel.Controls.Add(heading);
        var subheading = Label(description, 10, false, Theme.Muted); subheading.SetBounds(30, 74, 780, 25); subheading.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; panel.Controls.Add(subheading);
        host.Controls.Add(panel); pages[key] = panel; return panel;
    }
    private static void AddLabel(Panel panel, string text, int left, int top, int width = 170)
    {
        var label = Label(text, 8, true, Theme.Muted); label.SetBounds(left, top, width, 22); panel.Controls.Add(label);
    }
    private void BuildDownloads()
    {
        var panel = NewPage("downloads", "Tu próxima descarga.", "Tus videos y tu música, en un solo lugar. Pega los enlaces y continúa con tu día.");
        var paste = new SmoothButton { Text = "Pegar enlaces", GlyphName = "paste", Width = 155, Height = 36, Quiet = true };
        paste.Click += (_, _) => { try { if (Clipboard.ContainsText()) { var text = Clipboard.GetText(); links.Value = links.Value.Trim().Length == 0 ? text : links.Value + Environment.NewLine + text; links.Editor.Focus(); } } catch (Exception ex) { ShowDetails("Portapapeles", ex.Message); } };
        panel.Controls.Add(paste); AddLabel(panel, "ENLACES DE AUDIO O VIDEO", 30, 116, 270);
        links.SetBounds(28, 148, 790, 104); panel.Controls.Add(links);
        AddLabel(panel, "CALIDAD", 30, 265); AddLabel(panel, "FORMATO", 231, 265); AddLabel(panel, "EN PARALELO", 390, 265);
        quality.SetBounds(30, 292, 176, 34); format.SetBounds(230, 292, 134, 34); parallel.SetBounds(389, 292, 144, 34);
        panel.Controls.AddRange([quality, format, parallel]);
        var start = new SmoothButton { Text = "Descargar", GlyphName = "download", Primary = true, Width = 177, Height = 44 };
        start.Click += (_, _) => AddDownloads(); panel.Controls.Add(start);
        AddLabel(panel, "GUARDAR EN", 30, 347, 150);
        folder.SetBounds(28, 374, 608, 44); panel.Controls.Add(folder);
        var browse = new SmoothButton { Text = "Carpeta", GlyphName = "folder", Width = 130, Height = 44 };
        browse.Click += (_, _) => SelectFolder(); panel.Controls.Add(browse);
        var queueTitle = Label("Cola de descargas", 14, true); queueTitle.SetBounds(30, 440, 236, 30); panel.Controls.Add(queueTitle);
        panel.Controls.Add(queueSummary); panel.Controls.Add(pauseQueue); pauseQueue.Click += (_, _) => { engine.TogglePause(); dirty = true; };
        var openFolder = new SmoothButton { Text = "Abrir carpeta", GlyphName = "folder", Width = 142, Height = 36, Quiet = true };
        openFolder.Click += (_, _) => Safe(() => DesktopIntegration.OpenFolder(folder.Value)); panel.Controls.Add(openFolder);
        panel.Controls.Add(queueList); panel.Controls.Add(empty); panel.Controls.Add(status);
        empty.TextAlign = ContentAlignment.MiddleCenter;
        panel.Resize += (_, _) =>
        {
            var width = panel.ClientSize.Width; links.Width = Math.Max(400, width - 56);
            paste.Location = new Point(width - 187, 107); start.Location = new Point(width - 205, 286);
            folder.Width = width - 218; browse.Location = new Point(width - 160, 374);
            queueSummary.SetBounds(267, 445, Math.Max(100, width - 559), 22);
            pauseQueue.Location = new Point(width - 288, 437); openFolder.Location = new Point(width - 175, 437);
            if (queueList.Parent == panel)
            {
                queueList.SetBounds(28, 482, width - 45, Math.Max(58, panel.ClientSize.Height - 524));
                empty.SetBounds(30, 482, width - 60, Math.Max(58, panel.ClientSize.Height - 532));
            }
            status.SetBounds(30, panel.ClientSize.Height - 30, width - 60, 22); ResizeRows();
        };
        queueList.Resize += (_, _) => ResizeRows();
    }
    private void BuildConversion()
    {
        var panel = NewPage("conversion", "Dale otro formato.", "Convierte varios archivos a MP3, WAV o MP4. Los originales se conservan.");
        AddLabel(panel, "ARCHIVOS DE ORIGEN", 30, 128, 240);
        conversionFiles.SetBounds(28, 161, 790, 158); conversionFiles.Editor.ReadOnly = true; panel.Controls.Add(conversionFiles);
        var select = new SmoothButton { Text = "Elegir archivos", GlyphName = "folder", Width = 169 };
        select.SetBounds(28, 337, 170, 44); select.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Multiselect = true, Title = "Seleccionar archivos", Filter = "Archivos multimedia|*.mp4;*.mkv;*.webm;*.avi;*.mov;*.mp3;*.m4a;*.wav;*.flac;*.ogg;*.opus;*.aac|Todos los archivos|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK) SetMedia(dialog.FileNames);
        }; panel.Controls.Add(select);
        AddLabel(panel, "FORMATO DE SALIDA", 30, 413, 240); conversionFormat.SetBounds(30, 446, 198, 34); panel.Controls.Add(conversionFormat);
        var destination = Label("Los archivos se guardan en la carpeta elegida en Descargas.", 10, false, Theme.Muted); destination.SetBounds(30, 504, 744, 42); panel.Controls.Add(destination);
        var convert = new SmoothButton { Text = "Convertir archivos", GlyphName = "convert", Primary = true, Width = 206, Height = 48 };
        convert.SetBounds(28, 574, 206, 48); convert.Click += (_, _) => Safe(() =>
        {
            if (mediaFiles.Length == 0) throw new ArgumentException("Selecciona al menos un archivo para convertir.");
            engine.AddConversions(mediaFiles, folder.Value, conversionFormat.Value); mediaFiles = []; conversionFiles.Value = ""; ShowPage("downloads"); RefreshQueue();
        }); panel.Controls.Add(convert);
        panel.Resize += (_, _) => conversionFiles.Width = panel.Width - 56;
        panel.AutoScroll = true; panel.AutoScrollMinSize = new Size(0, 650);
    }
    private void BuildSettings()
    {
        var panel = NewPage("settings", "A tu manera.", "Elige cómo trabaja SenjiDownloader, incluso cuando la ventana está cerrada.");
        AddToggle(panel, "Iniciar con Windows", "Se inicia discretamente en la bandeja del sistema.", 125, preferences.StartWithWindows,
            value => Safe(() => { DesktopIntegration.SetStartup(value); preferences.StartWithWindows = value; SavePreferences(); }));
        AddToggle(panel, "Cerrar a la bandeja", "Las descargas continúan al cerrar la ventana.", 201, preferences.CloseToTray,
            value => { preferences.CloseToTray = value; SavePreferences(); });
        AddToggle(panel, "Actualizaciones automáticas", "Se aplican cuando las descargas terminan.", 277, preferences.AutoUpdate,
            value => { preferences.AutoUpdate = value; SavePreferences(); });
        var desktop = new SmoothButton { Text = "Crear acceso directo", GlyphName = "download", Width = 216 };
        desktop.SetBounds(28, 376, 216, 44); desktop.Click += (_, _) => Safe(() => { DesktopIntegration.CreateShortcut(); status.Text = "Acceso directo creado en el escritorio."; }); panel.Controls.Add(desktop);
        var check = new SmoothButton { Text = "Buscar actualizaciones", GlyphName = "convert", Width = 229 };
        check.SetBounds(258, 376, 229, 44); check.Click += async (_, _) => { check.Enabled = false; try { await updater.Check(true); updateStatus.Text = updater.Status; } finally { if (!check.IsDisposed) check.Enabled = true; } }; panel.Controls.Add(check);
        AddLabel(panel, "CANAL DE ACTUALIZACIONES EN GITHUB", 30, 452, 550);
        repository.Value = preferences.ReleaseRepository; repository.SetBounds(28, 484, 620, 44); panel.Controls.Add(repository);
        var saveRepo = new SmoothButton { Text = "Guardar", Width = 122 }; panel.Controls.Add(saveRepo); saveRepo.Click += (_, _) => Safe(() =>
        {
            var value = repository.Value.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new ArgumentException("Escribe el canal con el formato usuario/repositorio.");
            preferences.ReleaseRepository = value; SavePreferences(); updateStatus.Text = "Canal guardado.";
        });
        updateStatus.SetBounds(30, 544, 758, 48); panel.Controls.Add(updateStatus); updateStatus.Text = updater.Status;
        AddLabel(panel, "SESIÓN DEL NAVEGADOR (OPCIONAL)", 30, 621, 420);
        var cookies = new DarkCombo("Ninguno", "Chrome", "Edge", "Firefox"); cookies.SelectedItem = preferences.CookieBrowser;
        cookies.SetBounds(30, 654, 220, 34); cookies.SelectedIndexChanged += (_, _) => { preferences.CookieBrowser = cookies.Value; SavePreferences(); }; panel.Controls.Add(cookies);
        var note = Label("Utiliza tu sesión únicamente si un video requiere acceso a tu cuenta.\nPuedes dejarlo en Ninguno para videos públicos.", 9, false, Theme.Muted); note.SetBounds(30, 705, 730, 51); panel.Controls.Add(note);
        var data = new SmoothButton { Text = "Abrir datos y registros", GlyphName = "folder", Width = 224, Quiet = true }; data.SetBounds(28, 779, 224, 44); data.Click += (_, _) => Safe(() => DesktopIntegration.OpenFolder(AppPaths.Data)); panel.Controls.Add(data);
        panel.AutoScroll = true; panel.AutoScrollMinSize = new Size(0, 856);
        panel.Resize += (_, _) => { var available = panel.ClientSize.Width - 75; repository.Width = available - 135; saveRepo.Location = new Point(repository.Right + 12, 484); updateStatus.Width = available; };
    }
    private void AddToggle(Panel panel, string title, string hint, int top, bool value, Action<bool> change)
    {
        var row = new Surface { Height = 65, ShowBorder = false }; row.SetBounds(28, top, 790, 65);
        var label = Label(title, 11, true); label.SetBounds(18, 7, 600, 25); row.Controls.Add(label);
        var caption = Label(hint, 9, false, Theme.Muted); caption.SetBounds(18, 34, 600, 23); row.Controls.Add(caption);
        var toggle = new CheckBox { Checked = value, Text = "", AccessibleName = title, BackColor = Theme.Surface, ForeColor = Theme.Accent, Size = new Size(26, 28), Cursor = Cursors.Hand };
        toggle.Location = new Point(row.Width - 48, 20); toggle.CheckedChanged += (_, _) => change(toggle.Checked); row.Controls.Add(toggle);
        panel.Controls.Add(row);
        panel.Resize += (_, _) => { row.Width = panel.ClientSize.Width - 56; toggle.Location = new Point(row.Width - 48, 20); label.Width = caption.Width = row.Width - 78; };
    }
    private void BuildHistory()
    {
        var panel = NewPage("history", "Lo que ya guardaste.", "Encuentra tus archivos terminados y revisa las descargas anteriores.");
        var clear = new SmoothButton { Text = "Limpiar historial", Width = 170, Quiet = true }; clear.SetBounds(28, 126, 170, 40); clear.Click += (_, _) => { engine.ClearFinished(); dirty = true; RefreshQueue(); }; panel.Controls.Add(clear);
        var note = Label("Los archivos descargados se conservan.", 9, false, Theme.Muted); note.SetBounds(217, 135, 390, 24); panel.Controls.Add(note);
        panel.Resize += (_, _) =>
        {
            if (queueList.Parent != panel) return;
            queueList.SetBounds(28, 195, panel.Width - 45, Math.Max(90, panel.Height - 225));
            empty.SetBounds(30, 195, panel.Width - 60, Math.Max(90, panel.Height - 235));
        };
    }
    public void ShowPage(string name)
    {
        page = name;
        foreach (var item in pages) item.Value.Visible = item.Key == name;
        foreach (var item in navigation) { item.Value.Selected = item.Key == name; item.Value.Invalidate(); }
        if (name is "history" or "downloads")
        {
            var target = pages[name]; queueList.Parent = target; empty.Parent = target;
            if (name == "history")
            {
                queueList.SetBounds(28, 195, target.Width - 45, Math.Max(90, target.Height - 225));
                empty.SetBounds(30, 195, target.Width - 60, Math.Max(90, target.Height - 235));
            }
            else
            {
                queueList.SetBounds(28, 482, target.Width - 45, Math.Max(58, target.Height - 524));
                empty.SetBounds(30, 482, target.Width - 60, Math.Max(58, target.Height - 532));
            }
        }
        dirty = true; RefreshQueue();
    }
    private void ResizeRows() { foreach (var row in rows.Values) row.Width = Math.Max(300, queueList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12); }
    private void RefreshQueue()
    {
        if (page is not ("downloads" or "history")) return;
        var all = engine.Snapshot;
        var jobs = (page == "history" ? all.Where(j => j.IsTerminal).Reverse() : all.Where(j => !j.IsTerminal).Concat(all.Where(j => j.IsTerminal).TakeLast(4).Reverse())).ToArray();
        queueList.SuspendLayout();
        foreach (var id in rows.Keys.Where(id => !jobs.Any(j => j.Id == id)).ToArray()) { var row = rows[id]; queueList.Controls.Remove(row); row.Dispose(); rows.Remove(id); }
        for (var i = 0; i < jobs.Length; i++)
        {
            var job = jobs[i];
            if (!rows.TryGetValue(job.Id, out var row))
            {
                row = new JobRow(job); row.Command += JobCommand; rows[job.Id] = row; queueList.Controls.Add(row);
            }
            else row.SetJob(job);
            queueList.Controls.SetChildIndex(row, i);
        }
        ResizeRows(); queueList.ResumeLayout(true);
        empty.Visible = jobs.Length == 0; queueList.Visible = jobs.Length > 0;
        empty.Text = page == "history" ? "Tu historial aparecerá aquí.\n\nCada descarga terminada, siempre a mano." : "Tu cola está lista.\n\nPega uno o varios enlaces para comenzar.";
        var active = all.Count(j => j.IsActive); var pending = all.Count(j => j.State == JobState.Queued);
        queueSummary.Text = $"{active} en curso · {pending} en cola";
        pauseQueue.Text = engine.IsPaused ? "Continuar" : "Pausar"; pauseQueue.GlyphName = engine.IsPaused ? "play" : "pause"; pauseQueue.Invalidate();
        if (tray != null) tray.Text = active > 0 ? $"SenjiDownloader · {active} en curso" : "SenjiDownloader · listo";
    }
    private void JobCommand(MediaJob job, string command) => Safe(() =>
    {
        switch (command)
        {
            case "pause": engine.Pause(job.Id); break;
            case "retry": engine.Retry(job.Id); break;
            case "cancel": engine.Cancel(job.Id); break;
            case "reveal": DesktopIntegration.Reveal(job.OutputFile); break;
            case "error": ShowDetails("Detalles de la descarga", job.Error); break;
        }
        RefreshQueue();
    });
    private void AddDownloads() => Safe(() =>
    {
        var count = engine.AddDownloads(links.Value, folder.Value, quality.Value, format.Value);
        preferences.DownloadFolder = Path.GetFullPath(folder.Value); preferences.Quality = quality.Value; preferences.Format = format.Value; SavePreferences();
        status.Text = count == 0 ? "Los enlaces ya están en la cola." : $"{count} {(count == 1 ? "enlace añadido" : "enlaces añadidos")} · Ctrl+Enter para descargar";
        links.Value = ""; RefreshQueue();
    });
    private void SelectFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Carpeta de descargas", UseDescriptionForTitle = true, InitialDirectory = folder.Value };
        if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Value = dialog.SelectedPath; preferences.DownloadFolder = dialog.SelectedPath; SavePreferences(); }
    }
    private void SetMedia(IEnumerable<string> files) { mediaFiles = files.ToArray(); conversionFiles.Value = string.Join(Environment.NewLine, mediaFiles); }
    private void Dropping(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true || e.Data?.GetDataPresent(DataFormats.UnicodeText) == true) e.Effect = DragDropEffects.Copy;
    }
    private void Drop(object? sender, DragEventArgs e) => Safe(() =>
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            var linkFiles = files.Where(f => Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f).Equals(".url", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (linkFiles.Length > 0)
            {
                ShowPage("downloads"); links.Value = string.Join(Environment.NewLine, linkFiles.Select(f => new FileInfo(f).Length <= 1_000_000 ? File.ReadAllText(f) : throw new ArgumentException("El archivo de enlaces supera 1 MB.")));
            }
            else { SetMedia(files); ShowPage("conversion"); }
        }
        else if (e.Data?.GetData(DataFormats.UnicodeText) is string text) { ShowPage("downloads"); links.Value = text; }
    });
    private void Finished(MediaJob job)
    {
        if (tray != null && !Visible) { tray.BalloonTipTitle = "Descarga lista"; tray.BalloonTipText = job.Title; tray.ShowBalloonTip(2500); }
        dirty = true;
    }
    private void CreateTray()
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Surface, ForeColor = Theme.Text, ShowImageMargin = false, Renderer = new DarkMenuRenderer() };
        menu.Items.Add("Abrir SenjiDownloader", null, (_, _) => Restore());
        menu.Items.Add("Abrir carpeta de descargas", null, (_, _) => Safe(() => DesktopIntegration.OpenFolder(preferences.DownloadFolder)));
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Salir por completo", null, (_, _) => Quit());
        tray = new NotifyIcon { Icon = appIcon, Text = "SenjiDownloader", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Restore(); tray.BalloonTipClicked += (_, _) => Restore();
    }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!quitting && !preview && preferences.CloseToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; ShowInTaskbar = false; Hide(); return;
        }
        quitting = true; engine.Dispose(); updater.Dispose(); if (tray != null) { tray.Visible = false; tray.Dispose(); }
        refresh.Stop(); saveTimer.Stop(); updateTimer.Stop();
    }
    public void Restore() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
    public void Quit() { quitting = true; Close(); }
    private void Safe(Action action) { try { action(); } catch (Exception ex) { ShowDetails("SenjiDownloader", ex.Message); } }
    private void ShowDetails(string title, string text)
    {
        using var dialog = new Form { Text = title, BackColor = Theme.Canvas, ForeColor = Theme.Text, Font = Theme.Font(), Size = new Size(700, 400), MinimumSize = new Size(480, 300), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Icon = appIcon };
        var body = new TextBox { Multiline = true, ReadOnly = true, Text = text, BorderStyle = BorderStyle.None, BackColor = Theme.Canvas, ForeColor = Theme.Text, Font = Theme.Font(11), Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) }; content.Controls.Add(body); dialog.Controls.Add(content);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 68, Padding = new Padding(24, 8, 24, 16) };
        var close = new SmoothButton { Text = "Entendido", Primary = true, Width = 132, Dock = DockStyle.Right, DialogResult = DialogResult.OK };
        bottom.Controls.Add(close); dialog.Controls.Add(bottom); dialog.AcceptButton = close; dialog.CancelButton = close; dialog.Shown += (_, _) => Theme.DarkTitle(dialog); dialog.ShowDialog(this);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { refresh.Dispose(); saveTimer.Dispose(); updateTimer.Dispose(); appIcon.Dispose(); }
        base.Dispose(disposing);
    }
}
