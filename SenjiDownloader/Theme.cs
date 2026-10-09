using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace SenjiDownloader;

public static class Theme
{
    public static readonly Color Canvas = Color.FromArgb(21, 26, 23);
    public static readonly Color Sidebar = Color.FromArgb(15, 20, 17);
    public static readonly Color Surface = Color.FromArgb(29, 36, 31);
    public static readonly Color Field = Color.FromArgb(18, 24, 20);
    public static readonly Color Line = Color.FromArgb(60, 76, 65);
    public static readonly Color Text = Color.FromArgb(237, 244, 239);
    public static readonly Color Muted = Color.FromArgb(164, 183, 171);
    public static readonly Color Accent = Color.FromArgb(181, 235, 200);
    public static readonly Color AccentText = Color.FromArgb(17, 42, 26);
    public static readonly Color Error = Color.FromArgb(245, 160, 151);
    public static bool AnimationsEnabled { get; } = ReadAnimationSetting();
    private static bool ReadAnimationSetting() { var enabled = 1; try { SystemParametersInfo(0x1042, 0, ref enabled, 0); } catch { } return enabled != 0; }
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, ref int value, uint flags);
    public static Font Font(float size = 10, bool bold = false) => new("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
    public static GraphicsPath Rounded(RectangleF bounds, float radius = 12)
    {
        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2); var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }
    public static void FillRound(Graphics graphics, Color color, RectangleF bounds, float radius = 10)
    {
        using var path = Rounded(bounds, radius); using var brush = new SolidBrush(color); graphics.FillPath(brush, path);
    }
    public static void DarkTitle(Form form)
    {
        try { int enabled = 1; DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)); } catch { }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int length);
    public static void Glyph(Graphics graphics, string glyph, Color color, RectangleF rect)
    {
        var state = graphics.Save(); graphics.TranslateTransform(rect.X, rect.Y); graphics.ScaleTransform(rect.Width / 24f, rect.Height / 24f);
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        switch (glyph)
        {
            case "download": graphics.DrawLine(pen, 12, 3, 12, 15); graphics.DrawLines(pen, [new(7, 10), new(12, 15), new(17, 10)]); graphics.DrawLines(pen, [new(4, 16), new(4, 20), new(20, 20), new(20, 16)]); break;
            case "folder": graphics.DrawLines(pen, [new(3, 19), new(3, 5), new(10, 5), new(12, 8), new(21, 8), new(21, 19), new(3, 19)]); break;
            case "paste": graphics.DrawRectangle(pen, 5, 5, 14, 16); graphics.FillRectangle(brush, 9, 2, 6, 6); graphics.DrawLine(pen, 9, 12, 15, 12); graphics.DrawLine(pen, 9, 16, 15, 16); break;
            case "pause": graphics.FillRectangle(brush, 7, 5, 3, 14); graphics.FillRectangle(brush, 14, 5, 3, 14); break;
            case "play": graphics.FillPolygon(brush, [new(8, 4), new(20, 12), new(8, 20)]); break;
            case "close": graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 18, 6, 6, 18); break;
            case "check": graphics.DrawLines(pen, [new(5, 12), new(10, 17), new(19, 7)]); break;
            case "convert": graphics.DrawLines(pen, [new(3, 8), new(20, 8), new(16, 4)]); graphics.DrawLines(pen, [new(21, 16), new(4, 16), new(8, 20)]); break;
            case "clock": graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawLines(pen, [new(12, 7), new(12, 12), new(16, 14)]); break;
            case "settings": graphics.DrawEllipse(pen, 8, 8, 8, 8); graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawLine(pen, 12, 1, 12, 5); graphics.DrawLine(pen, 12, 19, 12, 23); graphics.DrawLine(pen, 1, 12, 5, 12); graphics.DrawLine(pen, 19, 12, 23, 12); break;
            case "link": graphics.DrawArc(pen, 3, 3, 12, 10, 100, 250); graphics.DrawArc(pen, 9, 11, 12, 10, 280, 250); graphics.DrawLine(pen, 8, 16, 16, 8); break;
            case "video": graphics.DrawRectangle(pen, 3, 5, 18, 14); graphics.FillPolygon(brush, [new(10, 8), new(16, 12), new(10, 16)]); break;
            case "audio": graphics.DrawLines(pen, [new(10, 17), new(10, 5), new(19, 3), new(19, 15)]); graphics.FillEllipse(brush, 5, 15, 6, 5); graphics.FillEllipse(brush, 14, 13, 6, 5); break;
            case "error": graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawLine(pen, 12, 7, 12, 13); graphics.FillEllipse(brush, 11, 16, 2, 2); break;
            case "arrow": graphics.DrawLines(pen, [new(9, 5), new(16, 12), new(9, 19)]); break;
            default: graphics.DrawEllipse(pen, 5, 5, 14, 14); break;
        }
        graphics.Restore(state);
    }
}

public class SmoothButton : Button
{
    [DefaultValue(false)] public bool Primary { get; set; }
    [DefaultValue(false)] public bool Selected { get; set; }
    [DefaultValue(false)] public bool Quiet { get; set; }
    [DefaultValue("")] public string GlyphName { get; set; } = "";
    [DefaultValue(false)] public bool AlignLeft { get; set; }
    private bool hover, pressed;
    private float hoverAmount;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 16 };
    public SmoothButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; ForeColor = Theme.Text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Font = Theme.Font(10, true); Cursor = Cursors.Hand; Height = 44;
        animation.Tick += (_, _) =>
        {
            var target = hover ? 1f : 0f; hoverAmount += (target - hoverAmount) * .28f;
            if (Math.Abs(target - hoverAmount) < .02) { hoverAmount = target; animation.Stop(); } Invalidate();
        };
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; if (Theme.AnimationsEnabled) animation.Start(); else { hoverAmount = 1; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; if (Theme.AnimationsEnabled) animation.Start(); else { hoverAmount = 0; Invalidate(); } }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var bg = Primary ? Theme.Accent : Selected ? Color.FromArgb(36, 57, 44) : Quiet ? Parent?.BackColor ?? Theme.Canvas : Theme.Surface;
        var delta = (int)(hoverAmount * (pressed ? -9 : 9));
        bg = Color.FromArgb(Math.Clamp(bg.R + delta, 0, 255), Math.Clamp(bg.G + delta, 0, 255), Math.Clamp(bg.B + delta, 0, 255));
        var foreground = !Enabled ? Theme.Muted : Primary ? Theme.AccentText : Selected ? Theme.Accent : ForeColor;
        Theme.FillRound(g, bg, new RectangleF(1, 1, Width - 2, Height - 2), 9);
        if (Focused)
        {
            using var pen = new Pen(Theme.Accent, 2); using var path = Theme.Rounded(new RectangleF(2, 2, Width - 4, Height - 4), 8); g.DrawPath(pen, path);
        }
        else if (!Primary && !Quiet && !Selected)
        {
            using var pen = new Pen(Theme.Line); using var path = Theme.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 9); g.DrawPath(pen, path);
        }
        var textWidth = TextRenderer.MeasureText(Text, Font, new Size(1000, Height), TextFormatFlags.NoPadding).Width;
        var start = AlignLeft ? 16 : (Width - textWidth - (GlyphName.Length > 0 && Text.Length > 0 ? 28 : 0)) / 2;
        if (GlyphName.Length > 0)
        {
            var x = Text.Length == 0 ? (Width - 20) / 2 : start;
            Theme.Glyph(g, GlyphName, foreground, new RectangleF(x, (Height - 20) / 2f, 20, 20));
            if (Text.Length > 0) start += 28;
        }
        if (Text.Length > 0) TextRenderer.DrawText(g, Text, Font, new Rectangle(start, 0, Math.Max(0, Width - start - 9), Height), foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}

public class Surface : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color Fill { get; set; } = Theme.Surface;
    [DefaultValue(true)] public bool ShowBorder { get; set; } = true;
    [DefaultValue(12)] public int Radius { get; set; } = 12;
    public Surface()
    {
        DoubleBuffered = true; BackColor = Theme.Surface;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRound(e.Graphics, Fill, new RectangleF(1, 1, Width - 2, Height - 2), Radius);
        if (ShowBorder)
        {
            using var pen = new Pen(ContainsFocus ? Theme.Accent : Theme.Line);
            using var path = Theme.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), Radius); e.Graphics.DrawPath(pen, path);
        }
    }
}

public sealed class TextField : Surface
{
    public TextBox Editor { get; } = new();
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string Value { get => Editor.Text; set => Editor.Text = value; }
    public TextField(bool multiline = false, string placeholder = "")
    {
        Fill = Theme.Field; BackColor = Theme.Field; Height = multiline ? 120 : 44; Padding = new Padding(14, multiline ? 12 : 11, 14, 10);
        Editor.Multiline = multiline; Editor.BorderStyle = BorderStyle.None; Editor.BackColor = Theme.Field; Editor.ForeColor = Theme.Text;
        Editor.Font = Theme.Font(11); Editor.Dock = DockStyle.Fill; Editor.PlaceholderText = placeholder;
        Editor.AcceptsReturn = multiline; Editor.ScrollBars = ScrollBars.None;
        Editor.Enter += (_, _) => Invalidate(); Editor.Leave += (_, _) => Invalidate();
        Controls.Add(Editor);
        if (multiline && placeholder.Length > 0)
        {
            var hint = new Label { Text = placeholder, Dock = DockStyle.Fill, ForeColor = Theme.Muted, BackColor = Theme.Field, Font = Theme.Font(11), UseMnemonic = false };
            Controls.Add(hint); hint.BringToFront(); hint.Click += (_, _) => Editor.Focus();
            Editor.TextChanged += (_, _) => hint.Visible = Editor.TextLength == 0;
            Editor.Enter += (_, _) => hint.Visible = Editor.TextLength == 0; Editor.Leave += (_, _) => hint.Visible = Editor.TextLength == 0;
        }
    }
}

public sealed class DarkCombo : Control
{
    private readonly string[] items;
    private int index;
    private ContextMenuStrip? menu;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int SelectedIndex
    {
        get => index;
        set { var next = Math.Clamp(value, 0, Math.Max(0, items.Length - 1)); if (next != index) { index = next; SelectedIndexChanged?.Invoke(this, EventArgs.Empty); } Invalidate(); }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public object? SelectedItem
    {
        get => items.Length > 0 ? items[index] : null;
        set { var position = Array.IndexOf(items, value?.ToString()); if (position >= 0) SelectedIndex = position; }
    }
    public string Value => SelectedItem?.ToString() ?? "";
    public event EventHandler? SelectedIndexChanged;
    public DarkCombo(params string[] items)
    {
        this.items = items; Font = Theme.Font(10); BackColor = Theme.Canvas; ForeColor = Theme.Text; Height = 38;
        DoubleBuffered = true; TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRound(g, Theme.Surface, new RectangleF(1, 1, Width - 2, Height - 2), 8);
        using var pen = new Pen(Focused ? Theme.Accent : Theme.Line); using var path = Theme.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 8); g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Value, Font, new Rectangle(12, 0, Width - 38, Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using var arrow = new Pen(Theme.Muted, 1.5f); g.DrawLines(arrow, [new(Width - 23, Height / 2 - 2), new(Width - 18, Height / 2 + 3), new(Width - 13, Height / 2 - 2)]);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Expand(); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up) { SelectedIndex--; e.Handled = true; }
        else if (e.KeyCode == Keys.Down) { SelectedIndex++; e.Handled = true; }
        else if (e.KeyCode == Keys.Home) { SelectedIndex = 0; e.Handled = true; }
        else if (e.KeyCode == Keys.End) { SelectedIndex = items.Length - 1; e.Handled = true; }
        else if (e.KeyCode is Keys.Enter or Keys.Space or Keys.F4) { Expand(); e.Handled = true; }
        base.OnKeyDown(e);
    }
    private void Expand()
    {
        if (!Enabled || IsDisposed) return;
        var popup = OptionsMenu;
        popup.MinimumSize = new Size(Width, 0);
        foreach (ToolStripItem item in popup.Items) item.Width = Math.Max(Width - 3, 165);
        if (!popup.Visible) popup.Show(this, new Point(0, Height + 4));
    }
    internal ContextMenuStrip OptionsMenu => menu ??= CreateMenu();
    private ContextMenuStrip CreateMenu()
    {
        var popup = new ContextMenuStrip { BackColor = Theme.Surface, ForeColor = Theme.Text, ShowImageMargin = false, ShowCheckMargin = false, Font = Font, Renderer = new DarkMenuRenderer() };
        for (var i = 0; i < items.Length; i++)
        {
            var selected = i; var item = new ToolStripMenuItem(items[i]) { Height = 34, AutoSize = false, Width = Math.Max(Width - 3, 165), Padding = new Padding(10, 4, 10, 4) };
            item.Click += (_, _) => SelectedIndex = selected; popup.Items.Add(item);
        }
        // WinForms still uses the popup after Closed returns. Its owner disposes it.
        return popup;
    }
    protected override void Dispose(bool disposing) { if (disposing) menu?.Dispose(); base.Dispose(disposing); }
    protected override AccessibleObject CreateAccessibilityInstance() => new SelectionAccessibleObject(this);
    private sealed class SelectionAccessibleObject(DarkCombo owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => owner.Value; set => owner.SelectedItem = value; }
        public override string DefaultAction => "Abrir opciones";
        public override void DoDefaultAction() => owner.Expand();
    }
}

public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.Item.Selected ? Color.FromArgb(47, 65, 54) : Theme.Surface); e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.Text; base.OnRenderItemText(e); }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.Surface); }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using var pen = new Pen(Theme.Line); e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
}

public sealed class JobRow : Panel
{
    private MediaJob job;
    private readonly SmoothButton action = new() { Width = 38, Height = 34 };
    private readonly SmoothButton extra = new() { Width = 38, Height = 34, Quiet = true };
    public event Action<MediaJob, string>? Command;
    public JobRow(MediaJob job)
    {
        this.job = job; DoubleBuffered = true; Height = 100; BackColor = Theme.Canvas; Margin = new Padding(0, 0, 0, 8);
        Controls.Add(action); Controls.Add(extra);
        action.Click += (_, _) => Command?.Invoke(this.job, this.job.State == JobState.Completed ? "reveal" : this.job.IsActive || this.job.State == JobState.Queued ? "pause" : "retry");
        extra.Click += (_, _) => Command?.Invoke(this.job, this.job.State == JobState.Failed ? "error" : "cancel");
        Resize += (_, _) => { action.Location = new Point(Width - 94, 30); extra.Location = new Point(Width - 50, 30); Invalidate(); };
        SetJob(job);
    }
    public void SetJob(MediaJob value)
    {
        job = value;
        action.GlyphName = job.State == JobState.Completed ? "folder" : job.IsActive || job.State == JobState.Queued ? "pause" : "play";
        action.AccessibleName = job.State == JobState.Completed ? "Mostrar archivo" : job.IsActive || job.State == JobState.Queued ? "Pausar descarga" : "Reanudar o reintentar";
        extra.GlyphName = job.State == JobState.Failed ? "error" : "close"; extra.AccessibleName = job.State == JobState.Failed ? "Detalles del error" : "Cancelar";
        extra.Visible = job.State is not (JobState.Completed or JobState.Cancelled);
        action.Enabled = job.State != JobState.Cancelled;
        AccessibleName = $"{job.Title}: {job.Detail}"; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRound(g, Theme.Surface, new RectangleF(0, 0, Width - 1, Height - 1), 11);
        var glyph = job.Format is "MP3" or "WAV" ? "audio" : "video";
        Theme.FillRound(g, Color.FromArgb(39, 51, 43), new RectangleF(16, 19, 44, 44), 10);
        Theme.Glyph(g, glyph, Theme.Accent, new RectangleF(27, 30, 22, 22));
        using var titleFont = Theme.Font(11, true); using var detailFont = Theme.Font(9); using var tagFont = Theme.Font(8, true);
        TextRenderer.DrawText(g, job.Title, titleFont, new Rectangle(74, 17, Math.Max(20, Width - 272), 25), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var tag = job.State switch { JobState.Completed => "COMPLETADO", JobState.Failed => "ERROR", JobState.Paused => "PAUSADO", JobState.Cancelled => "CANCELADO", JobState.Queued => "EN COLA", JobState.Processing => "PROCESANDO", _ => $"{job.Progress:0}%" };
        var stateColor = job.State == JobState.Failed ? Theme.Error : job.State == JobState.Completed || job.IsActive ? Theme.Accent : Theme.Muted;
        TextRenderer.DrawText(g, tag, tagFont, new Rectangle(Width - 190, 17, 176, 22), stateColor, TextFormatFlags.Right | TextFormatFlags.SingleLine);
        var detail = $"{job.Format}  ·  {(job.Kind == JobKind.Convert ? "Conversión" : job.Quality)}  ·  {job.Detail}";
        TextRenderer.DrawText(g, detail, detailFont, new Rectangle(74, 43, Math.Max(20, Width - 190), 23), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var barWidth = Math.Max(1, Width - 106); Theme.FillRound(g, Color.FromArgb(49, 62, 53), new RectangleF(74, 78, barWidth, 4), 2);
        if (job.Progress > 0) Theme.FillRound(g, stateColor, new RectangleF(74, 78, Math.Max(4, (float)(barWidth * job.Progress / 100)), 4), 2);
    }
}
