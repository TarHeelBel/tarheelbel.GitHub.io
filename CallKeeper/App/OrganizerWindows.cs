using System.Text.Json;
using MeetingDirectory;

namespace MeetingDirectoryApp;

sealed record StartupPreferences(int View = 0, bool RememberAccount = false, string? Account = null) {
    public static StartupPreferences Load(string path) {
        try {
            if (File.Exists(path) && new FileInfo(path).Length < 64 * 1024) {
                var saved = JsonSerializer.Deserialize<StartupPreferences>(File.ReadAllText(path));
                if (saved is { View: >= 0 and <= 3 }) return saved;
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) {
            // Preferences are optional. The directory must still open.
        }
        return new();
    }

    public void Save(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}

sealed class StartupWindow : Form {
    public StartupPreferences Result { get; private set; }

    public StartupWindow(StartupPreferences current) {
        Result = current;
        Text = "Startup settings";
        Font = new Font("Segoe UI", 12);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(570, 245);
        var view = Ui.Named(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill }, "Startup view");
        view.Items.AddRange(["Today's meetings", "Upcoming meetings", "Favorites", "All meetings"]);
        view.SelectedIndex = current.View;
        var remember = new CheckBox { Text = "&Remember the last Zoom account filter", AutoSize = true, Checked = current.RememberAccount };
        var save = Ui.Button("&Save settings", () => {
            Result = new StartupPreferences(view.SelectedIndex, remember.Checked);
            DialogResult = DialogResult.OK;
        });
        var cancel = Ui.Button("Cancel", () => DialogResult = DialogResult.Cancel);
        AcceptButton = save;
        CancelButton = cancel;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, AutoScroll = true };
        layout.Controls.Add(new Label { Text = "Startup &view", AutoSize = true });
        layout.Controls.Add(view);
        layout.Controls.Add(remember);
        layout.Controls.Add(Ui.Buttons(save, cancel));
        Controls.Add(layout);
        Shown += (_, _) => view.Focus();
    }
}

sealed class BulkLabelsWindow : Form {
    public string? Account { get; private set; }
    public string? Category { get; private set; }

    public BulkLabelsWindow(int count, int zoomCount, IEnumerable<string> accounts, IEnumerable<string> categories) {
        Text = "Change labels for checked meetings";
        Font = new Font("Segoe UI", 12);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 345);
        var changeAccount = new CheckBox { Text = "Change Zoom &account label", AutoSize = true, Enabled = zoomCount > 0 };
        var changeCategory = new CheckBox { Text = "Change &category", AutoSize = true };
        var account = LabelInput("New Zoom account label", accounts);
        var category = LabelInput("New category", categories);
        changeAccount.CheckedChanged += (_, _) => account.Enabled = changeAccount.Checked;
        changeCategory.CheckedChanged += (_, _) => category.Enabled = changeCategory.Checked;
        var apply = Ui.Button("&Apply to checked meetings", () => {
            if (!changeAccount.Checked && !changeCategory.Checked) {
                MessageBox.Show(this, "Check at least one field to change.", Text);
                return;
            }
            Account = changeAccount.Checked ? account.Text.Trim() : null;
            Category = changeCategory.Checked ? category.Text.Trim() : null;
            DialogResult = DialogResult.OK;
        });
        var cancel = Ui.Button("Cancel", () => DialogResult = DialogResult.Cancel);
        CancelButton = cancel;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, AutoScroll = true };
        layout.Controls.Add(new Label {
            Text = $"{count} meetings checked. Account changes apply to {zoomCount} Zoom meetings. A checked field left blank clears that label.",
            AutoSize = true, MaximumSize = new Size(620, 0)
        });
        layout.Controls.Add(changeAccount);
        layout.Controls.Add(account);
        layout.Controls.Add(changeCategory);
        layout.Controls.Add(category);
        layout.Controls.Add(Ui.Buttons(apply, cancel));
        Controls.Add(layout);
        Shown += (_, _) => (zoomCount > 0 ? changeAccount : changeCategory).Focus();
    }

    static ComboBox LabelInput(string name, IEnumerable<string> choices) {
        var box = Ui.Named(new ComboBox { Dock = DockStyle.Fill, MaxLength = 100, Enabled = false }, name);
        box.Items.AddRange(choices.Order(StringComparer.CurrentCultureIgnoreCase).ToArray());
        return box;
    }
}

sealed class CsvLinkWindow : Form {
    public CsvLinkWindow(CsvLinkChange change) {
        Text = "Review changed meeting link";
        Font = new Font("Segoe UI", 12);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(850, 520);
        MinimumSize = new Size(720, 400);
        var account = change.Existing.ZoomAccount.Length > 0 ? change.Existing.ZoomAccount : "No account label";
        var text = $"{change.Existing.Name}\r\n{change.Existing.Platform}, {account}\r\n\r\n" +
            $"Current link:\r\n{change.Existing.Link}\r\n\r\nCSV link:\r\n{change.Incoming.Link}\r\n\r\n" +
            "Update link uses the CSV meeting ID and passcode, including blank fields.";
        var review = Ui.Named(new TextBox {
            Multiline = true, ReadOnly = true, Text = text, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical
        }, "Current and CSV meeting links");
        var update = Ui.Button("&Update link", () => DialogResult = DialogResult.Yes);
        var keep = Ui.Button("&Keep current link", () => DialogResult = DialogResult.No);
        var add = Ui.Button("&Add as separate meeting", () => DialogResult = DialogResult.Retry);
        var cancel = Ui.Button("Cancel &import", () => DialogResult = DialogResult.Cancel);
        CancelButton = cancel;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(review, 0, 0);
        layout.Controls.Add(Ui.Buttons(update, keep, add, cancel), 0, 1);
        Controls.Add(layout);
        Shown += (_, _) => { review.Focus(); review.Select(0, 0); };
    }
}
