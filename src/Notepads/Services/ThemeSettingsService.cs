// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2019-2024, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Notepads.Services
{
    using System;
    using Microsoft.Toolkit.Uwp.Helpers;
    using Notepads.Brushes;
    using Notepads.Controls.Helpers;
    using Notepads.Settings;
    using Notepads.Utilities;
    using Windows.UI;
    using Windows.UI.ViewManagement;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media;

    public static class ThemeSettingsService
    {
        public static event EventHandler<ElementTheme> OnThemeChanged;
        public static event EventHandler<Brush> OnBackgroundChanged;
        public static event EventHandler<Color> OnAccentColorChanged;

        public static ElementTheme ThemeMode { get; set; }

        private static readonly UISettings UISettings = new UISettings();
        private static readonly ThemeListener ThemeListener = new ThemeListener();
        private static readonly AccessibilitySettings AccessibilitySettings = new AccessibilitySettings();
        private static readonly Color Graphite = Color.FromArgb(255, 41, 42, 40);
        private static readonly Color Paper = Color.FromArgb(255, 248, 245, 239);
        private static readonly Color DarkAmber = Color.FromArgb(255, 154, 75, 0);
        private static readonly Color Amber = Color.FromArgb(255, 239, 130, 14);
        private static Brush _currentAppBackgroundBrush;

        private static bool _useWindowsTheme;

        public static bool UseWindowsTheme
        {
            get => _useWindowsTheme;
            set
            {
                if (value != _useWindowsTheme)
                {
                    _useWindowsTheme = value;
                    if (value)
                    {
                        var currentWindowsTheme = Application.Current.RequestedTheme.ToElementTheme();
                        SetTheme(currentWindowsTheme);
                    }
                    ApplicationSettingsStore.Write(SettingsKey.UseWindowsThemeBool, _useWindowsTheme);
                }
            }
        }

        private static bool _useWindowsAccentColor;

        public static bool UseWindowsAccentColor
        {
            get => _useWindowsAccentColor;
            set
            {
                _useWindowsAccentColor = value;
                ApplicationSettingsStore.Write(SettingsKey.UseWindowsAccentColorBool, value);
                OnAccentColorChanged?.Invoke(null, AppAccentColor);
            }
        }

        private static double _appBackgroundPanelTintOpacity;

        public static double AppBackgroundPanelTintOpacity
        {
            get => _appBackgroundPanelTintOpacity;
            set
            {
                _appBackgroundPanelTintOpacity = value;
                OnBackgroundChanged?.Invoke(null, GetAppBackgroundBrush(ThemeMode));
                ApplicationSettingsStore.Write(SettingsKey.AppBackgroundTintOpacityDouble, value);
            }
        }

        private static Color? _customAccentColor;

        public static Color AppAccentColor
        {
            get
            {
                if (AccessibilitySettings.HighContrast) return UISettings.UIElementColor(UIElementType.Highlight);
                if (UseWindowsAccentColor) return UISettings.GetColorValue(UIColorType.Accent);
                return _customAccentColor ?? (ThemeMode == ElementTheme.Light ? DarkAmber : Amber);
            }
        }

        public static void SetCustomAccentColor(Color? color)
        {
            _customAccentColor = color;
            if (color.HasValue)
            {
                ApplicationSettingsStore.Write(SettingsKey.CustomAccentColorHexStr, color.Value.ToHex());
                ApplicationSettingsStore.Write(SettingsKey.AppAccentColorHexStr, color.Value.ToHex());
            }
            else
            {
                ApplicationSettingsStore.Remove(SettingsKey.CustomAccentColorHexStr);
                ApplicationSettingsStore.Remove(SettingsKey.AppAccentColorHexStr);
            }

            OnAccentColorChanged?.Invoke(null, AppAccentColor);
        }

        public static void Initialize()
        {
            InitializeThemeMode();
            InitializeAppAccentColor();
            InitializeAppBackgroundPanelTintOpacity();
        }

        private static void InitializeAppAccentColor()
        {
            _useWindowsAccentColor = ApplicationSettingsStore.Read(SettingsKey.UseWindowsAccentColorBool) as bool? ?? false;
            var stored = ApplicationSettingsStore.Read(SettingsKey.CustomAccentColorHexStr) as string;
            if (stored == null && !UseWindowsAccentColor)
            {
                stored = ApplicationSettingsStore.Read(SettingsKey.AppAccentColorHexStr) as string;
            }

            _customAccentColor = stored == null ? (Color?)null : stored.ToColor();
            UISettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
        }

        private static void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            if (UseWindowsAccentColor || AccessibilitySettings.HighContrast)
            {
                OnAccentColorChanged?.Invoke(null, AppAccentColor);
            }
        }

        private static void InitializeAppBackgroundPanelTintOpacity()
        {
            if (ApplicationSettingsStore.Read(SettingsKey.AppBackgroundTintOpacityDouble) is double tintOpacity)
            {
                _appBackgroundPanelTintOpacity = tintOpacity;
            }
            else
            {
                _appBackgroundPanelTintOpacity = 0.75;
            }
        }

        private static void InitializeThemeMode()
        {
            if (ApplicationSettingsStore.Read(SettingsKey.UseWindowsThemeBool) is bool useWindowsTheme)
            {
                _useWindowsTheme = useWindowsTheme;
            }
            else
            {
                _useWindowsTheme = true;
            }

            ThemeListener.ThemeChanged += ThemeListener_ThemeChanged;

            ThemeMode = Application.Current.RequestedTheme.ToElementTheme();

            if (!UseWindowsTheme)
            {
                if (ApplicationSettingsStore.Read(SettingsKey.RequestedThemeStr) is string themeModeStr)
                {
                    if (Enum.TryParse(typeof(ElementTheme), themeModeStr, out var theme))
                    {
                        ThemeMode = (ElementTheme)theme;
                    }
                }
            }
        }

        private static void ThemeListener_ThemeChanged(ThemeListener sender)
        {
            if (UseWindowsTheme)
            {
                var theme = sender.CurrentTheme.ToElementTheme();
                if (theme != ThemeMode)
                {
                    SetTheme(theme);
                    return;
                }
            }

            // High Contrast can change while the light/dark theme stays the same.
            OnThemeChanged?.Invoke(null, ThemeMode);
            OnAccentColorChanged?.Invoke(null, AppAccentColor);
        }

        public static void SetTheme(ElementTheme theme)
        {
            if (ThemeMode != theme)
            {
                ThemeMode = theme;
                ApplicationSettingsStore.Write(SettingsKey.RequestedThemeStr, ThemeMode.ToString());
                OnThemeChanged?.Invoke(null, theme);
                OnAccentColorChanged?.Invoke(null, AppAccentColor);
            }
        }

        public static void SetRequestedTheme(Panel backgroundPanel, UIElement currentContent, ApplicationViewTitleBar titleBar)
        {
            // Set requested theme for app background
            if (backgroundPanel != null)
            {
                backgroundPanel.Background = GetAppBackgroundBrush(ThemeMode);
            }

            if (currentContent is FrameworkElement frameworkElement)
            {
                frameworkElement.RequestedTheme = ThemeMode;
            }

            // Set requested theme for app title bar
            if (titleBar != null)
            {
                ApplyThemeForTitleBarButtons(titleBar, ThemeMode);
            }

            // Set ContentDialog background dimming color
            ((SolidColorBrush)Application.Current.Resources["SystemControlPageBackgroundMediumAltMediumBrush"]).Color =
                ThemeMode == ElementTheme.Dark ? Color.FromArgb(153, 0, 0, 0) : Color.FromArgb(153, 255, 255, 255);

            if (DialogManager.ActiveDialog != null)
            {
                DialogManager.ActiveDialog.RequestedTheme = ThemeMode;
            }

            // Set accent color
            UpdateSystemAccentColorAndBrushes(AppAccentColor);
        }

        public static void SetRequestedAccentColor()
        {
            UpdateSystemAccentColorAndBrushes(AppAccentColor);
        }

        public static ElementTheme ToElementTheme(this ApplicationTheme theme)
        {
            switch (theme)
            {
                case ApplicationTheme.Light:
                    return ElementTheme.Light;
                case ApplicationTheme.Dark:
                    return ElementTheme.Dark;
                default:
                    return ElementTheme.Default;
            }
        }

        private static Brush GetAppBackgroundBrush(ElementTheme theme)
        {
            if (AccessibilitySettings.HighContrast)
            {
                return new SolidColorBrush(UISettings.UIElementColor(UIElementType.Window));
            }

            var baseColor = theme == ElementTheme.Light ? Paper : Graphite;

            if (AppBackgroundPanelTintOpacity > 0.99f ||
                !Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush") ||
                App.IsGameBarWidget)
            {
                return new SolidColorBrush(baseColor);
            }
            else
            {
                if (_currentAppBackgroundBrush is HostBackdropAcrylicBrush hostBackdropAcrylicBrush)
                {
                    hostBackdropAcrylicBrush.LuminosityColor = baseColor;
                    hostBackdropAcrylicBrush.TintOpacity = (float)AppBackgroundPanelTintOpacity;
                    return _currentAppBackgroundBrush;
                }
                return _currentAppBackgroundBrush = BrushUtility.GetHostBackdropAcrylicBrushAsync(baseColor, (float)AppBackgroundPanelTintOpacity).Result;
            }
        }

        public static void ApplyThemeForTitleBarButtons(ApplicationViewTitleBar titleBar, ElementTheme theme)
        {
            if (AccessibilitySettings.HighContrast)
            {
                // Let the caption buttons inherit the user's Windows contrast palette.
                titleBar.ButtonForegroundColor = titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonHoverForegroundColor = titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonPressedForegroundColor = titleBar.ButtonPressedBackgroundColor = null;
                titleBar.ButtonInactiveForegroundColor = titleBar.ButtonInactiveBackgroundColor = null;
                titleBar.InactiveForegroundColor = titleBar.BackgroundColor = null;
                return;
            }

            if (theme == ElementTheme.Dark)
            {
                // Set active window colors
                titleBar.ButtonForegroundColor = Windows.UI.Colors.White;
                titleBar.ButtonBackgroundColor = Windows.UI.Colors.Transparent;
                titleBar.ButtonHoverForegroundColor = Windows.UI.Colors.White;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 90, 90, 90);
                titleBar.ButtonPressedForegroundColor = Windows.UI.Colors.White;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 120, 120, 120);

                // Set inactive window colors
                titleBar.InactiveForegroundColor = Windows.UI.Colors.Gray;
                titleBar.InactiveBackgroundColor = Windows.UI.Colors.Transparent;
                titleBar.ButtonInactiveForegroundColor = Windows.UI.Colors.Gray;
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Colors.Transparent;

                titleBar.BackgroundColor = Graphite;
            }
            else if (theme == ElementTheme.Light)
            {
                // Set active window colors
                titleBar.ButtonForegroundColor = Windows.UI.Colors.Black;
                titleBar.ButtonBackgroundColor = Windows.UI.Colors.Transparent;
                titleBar.ButtonHoverForegroundColor = Windows.UI.Colors.Black;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 180, 180, 180);
                titleBar.ButtonPressedForegroundColor = Windows.UI.Colors.Black;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 150, 150, 150);

                // Set inactive window colors
                titleBar.InactiveForegroundColor = Windows.UI.Colors.DimGray;
                titleBar.InactiveBackgroundColor = Windows.UI.Colors.Transparent;
                titleBar.ButtonInactiveForegroundColor = Windows.UI.Colors.DimGray;
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Colors.Transparent;

                titleBar.BackgroundColor = Paper;
            }
        }

        private static void UpdateSystemAccentColorAndBrushes(Color color)
        {
            if ((Color)Application.Current.Resources["SystemAccentColor"] == color)
            {
                return;
            }

            Application.Current.Resources["SystemAccentColor"] = color;

            // Took from: https://stackoverflow.com/questions/31831917/change-accent-color-in-windows-10-uwp/31844773
            var brushes = new string[]
            {
                "SystemControlBackgroundAccentBrush",
                "SystemControlDisabledAccentBrush",
                "SystemControlForegroundAccentBrush",
                "SystemControlHighlightAccentBrush",
                "SystemControlHighlightAltAccentBrush",
                "SystemControlHighlightAltListAccentHighBrush",
                "SystemControlHighlightAltListAccentLowBrush",
                "SystemControlHighlightAltListAccentMediumBrush",
                "SystemControlHighlightListAccentHighBrush",
                "SystemControlHighlightListAccentLowBrush",
                "SystemControlHighlightListAccentMediumBrush",
                "SystemControlHyperlinkTextBrush",
                "ContentDialogBorderThemeBrush",
                "JumpListDefaultEnabledBackground"
            };

            foreach (var brush in brushes)
            {
                try
                {
                    ((SolidColorBrush)Application.Current.Resources[brush]).Color = color;
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"[{nameof(ThemeSettingsService)}] Failed to apply color change for Brush: [{brush}]: {ex.Message}");
                }
            }

            try
            {
                // Overwrite MenuFlyoutSubItemRevealBackgroundSubMenuOpened resource color
                ((RevealBackgroundBrush)Application.Current.Resources["SystemControlHighlightAccent3RevealBackgroundBrush"]).Color = color;
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}