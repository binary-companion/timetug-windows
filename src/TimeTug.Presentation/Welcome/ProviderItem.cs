using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Presentation.Welcome;

/// <summary>One entry of the "+" menu: a kind of account that can be added (or, disabled, why it cannot). Its state is set by <see cref="AccountsViewModel"/>.</summary>
public sealed class ProviderItem : ObservableObject
{
    /// <param name="intent">For the Microsoft entry that signs in through the Windows account broker: which Windows dialog it opens. Null for every other entry.</param>
    public ProviderItem(string id, string name, bool isConfigured, bool opensBrowser, string hint, string automationName, BrokerIntent? intent = null)
    {
        Id = id;
        Name = name;
        IsConfigured = isConfigured;
        OpensBrowser = opensBrowser;
        Hint = hint;
        AutomationName = automationName;
        Intent = intent;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>The Windows dialog this entry opens (the account picker); null for an entry that does not use the broker.</summary>
    public BrokerIntent? Intent { get; }

    /// <summary>False when this build has no sign-in set up for the kind (it shows disabled with <see cref="Hint"/> as the reason).</summary>
    public bool IsConfigured { get; }

    public bool OpensBrowser { get; }

    /// <summary>The entry's second line: "Use your browser instead", or the reason the kind is unavailable. Empty when there is nothing to say.</summary>
    public string Hint { get; }

    public string AutomationName { get; }

    public ICommand? ConnectCommand { get; internal set; }

    /// <summary>Whether the entry can be chosen: the build can sign in to the kind.</summary>
    public bool IsEnabled { get; set; }
}
