using TimeTug.Platform.Instance;
using TimeTug.Presentation.JumpList;

namespace TimeTug.Platform.Tests.Instance;

public class ActivationParserTests
{
    [Theory]
    [InlineData("flyout", JumpAction.Flyout)]
    [InlineData("join-next", JumpAction.JoinNext)]
    [InlineData("test-tug", JumpAction.TestTug)]
    [InlineData("settings", JumpAction.Settings)]
    public void Each_action_parses_from_the_command_line_the_jump_list_item_carries(string word, JumpAction expected)
    {
        ActivationParser.TryGetJump(["TimeTug.exe", "--jump", word], out var action).Should().BeTrue();
        action.Should().Be(expected);

        ActivationParser.TryGetJumpFromCommandLine("--jump " + word, out var fromString).Should().BeTrue();
        fromString.Should().Be(expected);
    }

    [Theory]
    [InlineData(JumpAction.Flyout)]
    [InlineData(JumpAction.JoinNext)]
    [InlineData(JumpAction.TestTug)]
    [InlineData(JumpAction.Settings)]
    public void The_arguments_the_service_writes_are_the_arguments_the_parser_reads(JumpAction action)
    {
        ActivationParser.TryGetJumpFromCommandLine(JumpArguments.For(action), out var parsed).Should().BeTrue();
        parsed.Should().Be(action);
    }

    [Fact]
    public void The_program_path_and_other_arguments_around_the_switch_are_ignored()
    {
        ActivationParser.TryGetJump([@"C:\Program Files\WindowsApps\x\TimeTug.exe", "--other", "1", "--jump", "settings", "--more"], out var action).Should().BeTrue();
        action.Should().Be(JumpAction.Settings);
    }

    [Fact]
    public void Case_and_the_equals_form_do_not_matter()
    {
        ActivationParser.TryGetJumpFromCommandLine("--JUMP Join-Next", out var one).Should().BeTrue();
        one.Should().Be(JumpAction.JoinNext);
        ActivationParser.TryGetJumpFromCommandLine("--jump=test-tug", out var two).Should().BeTrue();
        two.Should().Be(JumpAction.TestTug);
    }

    [Fact]
    public void Quotes_group_a_word_and_a_program_path_with_spaces()
    {
        ActivationParser.TryGetJumpFromCommandLine("\"C:\\Program Files\\TimeTug.exe\" --jump \"flyout\"", out var action).Should().BeTrue();
        action.Should().Be(JumpAction.Flyout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--selftest hello --out C:\\x")]
    [InlineData("--jump")]
    [InlineData("--jump nonsense")]
    [InlineData("--jump --settings")]
    [InlineData("jump settings")]
    [InlineData("----AppNotificationActivated:")]
    public void Unknown_or_unrelated_arguments_are_ignored(string commandLine)
    {
        ActivationParser.TryGetJumpFromCommandLine(commandLine, out _).Should().BeFalse();
    }

    [Fact]
    public void A_null_command_line_is_nothing()
    {
        ActivationParser.TryGetJumpFromCommandLine(null, out _).Should().BeFalse();
        ActivationParser.TryGetJump([], out _).Should().BeFalse();
    }

    [Fact]
    public void A_launch_without_a_jump_is_a_plain_second_launch_that_opens_the_flyout()
    {
        ActivationParser.FromArguments(["TimeTug.exe"]).Should().Be(AppActivation.Flyout);
        ActivationParser.FromArguments(["TimeTug.exe", "--jump", "nonsense"]).Should().Be(AppActivation.Flyout);
        ActivationParser.FromArguments(["TimeTug.exe", "--jump", "join-next"]).Should().Be(AppActivation.ForJump(JumpAction.JoinNext));
        ActivationParser.FromCommandLine(null).Should().Be(AppActivation.Flyout);
        ActivationParser.FromCommandLine("--jump settings").Should().Be(AppActivation.ForJump(JumpAction.Settings));
    }
}
