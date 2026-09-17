using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OmenModeSwitcher
{
    // A four-zone schematic, not a per-key lighting controller.
    sealed class KeyboardLightingControl : Control
    {
        sealed class Key
        {
            internal string Text;
            internal RectangleF Bounds;
            internal int Zone;
            internal int Join;
        }

        readonly List<Key> keys = new List<Key>();
        readonly Color[] colors = { Color.White, Color.White, Color.White, Color.White };
        readonly int[] brightness = { 100, 100, 100, 100 };
        readonly int[] order = { 2, 3, 1, 0 };
        string[] names = { "右区", "中区", "左区", "WASD" };
        int selected = 2;
        public event EventHandler ZoneSelected;
        public event EventHandler ZoneActivated;

        public KeyboardLightingControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                         ControlStyles.Selectable,
                     true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Graphic;
            // ANSI 15-dc layout: main block 15 units, a separate four-column numpad.
            // Reference: HP 15-dc keyboard assembly L32770-001 / L32775-001.
            AddRow(0, "Esc F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 Ins Del");
            AddRow(1, "` 1 2 3 4 5 6 7 8 9 0 - = Back:2");
            AddRow(2, "Tab:1.5 Q W E R T Y U I O P [ ] \\:1.5");
            AddRow(3, "Caps:1.75 A S D F G H J K L ; ' Enter:2.25");
            AddRow(4, "Shift:2.25 Z X C V B N M , . / RShift:2.75");
            AddRow(5, "Ctrl:1.25 Fn Win Alt:1.25 Space:5.5 RAlt:1 RCtrl:1");
            AddKey("←", 12, 5.5f, 1, .5f, 0);
            AddKey("↑", 13, 5, 1, .5f, 0);
            AddKey("↓", 13, 5.5f, 1, .5f, 0);
            AddKey("→", 14, 5.5f, 1, .5f, 0);
            string[][] pad = { new[] { "OMEN", "End", "PgUp", "PgDn" },
                               new[] { "Num", "/", "*", "-" }, new[] { "7", "8", "9" },
                               new[] { "4", "5", "6" }, new[] { "1", "2", "3" } };
            for (int row = 0; row < pad.Length; row++)
                for (int col = 0; col < pad[row].Length; col++)
                    AddKey(pad[row][col], 15.25f + col, row, 1, 1, 0);
            AddKey("+", 18.25f, 2, 1, 2, 0);
            AddKey("Enter", 18.25f, 4, 1, 2, 0);
            AddKey("0", 15.25f, 5, 2, 1, 0);
            AddKey(".", 17.25f, 5, 1, 1, 0);
        }

        void AddKey(string text, float x, float y, float width, float height, int zone,
                    int join = 0)
        {
            keys.Add(new Key { Text = text, Zone = zone, Join = join,
                               Bounds = new RectangleF(x, y, width, height) });
        }

        void AddRow(int row, string layout)
        {
            float x = 0;
            string[] leftEdges = { "F5", "5", "R", "F", "V", "Alt" };
            bool left = true;
            foreach (string item in layout.Split(' '))
            {
                string[] parts = item.Split(':');
                float width =
                    parts.Length == 1
                        ? 1
                        : float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                string label = parts[0];
                if (label == "Space")
                {
                    // One physical spacebar, with adjacent left/middle lighting hit areas.
                    AddKey("", x, row, width / 2, 1, 2, 1);
                    AddKey("", x + width / 2, row, width / 2, 1, 1, 2);
                    x += width;
                    continue;
                }
                bool right = label == "Ins" || label == "Del" || label == "Back" || label == "\\" ||
                             label == "Enter" || label == "RShift";
                int zone = label == "W" || label == "A" || label == "S" || label == "D" ? 3
                           : right                                                      ? 0
                           : left                                                       ? 2
                                                                                        : 1;
                AddKey(label == "RShift"  ? "Shift"
                       : label == "RAlt"  ? "Alt"
                       : label == "RCtrl" ? "Ctrl"
                                          : label,
                       x, row, width, 1, zone);
                if (label == leftEdges[row])
                    left = false;
                x += width;
            }
        }

        public int SelectedZone
        {
            get {
                return selected;
            }
            set {
                if (value < 0 || value > 3)
                    throw new ArgumentOutOfRangeException("value");
                selected = value;
                AccessibleName = names[selected];
                Invalidate();
                if (ZoneSelected != null)
                    ZoneSelected(this, EventArgs.Empty);
            }
        }

        public void SetZone(int zone, Color color, int level)
        {
            colors[zone] = color;
            brightness[zone] = level;
            Invalidate();
        }

        public void SetNames(string[] value)
        {
            names = (string[])value.Clone();
            AccessibleName = names[selected];
            Invalidate();
        }

        Rectangle KeyBounds(Key key)
        {
            float unit = (ClientSize.Width - 12) / 19.25f;
            float rowHeight = (ClientSize.Height - 35) / 6f;
            return new Rectangle(
                6 + (int)(key.Bounds.X * unit), 4 + (int)(key.Bounds.Y * rowHeight),
                Math.Max(1, (int)((key.Bounds.X + key.Bounds.Width) * unit) -
                                (int)(key.Bounds.X * unit) - (key.Join == 1 ? 0 : 3)),
                Math.Max(1,
                         (int)(key.Bounds.Height * rowHeight) - (key.Bounds.Height < 1 ? 1 : 3)));
        }

        Rectangle ZoneBounds(int index)
        {
            int width = (ClientSize.Width - 12) / 4;
            return new Rectangle(6 + index * width, ClientSize.Height - 26, width - 5, 23);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.FromArgb(28, 31, 36));
            using (var arrowFont = new Font("Segoe UI", 5.5f)) using (
                var keyFont = new Font(
                    "Segoe UI",
                    6.8f)) using (var border =
                                      new Pen(Color.FromArgb(
                                          65, 70,
                                          78))) using (var selectedBorder =
                                                           new Pen(Color.FromArgb(95, 190, 255), 2))
            {
                foreach (Key key in keys)
                {
                    Rectangle rect = KeyBounds(key);
                    Color c = colors[key.Zone];
                    double level = brightness[key.Zone] / 100.0;
                    Color fill =
                        Color.FromArgb((int)(c.R * level), (int)(c.G * level), (int)(c.B * level));
                    using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, rect);
                    e.Graphics.DrawRectangle(key.Zone == selected ? selectedBorder : border, rect);
                    TextRenderer.DrawText(
                        e.Graphics, key.Text,
                        key.Bounds.Height < 1 || key.Text == "OMEN" ? arrowFont : keyFont, rect,
                        fill.GetBrightness() < 0.5f ? Color.White : Color.Black,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoPadding);
                }
                for (int i = 0; i < order.Length; i++)
                {
                    Rectangle rect = ZoneBounds(i);
                    bool active = order[i] == selected;
                    using (var brush = new SolidBrush(active ? Color.FromArgb(35, 90, 130)
                                                             : Color.FromArgb(45, 49, 56)))
                        e.Graphics.FillRectangle(brush, rect);
                    TextRenderer.DrawText(e.Graphics, names[order[i]], Font, rect, Color.White,
                                          TextFormatFlags.HorizontalCenter |
                                              TextFormatFlags.VerticalCenter);
                    if (active)
                        e.Graphics.DrawRectangle(selectedBorder, rect);
                }
            }
            if (Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            Focus();
            if (e.Y >= ClientSize.Height - 30)
            {
                int index =
                    Math.Max(0, Math.Min(3, (e.X - 6) * 4 / Math.Max(1, ClientSize.Width - 12)));
                SelectedZone = order[index];
                return;
            }

            // Assign the entire keyboard surface, including gaps, to the closest key's zone.
            // This preserves the irregular WASD region without requiring a keycap hit.
            int closestZone = selected;
            long closestDistance = long.MaxValue;
            foreach (Key key in keys)
            {
                Rectangle bounds = KeyBounds(key);
                long dx = Math.Max(bounds.Left - e.X, Math.Max(0, e.X - bounds.Right));
                long dy = Math.Max(bounds.Top - e.Y, Math.Max(0, e.Y - bounds.Bottom));
                long distance = dx * dx + dy * dy;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestZone = key.Zone;
                }
            }
            SelectedZone = closestZone;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && ZoneActivated != null)
                ZoneActivated(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int index = Array.IndexOf(order, selected);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                SelectedZone = order[(index + (e.KeyCode == Keys.Right ? 1 : 3)) % 4];
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                if (ZoneActivated != null)
                    ZoneActivated(this, EventArgs.Empty);
                e.Handled = e.SuppressKeyPress = true;
            }
        }
    }
}
