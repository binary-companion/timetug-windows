using TimeTug.Platform.Credentials;

namespace TimeTug.Platform.Tests.Credentials;

/// <summary>An in-memory vault that records what the store asked it to hold.</summary>
internal sealed class FakePasswordVault : IPasswordVault
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Resource, string UserName), string> _items = [];

    public Exception? FailWith { get; set; }

    public int WriteCount { get; private set; }

    public IReadOnlyDictionary<(string Resource, string UserName), string> Items
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<(string, string), string>(_items);
            }
        }
    }

    public string? Retrieve(string resource, string userName)
    {
        Throw();
        lock (_gate)
        {
            return _items.GetValueOrDefault((resource, userName));
        }
    }

    public void Add(string resource, string userName, string password)
    {
        Throw();
        lock (_gate)
        {
            _items[(resource, userName)] = password;
            WriteCount++;
        }
    }

    public void Remove(string resource, string userName)
    {
        Throw();
        lock (_gate)
        {
            _items.Remove((resource, userName));
        }
    }

    private void Throw()
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }
    }
}
