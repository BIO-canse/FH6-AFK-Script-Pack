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
    internal sealed class UserSettings
    {
        public int VisibleRows;
        public int VisibleColumns;
        public double GridCellLeft;
        public double GridCellTop;
        public double GridCellWidth;
        public double GridCellHeight;
        public bool DpiAwareCoordinates;
        public string CalibrationMode;
        public bool WindowBoundCalibration;
        public double CalibrationClientLeft;
        public double CalibrationClientTop;
        public double CalibrationClientWidth;
        public double CalibrationClientHeight;
        public int SkillVehiclePerformanceScore;
        public int DriveVehiclePerformanceScore;
        public int BlueprintSkillPointsPerRun;
        public int BlueprintNetTimeMs;
        public int BlueprintLoopExtraMs;
        public int BlueprintAfterXWaitMs;
        public int BlueprintPostEnterWaitMs;

        public static UserSettings LoadOrCreate(Config config)
        {
            string path = Path.Combine(config.BaseDir, "config", "user-settings.json");
            if (File.Exists(path))
            {
                UserSettings settings = Load(path, config);
                Apply(config, settings);
                Console.WriteLine("[SETTINGS] 已读取 " + path);
                PrintBlueprintSettings(config);
                Console.WriteLine("[SETTINGS] 我的车辆页面完整可见行数：" + settings.VisibleRows);
                Console.WriteLine("[SETTINGS] 我的车辆页面完整可见列数：" + settings.VisibleColumns);
                Console.WriteLine("[SETTINGS] 左上格子：left={0:0}, top={1:0}, width={2:0}, height={3:0}",
                    settings.GridCellLeft,
                    settings.GridCellTop,
                    settings.GridCellWidth,
                    settings.GridCellHeight);
                if (settings.WindowBoundCalibration)
                {
                    Console.WriteLine("[SETTINGS] 框选时窗口客户区：left={0:0}, top={1:0}, width={2:0}, height={3:0}",
                        settings.CalibrationClientLeft,
                        settings.CalibrationClientTop,
                        settings.CalibrationClientWidth,
                        settings.CalibrationClientHeight);
                }
                else
                {
                    Console.WriteLine("[SETTINGS] 旧设置没有窗口客户区基准；可继续按绝对坐标运行，但无法自动等比迁移。");
                }
                return settings;
            }

            UserSettings created = CreateFromConsole(path, config);
            Apply(config, created);
            return created;
        }

        public static UserSettings Reset(Config config)
        {
            string path = Path.Combine(config.BaseDir, "config", "user-settings.json");
            if (File.Exists(path))
            {
                ApplyExistingEditableSettings(path, config);
                File.Delete(path);
                Console.WriteLine("[SETTINGS] 已删除旧设置 " + path);
            }
            else
            {
                Console.WriteLine("[SETTINGS] 当前没有旧设置，直接创建新设置。");
            }

            UserSettings created = CreateFromConsole(path, config);
            Apply(config, created);
            return created;
        }

        private static UserSettings Load(string path, Config config)
        {
            Dictionary<string, object> json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            UserSettings settings = new UserSettings();
            settings.DpiAwareCoordinates = GetBool(json, "dpi_aware_coordinates", false);
            settings.CalibrationMode = GetString(json, "calibration_mode", "");
            settings.VisibleRows = GetInt(json, "visible_rows", 0);
            settings.VisibleColumns = GetInt(json, "visible_columns", 0);
            settings.GridCellLeft = GetDouble(json, "grid_cell_left", 0);
            settings.GridCellTop = GetDouble(json, "grid_cell_top", 0);
            settings.GridCellWidth = GetDouble(json, "grid_cell_width", 0);
            settings.GridCellHeight = GetDouble(json, "grid_cell_height", 0);
            settings.WindowBoundCalibration = GetBool(json, "window_bound_calibration", false);
            settings.CalibrationClientLeft = GetDouble(json, "calibration_client_left", 0);
            settings.CalibrationClientTop = GetDouble(json, "calibration_client_top", 0);
            settings.CalibrationClientWidth = GetDouble(json, "calibration_client_width", 0);
            settings.CalibrationClientHeight = GetDouble(json, "calibration_client_height", 0);
            settings.SkillVehiclePerformanceScore = GetPerformanceScore(json, "skill_vehicle_performance_score", config.SkillVehiclePerformanceScore);
            settings.DriveVehiclePerformanceScore = GetPerformanceScore(json, "drive_vehicle_performance_score", config.DriveVehiclePerformanceScore);
            settings.BlueprintSkillPointsPerRun = GetPositiveInt(json, "blueprint_skill_points_per_run", config.BlueprintSkillPointsPerRun);
            settings.BlueprintNetTimeMs = GetPositiveInt(json, "blueprint_net_time_ms", config.BlueprintNetTimeMs);
            settings.BlueprintLoopExtraMs = GetNonNegativeInt(json, "blueprint_loop_extra_ms", config.BlueprintLoopExtraMs);
            settings.BlueprintAfterXWaitMs = GetNonNegativeInt(json, "blueprint_after_x_wait_ms", config.BlueprintAfterXWaitMs);
            settings.BlueprintPostEnterWaitMs = GetNonNegativeInt(json, "blueprint_post_enter_wait_ms", config.BlueprintPostEnterWaitMs);
            if (!settings.DpiAwareCoordinates)
            {
                Console.WriteLine("[SETTINGS] 旧设置不是 DPI aware 坐标，截图会偏移，需要重新框选。");
                ApplyExistingEditableSettings(path, config);
                return CreateFromConsole(path, config);
            }
            if (!string.Equals(settings.CalibrationMode, "full_grid_v1", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SETTINGS] 旧设置只框选单个格子，需要改为框选完整可见格子区域。");
                ApplyExistingEditableSettings(path, config);
                return CreateFromConsole(path, config);
            }
            if (settings.VisibleRows <= 0 || settings.VisibleColumns <= 0 || settings.GridCellWidth <= 0 || settings.GridCellHeight <= 0)
            {
                Console.WriteLine("[SETTINGS] user-settings.json 缺少可见行列或格子尺寸，需要重新设置。");
                ApplyExistingEditableSettings(path, config);
                return CreateFromConsole(path, config);
            }
            return settings;
        }

        private static UserSettings CreateFromConsole(string path, Config config)
        {
            Console.WriteLine("首次运行或重设设置需要保存本机显示设置。");
            Console.WriteLine("先设置最重要的刷图参数。默认推荐蓝图码 123675780，约 101 秒 50 技术点；蓝图失效时请到视频评论区或其它来源找可用蓝图并实测一次。");
            Console.WriteLine("当前默认蓝图需要开启自动转向、手动挡、牵引力控制系统和稳定控制系统；如果使用其它蓝图，请按分享者提供的设置调整。");
            Console.WriteLine("运行前请在游戏设置的“抬头显示”里关闭“技术动画”。");

            int blueprintGain = ReadPositiveInt("请输入每次跑图获得的技术点，直接回车默认 " + config.BlueprintSkillPointsPerRun + "：", config.BlueprintSkillPointsPerRun);
            int blueprintNetTimeMs = ReadPositiveMilliseconds("请输入自己跑一次后结算显示的蓝图净时间（秒，可输入小数），直接回车默认 " + FormatMilliseconds(config.BlueprintNetTimeMs) + "：", config.BlueprintNetTimeMs);
            int driveScore = ReadPerformanceScore("请输入刷分开蓝图车辆性能分，直接回车默认 " + config.DriveVehiclePerformanceScore + "：", config.DriveVehiclePerformanceScore);
            int skillScore = ReadPerformanceScore("请输入点技能/删车识别用车辆性能分，直接回车默认 " + config.SkillVehiclePerformanceScore + "：", config.SkillVehiclePerformanceScore);
            int loopExtraMs = Math.Max(0, config.BlueprintLoopExtraMs);
            Console.WriteLine("[SETTINGS] 完整刷图循环时间会按“净时间 + {0}”自动计算。", FormatMilliseconds(loopExtraMs));

            Console.WriteLine("请进入“我的车辆”页面，数一下屏幕里能看到几行、几列完整车辆格子。");

            int rows = ReadPositiveInt("请输入完整可见行数，直接回车默认 3：", 3);
            int columns = ReadPositiveInt("请输入完整可见列数，然后回车：", 0);

            Console.WriteLine("接下来请用鼠标框选“所有完整可见车辆格子的整体区域”。");
            Console.WriteLine("例如 3 行 4 列，就从左上完整格子的左上角拖到右下完整格子的右下角。程序会按你输入的行列数自动切分。");
            WindowBinding targetBinding;
            if (WindowLocator.TryBindTargetWindow(Process.GetCurrentProcess().Id, config.TargetWindowProcessKeywords, config.TargetWindowTitleKeywords, out targetBinding))
            {
                Console.WriteLine("[SETTINGS] 框选将绑定 FH6 目标窗口：" + targetBinding.Summary());
                Console.WriteLine("[SETTINGS] 框选层只显示在该窗口所在显示器，避免主副屏缩放比例不同导致坐标偏移。");
            }
            else
            {
                targetBinding = null;
                Console.WriteLine("[SETTINGS] 未能提前找到 FH6 目标窗口，框选层将回退到配置显示器。");
            }
            Console.Write("按 Enter 后隐藏窗口并开始框选：");
            Console.ReadLine();
            RectangleF gridRect = MouseCellCalibrator.Capture(config, targetBinding);
            while (gridRect.Width <= 0 || gridRect.Height <= 0)
            {
                Console.WriteLine("框选无效，请重新框选。");
                Console.Write("按 Enter 后隐藏窗口并重新框选：");
                Console.ReadLine();
                gridRect = MouseCellCalibrator.Capture(config, targetBinding);
            }

            UserSettings settings = new UserSettings();
            settings.VisibleRows = rows;
            settings.VisibleColumns = columns;
            settings.GridCellLeft = gridRect.Left;
            settings.GridCellTop = gridRect.Top;
            settings.GridCellWidth = gridRect.Width / columns;
            settings.GridCellHeight = gridRect.Height / rows;
            settings.DpiAwareCoordinates = true;
            settings.CalibrationMode = "full_grid_v1";
            settings.SkillVehiclePerformanceScore = skillScore;
            settings.DriveVehiclePerformanceScore = driveScore;
            settings.BlueprintSkillPointsPerRun = blueprintGain;
            settings.BlueprintNetTimeMs = blueprintNetTimeMs;
            settings.BlueprintLoopExtraMs = loopExtraMs;
            settings.BlueprintAfterXWaitMs = config.BlueprintAfterXWaitMs;
            settings.BlueprintPostEnterWaitMs = config.BlueprintPostEnterWaitMs;

            Point center = new Point(
                (int)Math.Round(gridRect.Left + gridRect.Width / 2),
                (int)Math.Round(gridRect.Top + gridRect.Height / 2));
            WindowBinding binding = targetBinding;
            if (binding == null)
            {
                WindowLocator.TryBindFromPoint(center, Process.GetCurrentProcess().Id, out binding);
            }
            else
            {
                WindowLocator.TryRefresh(binding);
            }

            if (binding != null)
            {
                settings.WindowBoundCalibration = true;
                settings.CalibrationClientLeft = binding.ClientBounds.Left;
                settings.CalibrationClientTop = binding.ClientBounds.Top;
                settings.CalibrationClientWidth = binding.ClientBounds.Width;
                settings.CalibrationClientHeight = binding.ClientBounds.Height;
                Console.WriteLine("[SETTINGS] 已绑定框选区域下方窗口：" + binding.Summary());
            }
            else
            {
                settings.WindowBoundCalibration = false;
                Console.WriteLine("[SETTINGS] 未能识别框选区域下方窗口；本设置将按绝对坐标保存，无法自动等比迁移。");
            }

            Save(path, settings);
            Apply(config, settings);
            Console.WriteLine("[SETTINGS] 已保存 " + path);
            PrintBlueprintSettings(config);
            Console.WriteLine("[SETTINGS] 整体区域：left={0:0}, top={1:0}, width={2:0}, height={3:0}", gridRect.Left, gridRect.Top, gridRect.Width, gridRect.Height);
            Console.WriteLine("[SETTINGS] 单格尺寸：width={0:0}, height={1:0}", settings.GridCellWidth, settings.GridCellHeight);
            WriteCalibrationDiagnostic(config, settings);
            return settings;
        }

        private static void WriteCalibrationDiagnostic(Config config, UserSettings settings)
        {
            try
            {
                Rectangle capture = ResolveCalibrationCaptureBounds(config, settings);
                if (capture.Width <= 0 || capture.Height <= 0) return;

                string debugDir = config.ResolvePath(config.DebugDir);
                Directory.CreateDirectory(debugDir);
                string textPath = Path.Combine(debugDir, "calibration-grid-last.txt");
                File.WriteAllText(
                    textPath,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "capture={0},{1},{2},{3}\r\ngrid={4:0.##},{5:0.##},{6:0.##},{7:0.##}\r\nrows={8}\r\ncolumns={9}\r\nwindow_bound={10}\r\n",
                        capture.Left,
                        capture.Top,
                        capture.Width,
                        capture.Height,
                        settings.GridCellLeft,
                        settings.GridCellTop,
                        settings.GridCellWidth,
                        settings.GridCellHeight,
                        settings.VisibleRows,
                        settings.VisibleColumns,
                        settings.WindowBoundCalibration),
                    Encoding.UTF8);
                string path = Path.Combine(debugDir, "calibration-grid-last.png");
                using (Bitmap bitmap = new Bitmap(capture.Width, capture.Height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(capture.Left, capture.Top, 0, 0, capture.Size, CopyPixelOperation.SourceCopy);
                    using (Pen pen = new Pen(Color.Red, 4))
                    using (Brush brush = new SolidBrush(Color.Yellow))
                    using (Font font = new Font("Consolas", 24, FontStyle.Bold))
                    {
                        string title = string.Format(
                            CultureInfo.InvariantCulture,
                            "capture={0},{1},{2},{3} grid={4:0},{5:0},{6:0.##},{7:0.##}",
                            capture.Left,
                            capture.Top,
                            capture.Width,
                            capture.Height,
                            settings.GridCellLeft,
                            settings.GridCellTop,
                            settings.GridCellWidth,
                            settings.GridCellHeight);
                        g.DrawString(title, font, brush, 24, 24);
                        for (int row = 0; row < settings.VisibleRows; row++)
                        {
                            for (int col = 0; col < settings.VisibleColumns; col++)
                            {
                                float x = (float)(settings.GridCellLeft - capture.Left + col * settings.GridCellWidth);
                                float y = (float)(settings.GridCellTop - capture.Top + row * settings.GridCellHeight);
                                float width = (float)settings.GridCellWidth;
                                float height = (float)settings.GridCellHeight;
                                g.DrawRectangle(pen, x, y, width, height);
                                g.DrawString(col.ToString(CultureInfo.InvariantCulture) + "," + row.ToString(CultureInfo.InvariantCulture), font, brush, x + 10, y + 10);
                            }
                        }
                    }
                    bitmap.Save(path, ImageFormat.Png);
                }
                Console.WriteLine("[SETTINGS] 已生成框选诊断图：" + path);
                Console.WriteLine("[SETTINGS] 如果这张图里的红框已经偏了，说明保存值/框选阶段有问题；如果这张图是准的，说明运行时叠加层显示有问题。");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SETTINGS] 框选诊断图生成失败：" + ex.Message);
            }
        }

        private static Rectangle ResolveCalibrationCaptureBounds(Config config, UserSettings settings)
        {
            if (settings.WindowBoundCalibration && settings.CalibrationClientWidth > 0 && settings.CalibrationClientHeight > 0)
            {
                return new Rectangle(
                    (int)Math.Round(settings.CalibrationClientLeft),
                    (int)Math.Round(settings.CalibrationClientTop),
                    (int)Math.Round(settings.CalibrationClientWidth),
                    (int)Math.Round(settings.CalibrationClientHeight));
            }

            Screen[] screens = Screen.AllScreens;
            int index = Math.Max(0, config.MonitorIndex - 1);
            if (index >= screens.Length) index = 0;
            return screens[index].Bounds;
        }

        private static int ReadPositiveInt(string prompt, int defaultValue)
        {
            int columns = 0;
            while (columns <= 0)
            {
                Console.Write(prompt);
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) && defaultValue > 0) return defaultValue;
                if (!int.TryParse((input ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out columns) || columns <= 0)
                {
                    columns = 0;
                    Console.WriteLine("输入无效，请输入正整数。");
                }
            }
            return columns;
        }

        private static int ReadPerformanceScore(string prompt, int defaultValue)
        {
            while (true)
            {
                int value = ReadPositiveInt(prompt, defaultValue);
                if (value > 0 && value < 1000) return value;
                Console.WriteLine("输入无效，请输入 1 到 999 之间的性能分。");
            }
        }

        private static int ReadPositiveMilliseconds(string prompt, int defaultValue)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = (Console.ReadLine() ?? "").Trim();
                if (input.Length == 0 && defaultValue > 0) return defaultValue;
                double seconds;
                if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds > 0)
                {
                    return Math.Max(1, (int)Math.Round(seconds * 1000.0));
                }
                Console.WriteLine("输入无效，请输入大于 0 的秒数，例如 101 或 101.5。");
            }
        }

        private static void Save(string path, UserSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Dictionary<string, object> json = new Dictionary<string, object>();
            json["dpi_aware_coordinates"] = true;
            json["calibration_mode"] = "full_grid_v1";
            json["visible_rows"] = settings.VisibleRows;
            json["visible_columns"] = settings.VisibleColumns;
            json["grid_cell_left"] = Math.Round(settings.GridCellLeft, 2);
            json["grid_cell_top"] = Math.Round(settings.GridCellTop, 2);
            json["grid_cell_width"] = Math.Round(settings.GridCellWidth, 2);
            json["grid_cell_height"] = Math.Round(settings.GridCellHeight, 2);
            json["window_bound_calibration"] = settings.WindowBoundCalibration;
            json["calibration_client_left"] = Math.Round(settings.CalibrationClientLeft, 2);
            json["calibration_client_top"] = Math.Round(settings.CalibrationClientTop, 2);
            json["calibration_client_width"] = Math.Round(settings.CalibrationClientWidth, 2);
            json["calibration_client_height"] = Math.Round(settings.CalibrationClientHeight, 2);
            json["blueprint_skill_points_per_run"] = settings.BlueprintSkillPointsPerRun;
            json["blueprint_net_time_ms"] = settings.BlueprintNetTimeMs;
            json["blueprint_loop_extra_ms"] = settings.BlueprintLoopExtraMs;
            json["blueprint_after_x_wait_ms"] = settings.BlueprintAfterXWaitMs;
            json["blueprint_post_enter_wait_ms"] = settings.BlueprintPostEnterWaitMs;
            json["drive_vehicle_performance_score"] = settings.DriveVehiclePerformanceScore;
            json["skill_vehicle_performance_score"] = settings.SkillVehiclePerformanceScore;
            string body = new JavaScriptSerializer().Serialize(json);
            File.WriteAllText(path, PrettyJson(body), Encoding.UTF8);
        }

        private static void Apply(Config config, UserSettings settings)
        {
            if (settings.VisibleRows > 0) config.GridRows = settings.VisibleRows;
            if (settings.VisibleColumns > 0) config.VisibleColumns = settings.VisibleColumns;
            config.GridCellLeft = settings.GridCellLeft;
            config.GridCellTop = settings.GridCellTop;
            config.GridCellWidth = settings.GridCellWidth;
            config.GridCellHeight = settings.GridCellHeight;
            config.WindowBoundCalibration = settings.WindowBoundCalibration;
            config.CalibrationClientLeft = settings.CalibrationClientLeft;
            config.CalibrationClientTop = settings.CalibrationClientTop;
            config.CalibrationClientWidth = settings.CalibrationClientWidth;
            config.CalibrationClientHeight = settings.CalibrationClientHeight;
            if (settings.SkillVehiclePerformanceScore > 0)
            {
                config.SkillVehiclePerformanceScore = settings.SkillVehiclePerformanceScore;
                config.DeleteMarkerText = settings.SkillVehiclePerformanceScore.ToString(CultureInfo.InvariantCulture);
            }
            if (settings.DriveVehiclePerformanceScore > 0)
            {
                config.DriveVehiclePerformanceScore = settings.DriveVehiclePerformanceScore;
                config.DrivePerformanceScore = settings.DriveVehiclePerformanceScore;
                config.DriveMarkerText = settings.DriveVehiclePerformanceScore.ToString(CultureInfo.InvariantCulture);
            }
            if (settings.BlueprintSkillPointsPerRun > 0) config.BlueprintSkillPointsPerRun = settings.BlueprintSkillPointsPerRun;
            if (settings.BlueprintNetTimeMs > 0) config.BlueprintNetTimeMs = settings.BlueprintNetTimeMs;
            if (settings.BlueprintLoopExtraMs >= 0) config.BlueprintLoopExtraMs = settings.BlueprintLoopExtraMs;
            if (settings.BlueprintAfterXWaitMs >= 0) config.BlueprintAfterXWaitMs = settings.BlueprintAfterXWaitMs;
            if (settings.BlueprintPostEnterWaitMs >= 0) config.BlueprintPostEnterWaitMs = settings.BlueprintPostEnterWaitMs;
            config.MinuteLoopEnterToXWaitMs = config.BlueprintEnterToXWaitMs;
        }

        private static void ApplyExistingEditableSettings(string path, Config config)
        {
            try
            {
                Dictionary<string, object> json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                config.SkillVehiclePerformanceScore = GetPerformanceScore(json, "skill_vehicle_performance_score", config.SkillVehiclePerformanceScore);
                config.DeleteMarkerText = config.SkillVehiclePerformanceScore.ToString(CultureInfo.InvariantCulture);
                config.DriveVehiclePerformanceScore = GetPerformanceScore(json, "drive_vehicle_performance_score", config.DriveVehiclePerformanceScore);
                config.DrivePerformanceScore = config.DriveVehiclePerformanceScore;
                config.DriveMarkerText = config.DriveVehiclePerformanceScore.ToString(CultureInfo.InvariantCulture);
                config.BlueprintSkillPointsPerRun = GetPositiveInt(json, "blueprint_skill_points_per_run", config.BlueprintSkillPointsPerRun);
                config.BlueprintNetTimeMs = GetPositiveInt(json, "blueprint_net_time_ms", config.BlueprintNetTimeMs);
                config.BlueprintLoopExtraMs = GetNonNegativeInt(json, "blueprint_loop_extra_ms", config.BlueprintLoopExtraMs);
                config.BlueprintAfterXWaitMs = GetNonNegativeInt(json, "blueprint_after_x_wait_ms", config.BlueprintAfterXWaitMs);
                config.BlueprintPostEnterWaitMs = GetNonNegativeInt(json, "blueprint_post_enter_wait_ms", config.BlueprintPostEnterWaitMs);
                config.MinuteLoopEnterToXWaitMs = config.BlueprintEnterToXWaitMs;
            }
            catch
            {
            }
        }

        private static void PrintBlueprintSettings(Config config)
        {
            Console.WriteLine("[SETTINGS] 每次跑图技术点：+" + config.BlueprintSkillPointsPerRun);
            Console.WriteLine("[SETTINGS] 蓝图净时间：" + FormatMilliseconds(config.BlueprintNetTimeMs) + "；完整循环估算：" + FormatMilliseconds(config.BlueprintEstimatedLoopMs));
            Console.WriteLine("[SETTINGS] Enter -> X 默认等待：" + FormatMilliseconds(config.BlueprintEnterToXWaitMs));
            Console.WriteLine("[SETTINGS] 刷分车性能分：" + config.DriveVehiclePerformanceScore + "；点技能/删车性能分：" + config.SkillVehiclePerformanceScore);
        }

        private static int GetInt(Dictionary<string, object> json, string key, int fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static int GetPositiveInt(Dictionary<string, object> json, string key, int fallback)
        {
            int value = GetInt(json, key, fallback);
            return value > 0 ? value : fallback;
        }

        private static int GetNonNegativeInt(Dictionary<string, object> json, string key, int fallback)
        {
            int value = GetInt(json, key, fallback);
            return value >= 0 ? value : fallback;
        }

        private static int GetPerformanceScore(Dictionary<string, object> json, string key, int fallback)
        {
            int value = GetInt(json, key, fallback);
            return value > 0 && value < 1000 ? value : fallback;
        }

        private static double GetDouble(Dictionary<string, object> json, string key, double fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static bool GetBool(Dictionary<string, object> json, string key, bool fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static string GetString(Dictionary<string, object> json, string key, string fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static string FormatMilliseconds(int ms)
        {
            return (Math.Max(0, ms) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " 秒";
        }

        private static string PrettyJson(string compact)
        {
            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inString = false;
            for (int i = 0; i < compact.Length; i++)
            {
                char ch = compact[i];
                if (ch == '"' && (i == 0 || compact[i - 1] != '\\')) inString = !inString;

                if (!inString && (ch == '{' || ch == '['))
                {
                    sb.Append(ch).AppendLine();
                    indent++;
                    sb.Append(new string(' ', indent * 2));
                }
                else if (!inString && (ch == '}' || ch == ']'))
                {
                    sb.AppendLine();
                    indent--;
                    sb.Append(new string(' ', indent * 2)).Append(ch);
                }
                else if (!inString && ch == ',')
                {
                    sb.Append(ch).AppendLine();
                    sb.Append(new string(' ', indent * 2));
                }
                else if (!inString && ch == ':')
                {
                    sb.Append(": ");
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
