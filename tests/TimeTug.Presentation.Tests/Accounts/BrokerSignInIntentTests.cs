using TimeTug.Presentation.Accounts;

namespace TimeTug.Presentation.Tests.Accounts;

public class BrokerSignInIntentTests
{
    [Fact]
    public void Nothing_armed_means_the_windows_account()
    {
        new BrokerSignInIntent().Take().Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public void Take_returns_the_armed_intent_once_and_then_resets()
    {
        var intent = new BrokerSignInIntent();
        intent.Arm(BrokerIntent.PickAccount);

        intent.Take().Should().Be(BrokerIntent.PickAccount);
        intent.Take().Should().Be(BrokerIntent.WindowsAccount, "an intent never outlives the attempt that armed it");
    }

    [Fact]
    public void Arming_again_replaces_the_earlier_intent()
    {
        var intent = new BrokerSignInIntent();
        intent.Arm(BrokerIntent.PickAccount);
        intent.Arm(BrokerIntent.WindowsAccount);

        intent.Take().Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public async Task Concurrent_arm_and_take_never_lose_or_duplicate_an_intent()
    {
        var intent = new BrokerSignInIntent();
        var taken = 0;
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
        {
            intent.Arm(BrokerIntent.PickAccount);
            if (intent.Take() == BrokerIntent.PickAccount)
            {
                Interlocked.Increment(ref taken);
            }
        })));

        taken.Should().BeInRange(1, 200);
        intent.Take().Should().Be(BrokerIntent.WindowsAccount);
    }
}
