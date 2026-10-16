using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;

namespace TimeTug.App.Views;

/// <summary>
/// The sign-in form for iCloud and Other (CalDAV), built from the engine's field descriptions. Field labels are looked up by
/// field key in the app's resources (<c>Field_&lt;key&gt;</c>) and fall back to the engine's English label.
/// </summary>
public sealed partial class CredentialFormDialog : ContentDialog
{
    private readonly List<(string Key, Control Input)> _inputs = [];

    public CredentialFormDialog(AccountFormRequest request, ILocalizer text)
    {
        InitializeComponent();
        Title = string.IsNullOrWhiteSpace(request.Title) ? text.GetString("CredentialDialogTitle") : request.Title;
        if (!string.IsNullOrWhiteSpace(request.Error))
        {
            ErrorBar.Message = request.Error;
            ErrorBar.IsOpen = true;
        }

        foreach (var field in request.Fields)
        {
            var label = text.TryGetString("Field_" + field.Key) ?? field.Label;
            request.Values.TryGetValue(field.Key, out var initial);
            Control input;
            if (field.IsSecret)
            {
                var box = new PasswordBox { Header = label, PasswordRevealMode = PasswordRevealMode.Peek };
                box.PasswordChanged += (_, _) => UpdatePrimaryButton();
                input = box;
            }
            else
            {
                var box = new TextBox { Header = label, Text = initial ?? "", IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
                box.TextChanged += (_, _) => UpdatePrimaryButton();
                input = box;
            }

            AutomationProperties.SetName(input, label);
            _inputs.Add((field.Key, input));
            FieldsPanel.Children.Add(input);
        }

        if (request.Help is { } help)
        {
            HelpText.Text = help.Text;
            HelpText.Visibility = Visibility.Visible;
            if (help.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            {
                HelpLink.Content = string.IsNullOrWhiteSpace(help.LinkTitle) ? url : help.LinkTitle;
                HelpLink.NavigateUri = uri;
                HelpLink.Visibility = Visibility.Visible;
                AutomationProperties.SetName(HelpLink, HelpLink.Content.ToString());
            }
        }

        Opened += (_, _) => FocusFirstEmpty();
        UpdatePrimaryButton();
    }

    /// <summary>What was entered, by field key. Read after <see cref="ContentDialog.ShowAsync()"/> returns Primary.</summary>
    public IReadOnlyDictionary<string, string> Values =>
        _inputs.ToDictionary(i => i.Key, i => i.Input is PasswordBox p ? p.Password : ((TextBox)i.Input).Text, StringComparer.Ordinal);

    private static bool IsBlank(Control input) =>
        string.IsNullOrWhiteSpace(input is PasswordBox p ? p.Password : ((TextBox)input).Text);

    // Sign in needs every field; spaces alone do not count.
    private void UpdatePrimaryButton() => IsPrimaryButtonEnabled = _inputs.TrueForAll(i => !IsBlank(i.Input));

    private void FocusFirstEmpty()
    {
        var inputs = _inputs.Select(i => i.Input).ToList();
        (inputs.Find(IsBlank) ?? inputs.FirstOrDefault())?.Focus(FocusState.Programmatic);
    }
}
