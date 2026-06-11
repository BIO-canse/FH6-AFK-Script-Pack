using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FH6SkillPointOcr
{
    internal static class MouseCellCalibrator
    {
        public static RectangleF Capture(Config config, WindowBinding targetBinding)
        {
            Rectangle overlayBounds = ResolveOverlayBounds(config, targetBinding);
            using (ConsoleWindow.Hide())
            {
                using (CellCalibrationOverlayForm form = new CellCalibrationOverlayForm(overlayBounds))
                {
                    DialogResult result = form.ShowDialog();
                    return result == DialogResult.OK ? form.SelectedRectangle : RectangleF.Empty;
                }
            }
        }

        private static Rectangle ResolveOverlayBounds(Config config, WindowBinding targetBinding)
        {
            if (targetBinding != null && targetBinding.ClientBounds.Width > 0 && targetBinding.ClientBounds.Height > 0)
            {
                return targetBinding.ClientBounds;
            }

            Screen[] screens = Screen.AllScreens;
            int index = Math.Max(0, config.MonitorIndex - 1);
            if (index >= screens.Length) index = 0;
            return screens[index].Bounds;
        }
    }

    internal sealed class CellCalibrationOverlayForm : Form
    {
        private const int VK_LBUTTON = 0x01;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_ENTER = 0x0D;
        private const int VK_LEFT = 0x25;
        private const int VK_UP = 0x26;
        private const int VK_RIGHT = 0x27;
        private const int VK_DOWN = 0x28;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;

        private readonly Rectangle screenBounds;
        private readonly System.Windows.Forms.Timer timer;
        private bool dragging;
        private bool adjusting;
        private bool wasDown;
        private Point start;
        private Point current;
        private Rectangle selectedRect;
        private DateTime lastNudgeAt = DateTime.MinValue;
        private string message = "框选所有完整可见车辆格子的整体区域：从左上完整格拖到右下完整格，Esc 取消";

        public RectangleF SelectedRectangle { get; private set; }

        public CellCalibrationOverlayForm(Rectangle screenBounds)
        {
            this.screenBounds = screenBounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = screenBounds;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            DoubleBuffered = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 16;
            timer.Tick += PollMouse;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            MakeClickThrough();
            timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            base.OnFormClosed(e);
        }

        private void PollMouse(object sender, EventArgs e)
        {
            if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
            {
                if (adjusting)
                {
                    adjusting = false;
                    selectedRect = Rectangle.Empty;
                    message = "已放弃预览，请重新框选完整可见车辆区域，Esc 取消";
                    Invalidate();
                }
                else
                {
                    SelectedRectangle = RectangleF.Empty;
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                return;
            }

            if (adjusting)
            {
                PollAdjustmentKeys();
                wasDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                return;
            }

            Point cursor;
            GetCursorPos(out cursor);
            bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            bool inside = screenBounds.Contains(cursor);

            if (down && !wasDown && inside)
            {
                dragging = true;
                start = cursor;
                current = cursor;
                message = "松开鼠标左键保存这个完整可见车辆区域，Esc 取消";
                Invalidate();
            }
            else if (down && dragging)
            {
                current = cursor;
                Invalidate();
            }
            else if (!down && wasDown && dragging)
            {
                dragging = false;
                current = cursor;
                Rectangle rect = NormalizedRect(start, current);
                if (rect.Width < 20 || rect.Height < 20)
                {
                    message = "框选太小，请重新框选完整可见车辆区域";
                    Invalidate();
                }
                else
                {
                    selectedRect = rect;
                    adjusting = true;
                    message = "预览微调：方向键移动，Ctrl+方向键调宽高，Shift加速，Enter保存，Esc重画";
                    Invalidate();
                    return;
                }
            }

            wasDown = down;
        }

        private void PollAdjustmentKeys()
        {
            if ((GetAsyncKeyState(VK_ENTER) & 0x8000) != 0)
            {
                SelectedRectangle = new RectangleF(selectedRect.Left, selectedRect.Top, selectedRect.Width, selectedRect.Height);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            DateTime now = DateTime.UtcNow;
            if ((now - lastNudgeAt).TotalMilliseconds < 45) return;

            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool control = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            int step = shift ? 10 : 1;
            Rectangle next = selectedRect;
            bool changed = false;

            if ((GetAsyncKeyState(VK_LEFT) & 0x8000) != 0)
            {
                if (control) next.Width = Math.Max(20, next.Width - step);
                else next.X -= step;
                changed = true;
            }
            if ((GetAsyncKeyState(VK_RIGHT) & 0x8000) != 0)
            {
                if (control) next.Width += step;
                else next.X += step;
                changed = true;
            }
            if ((GetAsyncKeyState(VK_UP) & 0x8000) != 0)
            {
                if (control) next.Height = Math.Max(20, next.Height - step);
                else next.Y -= step;
                changed = true;
            }
            if ((GetAsyncKeyState(VK_DOWN) & 0x8000) != 0)
            {
                if (control) next.Height += step;
                else next.Y += step;
                changed = true;
            }

            if (!changed) return;
            selectedRect = next;
            lastNudgeAt = now;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Brush labelBack = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
            using (Brush labelBrush = new SolidBrush(Color.White))
            using (Font font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold))
            {
                SizeF size = g.MeasureString(message, font);
                RectangleF label = new RectangleF(Math.Max(24, (Width - size.Width - 28) / 2), 24, size.Width + 28, size.Height + 18);
                g.FillRectangle(labelBack, label);
                g.DrawString(message, font, labelBrush, label.X + 14, label.Y + 8);
            }

            if (!dragging && !adjusting) return;

            Rectangle absoluteRect = adjusting ? selectedRect : NormalizedRect(start, current);
            Rectangle local = ToLocal(absoluteRect);
            Point localStart = ToLocal(adjusting ? new Point(selectedRect.Left, selectedRect.Top) : start);
            Point localCurrent = ToLocal(adjusting ? new Point(selectedRect.Right, selectedRect.Bottom) : current);
            using (Pen rectPen = new Pen(Color.FromArgb(0, 180, 255), 2))
            using (Pen pointPen = new Pen(Color.Yellow, 2))
            using (Brush pointBrush = new SolidBrush(Color.Yellow))
            using (Brush infoBack = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            using (Brush infoBrush = new SolidBrush(Color.White))
            using (Font infoFont = new Font("Consolas", 11, FontStyle.Bold))
            {
                g.DrawRectangle(rectPen, local);
                g.FillEllipse(pointBrush, localStart.X - 3, localStart.Y - 3, 6, 6);
                g.DrawEllipse(pointPen, localCurrent.X - 4, localCurrent.Y - 4, 8, 8);
                if (adjusting)
                {
                    string info = string.Format(CultureInfo.InvariantCulture, "left={0} top={1} width={2} height={3}", selectedRect.Left, selectedRect.Top, selectedRect.Width, selectedRect.Height);
                    SizeF size = g.MeasureString(info, infoFont);
                    RectangleF infoRect = new RectangleF(local.Left, Math.Max(0, local.Bottom + 8), size.Width + 12, size.Height + 8);
                    g.FillRectangle(infoBack, infoRect);
                    g.DrawString(info, infoFont, infoBrush, infoRect.X + 6, infoRect.Y + 4);
                }
            }
        }

        private Point ToLocal(Point screenPoint)
        {
            return PointToClient(screenPoint);
        }

        private Rectangle ToLocal(Rectangle screenRect)
        {
            Point topLeft = PointToClient(new Point(screenRect.Left, screenRect.Top));
            Point bottomRight = PointToClient(new Point(screenRect.Right, screenRect.Bottom));
            return Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        }

        private static Rectangle NormalizedRect(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int right = Math.Max(a.X, b.X);
            int bottom = Math.Max(a.Y, b.Y);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void MakeClickThrough()
        {
            int style = GetWindowLong(Handle, -20);
            SetWindowLong(Handle, -20, style | 0x00080000 | 0x00000020 | 0x00000080);
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);
    }
}
