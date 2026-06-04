using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class TimingAdjustmentMenu
    {
        public static Config Run(Config config)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("等待时间调整：默认值偏保守。性能强的电脑可以适当降低；如果出现吞键、页面没加载完、OCR 太早，就调高。");
                PrintCurrent(config);
                Console.WriteLine("1. 开始确认后的等待时间");
                Console.WriteLine("2. 全自动各大阶段之间的预留等待");
                Console.WriteLine("3. 鼠标滚动后等待页面稳定");
                Console.WriteLine("4. UI OCR 前等待画面稳定");
                Console.WriteLine("5. 复用 UI 坐标缓存后，点击前的等待补偿");
                Console.WriteLine("6. 刷技术点：Enter 后等待多久按 X");
                Console.WriteLine("7. 恢复这些等待为默认保守值");
                Console.WriteLine("0. 返回运行模式选择");
                Console.Write("请选择要调整的项目：");
                string choice = (Console.ReadLine() ?? "").Trim();
                if (choice.Length == 0 || choice == "0") return config;

                if (choice == "1")
                {
                    config = Update(config, "startup_delay_ms", "开始确认后的等待时间", config.StartupDelayMs, 0, 30000);
                }
                else if (choice == "2")
                {
                    config = Update(config, "full_auto_stage_gap_ms", "全自动各大阶段之间的预留等待", config.FullAutoStageGapMs, 0, 10000);
                }
                else if (choice == "3")
                {
                    config = Update(config, "single_scroll_delay_ms", "鼠标滚动后等待页面稳定", config.SingleScrollDelayMs, 50, 5000);
                }
                else if (choice == "4")
                {
                    config = Update(config, "ui_ocr_stable_wait_ms", "UI OCR 前等待画面稳定", config.UiOcrStableWaitMs, 0, 10000);
                }
                else if (choice == "5")
                {
                    config = Update(config, "ui_cache_click_wait_ms", "复用 UI 坐标缓存后点击前等待补偿", config.UiCacheClickWaitMs, 0, 10000);
                }
                else if (choice == "6")
                {
                    config = Update(config, "minute_loop_enter_to_x_wait_ms", "刷技术点 Enter 后等待多久按 X", config.MinuteLoopEnterToXWaitMs, 5000, 120000);
                }
                else if (choice == "7")
                {
                    ResetDefaults(config.SourcePath);
                    config = Config.Load(config.SourcePath);
                    Console.WriteLine("[TIMING] 已恢复默认保守等待。");
                }
                else
                {
                    Console.WriteLine("输入无效，请输入 0-7。");
                }
            }
        }

        private static void PrintCurrent(Config config)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "当前：启动 {0}ms；阶段间隔 {1}ms；滚动稳定 {2}ms；UI OCR 稳定 {3}ms；缓存点击补偿 {4}ms；Enter->X {5}ms",
                config.StartupDelayMs,
                config.FullAutoStageGapMs,
                config.SingleScrollDelayMs,
                config.UiOcrStableWaitMs,
                config.UiCacheClickWaitMs,
                config.MinuteLoopEnterToXWaitMs));
        }

        private static Config Update(Config config, string key, string label, int current, int min, int max)
        {
            Console.Write(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} 当前 {1}ms，允许范围 {2}-{3}ms。输入新毫秒数，直接回车取消：",
                    label,
                    current,
                    min,
                    max));
            string input = (Console.ReadLine() ?? "").Trim();
            if (input.Length == 0) return config;

            int value;
            if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < min || value > max)
            {
                Console.WriteLine("输入无效，未修改。");
                return config;
            }

            WriteInt(config.SourcePath, key, value);
            Console.WriteLine("[TIMING] 已保存 {0} = {1}ms。", key, value);
            return Config.Load(config.SourcePath);
        }

        private static void ResetDefaults(string path)
        {
            WriteInt(path, "startup_delay_ms", FH6AutomationConstants.Timing.StartupDelayMs);
            WriteInt(path, "full_auto_stage_gap_ms", FH6AutomationConstants.Timing.FullAutoStageGapMs);
            WriteInt(path, "single_scroll_delay_ms", FH6AutomationConstants.Flow.SingleScrollDelayMs);
            WriteInt(path, "ui_ocr_stable_wait_ms", FH6AutomationConstants.Timing.UiOcrStableWaitMs);
            WriteInt(path, "ui_cache_click_wait_ms", FH6AutomationConstants.Timing.HalfSecondMs);
            WriteInt(path, "minute_loop_enter_to_x_wait_ms", FH6AutomationConstants.SkillPoints.MinuteLoopEnterToXWaitMs);
        }

        private static void WriteInt(string path, string key, int value)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            string pattern = "(\"" + Regex.Escape(key) + "\"\\s*:\\s*)-?\\d+";
            Regex regex = new Regex(pattern);
            string updated = regex.Replace(
                text,
                m => m.Groups[1].Value + value.ToString(CultureInfo.InvariantCulture));

            if (updated == text && !Regex.IsMatch(text, pattern))
            {
                string line = "  \"" + key + "\": " + value.ToString(CultureInfo.InvariantCulture) + "," + Environment.NewLine;
                Match anchor = Regex.Match(text, "^\\s*\"ocr_scale\"\\s*:", RegexOptions.Multiline);
                if (anchor.Success)
                {
                    updated = text.Insert(anchor.Index, line);
                }
                else
                {
                    int insertAt = text.LastIndexOf('}');
                    updated = insertAt >= 0 ? text.Insert(insertAt, line) : text + Environment.NewLine + line;
                }
            }

            File.WriteAllText(path, updated, Encoding.UTF8);
        }
    }
}
