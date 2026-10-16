using Windows.Security.Credentials;

namespace TimeTug.Platform.Credentials;

/// <summary>Windows Credential Locker (<see cref="PasswordVault"/>). It needs a user session; it is not available to services.</summary>
public sealed class WindowsPasswordVault : IPasswordVault
{
    private const int ElementNotFound = unchecked((int)0x80070490);
    private readonly PasswordVault _vault = new();

    public string? Retrieve(string resource, string userName)
    {
        try
        {
            var credential = _vault.Retrieve(resource, userName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            return null;
        }
    }

    public void Add(string resource, string userName, string password) =>
        _vault.Add(new PasswordCredential(resource, userName, password));

    public void Remove(string resource, string userName)
    {
        try
        {
            _vault.Remove(_vault.Retrieve(resource, userName));
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            // Already gone.
        }
    }
}
