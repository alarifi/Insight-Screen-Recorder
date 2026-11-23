namespace InsightScreenRecorder;

internal sealed class RegionSelector : Form
{
    private Point _anchor;
    private Rectangle _selection;
    private bool _dragging;
    public Rectangle SelectedRegion { get; private set; }
    public RegionSelector()
    {
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen; BackColor = Color.Black; Opacity = 0.45;
        TopMost = true; DoubleBuffered = true; Cursor = Cursors.Cross; KeyPreview = true;
        MouseDown += (_, e) => { if (e.Button != MouseButtons.Left) return; _anchor = e.Location; _dragging = true; Capture = true; };
        MouseMove += (_, e) => { if (!_dragging) return; _selection = Rectangle.FromLTRB(Math.Min(_anchor.X, e.X), Math.Min(_anchor.Y, e.Y), Math.Max(_anchor.X, e.X), Math.Max(_anchor.Y, e.Y)); Invalidate(); };
        MouseUp += (_, e) => {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false; Capture = false;
            if (_selection.Width < 16 || _selection.Height < 16) return;
            SelectedRegion = new Rectangle(Left + _selection.X, Top + _selection.Y, _selection.Width / 2 * 2, _selection.Height / 2 * 2);
            DialogResult = DialogResult.OK; Close();
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.Turquoise, 3);
        e.Graphics.DrawRectangle(pen, _selection);
        using var font = new Font("Segoe UI", 18, FontStyle.Bold);
        e.Graphics.DrawString("Drag to select a recording area • Esc to cancel", font, Brushes.White, 32, 32);
        if (_selection.Width > 0) e.Graphics.DrawString($"{_selection.Width / 2 * 2} × {_selection.Height / 2 * 2}", font, Brushes.White, _selection.Left + 8, _selection.Top + 8);
    }
}
