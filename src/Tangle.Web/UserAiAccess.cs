using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Tangle.Data;

namespace Tangle.Web;

public sealed class UserAiAccess(IUserContext user, ControlDatabase control, IDataProtectionProvider protection, IConfiguration config)
{
    public AiAccount Settings => control.AiAsync(user.UserId).GetAwaiter().GetResult();
    public bool Enabled
    {
        get
        {
            if (!config.GetValue<bool>("Ai:Enabled")) return false;
            if (user.UserId == "local") return !string.IsNullOrWhiteSpace(config["ANTHROPIC_API_KEY"]);
            var settings = Settings;
            return settings.Mode == "own_key" && settings.EncryptedKey is not null ||
                settings.Mode == "company" && settings.CompanyGranted && config.GetValue<bool>("Ai:CompanyEnabled")
                && !string.IsNullOrWhiteSpace(config["ANTHROPIC_API_KEY"]);
        }
    }
    public string Key
    {
        get
        {
            if (!Enabled) return "";
            if (user.UserId == "local" || Settings.Mode == "company") return config["ANTHROPIC_API_KEY"] ?? "";
            return protection.CreateProtector("Tangle.Anthropic.Keys.v1").Unprotect(Settings.EncryptedKey!);
        }
    }
}
