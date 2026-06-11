using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace FH6SkillPointOcr
{
    internal sealed class WindowBinding
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string ProcessName;
        public string Title;
        public Rectangle ClientBounds;
        public Size MessageClientSize;

        public string Summary()
        {
            return string.Format(
                "hwnd=0x{0:X}, pid={1}, process={2}, title={3}, capture=[{4},{5},{6},{7}], messageClient=[{8},{9}]",
                Handle.ToInt64(),
                ProcessId,
                string.IsNullOrWhiteSpace(ProcessName) ? "?" : ProcessName,
                string.IsNullOrWhiteSpace(Title) ? "?" : Title,
                ClientBounds.Left,
                ClientBounds.Top,
                ClientBounds.Width,
                ClientBounds.Height,
                MessageClientSize.Width,
                MessageClientSize.Height);
        }
    }

    internal static class WindowLocator
    {
        private const int GA_ROOT = 2;
        private const int GW_OWNER = 4;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int MinClientWidth = 320;
        private const int MinClientHeight = 200;

        public static bool TryBindForeground(int currentProcessId, out WindowBinding binding)
        {
            binding = null;
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            hwnd = GetAncestor(hwnd, GA_ROOT);
            return TryCreateBinding(hwnd, currentProcessId, out binding);
        }

        public static bool TryBindTargetWindow(int currentProcessId, IEnumerable<string> processKeywords, IEnumerable<string> titleKeywords, out WindowBinding binding)
        {
            binding = null;
            int bestScore = 0;
            long bestArea = 0;
            WindowBinding best = null;
            ConsiderProcessMainWindows(currentProcessId, processKeywords, titleKeywords, ref best, ref bestScore, ref bestArea);

            EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
            {
                WindowBinding candidate;
                if (!TryCreateBinding(hwnd, currentProcessId, out candidate, true)) return true;

                int score = MatchScore(candidate, processKeywords, titleKeywords);
                if (score <= 0) return true;

                long area = (long)candidate.ClientBounds.Width * candidate.ClientBounds.Height;
                if (best == null || score > bestScore || (score == bestScore && area > bestArea))
                {
                    best = candidate;
                    bestScore = score;
                    bestArea = area;
                }
                return true;
            }, IntPtr.Zero);

            binding = best;
            return binding != null;
        }

        private static void ConsiderProcessMainWindows(int currentProcessId, IEnumerable<string> processKeywords, IEnumerable<string> titleKeywords, ref WindowBinding best, ref int bestScore, ref long bestArea)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return;
            }

            foreach (Process process in processes)
            {
                using (process)
                {
                    if (process.Id == currentProcessId) continue;
                    string processName;
                    try
                    {
                        processName = process.ProcessName ?? "";
                    }
                    catch
                    {
                        continue;
                    }
                    if (!MatchesAny(processName, processKeywords)) continue;

                    IntPtr hwnd;
                    try
                    {
                        hwnd = process.MainWindowHandle;
                    }
                    catch
                    {
                        continue;
                    }
                    if (hwnd == IntPtr.Zero) continue;

                    WindowBinding candidate;
                    if (!TryCreateBinding(hwnd, currentProcessId, out candidate, true)) continue;
                    int score = MatchScore(candidate, processKeywords, titleKeywords) + 5000;
                    long area = (long)candidate.ClientBounds.Width * candidate.ClientBounds.Height;
                    if (best == null || score > bestScore || (score == bestScore && area > bestArea))
                    {
                        best = candidate;
                        bestScore = score;
                        bestArea = area;
                    }
                }
            }
        }

        public static bool TryBindFromPoint(Point point, int currentProcessId, out WindowBinding binding)
        {
            binding = null;
            IntPtr found = IntPtr.Zero;
            WindowBinding selected = null;
            EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
            {
                WindowBinding candidate;
                if (!TryCreateBinding(hwnd, currentProcessId, out candidate)) return true;
                if (!candidate.ClientBounds.Contains(point)) return true;
                found = hwnd;
                selected = candidate;
                return false;
            }, IntPtr.Zero);

            binding = selected;
            return found != IntPtr.Zero && binding != null;
        }

        public static bool TryRefresh(WindowBinding binding)
        {
            if (binding == null || binding.Handle == IntPtr.Zero) return false;
            Rectangle bounds;
            if (!TryGetWindowCaptureBounds(binding.Handle, out bounds)) return false;
            Size messageClientSize;
            if (!TryGetMessageClientSize(binding.Handle, out messageClientSize)) return false;
            binding.ClientBounds = bounds;
            binding.MessageClientSize = messageClientSize;
            binding.Title = GetWindowTitle(binding.Handle);
            binding.ProcessName = GetProcessName(binding.ProcessId);
            return true;
        }

        private static int MatchScore(WindowBinding candidate, IEnumerable<string> processKeywords, IEnumerable<string> titleKeywords)
        {
            int score = 0;
            string processName = candidate.ProcessName ?? "";
            string title = candidate.Title ?? "";

            foreach (string keyword in NormalizeKeywords(processKeywords))
            {
                if (ContainsIgnoreCase(processName, keyword))
                {
                    score += string.Equals(processName, keyword, StringComparison.OrdinalIgnoreCase) ? 2000 : 1200;
                }
            }

            foreach (string keyword in NormalizeKeywords(titleKeywords))
            {
                if (ContainsIgnoreCase(title, keyword))
                {
                    score += string.Equals(title, keyword, StringComparison.OrdinalIgnoreCase) ? 2500 : 300;
                }
            }

            if (string.IsNullOrWhiteSpace(title)) score -= 800;
            if (candidate.ClientBounds.Width >= 1280 && candidate.ClientBounds.Height >= 720) score += 50;
            return score;
        }

        private static IEnumerable<string> NormalizeKeywords(IEnumerable<string> keywords)
        {
            if (keywords == null) yield break;
            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                yield return keyword.Trim();
            }
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text) &&
                   !string.IsNullOrEmpty(value) &&
                   text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchesAny(string text, IEnumerable<string> keywords)
        {
            foreach (string keyword in NormalizeKeywords(keywords))
            {
                if (ContainsIgnoreCase(text, keyword)) return true;
            }
            return false;
        }

        private static bool TryCreateBinding(IntPtr hwnd, int currentProcessId, out WindowBinding binding)
        {
            return TryCreateBinding(hwnd, currentProcessId, out binding, false);
        }

        private static bool TryCreateBinding(IntPtr hwnd, int currentProcessId, out WindowBinding binding, bool allowOwnedWindow)
        {
            binding = null;
            if (hwnd == IntPtr.Zero) return false;
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
            if (!allowOwnedWindow && GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;

            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0 || processId == (uint)currentProcessId) return false;

            Rectangle bounds;
            if (!TryGetWindowCaptureBounds(hwnd, out bounds)) return false;
            if (bounds.Width < MinClientWidth || bounds.Height < MinClientHeight) return false;
            Size messageClientSize;
            if (!TryGetMessageClientSize(hwnd, out messageClientSize)) return false;

            binding = new WindowBinding
            {
                Handle = hwnd,
                ProcessId = processId,
                ProcessName = GetProcessName(processId),
                Title = GetWindowTitle(hwnd),
                ClientBounds = bounds,
                MessageClientSize = messageClientSize
            };
            return true;
        }

        private static bool TryGetWindowCaptureBounds(IntPtr hwnd, out Rectangle bounds)
        {
            if (TryGetDwmFrameBounds(hwnd, out bounds)) return true;
            return TryGetClientBounds(hwnd, out bounds);
        }

        private static bool TryGetDwmFrameBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            RECT rect;
            int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(RECT)));
            if (hr != 0) return false;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return false;
            bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static bool TryGetClientBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            RECT client;
            if (!GetClientRect(hwnd, out client)) return false;
            if (client.Right <= client.Left || client.Bottom <= client.Top) return false;

            POINT topLeft = new POINT { X = client.Left, Y = client.Top };
            if (!ClientToScreen(hwnd, ref topLeft)) return false;
            bounds = new Rectangle(topLeft.X, topLeft.Y, client.Right - client.Left, client.Bottom - client.Top);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static bool TryGetMessageClientSize(IntPtr hwnd, out Size size)
        {
            size = Size.Empty;
            RECT client;
            if (!GetClientRect(hwnd, out client)) return false;
            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0) return false;
            size = new Size(width, height);
            return true;
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            if (length <= 0) return "";
            StringBuilder sb = new StringBuilder(length + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetProcessName(uint processId)
        {
            try
            {
                Process process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch
            {
                return "";
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, int gaFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
