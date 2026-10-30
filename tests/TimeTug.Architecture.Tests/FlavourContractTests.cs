using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TimeTug.Architecture.Tests;

/// <summary>
/// The three package manifests (Dev, Store, Direct) and the build files that choose between them. These are contracts over text, so they
/// run on every machine and fail a pull request that gives a release package development scaffolding.
/// </summary>
public partial class FlavourContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string[] AllManifests = ["Package.appxmanifest", "Package.Store.appxmanifest", "Package.Direct.appxmanifest"];

    private static XDocument Manifest(string name) =>
        XDocument.Load(Path.Combine(Root, "src", "TimeTug.App", name));

    private static IEnumerable<XElement> Descendants(XDocument document, string localName) =>
        document.Descendants().Where(e => e.Name.LocalName == localName);

    private static string Attribute(XDocument document, string element, string attribute) =>
        Descendants(document, element).Single().Attribute(attribute)!.Value;

    private static string PackageName(XDocument document) => Attribute(document, "Identity", "Name");

    private static string Publisher(XDocument document) => Attribute(document, "Identity", "Publisher");

    private static string[] Capabilities(XDocument document) =>
        Descendants(document, "Capability").Select(e => e.Attribute("Name")!.Value).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void The_development_manifest_has_the_alias_and_the_throwaway_publisher()
    {
        var dev = Manifest("Package.appxmanifest");

        PackageName(dev).Should().Be("BinaryCompanion.TimeTug.Dev");
        Publisher(dev).Should().Be("CN=TimeTug Development");
        Descendants(dev, "ExecutionAlias").Select(e => e.Attribute("Alias")!.Value).Should().Equal("timetug-dev.exe");
    }

    [Fact]
    public void The_store_manifest_identity_is_all_placeholders()
    {
        var store = Manifest("Package.Store.appxmanifest");

        PackageName(store).Should().MatchRegex(PartnerCenterPlaceholder().ToString());
        Publisher(store).Should().MatchRegex(PartnerCenterPlaceholder().ToString());
    }

    [Fact]
    public void The_store_manifest_names_each_partner_center_value_with_a_placeholder()
    {
        var store = Manifest("Package.Store.appxmanifest");

        Descendants(store, "DisplayName").First(e => e.Parent!.Name.LocalName == "Properties").Value.Should().MatchRegex(PartnerCenterPlaceholder().ToString());
        Descendants(store, "PublisherDisplayName").Single().Value.Should().MatchRegex(PartnerCenterPlaceholder().ToString());
    }

    [Fact]
    public void The_direct_manifest_has_its_own_identity_and_a_placeholder_publisher()
    {
        var direct = Manifest("Package.Direct.appxmanifest");

        PackageName(direct).Should().Be("BinaryCompanion.TimeTug");
        Publisher(direct).Should().Be("CN=Binary Companion");
    }

    [Theory]
    [InlineData("Package.Store.appxmanifest")]
    [InlineData("Package.Direct.appxmanifest")]
    public void A_release_manifest_carries_no_alias_no_dev_publisher_and_no_extra_capabilities(string name)
    {
        var release = Manifest(name);
        var dev = Manifest("Package.appxmanifest");
        var text = File.ReadAllText(Path.Combine(Root, "src", "TimeTug.App", name));

        Descendants(release, "ExecutionAlias").Should().BeEmpty();
        Descendants(release, "AppExecutionAlias").Should().BeEmpty();
        text.Should().NotContain("timetug-dev");
        text.Should().NotContain("CN=TimeTug Development");
        text.Should().NotContain(".Dev");
        Capabilities(release).Should().Equal(Capabilities(dev), "a release package declares exactly the capabilities the development package needs");
    }

    [Fact]
    public void The_three_flavours_keep_separate_data_so_their_identities_and_com_classes_differ()
    {
        var all = AllManifests.Select(Manifest).ToArray();

        all.Select(PackageName).Should().OnlyHaveUniqueItems();
        all.Select(m => Descendants(m, "Class").Single().Attribute("Id")!.Value).Should().OnlyHaveUniqueItems("two packages must not register the same COM class");
        all.Select(m => Descendants(m, "ToastNotificationActivation").Single().Attribute("ToastActivatorCLSID")!.Value)
            .Should().Equal(all.Select(m => Descendants(m, "Class").Single().Attribute("Id")!.Value), "each manifest's toast activator is its own COM class");
    }

    [Theory]
    [InlineData("Package.Store.appxmanifest")]
    [InlineData("Package.Direct.appxmanifest")]
    public void A_release_manifest_differs_from_the_development_one_only_in_identity_alias_and_com_class(string name)
    {
        string Normalised(string file)
        {
            var document = Manifest(file);
            document.DescendantNodes().OfType<XComment>().ToList().ForEach(comment => comment.Remove());
            foreach (var extension in Descendants(document, "Extension").Where(e => (string?)e.Attribute("Category") == "windows.appExecutionAlias").ToList())
            {
                extension.Remove();
            }

            foreach (var identity in Descendants(document, "Identity"))
            {
                identity.ReplaceAttributes();
            }

            foreach (var properties in Descendants(document, "Properties").Elements().Where(e => e.Name.LocalName is "DisplayName" or "PublisherDisplayName"))
            {
                properties.Value = string.Empty;
            }

            var text = document.ToString(SaveOptions.DisableFormatting);
            text = Guid().Replace(text, "{guid}");
            return text.Replace(" xmlns:uap5=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/5\"", string.Empty, StringComparison.Ordinal)
                .Replace("IgnorableNamespaces=\"uap uap5 rescap desktop com\"", "IgnorableNamespaces=\"uap rescap desktop com\"", StringComparison.Ordinal);
        }

        Normalised(name).Should().Be(Normalised("Package.appxmanifest"), "everything else in a release manifest is the development manifest, so the flavours cannot drift");
    }

    [Fact]
    public void Flavour_builds_define_only_their_own_constant_and_never_the_selftest()
    {
        var props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));

        props.Should().Contain("'$(Flavour)' == 'Store'");
        props.Should().Contain("'$(Flavour)' == 'Direct'");
        // SELFTEST is tied to the Debug and CI configurations alone, and a non-Dev flavour is refused in any of them.
        SelfTestCondition().Matches(props).Should().ContainSingle();
        props.Should().Contain("-c Release");
        props.Should().MatchRegex(@"'\$\(Flavour\)' != 'Dev' And '\$\(Configuration\)' != 'Release'");
    }

    [Fact]
    public void The_app_project_chooses_the_manifest_by_flavour()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "TimeTug.App", "TimeTug.App.csproj"));

        project.Should().Contain("Package.$(Flavour).appxmanifest");
        project.Should().Contain("ReleaseManifestPath");
    }

    [Fact]
    public void No_source_outside_the_development_flavour_names_the_dev_alias_or_the_dev_oauth_file()
    {
        // The alias is used by the development scripts and the self-test (compiled only under SELFTEST). The OAuth file is read only under SELFTEST.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}SelfTest{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("timetug-dev", StringComparison.Ordinal))
            {
                continue;
            }

            // Mentions in comments and doc comments are fine; code must be under #if SELFTEST.
            var code = string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith('*')));
            if (code.Contains("timetug-dev", StringComparison.Ordinal))
            {
                text.Should().Contain("#if SELFTEST", $"{Path.GetRelativePath(Root, file)} names the development alias");
            }
        }
    }

    [GeneratedRegex(@"^__PARTNER_CENTER_[A-Z_]+__$")]
    private static partial Regex PartnerCenterPlaceholder();

    [GeneratedRegex(@"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex Guid();

    [GeneratedRegex(@"<DefineConstants>[^<]*SELFTEST[^<]*</DefineConstants>")]
    private static partial Regex SelfTestCondition();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TimeTug.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("TimeTug.sln not found above " + AppContext.BaseDirectory);
    }
}
