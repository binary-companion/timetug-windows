using System.Text;
using TimeTug.Presentation.JumpList;

namespace TimeTug.Platform.Instance;

/// <summary>
/// Reads the launch arguments of the app: <c>--jump flyout|join-next|test-tug|settings</c> (also <c>--jump=word</c>, any case) is a jump list
/// action, wherever it stands among other arguments and with or without the program name before it. Anything else is ignored: an unknown
/// word, a missing word, the self-test's switches, the toast activator's argument. Arguments arrive two ways, as the process's argument
/// list (the execution alias, <c>Environment.GetCommandLineArgs</c>) and as one string (the launch activation a redirected launch carries);
/// both end up here.
/// </summary>
public static class ActivationParser
{
    /// <summary>The jump action in an argument list, if there is one.</summary>
    public static bool TryGetJump(IEnumerable<string>? arguments, out JumpAction action)
    {
        action = default;
        if (arguments is null)
        {
            return false;
        }

        var list = arguments.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var argument = list[i];
            if (argument.StartsWith(JumpArguments.Switch + "=", StringComparison.OrdinalIgnoreCase))
            {
                if (JumpArguments.TryParseWord(argument[(JumpArguments.Switch.Length + 1)..], out action))
                {
                    return true;
                }
            }
            else if (string.Equals(argument, JumpArguments.Switch, StringComparison.OrdinalIgnoreCase)
                && i + 1 < list.Count
                && JumpArguments.TryParseWord(list[i + 1], out action))
            {
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>The jump action in one command-line string (the launch activation's <c>Arguments</c>), if there is one.</summary>
    public static bool TryGetJumpFromCommandLine(string? commandLine, out JumpAction action) => TryGetJump(Split(commandLine), out action);

    /// <summary>The request an argument list makes of a running copy: its jump action, else a plain second launch (open the flyout).</summary>
    public static AppActivation FromArguments(IEnumerable<string>? arguments) =>
        TryGetJump(arguments, out var action) ? AppActivation.ForJump(action) : AppActivation.Flyout;

    /// <summary>The request a command-line string makes of a running copy.</summary>
    public static AppActivation FromCommandLine(string? commandLine) =>
        TryGetJumpFromCommandLine(commandLine, out var action) ? AppActivation.ForJump(action) : AppActivation.Flyout;

    /// <summary>Splits on white space; double quotes group words (the quotes themselves are dropped).</summary>
    internal static IReadOnlyList<string> Split(string? commandLine)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return words;
        }

        var word = new StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    started = false;
                }
            }
            else
            {
                word.Append(c);
                started = true;
            }
        }

        if (started)
        {
            words.Add(word.ToString());
        }

        return words;
    }
}
