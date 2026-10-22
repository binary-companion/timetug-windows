using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Tests.Engine;

public sealed class ValueListTests
{
    private sealed record Holder(ValueList<string> Items);

    [Fact]
    public void Lists_with_the_same_items_are_equal_and_hash_alike()
    {
        ValueList<string> a = ["x", "y"];
        var b = new ValueList<string>(new List<string> { "x", "y" });

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        new Holder(a).Should().Be(new Holder(b), "a record holding the list compares by the items");
    }

    [Fact]
    public void Lists_with_other_items_or_a_different_order_are_not_equal()
    {
        ValueList<string> a = ["x", "y"];

        a.Equals(["y", "x"]).Should().BeFalse();
        a.Equals(["x"]).Should().BeFalse();
        (a != ["x", "z"]).Should().BeTrue();
    }

    [Fact]
    public void The_default_list_is_empty_and_equals_an_empty_list()
    {
        ValueList<int> none = default;

        none.Count.Should().Be(0);
        none.Should().BeEmpty();
        none.Equals([]).Should().BeTrue();
        none.GetHashCode().Should().Be(new ValueList<int>([]).GetHashCode());
    }

    [Fact]
    public void Items_are_read_by_index_and_in_order()
    {
        ValueList<string> list = ["a", "b", "c"];

        list.Count.Should().Be(3);
        list[1].Should().Be("b");
        list.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void A_list_is_a_copy_so_changing_the_source_does_not_change_it()
    {
        var source = new List<string> { "a" };
        var list = new ValueList<string>(source);
        source.Add("b");

        list.Should().Equal("a");
    }
}
