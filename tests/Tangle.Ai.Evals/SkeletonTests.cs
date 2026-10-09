using Tangle.Core;

namespace Tangle.Ai.Evals;

public sealed class SkeletonTests
{
    [Fact]
    public void LocalDefaultAddressIsLoopback() =>
        Assert.Equal("127.0.0.1", new Uri(new RuntimeSettings().Address).Host);
}
