using TimeTug.Platform.Instance;
using TimeTug.Presentation.Startup;

namespace TimeTug.Platform.Tests.Instance;

public class DistributionGuardTests
{
    /// <summary>The machine's table of named markers, as a set of names.</summary>
    private sealed class FakeMarkers : IDistributionMarkers
    {
        public HashSet<string> Present { get; } = [];

        public List<string> Claims { get; } = [];

        public int Released { get; private set; }

        public IDisposable Claim(string name)
        {
            Claims.Add(name);
            Present.Add(name);
            return new Release(this, name);
        }

        public bool IsPresent(string name) => Present.Contains(name);

        private sealed class Release(FakeMarkers owner, string name) : IDisposable
        {
            public void Dispose()
            {
                owner.Present.Remove(name);
                owner.Released++;
            }
        }
    }

    [Fact]
    public void Every_distribution_has_its_own_marker_name()
    {
        Enum.GetValues<Flavour>().Select(DistributionGuard.MarkerName).Should().OnlyHaveUniqueItems();
        DistributionGuard.MarkerName(Flavour.Store).Should().StartWith(@"Local\").And.EndWith("store");
    }

    [Fact]
    public void Starting_alone_claims_the_own_marker_and_sees_no_other_distribution()
    {
        var markers = new FakeMarkers();

        using var guard = DistributionGuard.Enter(Flavour.Store, markers);

        markers.Claims.Should().Equal(DistributionGuard.MarkerName(Flavour.Store));
        guard.OthersRunning.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Flavour.Dev, Flavour.Store)]
    [InlineData(Flavour.Store, Flavour.Direct)]
    [InlineData(Flavour.Direct, Flavour.Dev)]
    public void Another_distributions_marker_is_reported(Flavour own, Flavour other)
    {
        var markers = new FakeMarkers();
        markers.Present.Add(DistributionGuard.MarkerName(other));

        using var guard = DistributionGuard.Enter(own, markers);

        guard.OthersRunning.Should().Equal(other);
    }

    [Fact]
    public void Every_other_running_distribution_is_reported_and_the_own_marker_is_not_a_collision()
    {
        var markers = new FakeMarkers();
        foreach (var flavour in Enum.GetValues<Flavour>())
        {
            markers.Present.Add(DistributionGuard.MarkerName(flavour));
        }

        using var guard = DistributionGuard.Enter(Flavour.Store, markers);

        guard.OthersRunning.Should().BeEquivalentTo([Flavour.Dev, Flavour.Direct]);
    }

    [Fact]
    public void The_marker_is_claimed_before_the_others_are_looked_at_so_two_starting_at_once_see_each_other()
    {
        var order = new List<string>();
        var markers = new OrderRecordingMarkers(order);

        using var guard = DistributionGuard.Enter(Flavour.Dev, markers);

        order.First().Should().StartWith("claim");
    }

    [Fact]
    public void Disposing_the_guard_releases_the_marker_so_the_next_start_is_clean()
    {
        var markers = new FakeMarkers();
        DistributionGuard.Enter(Flavour.Dev, markers).Dispose();

        markers.Released.Should().Be(1);
        using var next = DistributionGuard.Enter(Flavour.Store, markers);
        next.OthersRunning.Should().BeEmpty();
    }

    private sealed class OrderRecordingMarkers(List<string> order) : IDistributionMarkers
    {
        public IDisposable Claim(string name)
        {
            order.Add("claim " + name);
            return new NullDisposable();
        }

        public bool IsPresent(string name)
        {
            order.Add("look " + name);
            return false;
        }

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    [Theory]
    [InlineData(true, false, StartupDecision.Run)]
    [InlineData(true, true, StartupDecision.ShowCollisionAndExit)]
    [InlineData(false, false, StartupDecision.RedirectAndExit)]
    public void Startup_runs_redirects_or_shows_the_collision_notice(bool isPrimary, bool otherDistributionRunning, StartupDecision expected)
    {
        var markers = new FakeMarkers();
        if (otherDistributionRunning)
        {
            markers.Present.Add(DistributionGuard.MarkerName(Flavour.Direct));
        }

        using var guard = isPrimary ? DistributionGuard.Enter(Flavour.Dev, markers) : null;

        StartupArbiter.Decide(isPrimary, guard).Should().Be(expected);
    }

    [Fact]
    public void A_second_launch_of_the_same_distribution_is_redirected_even_when_another_distribution_runs()
    {
        // The existing behaviour: the first copy of THIS distribution handles it; the other distribution is not this launch's business.
        StartupArbiter.Decide(isPrimary: false, guard: null).Should().Be(StartupDecision.RedirectAndExit);
    }

    [Fact]
    public void The_real_named_marker_is_visible_to_a_second_handle_and_gone_after_release()
    {
        var markers = new NamedMutexMarkers();
        var name = @"Local\TimeTug.Tests.Marker." + Guid.NewGuid().ToString("N");

        markers.IsPresent(name).Should().BeFalse();
        var claim = markers.Claim(name);
        markers.IsPresent(name).Should().BeTrue("another process opening the same name finds it");
        claim.Dispose();
        markers.IsPresent(name).Should().BeFalse();
    }
}
