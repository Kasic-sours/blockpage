namespace AntiBlockManager.Application.Models;

public class AppSettings
{
    public bool AutoStart { get; set; } = false;
    public bool StartEngineOnLaunch { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool CheckUpdatesAuto { get; set; } = true;
    public string Language { get; set; } = "ru";
    public bool IsFirstLaunch { get; set; } = true;
    public string ZapretStrategy { get; set; } = "Fake";
    public bool IsLightTheme { get; set; } = false;
}