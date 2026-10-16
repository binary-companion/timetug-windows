using TimeTug.Platform.Instance;

namespace TimeTug.Platform.Tests.Instance;

public class SingleInstanceGuardTests
{
    private static string UniqueName() => @"Local\TimeTug.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_first_launch_gets_the_guard_and_a_second_one_does_not()
    {
        var name = UniqueName();
        using var first = SingleInstanceGuard.TryAcquire(name);

        first.Should().NotBeNull();
        SingleInstanceGuard.TryAcquire(name).Should().BeNull("the data belongs to the first instance");
    }

    [Fact]
    public void Releasing_the_guard_lets_the_next_launch_take_it()
    {
        var name = UniqueName();
        SingleInstanceGuard.TryAcquire(name)!.Dispose();

        using var again = SingleInstanceGuard.TryAcquire(name);

        again.Should().NotBeNull();
    }

    [Fact]
    public void Different_names_do_not_block_each_other()
    {
        using var one = SingleInstanceGuard.TryAcquire(UniqueName());
        using var two = SingleInstanceGuard.TryAcquire(UniqueName());

        one.Should().NotBeNull();
        two.Should().NotBeNull();
    }

    [Fact]
    public void Disposing_from_another_thread_closes_the_handle_without_throwing()
    {
        var name = UniqueName();
        var guard = SingleInstanceGuard.TryAcquire(name)!;

        var dispose = () => Task.Run(guard.Dispose).GetAwaiter().GetResult();

        dispose.Should().NotThrow();
    }

    [Fact]
    public void The_name_is_per_session_and_safe_for_any_identity()
    {
        SingleInstanceGuard.NameFor("BinaryCompanion.TimeTug.Dev_8wekyb3d8bbwe").Should().Be(@"Local\TimeTug.SingleInstance.BinaryCompanion.TimeTug.Dev_8wekyb3d8bbwe");
        SingleInstanceGuard.NameFor(@"a b\c/d").Should().Be(@"Local\TimeTug.SingleInstance.a_b_c_d");
    }

    [Fact]
    public void An_empty_identity_is_refused()
    {
        var name = () => SingleInstanceGuard.NameFor(" ");

        name.Should().Throw<ArgumentException>();
    }
}
