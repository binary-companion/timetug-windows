using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace TimeTug.Presentation.Welcome;

/// <summary>One account kind in the list of places to connect. Its state is set by <see cref="AccountsViewModel"/>.</summary>
public sealed class ProviderItem : ObservableObject
{
    public ProviderItem(string id, string name, bool isConfigured, bool opensBrowser, string hint, string automationName)
    {
        Id = id;
        Name = name;
        IsConfigured = isConfigured;
        OpensBrowser = opensBrowser;
        Hint = hint;
        AutomationName = automationName;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>False when this build has no sign-in set up for the kind (it shows disabled with <see cref="Hint"/> as the reason).</summary>
    public bool IsConfigured { get; }

    public bool OpensBrowser { get; }

    /// <summary>"Opens your browser", "Asks for your details" or the reason the kind is unavailable.</summary>
    public string Hint { get; }

    public string AutomationName { get; }

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
