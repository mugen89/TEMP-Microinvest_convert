using System.ComponentModel;

namespace MicroinvestConvert.App.Ui;

/// <summary>
/// The table used everywhere: reads its cells through delegates (virtual mode), sorts by a
/// click on a header, and takes pasted text like Excel.
/// </summary>
internal sealed class Grid<T> : DataGridView where T : class
{
    internal sealed class Column
    {
        public required string Header { get; init; }
        public required Func<T, object?> Get { get; init; }
        public int Width { get; init; } = 90;
        public bool Fill { get; init; }
        public bool Right { get; init; }
        public bool Center { get; init; }
        public string Tip { get; init; } = "";

        /// <summary>Sets the value from text; returns an error or null. Null delegate = read-only.</summary>
        public Func<T, string, string?>? Set { get; init; }

        /// <summary>The values of a drop-down; <see cref="ItemLabel"/> is what its list shows.</summary>
        public IReadOnlyList<string>? Items { get; init; }
        public Func<string, string>? ItemLabel { get; init; }
        public bool IsCheck { get; init; }

        /// <summary>What to sort by, when it is not the shown value.</summary>
        public Func<T, IComparable?>? SortKey { get; init; }
    }

    private readonly List<Column> _columns = [];
    private List<T> _rows = [];
    private int _sortColumn = -1;
    private bool _sortDescending;

    /// <summary>After one or more cells of these rows were changed by the user.</summary>
    public event Action<IReadOnlyList<T>>? Edited;

    /// <summary>The colours of a row or cell; called for every painted cell.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<T, int, DataGridViewCellStyle>? StyleCell { get; set; }

    public Grid()
    {
        DoubleBuffered = true;
        VirtualMode = true;
        AutoGenerateColumns = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        RowHeadersVisible = false;
        Dock = DockStyle.Fill;
        BorderStyle = BorderStyle.None;
        BackgroundColor = Theme.Surface;
        GridColor = Color.FromArgb(230, 235, 240);
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        EnableHeadersVisualStyles = false;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        SelectionMode = DataGridViewSelectionMode.CellSelect;
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        ShowCellToolTips = true;

        DefaultCellStyle.Font = Theme.Base;
        DefaultCellStyle.ForeColor = Theme.Ink;
        DefaultCellStyle.SelectionForeColor = Theme.Ink;
        DefaultCellStyle.Padding = new Padding(3, 0, 3, 0);
        AlternatingRowsDefaultCellStyle.BackColor = Theme.AltRow;
        ColumnHeadersDefaultCellStyle.Font = Theme.Bold;
        ColumnHeadersDefaultCellStyle.ForeColor = Theme.Ink;
        ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        ColumnHeadersDefaultCellStyle.Padding = new Padding(3, 0, 3, 0);
        Use(Palette.Neutral);
    }

    public void Use(Palette palette)
    {
        ColumnHeadersDefaultCellStyle.BackColor = palette.Header;
        ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Header;
        ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Ink;
        DefaultCellStyle.SelectionBackColor = palette.SoftBorder;
    }

    /// <summary>Heights in pixels at 96 DPI.</summary>
    public void SetHeights(int row, int header)
    {
        RowTemplate.Height = row;
        ColumnHeadersHeight = header;
    }

    // DataGridView leaves its columns and rows out of the scaling of the window.
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        if (factor.Width == 1f && factor.Height == 1f) return;

        foreach (DataGridViewColumn column in Columns)
        {
            column.MinimumWidth = Math.Max(2, (int)Math.Round(column.MinimumWidth * factor.Width));
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill)
                column.Width = (int)Math.Round(column.Width * factor.Width);
        }

        SetHeights((int)Math.Round(RowTemplate.Height * factor.Height), (int)Math.Round(ColumnHeadersHeight * factor.Height));
        if (RowCount > 0) SetRows(_rows);   // rows take their height when they are added
    }

    // ----- columns and rows -----

    public int AddColumn(Column column)
    {
        DataGridViewColumn view;
        if (column.IsCheck)
        {
            view = new DataGridViewCheckBoxColumn();
        }
        else if (column.Items != null)
        {
            var combo = new DataGridViewComboBoxColumn
            {
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DisplayStyleForCurrentCellOnly = true,
                DropDownWidth = 360,
            };
            combo.Items.AddRange([.. column.Items]);
            view = combo;
        }
        else
        {
            view = new DataGridViewTextBoxColumn();
        }

        view.HeaderText = column.Header;
        view.Width = column.Width;
        view.MinimumWidth = 24;
        view.ReadOnly = column.Set == null;
        view.SortMode = DataGridViewColumnSortMode.Programmatic;
        view.ToolTipText = column.Tip.Length > 0 ? column.Tip : column.Header;
        if (column.Fill)
        {
            view.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            view.MinimumWidth = column.Width;
        }

        if (column.Right) view.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        if (column.Center) view.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        if (column.Right) view.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;

        _columns.Add(column);
        Columns.Add(view);
        return _columns.Count - 1;
    }

    public IReadOnlyList<T> Items => _rows;

    public void SetRows(IEnumerable<T> rows)
    {
        var current = Current;
        _rows = rows.ToList();
        if (_sortColumn >= 0) SortRows();

        RowCount = 0;            // drops stale cell state
        RowCount = _rows.Count;
        int at = current != null ? _rows.IndexOf(current) : -1;
        if (at < 0 && _rows.Count > 0) at = 0;
        if (at >= 0) CurrentCell = this[FirstVisibleColumn(), at];
        Invalidate();
    }

    private int FirstVisibleColumn() => Columns.GetFirstColumn(DataGridViewElementStates.Visible)?.Index ?? 0;

    public T? Current => CurrentCell is { } cell && cell.RowIndex < _rows.Count ? _rows[cell.RowIndex] : null;

    /// <summary>The rows with at least one selected cell, in their order.</summary>
    public List<T> SelectedItems() => SelectedCells.Cast<DataGridViewCell>()
        .Select(c => c.RowIndex).Where(i => i >= 0 && i < _rows.Count).Distinct().OrderBy(i => i)
        .Select(i => _rows[i]).ToList();

    // ----- values -----

    protected override void OnCellValueNeeded(DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex >= 0 && e.RowIndex < _rows.Count && e.ColumnIndex < _columns.Count)
            e.Value = _columns[e.ColumnIndex].Get(_rows[e.RowIndex]);
        base.OnCellValueNeeded(e);
    }

    protected override void OnCellValuePushed(DataGridViewCellValueEventArgs e)
    {
        base.OnCellValuePushed(e);
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;

        var row = _rows[e.RowIndex];
        string text = e.Value switch { bool b => b ? "1" : "", _ => e.Value?.ToString() ?? "" };
        string? error = _columns[e.ColumnIndex].Set?.Invoke(row, text);

        // The edit is still being committed; what follows may rebuild rows.
        BeginInvoke(() =>
        {
            if (error != null) Dialogs.Warning(FindForm(), "Стойността не е приета", error);
            else Edited?.Invoke([row]);
            Invalidate();
        });
    }

    protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
    {
        base.OnCellFormatting(e);
        if (e.RowIndex >= 0 && e.RowIndex < _rows.Count && e.CellStyle != null)
            StyleCell?.Invoke(_rows[e.RowIndex], e.ColumnIndex, e.CellStyle);
    }

    // A value outside the list of a drop-down is shown as it is, without complaint.
    protected override void OnDataError(bool displayErrorDialogIfNoHandler, DataGridViewDataErrorEventArgs e) => e.ThrowException = false;

    // ----- editing -----

    protected override void OnCurrentCellDirtyStateChanged(EventArgs e)
    {
        base.OnCurrentCellDirtyStateChanged(e);
        // A tick and a choice from a list take effect at once.
        if (IsCurrentCellDirty && CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
            CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    protected override void OnEditingControlShowing(DataGridViewEditingControlShowingEventArgs e)
    {
        base.OnEditingControlShowing(e);
        if (e.Control is not ComboBox combo || CurrentCell == null) return;

        var label = _columns[CurrentCell.ColumnIndex].ItemLabel;
        combo.DrawItem -= DrawComboItem;
        combo.DrawMode = label != null ? DrawMode.OwnerDrawFixed : DrawMode.Normal;
        if (label != null) combo.DrawItem += DrawComboItem;
    }

    private void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo || e.Index < 0 || CurrentCell == null) return;
        string value = combo.Items[e.Index]?.ToString() ?? "";

        // The closed box shows the short value, the list shows the whole label.
        bool closed = (e.State & DrawItemState.ComboBoxEdit) != 0;
        string text = closed ? value : _columns[CurrentCell.ColumnIndex].ItemLabel?.Invoke(value) ?? value;

        e.DrawBackground();
        TextRenderer.DrawText(e.Graphics, text, e.Font, e.Bounds, e.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!IsCurrentCellInEditMode)
        {
            if (keyData == (Keys.Control | Keys.V))
            {
                Paste();
                return true;
            }

            if (keyData == Keys.Delete)
            {
                Apply(EditableSelection().Select(c => (c.Row, c.Column, "")));
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private IEnumerable<(int Row, int Column)> EditableSelection() => SelectedCells.Cast<DataGridViewCell>()
        .Where(c => c.Visible && !c.ReadOnly && _columns[c.ColumnIndex].Set != null && !_columns[c.ColumnIndex].IsCheck)
        .OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex)
        .Select(c => (c.RowIndex, c.ColumnIndex));

    private void Paste()
    {
        try { PasteText(Clipboard.GetText()); }
        catch (System.Runtime.InteropServices.ExternalException) { }   // the clipboard is busy
    }

    /// <summary>One value fills every selected cell; a block goes down and right from the first one.</summary>
    internal void PasteText(string text)
    {
        if (text.Length == 0 || SelectedCells.Count == 0) return;

        var block = text.TrimEnd('\r', '\n').Split('\n').Select(l => l.TrimEnd('\r').Split('\t')).ToList();
        if (block.Count == 1 && block[0].Length == 1)
        {
            Apply(EditableSelection().Select(c => (c.Row, c.Column, block[0][0])));
            return;
        }

        int top = SelectedCells.Cast<DataGridViewCell>().Min(c => c.RowIndex);
        int left = SelectedCells.Cast<DataGridViewCell>().Min(c => Columns[c.ColumnIndex].DisplayIndex);
        var visible = Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c.DisplayIndex >= left)
            .OrderBy(c => c.DisplayIndex).Select(c => c.Index).ToList();

        var cells = new List<(int, int, string)>();
        for (int r = 0; r < block.Count && top + r < _rows.Count; r++)
            for (int c = 0; c < block[r].Length && c < visible.Count; c++)
                if (_columns[visible[c]] is { Set: not null, IsCheck: false })
                    cells.Add((top + r, visible[c], block[r][c]));
        Apply(cells);
    }

    private void Apply(IEnumerable<(int Row, int Column, string Text)> cells)
    {
        var changed = new List<T>();
        var errors = new List<string>();
        foreach (var (r, c, text) in cells.ToList())
        {
            string? error = _columns[c].Set!(_rows[r], text);
            if (error == null) changed.Add(_rows[r]);
            else if (!errors.Contains(error)) errors.Add(error);
        }

        if (changed.Count > 0) Edited?.Invoke(changed.Distinct().ToList());
        Invalidate();
        if (errors.Count > 0) Dialogs.Warning(FindForm(), "Част от стойностите не са приети", string.Join("\n", errors.Take(8)));
    }

    // ----- sorting -----

    protected override void OnColumnHeaderMouseClick(DataGridViewCellMouseEventArgs e)
    {
        base.OnColumnHeaderMouseClick(e);
        if (e.Button != MouseButtons.Left || e.ColumnIndex < 0) return;

        if (_sortColumn == e.ColumnIndex && _sortDescending) _sortColumn = -1;   // third click: the order of the file
        else
        {
            _sortDescending = _sortColumn == e.ColumnIndex;
            _sortColumn = e.ColumnIndex;
        }

        foreach (DataGridViewColumn column in Columns) column.HeaderCell.SortGlyphDirection = SortOrder.None;
        if (_sortColumn >= 0)
            Columns[_sortColumn].HeaderCell.SortGlyphDirection = _sortDescending ? SortOrder.Descending : SortOrder.Ascending;
        SortRequested?.Invoke();
    }

    /// <summary>The owner gives the rows again (in the order of the file); they are then sorted.</summary>
    public event Action? SortRequested;

    private void SortRows()
    {
        var column = _columns[_sortColumn];
        object? Key(T row) => column.SortKey != null ? column.SortKey(row) : column.Get(row)?.ToString();
        var sorted = _sortDescending
            ? _rows.OrderByDescending(Key, KeyComparer.Instance)
            : _rows.OrderBy(Key, KeyComparer.Instance);
        _rows = sorted.ToList();
    }

    private sealed class KeyComparer : IComparer<object?>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(object? x, object? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            (string a, string b) => string.Compare(a, b, Fmt.Bulgarian, System.Globalization.CompareOptions.IgnoreCase),
            (IComparable a, _) => a.CompareTo(y),
            _ => 0,
        };
    }
}
