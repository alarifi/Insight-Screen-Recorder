using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace InsightScreenRecorder;

// A single renderer owns each pixel. No native BUTTON window paints a second caption.
internal class ActionButton : Control, IButtonControl
{
    private bool _selected, _hover, _pressed, _mouseArmed;
    private string _glyph = "";
    private readonly bool _primary;
    protected const TextFormatFlags TextFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
        TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;

    [DefaultValue(false)] public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
    [DefaultValue(false)] public bool Polished { get; set; }
    [DefaultValue("")] public string Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }
    [DefaultValue(DialogResult.None)] public DialogResult DialogResult { get; set; }

    public ActionButton(string text, bool primary = false)
    {
        Text = text; _primary = primary; Cursor = Cursors.Hand; TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        Font = new Font("Segoe UI", 10, FontStyle.Bold); ForeColor = MainForm.Ink;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
    }
    protected void PaintBackground(Graphics graphics)
    {
        graphics.SetClip(ClientRectangle, CombineMode.Intersect);
        Color background = MainForm.Surface;
        for (Control? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.BackColor.A == 255) { background = ancestor.BackColor; break; }
        using var brush = new SolidBrush(background); graphics.FillRectangle(brush, ClientRectangle);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }
    protected override void OnPaintBackground(PaintEventArgs e) => PaintBackground(e.Graphics);
    protected override void OnPaint(PaintEventArgs e)
    {
        PaintBackground(e.Graphics);
        if (Width < 20 || Height < 20) return;
        bool active = Selected || _primary;
        var fillColor = active ? Color.FromArgb(20, 76, 83) : Color.FromArgb(31, 47, 63);
        if (_hover && Enabled) fillColor = active ? Color.FromArgb(23, 91, 97) : Color.FromArgb(40, 60, 77);
        if (_pressed && Enabled) fillColor = Color.FromArgb(16, 65, 73);
        using var path = Shape.Round(new RectangleF(1, 1, Width - 3, Height - 3), Math.Min(10, Height / 4));
        using Brush fill = Polished
            ? new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.FromArgb(Math.Min(255, fillColor.R + 12), Math.Min(255, fillColor.G + 15), Math.Min(255, fillColor.B + 18)), fillColor, 90f)
            : new SolidBrush(fillColor);
        using var border = new Pen(active ? MainForm.Teal : Color.FromArgb(57, 79, 97), active ? 1.5f : 1);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        if (Polished) {
            using var highlight = new Pen(Color.FromArgb(active ? 90 : 45, 170, 220, 230));
            e.Graphics.DrawLine(highlight, 11, 3, Width - 12, 3);
            if (active) { using var accent = new Pen(MainForm.Teal, 2); e.Graphics.DrawLine(accent, 15, Height - 3, Width - 16, Height - 3); }
        }
        Color textColor = Enabled ? (active ? MainForm.Teal : MainForm.Ink) : MainForm.Muted;
        Rectangle textBounds = Rectangle.Inflate(ClientRectangle, -8, -3);
        if (Glyph.Length > 0)
        {
            int captionHeight = Math.Min(32, Height / 3);
            textBounds = new Rectangle(8, Height - captionHeight - 9, Math.Max(1, Width - 16), captionHeight);
            float cx = Width / 2f, cy = Math.Max(22, (Height - captionHeight - 12) / 2f);
            using var pen = new Pen(textColor, 2.5f);
            if (Glyph == "region") pen.DashStyle = DashStyle.Dash;
            e.Graphics.DrawRectangle(pen, cx - 22, cy - 15, 44, 30);
            if (Glyph == "screen") { e.Graphics.DrawLine(pen, cx, cy + 15, cx, cy + 22); e.Graphics.DrawLine(pen, cx - 13, cy + 23, cx + 13, cy + 23); }
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor, TextFlags | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
        {
            using var focus = Shape.Round(new RectangleF(4, 4, Width - 9, Height - 9), Math.Min(7, Height / 5));
            using var pen = new Pen(MainForm.Teal) { DashStyle = DashStyle.Dot }; e.Graphics.DrawPath(pen, focus);
        }
    }
    public void NotifyDefault(bool value) { }
    public void PerformClick() { if (Enabled && Visible) { OnClick(EventArgs.Empty); if (DialogResult != DialogResult.None && FindForm() is Form form) form.DialogResult = DialogResult; } }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) { _pressed = true; Invalidate(); e.Handled = true; e.SuppressKeyPress = true; } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) { bool activate = _pressed; _pressed = false; Invalidate(); e.Handled = true; if (activate) PerformClick(); } base.OnKeyUp(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; _pressed = _mouseArmed; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (Enabled && e.Button == MouseButtons.Left) { Focus(); _mouseArmed = true; _pressed = true; Capture = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { bool activate = _mouseArmed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location); _mouseArmed = false; _pressed = false; Capture = false; Invalidate(); base.OnMouseUp(e); if (activate) PerformClick(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) { _mouseArmed = false; _pressed = false; Invalidate(); } base.OnMouseCaptureChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { _mouseArmed = false; _pressed = false; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnLostFocus(EventArgs e) { _mouseArmed = false; _pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override AccessibleObject CreateAccessibilityInstance() => new ButtonAccessibility(this);
    private sealed class ButtonAccessibility(ActionButton owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override string DefaultAction => "Press";
        public override void DoDefaultAction() => owner.PerformClick();
    }
}

internal sealed class RecordButton : ActionButton
{
    private bool _recording;
    private bool _paused;
    [DefaultValue(false)] public bool Recording { get => _recording; set { _recording = value; UpdateCaption(); } }
    [DefaultValue(false)] public bool Paused { get => _paused; set { _paused = value; UpdateCaption(); } }
    private void UpdateCaption() { Text = !Recording ? "REC" : Paused ? "Resume" : "Pause"; AccessibleName = !Recording ? "Start recording" : Paused ? "Resume recording" : "Pause recording"; Invalidate(); }
    public RecordButton() : base("REC") { AccessibleName = "Start recording"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        PaintBackground(e.Graphics);
        int diameter = Math.Min(144, Math.Min(Width, Height) - 12);
        if (diameter < 24) return;
        int faceWidth = diameter * 17 / 18;
        var bounds = new Rectangle((Width - faceWidth) / 2, (Height - diameter) / 2, faceWidth, diameter);
        using var halo = new SolidBrush(Color.FromArgb(81, 42, 55)); e.Graphics.FillEllipse(halo, bounds);
        bounds.Inflate(-5, -4);
        using var gradient = new LinearGradientBrush(bounds, Color.FromArgb(255, 89, 105), Color.FromArgb(213, 39, 67), 90);
        e.Graphics.FillEllipse(gradient, bounds);
        float points = Math.Clamp(bounds.Width * (Recording ? .15f : .20f) * 72f / DeviceDpi, 9f, 16f);
        using var font = new Font("Segoe UI", points, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, Text, font, bounds, Color.White, TextFlags);
        if (Focused && ShowFocusCues) { using var pen = new Pen(Color.White, 1.5f) { DashStyle = DashStyle.Dot }; bounds.Inflate(-4, -4); e.Graphics.DrawEllipse(pen, bounds); }
    }
}
