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
    internal sealed class ScreenCapture
    {
        private readonly int monitorIndex;
        private readonly int currentProcessId;
        private readonly List<string> targetWindowProcessKeywords;
        private readonly List<string> targetWindowTitleKeywords;
        private bool windowBindingEnabled;
        private WindowBinding boundWindow;

        public ScreenCapture(int monitorIndex)
            : this(monitorIndex, null, null)
        {
        }

        public ScreenCapture(int monitorIndex, List<string> targetWindowProcessKeywords, List<string> targetWindowTitleKeywords)
        {
            this.monitorIndex = monitorIndex;
            currentProcessId = Process.GetCurrentProcess().Id;
            this.targetWindowProcessKeywords = targetWindowProcessKeywords ?? new List<string>();
            this.targetWindowTitleKeywords = targetWindowTitleKeywords ?? new List<string>();
        }

        public Screenshot Grab()
        {
            Rectangle bounds = GetBounds();
            return Grab(bounds);
        }

        public Screenshot Grab(Rectangle region)
        {
            Rectangle bounds = GetBounds();
            Rectangle clipped = Rectangle.Intersect(bounds, region);
            if (clipped.Width <= 0 || clipped.Height <= 0) clipped = bounds;
            Bitmap bitmap = new Bitmap(clipped.Width, clipped.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(clipped.Left, clipped.Top, 0, 0, clipped.Size, CopyPixelOperation.SourceCopy);
            }
            return new Screenshot(bitmap, clipped.Left, clipped.Top);
        }

        public Rectangle GetBounds()
        {
            Rectangle windowBounds;
            if (TryGetBoundWindowBounds(out windowBounds)) return windowBounds;
            return GetConfiguredBounds();
        }

        public void EnableWindowBinding(string reason)
        {
            windowBindingEnabled = true;
            Rectangle ignored;
            TryGetBoundWindowBounds(out ignored);
            if (boundWindow != null)
            {
                Console.WriteLine("[WINDOW_BIND] " + reason + " -> " + boundWindow.Summary());
            }
            else
            {
                Console.WriteLine("[WINDOW_BIND] " + reason + " -> 未能找到 FH6/Forza 目标窗口，暂时回退到配置显示器。");
            }
        }

        public bool IsWindowBound
        {
            get { return boundWindow != null && WindowLocator.TryRefresh(boundWindow); }
        }

        public bool WindowBindingEnabled
        {
            get { return windowBindingEnabled; }
        }

        public string BoundWindowSummary
        {
            get
            {
                return IsWindowBound ? boundWindow.Summary() : "未绑定";
            }
        }

        public uint BoundProcessId
        {
            get
            {
                return IsWindowBound && boundWindow != null ? boundWindow.ProcessId : 0;
            }
        }

        public Size BoundMessageClientSize
        {
            get
            {
                return IsWindowBound && boundWindow != null ? boundWindow.MessageClientSize : Size.Empty;
            }
        }

        public bool TryGetBoundWindowTarget(out IntPtr hwnd, out Rectangle bounds)
        {
            hwnd = IntPtr.Zero;
            bounds = Rectangle.Empty;
            Rectangle refreshedBounds;
            if (!TryGetBoundWindowBounds(out refreshedBounds) || boundWindow == null) return false;
            hwnd = boundWindow.Handle;
            bounds = refreshedBounds;
            return hwnd != IntPtr.Zero;
        }

        private Rectangle GetConfiguredBounds()
        {
            Screen[] screens = Screen.AllScreens;
            int index = Math.Max(0, monitorIndex - 1);
            if (index >= screens.Length) index = 0;
            return screens[index].Bounds;
        }

        private bool TryGetBoundWindowBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (!windowBindingEnabled) return false;

            WindowBinding targetBinding;
            if (WindowLocator.TryBindTargetWindow(currentProcessId, targetWindowProcessKeywords, targetWindowTitleKeywords, out targetBinding))
            {
                if (boundWindow == null || boundWindow.Handle != targetBinding.Handle)
                {
                    Console.WriteLine("[WINDOW_BIND] 自动找到目标窗口 -> " + targetBinding.Summary());
                }
                boundWindow = targetBinding;
                bounds = targetBinding.ClientBounds;
                return true;
            }

            if (boundWindow != null && WindowLocator.TryRefresh(boundWindow))
            {
                bounds = boundWindow.ClientBounds;
                return true;
            }

            WindowBinding binding;
            if (!WindowLocator.TryBindForeground(currentProcessId, out binding)) return false;
            Console.WriteLine("[WINDOW_BIND] 未找到 FH6/Forza 目标窗口，回退绑定前台窗口 -> " + binding.Summary());
            boundWindow = binding;
            bounds = binding.ClientBounds;
            return true;
        }
    }

}
