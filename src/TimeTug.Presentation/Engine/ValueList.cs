using System.Collections;
using System.Runtime.CompilerServices;

namespace TimeTug.Presentation.Engine;

/// <summary>
/// An immutable list that compares by its items, so a record that holds one is equal to another record with the same items. The flyout
/// uses record equality to tell that nothing it shows has changed; a plain list would compare by reference and redraw every minute.
/// The default value is an empty list.
/// </summary>
[CollectionBuilder(typeof(ValueList), nameof(ValueList.Create))]
public readonly struct ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[]? _items;

    public ValueList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
    }

    public int Count => _items?.Length ?? 0;

    public T this[int index] => (_items ?? [])[index];

    public bool Equals(ValueList<T> other) => (_items ?? []).AsSpan().SequenceEqual(other._items ?? [], EqualityComparer<T>.Default);

    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items ?? [])
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(ValueList<T> left, ValueList<T> right) => left.Equals(right);

    public static bool operator !=(ValueList<T> left, ValueList<T> right) => !left.Equals(right);
}

/// <summary>Collection-expression support for <see cref="ValueList{T}"/>.</summary>
public static class ValueList
{
    public static ValueList<T> Create<T>(ReadOnlySpan<T> items) => new(items.ToArray());
}
