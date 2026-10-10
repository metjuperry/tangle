using Microsoft.Extensions.Configuration;
using Tangle.Core;

namespace Tangle.Ai;

public sealed class DeciderRegistry(HaikuDecider haiku, JevDecider jev, IConfiguration configuration)
{
    public IMeasuredDecider Get(string name) => name switch
    {
        "haiku-5-5" => haiku,
        "jev" when configuration.GetValue<bool>("Ai:JevEnabled") => jev,
        "jev" => throw new InvalidOperationException("Jev is disabled. Set Ai:JevEnabled only after authorizing this provider."),
        _ => throw new ArgumentException("Unknown AI decider.")
    };
    public bool JevEnabled => configuration.GetValue<bool>("Ai:JevEnabled");
}
