using TimeTug.Presentation.Accounts;
using static TimeTug.Presentation.Tests.Accounts.ListRig;

namespace TimeTug.Presentation.Tests.Accounts;

/// <summary>Which service logo a row or menu entry shows (spec D19, A1.5).</summary>
public class ProviderMarkTests
{
    [Theory]
    [InlineData("microsoft", ProviderMark.Microsoft)]
    [InlineData("microsoft-brokered", ProviderMark.Microsoft)]
    [InlineData("microsoft-brokered:pick", ProviderMark.Microsoft)]
    [InlineData("google", ProviderMark.Google)]
    [InlineData("icloud", ProviderMark.None)]
    [InlineData("caldav", ProviderMark.None)]
    [InlineData("icalsub", ProviderMark.None)]
    [InlineData("something-new", ProviderMark.None)]
    public void Every_kind_maps_to_its_service_mark(string kindId, ProviderMark expected) => ProviderMarks.Of(kindId).Should().Be(expected);

    [Fact]
    public void Both_microsoft_sign_in_kinds_show_the_same_mark_and_name()
    {
        var rig = ListRig.Create(Named);

        rig.Push(Account("p", "microsoft-brokered", "me@example.com"), Account("b", "microsoft", "other@example.com"), Account("g", "google", "you@example.com"), Account("c", "caldav", "Team"));

        rig.Model.ListAccounts.Select(a => a.Mark).Should().Equal(ProviderMark.Microsoft, ProviderMark.Microsoft, ProviderMark.Google, ProviderMark.None);
        rig.Model.ListAccounts.Select(a => a.LogoAutomationName).Should().Equal("Microsoft logo", "Microsoft logo", "Google logo", "");
    }
}
