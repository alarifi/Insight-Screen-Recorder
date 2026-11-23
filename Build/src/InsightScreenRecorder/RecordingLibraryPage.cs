using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace InsightScreenRecorder;

internal sealed class RecordingLibraryPage : UserControl
{
    private readonly string _folder, _probe;
    private readonly DataGridView _grid = new() { Name = "RecordingsGrid", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None, BackgroundColor = MainForm.Surface, GridColor = Color.FromArgb(45, 63, 80), EnableHeadersVisualStyles = false, ScrollBars = ScrollBars.None };
    private readonly TextBox _search = new() { Name = "RecordingSearch", Dock = DockStyle.Fill, PlaceholderText = "Search recordings…", BackColor = MainForm.Surface, ForeColor = MainForm.Ink, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _notice = new() { Dock = DockStyle.Fill, ForeColor = MainForm.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly LibraryScroll _scroll = new() { Dock = DockStyle.Right, Width = 14 };
    private readonly List<ActionButton> _selectionActions = new();
    private List<RecordingItem> _items = new();
    private CancellationTokenSource? _loading;
    private int _sortColumn = 2;
    private bool _descending = true;
    private RecordingItem? Selected => _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as RecordingItem : null;
    internal RecordingLibraryPage(string folder, string probe)
    {
        _folder = folder; _probe = probe; Dock = DockStyle.Fill; BackColor = MainForm.Background;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); foreach (int height in new[] { 38, 42, 38, 1, 26 }) root.RowStyles.Add(new RowStyle(height == 1 ? SizeType.Percent : SizeType.Absolute, height == 1 ? 100 : height));
        root.Controls.Add(new Label { Text = "Recording library", Dock = DockStyle.Fill, ForeColor = MainForm.Ink, Font = new Font("Segoe UI", 18, FontStyle.Bold) }, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        void Button(string text, Action action, bool needsSelection = false) { var button = new ActionButton(text) { Polished = true, Width = text == "Open folder" ? 120 : 106, Height = 34, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 6, 0) }; button.Click += (_, _) => Execute(action); toolbar.Controls.Add(button); if (needsSelection) _selectionActions.Add(button); }
        Button("Open folder", () => Shell(_folder)); Button("Play", () => WithFile(path => Shell(path)), true); Button("Show file", () => WithFile(path => { var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true }; info.ArgumentList.Add("/select,"); info.ArgumentList.Add(path); Process.Start(info); }), true);
        Button("Rename", RenameSelected, true); Button("Delete", DeleteSelected, true); Button("Refresh", () => _ = RefreshAsync()); root.Controls.Add(toolbar, 0, 1);
        _search.Margin = new Padding(0, 2, 0, 8); _search.TextChanged += (_, _) => Populate();
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty }; searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(new Label { Text = "Search", Dock = DockStyle.Fill, ForeColor = MainForm.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 0); searchRow.Controls.Add(_search, 1, 0); root.Controls.Add(searchRow, 0, 2);
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(32, 51, 69), ForeColor = MainForm.Ink, SelectionBackColor = Color.FromArgb(32, 51, 69), Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(6) };
        _grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = MainForm.Surface, ForeColor = MainForm.Ink, SelectionBackColor = Color.FromArgb(21, 82, 89), SelectionForeColor = Color.White, Padding = new Padding(6, 2, 6, 2) };
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(20, 33, 49); _grid.RowTemplate.Height = 34; _grid.ColumnHeadersHeight = 36; _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        foreach (var column in new[] { ("Recording", 47f), ("Duration", 13f), ("Created", 26f), ("Size", 14f) }) _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Item1, FillWeight = column.Item2, MinimumWidth = column.Item1 == "Recording" ? 160 : 80, SortMode = DataGridViewColumnSortMode.Programmatic });
        _grid.SelectionChanged += (_, _) => { foreach (var button in _selectionActions) button.Enabled = Selected != null; };
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Execute(() => WithFile(Shell)); };
        _grid.ColumnHeaderMouseClick += (_, e) => { _descending = _sortColumn == e.ColumnIndex ? !_descending : false; _sortColumn = e.ColumnIndex; Populate(); };
        _grid.MouseWheel += (_, e) => ScrollTo(Math.Max(0, _grid.FirstDisplayedScrollingRowIndex) - Math.Sign(e.Delta) * 3);
        _grid.Scroll += (_, _) => UpdateScroll(); _grid.SizeChanged += (_, _) => UpdateScroll();
        _scroll.Changed += value => ScrollTo(value); var frame = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; frame.Controls.Add(_grid); frame.Controls.Add(_scroll); root.Controls.Add(frame, 0, 3); root.Controls.Add(_notice, 0, 4); Controls.Add(root);
        foreach (var button in _selectionActions) button.Enabled = false;
        Load += async (_, _) => await RefreshAsync();
    }
    private static void Shell(string path) { if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The file or folder no longer exists. Choose Refresh."); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    private void Execute(Action action) { try { action(); } catch (Exception ex) { _notice.Text = ex.Message; _notice.ForeColor = Color.Salmon; } }
    private void WithFile(Action<string> action) { if (Selected is not { } item) return; if (!File.Exists(item.Path)) throw new FileNotFoundException("This recording no longer exists. Choose Refresh."); action(item.Path); }
    internal async Task RefreshAsync()
    {
        _loading?.Cancel(); var source = new CancellationTokenSource(); _loading = source; var token = source.Token;
        try {
            _notice.ForeColor = MainForm.Muted; _notice.Text = "Reading recordings…";
            var items = await Task.Run(() => RecordingLibrary.Scan(_folder), token); if (IsDisposed || token.IsCancellationRequested) return;
            _items = items; Populate();
            using var limit = new SemaphoreSlim(2);
            await Task.WhenAll(items.Select(async item => {
                await limit.WaitAsync(token); try { item.Seconds = await RecordingLibrary.ReadDurationAsync(_probe, item.Path, token); item.DurationError = ""; }
                catch (OperationCanceledException) { throw; } catch (Exception ex) { item.DurationError = ex.Message; }
                finally { limit.Release(); }
            }));
            if (!IsDisposed && !token.IsCancellationRequested) Populate();
        } catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed && !token.IsCancellationRequested) { _notice.Text = "Unable to read folder: " + ex.Message; _notice.ForeColor = Color.Salmon; } }
        finally { if (ReferenceEquals(_loading, source)) _loading = null; source.Dispose(); }
    }
    private void Populate()
    {
        string? selected = Selected?.Path; var filtered = _items.Where(item => RecordingLibrary.Matches(item, _search.Text.Trim()));
        Func<RecordingItem, object> key = _sortColumn switch { 0 => item => item.DisplayName, 1 => item => item.Seconds ?? -1, 3 => item => item.Bytes, _ => item => item.Created };
        var ordered = _descending ? filtered.OrderByDescending(key) : filtered.OrderBy(key);
        _grid.Rows.Clear(); foreach (var item in ordered) { int row = _grid.Rows.Add(item.DisplayName, item.Duration, item.Created.ToString("yyyy-MM-dd HH:mm"), $"{item.Bytes / 1048576d:F2} MB"); _grid.Rows[row].Tag = item; _grid.Rows[row].Cells[0].ToolTipText = item.Path; _grid.Rows[row].Cells[1].ToolTipText = item.DurationError; if (item.Path == selected) _grid.Rows[row].Selected = true; }
        foreach (DataGridViewColumn column in _grid.Columns) column.HeaderCell.SortGlyphDirection = column.Index == _sortColumn ? (_descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
        foreach (var button in _selectionActions) button.Enabled = Selected != null;
        _notice.ForeColor = MainForm.Muted; _notice.Text = Directory.Exists(_folder) ? $"{_grid.Rows.Count} shown · {_items.Count} recordings · {_items.Sum(item => (double)item.Bytes) / 1048576:F1} MB total" : "Recording folder is unavailable. Check the drive or choose a folder in Record Studio."; UpdateScroll();
    }
    private void RenameSelected()
    {
        if (Selected is not { } item) return;
        using var dialog = new Form { Text = "Rename recording", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(540, 130), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, BackColor = MainForm.Background, ForeColor = MainForm.Ink };
        var name = new TextBox { Text = item.FileName, Location = new Point(18, 20), Width = 500, BackColor = MainForm.Surface, ForeColor = MainForm.Ink };
        var save = new ActionButton("Rename", true) { Polished = true, DialogResult = DialogResult.OK, Location = new Point(320, 76), Size = new Size(95, 34) }; var cancel = new ActionButton("Cancel") { Polished = true, DialogResult = DialogResult.Cancel, Location = new Point(423, 76), Size = new Size(95, 34) };
        dialog.Controls.AddRange(new Control[] { name, save, cancel }); dialog.AcceptButton = save; dialog.CancelButton = cancel;
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return; RecordingLibrary.Rename(item.Path, name.Text); _ = RefreshAsync();
    }
    private void DeleteSelected()
    {
        if (Selected is not { } item) return;
        if (MessageBox.Show(FindForm(), "Move this recording to the Recycle Bin?\n\n" + item.FileName, "Delete recording", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException); _ = RefreshAsync();
    }
    private void ScrollTo(int row) { if (_grid.Rows.Count == 0) return; int max = Math.Max(0, _grid.RowCount - Math.Max(1, _grid.DisplayedRowCount(false))); _grid.FirstDisplayedScrollingRowIndex = Math.Clamp(row, 0, max); UpdateScroll(); }
    private void UpdateScroll() { _scroll.Maximum = Math.Max(0, _grid.RowCount - Math.Max(1, _grid.DisplayedRowCount(false))); _scroll.Value = Math.Max(0, _grid.FirstDisplayedScrollingRowIndex); _scroll.Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) _loading?.Cancel(); base.Dispose(disposing); }
}

internal sealed class LibraryScroll : Control
{
    internal int Maximum, Value;
    internal event Action<int>? Changed;
    internal LibraryScroll() { DoubleBuffered = true; BackColor = MainForm.Surface; TabStop = true; AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "Scroll content"; }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(BackColor); int thumb = Math.Max(28, Height / Math.Max(2, Maximum / 3 + 2)); if (Maximum <= 0) return; using var brush = new SolidBrush(MainForm.Muted); e.Graphics.FillRectangle(brush, 4, (Height - thumb) * Math.Clamp(Value, 0, Maximum) / Maximum, Width - 8, thumb); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Capture = true; MoveThumb(e.Y); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) MoveThumb(e.Y); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    private void MoveThumb(int y) { if (Height > 0) Changed?.Invoke(Math.Clamp(y * Maximum / Height, 0, Maximum)); }
    protected override void OnKeyDown(KeyEventArgs e) { int? next = e.KeyCode switch { Keys.Up => Value - 1, Keys.Down => Value + 1, Keys.PageUp => Value - Math.Max(1, Maximum / 10), Keys.PageDown => Value + Math.Max(1, Maximum / 10), Keys.Home => 0, Keys.End => Maximum, _ => null }; if (next.HasValue) { Changed?.Invoke(Math.Clamp(next.Value, 0, Maximum)); e.Handled = true; e.SuppressKeyPress = true; } base.OnKeyDown(e); }
}
