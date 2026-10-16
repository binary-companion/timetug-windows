using TimeTug.Presentation;

namespace TimeTug.Presentation.Tests;

public class PlaceholderTests
{
    [Fact]
    public void Assembly_is_named_for_the_presentation_layer()
    {
        PresentationAssembly.Name.Should().Be("TimeTug.Presentation");
    }
}
