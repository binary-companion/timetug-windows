using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Presentation.Welcome;

/// <summary>One account kind in the list of places to connect. Its state is set by <see cref="AccountsViewModel"/>.</summary>
public sealed class ProviderItem : ObservableObject
{
    public ProviderItem(string id, string name, bool isConfigured, bool opensBrowser, string hint, string automationName)
        : this(id, name, isConfigured, opensBrowser, hint, automationName, null)
    {
    }

    /// <param name="intent">For the cards that sign in through the Windows account broker: which Windows dialog the card opens. Null for every other card.</param>
    public ProviderItem(string id, string name, bool isConfigured, bool opensBrowser, string hint, string automationName, BrokerIntent? intent)
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

    /// <summary>The card's title. It changes once for "Microsoft account on this PC" when Windows names the account ("Use name@example.com").</summary>
    public string Name
    {
        get;
        internal set => SetProperty(ref field, value);
    }

    /// <summary>The Windows dialog this card opens (the Windows account or the account picker); null for a card that does not use the broker.</summary>
    public BrokerIntent? Intent { get; }

    /// <summary>The first-run window pre-selects this card (the Windows account card, when the engine offers it): it takes the accent and the keyboard focus.</summary>
    public bool IsPreselected { get; internal set; }

    /// <summary>True after a Windows sign-in attempt on this card failed: the card then also offers the browser sign-in.</summary>
    public bool OffersBrowserFallback
    {
        get;
        internal set => SetProperty(ref field, value);
    }

    public string BrowserFallbackText
    {
        get;
        internal set => SetProperty(ref field, value);
    } = "";

    public string BrowserFallbackAutomationName
    {
        get;
        internal set => SetProperty(ref field, value);
    } = "";

    /// <summary>Starts the browser sign-in for the same Microsoft account type ("Use your browser instead").</summary>
    public ICommand? BrowserFallbackCommand
    {
        get;
        internal set => SetProperty(ref field, value);
    }

    /// <summary>False when this build has no sign-in set up for the kind (it shows disabled with <see cref="Hint"/> as the reason).</summary>
    public bool IsConfigured { get; }

    public bool OpensBrowser { get; }

    /// <summary>"Opens your browser", "Asks for your details" or the reason the kind is unavailable.</summary>
    public string Hint { get; }

    public string AutomationName
    {
        get;
        internal set => SetProperty(ref field, value);
    }

    public ICommand? ConnectCommand { get; internal set; }

    /// <summary>Whether the button can be used: configured and no other sign-in running.</summary>
    public bool IsEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? Error
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>The engine's own words about the failure, shown smaller beneath <see cref="Error"/>.</summary>
    public string? ErrorDetail
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool HasError => !string.IsNullOrEmpty(Error);
}
