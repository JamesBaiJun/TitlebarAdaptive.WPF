using Microsoft.Win32;

public static class ThemeHelper
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsSystemLightTheme()
    {
        object value = Registry.CurrentUser
            .OpenSubKey(PersonalizeKeyPath)?
            .GetValue("SystemUsesLightTheme");

        return value is int intValue ? intValue != 0 : true;
    }

    public static bool IsAppLightTheme()
    {
        object value = Registry.CurrentUser
            .OpenSubKey(PersonalizeKeyPath)?
            .GetValue("AppsUseLightTheme");

        return value is int intValue ? intValue != 0 : true;
    }
}