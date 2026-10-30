using TimeTug.Presentation.Startup;

namespace TimeTug.Presentation.Tests.Startup;

public class BuildFlavourTests
{
    [Theory]
    [InlineData(false, false, Flavour.Dev)]
    [InlineData(true, false, Flavour.Store)]
    [InlineData(false, true, Flavour.Direct)]
    public void The_flavour_follows_the_compile_constants(bool store, bool direct, Flavour expected)
    {
        BuildFlavour.Resolve(store, direct).Should().Be(expected);
    }

    [Fact]
    public void Defining_both_release_constants_is_a_build_mistake()
    {
        var resolve = () => BuildFlavour.Resolve(store: true, direct: true);

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_test_build_is_a_development_build()
    {
        // The unit tests are built without -p:Flavour, which is the Dev default.
        BuildFlavour.Current.Should().Be(Flavour.Dev);
    }

    [Theory]
    [InlineData(Flavour.Dev, true)]
    [InlineData(Flavour.Store, false)]
    [InlineData(Flavour.Direct, false)]
    public void Only_a_development_build_carries_development_scaffolding(Flavour flavour, bool expected)
    {
        BuildFlavour.HasDevelopmentScaffolding(flavour).Should().Be(expected);
    }

    [Theory]
    [InlineData(Flavour.Dev, "TimeTug (development)", "dev")]
    [InlineData(Flavour.Store, "TimeTug", "store")]
    [InlineData(Flavour.Direct, "TimeTug", "direct")]
    public void Each_flavour_names_itself_for_diagnostics_only(Flavour flavour, string displayName, string dataNamespace)
    {
        var identity = AppIdentity.For(flavour);

        identity.DisplayName.Should().Be(displayName);
        identity.DataNamespace.Should().Be(dataNamespace);
    }

    [Fact]
    public void The_data_namespaces_differ_because_the_distributions_keep_separate_data()
    {
        Enum.GetValues<Flavour>().Select(f => AppIdentity.For(f).DataNamespace).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(Flavour.Dev, "1.2.3.0 (dev)")]
    [InlineData(Flavour.Store, "1.2.3.0 (store)")]
    [InlineData(Flavour.Direct, "1.2.3.0 (direct)")]
    public void The_diagnostics_report_says_which_distribution_made_it(Flavour flavour, string expected)
    {
        AppIdentity.For(flavour).Label("1.2.3.0").Should().Be(expected);
    }
}
