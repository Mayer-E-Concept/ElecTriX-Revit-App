// NumberRoomsDialog.cs -- ME-Tools | ElecTriX
// Writes room numbers in format HFS (Haus-Floor-Suite) to Revit ROOM_NUMBER param.
// Format example: 101 = Haus 1, Floor 0, Suite/Apartment 1
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace METools.FamilyPlacer
{
    public class NumberRoomsDialog : Window
    {
        private readonly Document         _doc;
        private readonly List<RaumZeile>  _rows;

        // Inputs
        private TextBox  _tbHaus;
        private ComboBox _cbFloorMode;
        private TextBox  _tbFloorOffset;
        private TextBox  _tbSuiteStart;
        private TextBlock _lblPreview;

        public NumberRoomsDialog(Document doc, List<RaumZeile> rows)
        {
            _doc  = doc;
            _rows = rows;

            Title  = "Number Rooms";
            Width  = 480;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = MeToolsTheme.BrBg;
            Foreground = MeToolsTheme.BrText;

            Build();
        }

        void Build()
        {
            var root = new DockPanel { Margin = new Thickness(16) };

            // ── Explanation ──────────────────────────────────────────────
            var desc = new TextBlock
            {
                Text = "Assigns room numbers in format HFS:\n" +
                       "  H = Haus number (1-digit)\n" +
                       "  F = Floor index (1-digit, from level name suffix)\n" +
                       "  S = Suite/apartment number within the floor (1-digit)\n\n" +
                       "Example: 101 = Haus 1, Ground floor (0), Apartment 1",
                FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            };
            DockPanel.SetDock(desc, Dock.Top);
            root.Children.Add(desc);

            // ── Buttons ──────────────────────────────────────────────────
            var btnSp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var btnCancel = Btn("Cancel", false); btnCancel.Margin = new Thickness(0, 0, 8, 0);
            var btnApply  = Btn("Apply Numbering", true);
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnApply.Click  += (s, e) => Apply();
            btnSp.Children.Add(btnCancel);
            btnSp.Children.Add(btnApply);
            DockPanel.SetDock(btnSp, Dock.Bottom);
            root.Children.Add(btnSp);

            // ── Preview ──────────────────────────────────────────────────
            _lblPreview = new TextBlock
            {
                FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Text = "",
            };
            DockPanel.SetDock(_lblPreview, Dock.Bottom);
            root.Children.Add(_lblPreview);

            // ── Form ─────────────────────────────────────────────────────
            var form = new StackPanel();

            form.Children.Add(Label("Haus number (H):"));
            _tbHaus = Input("1"); _tbHaus.Width = 60;
            _tbHaus.TextChanged += (s, e) => UpdatePreview();
            form.Children.Add(_tbHaus);

            form.Children.Add(Label("Floor index source:"));
            _cbFloorMode = new ComboBox
            {
                Width = 260, Height = 26, Margin = new Thickness(0, 2, 0, 10),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, FontSize = 11,
            };
            _cbFloorMode.Items.Add("From room level name (e.g. 'FFB 1.OG' -> floor 1)");
            _cbFloorMode.Items.Add("Fixed value (type below)");
            _cbFloorMode.SelectedIndex = 0;
            _cbFloorMode.SelectionChanged += (s, e) => UpdatePreview();
            form.Children.Add(_cbFloorMode);

            form.Children.Add(Label("Floor offset (added to detected floor index):"));
            _tbFloorOffset = Input("0"); _tbFloorOffset.Width = 60;
            _tbFloorOffset.TextChanged += (s, e) => UpdatePreview();
            form.Children.Add(_tbFloorOffset);

            form.Children.Add(Label("Suite start number (S) per apartment:"));
            _tbSuiteStart = Input("1"); _tbSuiteStart.Width = 60;
            _tbSuiteStart.TextChanged += (s, e) => UpdatePreview();
            form.Children.Add(_tbSuiteStart);

            root.Children.Add(form);
            Content = root;

            UpdatePreview();
        }

        void UpdatePreview()
        {
            try
            {
                var numbering = BuildNumbering();
                if (numbering.Count == 0) { _lblPreview.Text = "No rooms to number."; return; }
                var preview = numbering.Take(5)
                    .Select(kv => $"  Room '{kv.Key}' -> {kv.Value}");
                _lblPreview.Text = "Preview (first 5):\n" + string.Join("\n", preview) +
                    (numbering.Count > 5 ? $"\n  ... and {numbering.Count - 5} more" : "");
            }
            catch (Exception ex)
            {
                _lblPreview.Text = "Error: " + ex.Message;
            }
        }

        // Returns elementId -> new number string
        Dictionary<ElementId, string> BuildNumbering()
        {
            var result = new Dictionary<ElementId, string>();
            if (!int.TryParse(_tbHaus.Text?.Trim(), out int haus)) haus = 1;
            if (!int.TryParse(_tbFloorOffset.Text?.Trim(), out int floorOffset)) floorOffset = 0;
            if (!int.TryParse(_tbSuiteStart.Text?.Trim(), out int suiteStart)) suiteStart = 1;
            bool autoFloor = _cbFloorMode.SelectedIndex == 0;

            // Collect all Revit rooms
            var rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            // Group by Verteiler (panel = apartment identifier on RaumZeile).
            // Each apartment gets a sequential suite number.
            // Within each apartment, group by floor.
            // We match Revit rooms by their current room number to find the panel assignment.
            var nummerToWohnungsId = _rows
                .Where(z => !string.IsNullOrEmpty(z.RaumNummer))
                .ToDictionary(z => z.RaumNummer.Trim(), z => z.Verteiler ?? "");

            // Group rooms by apartment
            var byApt = rooms
                .GroupBy(r =>
                {
                    var nr = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString()?.Trim() ?? "";
                    return nummerToWohnungsId.TryGetValue(nr, out var wid) ? wid : "";
                })
                .OrderBy(g => g.Key)
                .ToList();

            int suiteCounter = suiteStart;
            foreach (var aptGroup in byApt)
            {
                // Within each apartment group, sort by floor then by room number
                var sorted = aptGroup.OrderBy(r =>
                {
                    int f = autoFloor ? FloorFromLevel(r, floorOffset)
                                      : floorOffset;
                    return f * 1000 + (int.TryParse(r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString(), out int rn) ? rn : 0);
                }).ToList();

                // Assign sequential suite numbers per apartment (not per room)
                // Actually: one suite number per apartment, rooms within an apartment share it
                int apt = suiteCounter;

                foreach (var room in sorted)
                {
                    int floor = autoFloor ? FloorFromLevel(room, floorOffset) : floorOffset;
                    // Format: HFS single digits clamped to 0-9
                    int h = Math.Max(0, Math.Min(9, haus));
                    int f = Math.Max(0, Math.Min(9, floor));
                    int s = Math.Max(0, Math.Min(9, apt));
                    result[room.Id] = $"{h}{f}{s}";
                }

                suiteCounter++;
            }

            return result;
        }

        int FloorFromLevel(Room room, int offset)
        {
            // Extract floor index from level name:
            // "EG" / "KG" -> 0, "1.OG" -> 1, "2.OG" -> 2, "UG" -> -1 (clamped to 0), etc.
            try
            {
                var lvlName = (room.Level?.Name ?? "").ToUpperInvariant();
                if (lvlName.Contains("EG"))  return 0 + offset;
                if (lvlName.Contains("KG"))  return 0 + offset;
                if (lvlName.Contains("UG"))  return 0 + offset;
                if (lvlName.Contains("DG"))  return 5 + offset; // Dachgeschoss
                // Match "1.OG", "2.OG", etc.
                var m = System.Text.RegularExpressions.Regex.Match(lvlName, @"(\d+)\.OG");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int og))
                    return og + offset;
                // Match plain "FFB 1.OG"
                m = System.Text.RegularExpressions.Regex.Match(lvlName, @"(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                    return n + offset;
            }
            catch { }
            return offset;
        }

        void Apply()
        {
            try
            {
                var numbering = BuildNumbering();
                if (numbering.Count == 0)
                { MessageBox.Show("No rooms to number.", "Number Rooms"); return; }

                using (var tx = new Transaction(_doc, "ME-Tools: Number Rooms"))
                {
                    tx.Start();
                    int written = 0;
                    foreach (var kv in numbering)
                    {
                        try
                        {
                            var room = _doc.GetElement(kv.Key) as Room;
                            if (room == null) continue;
                            var p = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                            if (p != null && !p.IsReadOnly) { p.Set(kv.Value); written++; }
                        }
                        catch { }
                    }
                    tx.Commit();
                    MessageBox.Show($"Numbered {written} room(s).\n\nFormat: HFS (Haus-Floor-Suite)",
                        "Number Rooms", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Number Rooms",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        TextBlock Label(string t) => new TextBlock
        {
            Text = t, FontSize = 11, Foreground = MeToolsTheme.BrText,
            Margin = new Thickness(0, 6, 0, 2),
        };

        TextBox Input(string val) => new TextBox
        {
            Text = val, Height = 26, Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(6, 2, 6, 2), FontSize = 11,
            Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
            BorderBrush = MeToolsTheme.BrBorder,
        };

        System.Windows.Controls.Button Btn(string label, bool primary) =>
            new System.Windows.Controls.Button
            {
                Content = label, Height = 30, Padding = new Thickness(14, 0, 14, 0),
                FontSize = 11,
                Background = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrSurface,
                Foreground = primary ? System.Windows.Media.Brushes.White : MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
    }
}
