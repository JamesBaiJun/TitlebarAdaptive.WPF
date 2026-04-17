using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TitlebarAdaptive.WPF
{
    public class OriginalWindowHelper
    {
        // Windows 深色模式相关的 DWM 属性 ID。
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private const int WM_NCACTIVATE = 0x0086;

        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_ERASE = 0x0004;
        private const uint RDW_FRAME = 0x0400;
        private const uint RDW_UPDATENOW = 0x0100;

        // 保存已启用自适应标题栏的窗口，主题变化时统一刷新。
        private static readonly HashSet<Window> trackedWindows = new HashSet<Window>();

        // 缓存当前系统主题，避免重复读取注册表。
        private static bool isSystemLightTheme = ThemeHelper.IsSystemLightTheme();

        static OriginalWindowHelper()
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public static readonly DependencyProperty AdaptiveProperty =
            DependencyProperty.RegisterAttached(
                "Adaptive",
                typeof(bool),
                typeof(OriginalWindowHelper),
                new FrameworkPropertyMetadata(false, OnAdaptivePropertyChanged));

        /// <summary>
        /// 手动指定标题栏是否使用浅色主题。
        /// 仅在 <see cref="AdaptiveProperty"/> 为 <c>false</c> 时生效。
        /// </summary>
        public static readonly DependencyProperty TitleBarIsLightProperty =
            DependencyProperty.RegisterAttached(
                "TitleBarIsLight",
                typeof(bool?),
                typeof(OriginalWindowHelper),
                new FrameworkPropertyMetadata(null, OnTitleBarIsLightChanged));

        /// <summary>
        /// 当附加属性变化时，注册或取消注册窗口，并在句柄可用后应用标题栏主题。
        /// </summary>
        private static void OnAdaptivePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            Window window = sender as Window;
            if (window == null || !(e.NewValue is bool))
            {
                return;
            }

            bool isAdaptive = (bool)e.NewValue;

            if (!isAdaptive)
            {
                UntrackWindow(window);
                ApplyManualThemeWhenReady(window);
                return;
            }

            TrackWindow(window);
            ApplyThemeWhenReady(window);
        }

        /// <summary>
        /// 读取当前附加属性值。
        /// </summary>
        public static bool GetAdaptive(DependencyObject dp)
        {
            return (bool)dp.GetValue(AdaptiveProperty);
        }

        /// <summary>
        /// 设置附加属性值。
        /// </summary>
        public static void SetAdaptive(DependencyObject dp, bool value)
        {
            dp.SetValue(AdaptiveProperty, value);
        }

        /// <summary>
        /// 读取手动设置的标题栏浅色标记。
        /// <c>null</c> 表示未启用手动设置。
        /// </summary>
        public static bool? GetTitleBarIsLight(DependencyObject dp)
        {
            return (bool?)dp.GetValue(TitleBarIsLightProperty);
        }

        /// <summary>
        /// 设置手动标题栏主题。
        /// <c>true</c> 为浅色，<c>false</c> 为深色，<c>null</c> 表示不指定。
        /// </summary>
        public static void SetTitleBarIsLight(DependencyObject dp, bool? value)
        {
            dp.SetValue(TitleBarIsLightProperty, value);
        }

        /// <summary>
        /// 根据系统浅色/深色主题切换窗口标题栏。
        /// </summary>
        public static void ApplyTitleBarTheme(Window window, bool isLightTheme)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int useDarkMode = isLightTheme ? 0 : 1;
            int attributeSize = Marshal.SizeOf(typeof(int));

            if (!DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, attributeSize))
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, attributeSize);
            }

            // 刷新非客户区，让标题栏颜色尽快生效。
            SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);
            SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(0), IntPtr.Zero);
            SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);

            RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_FRAME | RDW_UPDATENOW | RDW_ERASE);
        }

        /// <summary>
        /// 当手动主题发生变化时，如果窗口未启用自适应，则立即应用。
        /// </summary>
        private static void OnTitleBarIsLightChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            Window window = sender as Window;
            if (window == null)
            {
                return;
            }

            if (GetAdaptive(window))
            {
                return;
            }

            ApplyManualThemeWhenReady(window);
        }

        /// <summary>
        /// 监听系统用户偏好变化，更新缓存的主题状态并刷新所有已跟踪窗口。
        /// </summary>
        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.Color)
            {
                isSystemLightTheme = ThemeHelper.IsSystemLightTheme();
                RefreshTrackedWindows();
            }
        }

        /// <summary>
        /// 窗口句柄可用后再应用主题，避免过早访问 HWND。
        /// </summary>
        private static void ApplyThemeWhenReady(Window window)
        {
            if (window.IsLoaded)
            {
                ApplyTitleBarTheme(window, isSystemLightTheme);
                return;
            }

            void OnSourceInitialized(object sender, EventArgs args)
            {
                window.SourceInitialized -= OnSourceInitialized;
                ApplyTitleBarTheme(window, isSystemLightTheme);
            }

            window.SourceInitialized += OnSourceInitialized;
        }

        /// <summary>
        /// 在窗口句柄可用后，应用手动设置的标题栏主题。
        /// </summary>
        private static void ApplyManualThemeWhenReady(Window window)
        {
            bool? isLightTheme = GetTitleBarIsLight(window);
            if (isLightTheme is null)
            {
                return;
            }

            if (window.IsLoaded)
            {
                ApplyTitleBarTheme(window, isLightTheme.Value);
                return;
            }

            void OnSourceInitialized(object sender, EventArgs args)
            {
                window.SourceInitialized -= OnSourceInitialized;
                ApplyTitleBarTheme(window, isLightTheme.Value);
            }

            window.SourceInitialized += OnSourceInitialized;
        }

        /// <summary>
        /// 添加到跟踪列表，避免重复注册。
        /// </summary>
        private static void TrackWindow(Window window)
        {
            if (trackedWindows.Add(window))
            {
                window.Closed += OnTrackedWindowClosed;
            }
        }

        /// <summary>
        /// 从跟踪列表移除窗口。
        /// </summary>
        private static void UntrackWindow(Window window)
        {
            if (trackedWindows.Remove(window))
            {
                window.Closed -= OnTrackedWindowClosed;
            }
        }

        /// <summary>
        /// 窗口关闭时自动移除，避免集合保留已释放的窗口引用。
        /// </summary>
        private static void OnTrackedWindowClosed(object sender, EventArgs e)
        {
            Window window = sender as Window;
            if (window != null)
            {
                UntrackWindow(window);
            }
        }

        /// <summary>
        /// 重新应用当前系统主题到所有已跟踪窗口。
        /// </summary>
        private static void RefreshTrackedWindows()
        {
            foreach (Window window in trackedWindows)
            {
                ApplyTitleBarTheme(window, isSystemLightTheme);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RedrawWindow(
            IntPtr hWnd,
            IntPtr lprcUpdate,
            IntPtr hrgnUpdate,
            uint flags);

        [DllImport("dwmapi.dll")]
        private static extern bool DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);
    }
}
