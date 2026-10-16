using TimeTug.Platform;

namespace TimeTug.Platform.Tests;

public class PlaceholderTests
{
    [Fact]
    public void Assembly_is_named_for_the_platform_layer()
    {
        PlatformAssembly.Name.Should().Be("TimeTug.Platform");
    }
}
