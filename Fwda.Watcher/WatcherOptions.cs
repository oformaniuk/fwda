namespace Fwda.Watcher;

public class WatcherOptions
{
    public string OutputPath { get; set; } = "/config/config.yaml";
    public string SessionSecret { get; set; } = "change-me-in-production";
    public int SessionTimeoutMinutes { get; set; } = 60;
    public string LabelPrefix { get; set; } = "fwda";
    public string Container { get; set; } = "fwda";
}

