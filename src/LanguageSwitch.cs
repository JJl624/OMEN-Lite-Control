using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OmenModeSwitcher
{
    sealed class LanguageSwitch : Control
    {
        bool english;
        public event EventHandler SelectionChanged;

        public LanguageSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.Selectable,
                     true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleName = "语言 / Language";
        }

        public bool English
        {
            get {
                return english;
            }
            set {
                english = value;
                Invalidate();
            }
        }

        void SelectLanguage(bool value)
        {
            if (value == english)
                return;
            English = value;
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            if (SelectionChanged != null)
                SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            Focus();
            SelectLanguage(e.X >= Width / 2);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Space ||
                e.KeyCode == Keys.Enter)
            {
                SelectLanguage(e.KeyCode == Keys.Left    ? false
                               : e.KeyCode == Keys.Right ? true
                                                         : !english);
                e.Handled = e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        static GraphicsPath Round(RectangleF rect, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 10 || Height < 10)
                return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var track = Round(
                       new RectangleF(1, 1, Width - 2, Height - 2),
                       (Height - 2) /
                           2f)) using (var brush =
                                           new SolidBrush(
                                               SystemColors
                                                   .ControlLight)) using (var border =
                                                                              new Pen(
                                                                                  SystemColors
                                                                                      .ControlDark))
            {
                e.Graphics.FillPath(brush, track);
                e.Graphics.DrawPath(border, track);
            }
            float half = (Width - 6) / 2f;
            using (var selected =
                       Round(new RectangleF(3 + (english ? half : 0), 3, half, Height - 6),
                             (Height - 6) / 2f)) using (var brush =
                                                            new SolidBrush(SystemColors.Highlight))
                e.Graphics.FillPath(brush, selected);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding;
            TextRenderer.DrawText(e.Graphics, "中文", Font,
                                  new Rectangle(3, 3, (int)half, Height - 6),
                                  english ? ForeColor : SystemColors.HighlightText, flags);
            TextRenderer.DrawText(e.Graphics, "EN", Font,
                                  new Rectangle(3 + (int)half, 3, (int)half, Height - 6),
                                  english ? SystemColors.HighlightText : ForeColor, flags);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics,
                                                new Rectangle(5, 5, Width - 10, Height - 10));
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new SwitchAccessibility(this);
        }

        sealed class SwitchAccessibility : ControlAccessibleObject
        {
            readonly LanguageSwitch owner;

            internal SwitchAccessibility(LanguageSwitch control) : base(control)
            {
                owner = control;
            }

            public override AccessibleRole Role
            {
                get {
                    return AccessibleRole.CheckButton;
                }
            }
            public override AccessibleStates State
            {
                get {
                    return base.State |
                           (owner.English ? AccessibleStates.Checked : AccessibleStates.None);
                }
            }
            public override string Value
            {
                get {
                    return owner.English ? "English" : "中文";
                }
            }
            public override string DefaultAction
            {
                get {
                    return "切换语言 / Switch language";
                }
            }

            public override void DoDefaultAction()
            {
                owner.SelectLanguage(!owner.English);
            }
        }
    }
}
