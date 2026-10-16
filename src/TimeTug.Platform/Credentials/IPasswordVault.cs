namespace TimeTug.Platform.Credentials;

/// <summary>The three operations the store needs from Windows Credential Locker, so the store can be tested.</summary>
public interface IPasswordVault
{
    /// <summary>The stored password, or null when there is no entry.</summary>
    string? Retrieve(string resource, string userName);

    /// <summary>Adds the entry, replacing one with the same resource and user name.</summary>
    void Add(string resource, string userName, string password);

    /// <summary>Removes the entry; no error when it does not exist.</summary>
    void Remove(string resource, string userName);
}
