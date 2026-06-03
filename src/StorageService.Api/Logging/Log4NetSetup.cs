using log4net;
using log4net.Core;
using log4net.Repository.Hierarchy;
using StorageService.Api.Models;

namespace StorageService.Api.Logging;

public static class Log4NetSetup
{
    public static void Configure(LogConfig logConfig, string baseDirectory)
    {
        var logPath = logConfig.LogPath;
        if (!Path.IsPathRooted(logPath))
        {
            logPath = Path.GetFullPath(Path.Combine(baseDirectory, logPath));
        }

        Directory.CreateDirectory(logPath);
        GlobalContext.Properties["LOG_PATH"] = logPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        GlobalContext.Properties["MAX_ROLLBACKS"] = Math.Max(1, logConfig.LogCleanDays * 8).ToString();

        var configFile = new FileInfo(Path.Combine(AppContext.BaseDirectory, "log4net.config"));
        if (!configFile.Exists)
        {
            configFile = new FileInfo(Path.Combine(baseDirectory, "log4net.config"));
        }

        log4net.Config.XmlConfigurator.ConfigureAndWatch(LogManager.GetRepository(), configFile);

        if (LogManager.GetRepository() is Hierarchy hierarchy)
        {
            hierarchy.Threshold = ToLevel(logConfig.MinLogLevel);
            hierarchy.RaiseConfigurationChanged(EventArgs.Empty);
        }

        LogManager.GetLogger(typeof(Log4NetSetup)).Info($"log4net configured. logPath={logPath}");
    }

    private static Level ToLevel(int minLogLevel) => minLogLevel switch
    {
        <= 0 => Level.Debug,
        1 => Level.Info,
        2 => Level.Warn,
        _ => Level.Error
    };
}
