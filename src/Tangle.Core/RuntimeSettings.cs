namespace Tangle.Core;

public sealed class RuntimeSettings
{
    public string Address { get; set; } = "http://127.0.0.1:5202";
    public string DevelopmentKey { get; set; } = "";
    public string DatabasePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tangle", "tangle.db");
}
