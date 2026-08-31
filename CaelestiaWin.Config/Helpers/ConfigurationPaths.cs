namespace CaelestiaWin.Config.Helpers;

public static class ConfigurationPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NebulaShell");

    public static string ConfigPath => Path.Combine(RootDirectory, "config.json");

    public static string LogDirectory => Path.Combine(RootDirectory, "logs");

    public static string LogPath => Path.Combine(LogDirectory, "nebula.log");

    public static string SessionPath => Path.Combine(RootDirectory, "session.json");
}
