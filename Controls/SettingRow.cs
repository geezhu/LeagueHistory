namespace League.Controls;

/// <summary>
/// One setting per row: the name and an optional note on the left, its controls on the right,
/// separated from the next row by a thin divider.
/// </summary>
internal sealed class SettingRow : TableLayoutPanel
{
    private static readonly Color NameColor = Color.FromArgb(39, 62, 79);
    private static readonly Color NoteColor = Color.FromArgb(120, 136, 150);
    private static readonly Color DividerColor = Color.FromArgb(232, 238, 243);
    private readonly FlowLayoutPanel _text = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = Padding.Empty,
        Anchor = AnchorStyles.Left,
    };
    private readonly FlowLayoutPanel _controls = new()
    {
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(16, 0, 0, 0),
        Anchor = AnchorStyles.Right,
    };

    public SettingRow(string name, string? note, params Control[] controls)
    {
        ColumnCount = 2;
        RowCount = 1;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = new Padding(0, 9, 0, 9);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        NameLabel = new Label
        {
            Text = name,
            AutoSize = true,
            ForeColor = NameColor,
            Margin = Padding.Empty,
        };
        _text.Controls.Add(NameLabel);
        if (!string.IsNullOrEmpty(note))
        {
            AddNote(new Label { Text = note });
        }

        foreach (var control in controls)
        {
            Style(control);
            control.Margin = new Padding(8, 0, 0, 0);
            _controls.Controls.Add(control);
        }

        // A lone switch can also be toggled from its name, like a standard settings list.
        if (controls is [CheckBox toggle])
        {
            NameLabel.Cursor = Cursors.Hand;
            NameLabel.Click += (_, _) => toggle.Checked = !toggle.Checked;
        }

        Controls.Add(_text, 0, 0);
        Controls.Add(_controls, 1, 0);
    }

    public Label NameLabel { get; }

    /// <summary>Adds a note or inline control under the name, such as a status that changes over time.</summary>
    public void AddNote(Control note)
    {
        if (note is Label label)
        {
            label.AutoSize = true;
            label.ForeColor = NoteColor;
        }

        note.Margin = new Padding(0, 4, 0, 0);
        _text.Controls.Add(note);
    }

    /// <summary>Fits the row to the card width and wraps notes beside the right-hand controls.</summary>
    public void FitWidth(int width)
    {
        MinimumSize = MaximumSize = new Size(width, 0);
        int text = Math.Max(160, width - _controls.PreferredSize.Width - _controls.Margin.Horizontal - 8);
        foreach (var label in _text.Controls.OfType<Label>())
        {
            label.MaximumSize = new Size(text, 0);
        }
    }

    /// <summary>Settings-page look for buttons, links and switches, shared with stacked card content.</summary>
    public static void Style(Control control)
    {
        control.Dock = DockStyle.None;
        control.ForeColor = Color.FromArgb(59, 81, 99);
        switch (control)
        {
            case CheckBox check:
                check.AutoSize = true;
                break;
            case RadioButton radio:
                radio.AutoSize = true;
                break;
            case LinkLabel link:
                link.AutoSize = true;
                link.LinkColor = Theme.Accent;
                link.LinkBehavior = LinkBehavior.HoverUnderline;
                break;
            case Label label:
                label.AutoSize = true;
                label.ForeColor = Color.FromArgb(105, 122, 137);
                break;
            case Button button:
                button.AutoSize = false;
                button.Size = new Size(
                    Math.Max(120, TextRenderer.MeasureText(button.Text, button.Font).Width + 28),
                    32
                );
                ButtonStyles.Secondary(button);
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Parent is not { } card)
        {
            return;
        }

        int index = card.Controls.GetChildIndex(this);
        if (index + 1 < card.Controls.Count && card.Controls[index + 1] is SettingRow { Visible: true })
        {
            using var divider = new Pen(DividerColor);
            e.Graphics.DrawLine(divider, 0, Height - 1, Width, Height - 1);
        }
    }
}
