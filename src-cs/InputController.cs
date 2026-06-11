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
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed class InputController : IDisposable
    {
        public const int VK_STEP = FH6AutomationConstants.Keys.DebugStepVirtualKey;
        private const int VK_HOTKEY_MODIFIER = FH6AutomationConstants.Keys.HotkeyModifierVirtualKey;
        private const int VK_C = FH6AutomationConstants.Keys.ExitVirtualKey;
        private const int VK_V = FH6AutomationConstants.Keys.SafeStopVirtualKey;
        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int WHEEL_DELTA = 120;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int MK_LBUTTON = 0x0001;

        private readonly int tapMs;
        private readonly int repeatIntervalMs;
        private readonly bool dryRun;
        private readonly string safeStopFile;
        private readonly bool useWindowMessageMouseInput;
        private readonly bool blockPhysicalMouseOnBoundWindow;
        private readonly object targetWindowLock = new object();
        private IntPtr targetWindowHandle;
        private Rectangle targetClientBounds;
        private Size targetMessageClientSize;
        private bool hasTargetWindow;
        private Point virtualMousePoint;
        private bool hasVirtualMousePoint;
        private MouseInputBlocker mouseInputBlocker;
        private bool safeStopRequested;
        private readonly Dictionary<string, ushort> vk = new Dictionary<string, ushort>
        {
            {"BACKSPACE", FH6AutomationConstants.Keys.Backspace},
            {"ENTER", FH6AutomationConstants.Keys.Enter},
            {"ESC", FH6AutomationConstants.Keys.Escape},
            {"UP", FH6AutomationConstants.Keys.Up},
            {"DOWN", FH6AutomationConstants.Keys.Down},
            {"LEFT", FH6AutomationConstants.Keys.Left},
            {"RIGHT", FH6AutomationConstants.Keys.Right}
        };

        public InputController(int tapMs, int repeatIntervalMs, bool dryRun, string safeStopFile, bool useWindowMessageMouseInput, bool blockPhysicalMouseOnBoundWindow)
        {
            this.tapMs = tapMs;
            this.repeatIntervalMs = repeatIntervalMs;
            this.dryRun = dryRun;
            this.safeStopFile = safeStopFile;
            this.useWindowMessageMouseInput = useWindowMessageMouseInput;
            this.blockPhysicalMouseOnBoundWindow = blockPhysicalMouseOnBoundWindow;
        }

        public void Dispose()
        {
            if (mouseInputBlocker != null)
            {
                mouseInputBlocker.Dispose();
                mouseInputBlocker = null;
            }
        }

        public void BindTargetWindow(IntPtr hwnd, Rectangle clientBounds, string reason)
        {
            BindTargetWindow(hwnd, clientBounds, clientBounds.Size, reason);
        }

        public void BindTargetWindow(IntPtr hwnd, Rectangle clientBounds, Size messageClientSize, string reason)
        {
            lock (targetWindowLock)
            {
                hasTargetWindow = hwnd != IntPtr.Zero && clientBounds.Width > 0 && clientBounds.Height > 0;
                targetWindowHandle = hasTargetWindow ? hwnd : IntPtr.Zero;
                targetClientBounds = hasTargetWindow ? clientBounds : Rectangle.Empty;
                targetMessageClientSize = hasTargetWindow && messageClientSize.Width > 0 && messageClientSize.Height > 0 ? messageClientSize : targetClientBounds.Size;
                if (hasTargetWindow && !hasVirtualMousePoint)
                {
                    virtualMousePoint = new Point(
                        targetClientBounds.Left + targetClientBounds.Width / 2,
                        targetClientBounds.Top + targetClientBounds.Height / 2);
                    hasVirtualMousePoint = true;
                }
            }

            if (hasTargetWindow)
            {
                Console.WriteLine("[INPUT] TARGET_WINDOW hwnd=0x" + hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture) + " bounds=" + targetClientBounds + " messageClient=" + targetMessageClientSize + " reason=" + reason);
            }

            if (blockPhysicalMouseOnBoundWindow)
            {
                if (mouseInputBlocker == null) mouseInputBlocker = new MouseInputBlocker();
                if (hasTargetWindow) mouseInputBlocker.SetTarget(hwnd, clientBounds);
                mouseInputBlocker.Start();
            }
        }

        public bool ShouldStop()
        {
            return (GetAsyncKeyState(VK_HOTKEY_MODIFIER) & 0x8000) != 0 && (GetAsyncKeyState(VK_C) & 0x8000) != 0;
        }

        public bool SafeStopRequested
        {
            get
            {
                PollSafeStop();
                return safeStopRequested;
            }
        }

        public bool PollSafeStop()
        {
            if (!safeStopRequested && (GetAsyncKeyState(VK_HOTKEY_MODIFIER) & 0x8000) != 0 && (GetAsyncKeyState(VK_V) & 0x8000) != 0)
            {
                safeStopRequested = true;
                Console.WriteLine("[SAFE_STOP] Space+V detected, will stop after current loop reset.");
            }
            if (!safeStopRequested && !string.IsNullOrWhiteSpace(safeStopFile) && File.Exists(safeStopFile))
            {
                safeStopRequested = true;
                Console.WriteLine("[SAFE_STOP] safe-stop-file detected, will stop after current loop reset.");
            }
            return safeStopRequested;
        }

        public void SleepMs(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (ShouldStop()) throw new StopRequestedException();
                PollSafeStop();
                Thread.Sleep(Math.Min(FH6AutomationConstants.Timing.SleepSliceMs, Math.Max(1, ms - (int)sw.ElapsedMilliseconds)));
            }
        }

        public void WaitForVkPress(int key)
        {
            while ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                if (ShouldStop()) throw new StopRequestedException();
                PollSafeStop();
                Thread.Sleep(FH6AutomationConstants.Timing.DebugKeyPollMs);
            }
            while ((GetAsyncKeyState(key) & 0x8000) == 0)
            {
                if (ShouldStop()) throw new StopRequestedException();
                PollSafeStop();
                Thread.Sleep(FH6AutomationConstants.Timing.DebugKeyPollMs);
            }
            while ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                if (ShouldStop()) throw new StopRequestedException();
                PollSafeStop();
                Thread.Sleep(FH6AutomationConstants.Timing.DebugKeyPollMs);
            }
        }

        public void Tap(string key)
        {
            Console.WriteLine("[INPUT] " + key);
            KeyDown(key);
            SleepMs(tapMs);
            KeyUp(key);
            SleepMs(repeatIntervalMs);
        }

        public void Click()
        {
            Console.WriteLine("[INPUT] LEFT_CLICK");
            if (!dryRun)
            {
                if (useWindowMessageMouseInput)
                {
                    SendWindowClick();
                }
                else
                {
                    SendMouse(0, 0, 0, MOUSEEVENTF_LEFTDOWN);
                    SleepMs(tapMs);
                    SendMouse(0, 0, 0, MOUSEEVENTF_LEFTUP);
                }
            }
            else
            {
                SleepMs(tapMs);
            }
            SleepMs(repeatIntervalMs);
        }

        public void MoveTo(int x, int y)
        {
            if (useWindowMessageMouseInput)
            {
                Console.WriteLine("[INPUT] VIRTUAL_MOVE " + x + "," + y);
                lock (targetWindowLock)
                {
                    virtualMousePoint = new Point(x, y);
                    hasVirtualMousePoint = true;
                }
            }
            else
            {
                Console.WriteLine("[INPUT] MOVE " + x + "," + y);
                if (!dryRun) SetCursorPos(x, y);
            }
            SleepMs(repeatIntervalMs);
        }

        public void ScrollDown(int ticks, int tickDelayMs)
        {
            Console.WriteLine("[INPUT] WHEEL_DOWN x" + ticks);
            for (int i = 0; i < ticks; i++)
            {
                if (!dryRun)
                {
                    if (useWindowMessageMouseInput)
                    {
                        SendWindowWheel(-WHEEL_DELTA);
                    }
                    else
                    {
                        uint sent = SendMouse(0, 0, -WHEEL_DELTA, MOUSEEVENTF_WHEEL);
                        if (sent == 0)
                        {
                            Console.WriteLine("[INPUT_ERROR] WHEEL SendInput failed, lastError=" + Marshal.GetLastWin32Error());
                        }
                    }
                }
                SleepMs(tickDelayMs);
            }
        }

        public void ScrollUp(int ticks, int tickDelayMs)
        {
            Console.WriteLine("[INPUT] WHEEL_UP x" + ticks);
            for (int i = 0; i < ticks; i++)
            {
                if (!dryRun)
                {
                    if (useWindowMessageMouseInput)
                    {
                        SendWindowWheel(WHEEL_DELTA);
                    }
                    else
                    {
                        uint sent = SendMouse(0, 0, WHEEL_DELTA, MOUSEEVENTF_WHEEL);
                        if (sent == 0)
                        {
                            Console.WriteLine("[INPUT_ERROR] WHEEL SendInput failed, lastError=" + Marshal.GetLastWin32Error());
                        }
                    }
                }
                SleepMs(tickDelayMs);
            }
        }

        private void KeyDown(string key)
        {
            if (!dryRun) SendKeyboard(vk[key], 0);
        }

        private void KeyUp(string key)
        {
            if (!dryRun) SendKeyboard(vk[key], KEYEVENTF_KEYUP);
        }

        private void SendKeyboard(ushort virtualKey, int flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U.ki.wVk = virtualKey;
            input.U.ki.wScan = 0;
            input.U.ki.dwFlags = flags;
            input.U.ki.time = 0;
            input.U.ki.dwExtraInfo = IntPtr.Zero;
            SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        private uint SendMouse(int dx, int dy, int data, int flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_MOUSE;
            input.U.mi.dx = dx;
            input.U.mi.dy = dy;
            input.U.mi.mouseData = data;
            input.U.mi.dwFlags = flags;
            input.U.mi.time = 0;
            input.U.mi.dwExtraInfo = IntPtr.Zero;
            return SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        private void SendWindowClick()
        {
            IntPtr hwnd;
            Rectangle bounds;
            Size messageClientSize;
            Point screenPoint;
            if (!TryResolveWindowMouseTarget(out hwnd, out bounds, out messageClientSize, out screenPoint))
            {
                throw new InvalidOperationException("窗口消息点击需要先绑定目标窗口。当前没有可用绑定，已拒绝移动真实鼠标。");
            }

            Point client = ToClientPoint(bounds, messageClientSize, screenPoint);
            IntPtr lParam = MakePointLParam(client.X, client.Y);
            PostMouseMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam, "WM_MOUSEMOVE");
            PostMouseMessage(hwnd, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), lParam, "WM_LBUTTONDOWN");
            SleepMs(tapMs);
            PostMouseMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam, "WM_LBUTTONUP");
        }

        private void SendWindowWheel(int delta)
        {
            IntPtr hwnd;
            Rectangle bounds;
            Size messageClientSize;
            Point screenPoint;
            if (!TryResolveWindowMouseTarget(out hwnd, out bounds, out messageClientSize, out screenPoint))
            {
                throw new InvalidOperationException("窗口消息滚轮需要先绑定目标窗口。当前没有可用绑定，已拒绝使用真实鼠标滚轮。");
            }

            Point client = ToClientPoint(bounds, messageClientSize, screenPoint);
            PostMouseMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, MakePointLParam(client.X, client.Y), "WM_MOUSEMOVE");
            PostMouseMessage(hwnd, WM_MOUSEWHEEL, MakeWheelWParam(delta), MakePointLParam(screenPoint.X, screenPoint.Y), "WM_MOUSEWHEEL");
        }

        private bool TryResolveWindowMouseTarget(out IntPtr hwnd, out Rectangle bounds, out Size messageClientSize, out Point screenPoint)
        {
            lock (targetWindowLock)
            {
                hwnd = targetWindowHandle;
                bounds = targetClientBounds;
                messageClientSize = targetMessageClientSize;
                if (!hasTargetWindow || hwnd == IntPtr.Zero || bounds.Width <= 0 || bounds.Height <= 0)
                {
                    messageClientSize = Size.Empty;
                    screenPoint = Point.Empty;
                    return false;
                }

                if (!hasVirtualMousePoint)
                {
                    screenPoint = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                }
                else
                {
                    screenPoint = virtualMousePoint;
                }
            }

            screenPoint = new Point(
                Math.Max(bounds.Left, Math.Min(bounds.Right - 1, screenPoint.X)),
                Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, screenPoint.Y)));
            return true;
        }

        private static Point ToClientPoint(Rectangle bounds, Size messageClientSize, Point screenPoint)
        {
            int width = messageClientSize.Width > 0 ? messageClientSize.Width : bounds.Width;
            int height = messageClientSize.Height > 0 ? messageClientSize.Height : bounds.Height;
            double scaleX = width / (double)Math.Max(1, bounds.Width);
            double scaleY = height / (double)Math.Max(1, bounds.Height);
            int x = (int)Math.Round((screenPoint.X - bounds.Left) * scaleX);
            int y = (int)Math.Round((screenPoint.Y - bounds.Top) * scaleY);
            x = Math.Max(0, Math.Min(width - 1, x));
            y = Math.Max(0, Math.Min(height - 1, y));
            return new Point(x, y);
        }

        private static IntPtr MakePointLParam(int x, int y)
        {
            return new IntPtr((y << 16) | (x & 0xFFFF));
        }

        private static IntPtr MakeWheelWParam(int delta)
        {
            return new IntPtr((delta << 16) & unchecked((int)0xFFFF0000));
        }

        private static void PostMouseMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, string label)
        {
            if (!PostMessage(hwnd, message, wParam, lParam))
            {
                Console.WriteLine("[INPUT_ERROR] " + label + " PostMessage failed, lastError=" + Marshal.GetLastWin32Error());
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private sealed class MouseInputBlocker : IDisposable
        {
            private const int WH_MOUSE_LL = 14;
            private const int WM_QUIT = 0x0012;
            private const int LLMHF_INJECTED = 0x00000001;
            private const int LLMHF_LOWER_IL_INJECTED = 0x00000002;
            private readonly object sync = new object();
            private LowLevelMouseProc hookProc;
            private IntPtr hookHandle;
            private Thread hookThread;
            private uint hookThreadId;
            private IntPtr targetHwnd;
            private Rectangle targetBounds;
            private bool targetEnabled;
            private bool disposed;

            public void SetTarget(IntPtr hwnd, Rectangle bounds)
            {
                lock (sync)
                {
                    targetHwnd = hwnd;
                    targetBounds = bounds;
                    targetEnabled = hwnd != IntPtr.Zero && bounds.Width > 0 && bounds.Height > 0;
                }
            }

            public void Start()
            {
                if (disposed) return;
                if (hookThread != null) return;

                hookProc = HookCallback;
                hookThread = new Thread(HookThreadMain);
                hookThread.IsBackground = true;
                hookThread.Name = "FH6MouseInputBlocker";
                hookThread.Start();
            }

            public void Dispose()
            {
                disposed = true;
                uint threadId = hookThreadId;
                if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                if (hookThread != null && hookThread.IsAlive) hookThread.Join(1000);
                lock (sync)
                {
                    if (hookHandle != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(hookHandle);
                        hookHandle = IntPtr.Zero;
                    }
                }
            }

            private void HookThreadMain()
            {
                hookThreadId = GetCurrentThreadId();
                IntPtr module = GetModuleHandle(null);
                IntPtr handle = SetWindowsHookEx(WH_MOUSE_LL, hookProc, module, 0);
                lock (sync)
                {
                    hookHandle = handle;
                }

                if (handle == IntPtr.Zero)
                {
                    Console.WriteLine("[INPUT_ERROR] 安装真实鼠标屏蔽钩子失败，lastError=" + Marshal.GetLastWin32Error());
                    return;
                }

                Console.WriteLine("[INPUT] 已启用绑定窗口真实鼠标屏蔽。");
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                lock (sync)
                {
                    if (hookHandle != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(hookHandle);
                        hookHandle = IntPtr.Zero;
                    }
                }
            }

            private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0)
                {
                    MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    bool injected = (data.flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;
                    if (!injected && ShouldBlock(data.pt.X, data.pt.Y))
                    {
                        return new IntPtr(1);
                    }
                }

                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            private bool ShouldBlock(int x, int y)
            {
                lock (sync)
                {
                    return targetEnabled && targetBounds.Contains(x, y);
                }
            }

            private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern IntPtr GetModuleHandle(string lpModuleName);

            [DllImport("kernel32.dll")]
            private static extern uint GetCurrentThreadId();

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

            [DllImport("user32.dll")]
            private static extern bool TranslateMessage(ref MSG lpMsg);

            [DllImport("user32.dll")]
            private static extern IntPtr DispatchMessage(ref MSG lpMsg);

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MSLLHOOKSTRUCT
            {
                public POINT pt;
                public int mouseData;
                public int flags;
                public int time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MSG
            {
                public IntPtr hwnd;
                public uint message;
                public IntPtr wParam;
                public IntPtr lParam;
                public uint time;
                public POINT pt;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }
    }

}
