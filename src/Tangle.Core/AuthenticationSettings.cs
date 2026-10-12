namespace Tangle.Core;

public sealed class AuthenticationSettings
{
    public string Mode { get; set; } = "Local";
    public string[] AllowedLogins { get; set; } = ["metjuperry"];
    public string[] AllowedOrganizations { get; set; } = [];
    public string[] TrustedProxyAddresses { get; set; } = [];

    public void Validate()
    {
        if (Mode is not ("Local" or "ContainerApps" or "GitHub"))
            throw new InvalidOperationException("Auth:Mode must be Local, ContainerApps or GitHub.");
        if (Mode == "GitHub" && AllowedLogins.Length == 0 && AllowedOrganizations.Length == 0)
            throw new InvalidOperationException("GitHub authentication requires an explicit login or organization allowlist.");
        if (Mode == "ContainerApps" && (AllowedLogins.Length == 0 || TrustedProxyAddresses.Length == 0))
            throw new InvalidOperationException("ContainerApps authentication requires Auth:AllowedLogins and Auth:TrustedProxyAddresses. Only trusted ingress may supply identity headers.");
        if (TrustedProxyAddresses.Any(address => !System.Net.IPAddress.TryParse(address, out _)))
            throw new InvalidOperationException("Auth:TrustedProxyAddresses must contain explicit IP addresses.");
    }
}
