using TimeTug.Platform.Hotkey;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Settings;
using TimeTug.Platform.Tests.OsState;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace TimeTug.Platform.Tests.Hotkey;

/// <summary>The real <c>RegisterHotKey</c>, run only with <c>TIMETUG_OS_SMOKE=1</c> in a signed-in desktop session (see docs/development.md). The self-test scenario <c>hotkey</c> covers the same in the packaged app on every runner.</summary>
public class HotkeyRegistrarSmokeTests
{
    // Ctrl+Alt+Shift+F23: a chord no program is likely to hold.
    private static readonly HotkeyBinding Chord = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x86);

    [OsSmokeFact]
    public async Task Windows_accepts_a_free_shortcut_and_refuses_one_that_is_held()
    {
        using var firstWindow = new MessageWindow();
        using var secondWindow = new MessageWindow();
        using var first = new HotkeyRegistrar(firstWindow);
        using var second = new HotkeyRegistrar(secondWindow);

        (await first.RegisterAsync(Chord)).Should().BeTrue();
        (await second.RegisterAsync(Chord)).Should().BeFalse("another registration holds it");

        await first.UnregisterAsync();
        (await second.RegisterAsync(Chord)).Should().BeTrue("released shortcuts can be taken again");
        await second.UnregisterAsync();
    }

    [OsSmokeFact]
    public async Task A_hotkey_message_for_the_registration_raises_pressed_and_others_do_not()
    {
        using var window = new MessageWindow();
        using var registrar = new HotkeyRegistrar(window);
        var presses = 0;
        registrar.Pressed += () => Interlocked.Increment(ref presses);
        (await registrar.RegisterAsync(Chord)).Should().BeTrue();
        var handle = new HWND(((IWindowMessageSource)window).Handle);

        PInvoke.SendMessage(handle, WindowMessage.Hotkey, (WPARAM)0x1234, 0);
        PInvoke.SendMessage(handle, WindowMessage.Hotkey, (WPARAM)(nuint)HotkeyRegistrar.RegistrationId, 0);

        await Wait.UntilAsync(() => Volatile.Read(ref presses) == 1, "the press");
        presses.Should().Be(1);
    }

    [OsSmokeFact]
    public async Task Registering_twice_without_releasing_is_a_programming_error()
    {
        using var window = new MessageWindow();
        using var registrar = new HotkeyRegistrar(window);
        (await registrar.RegisterAsync(Chord)).Should().BeTrue();

        var again = () => registrar.RegisterAsync(new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x85));

        await again.Should().ThrowAsync<InvalidOperationException>();
    }

    [OsSmokeFact]
    public async Task Disposing_the_registrar_releases_the_shortcut()
    {
        using var firstWindow = new MessageWindow();
        using var secondWindow = new MessageWindow();
        var first = new HotkeyRegistrar(firstWindow);
        using var second = new HotkeyRegistrar(secondWindow);
        (await first.RegisterAsync(Chord)).Should().BeTrue();

        first.Dispose();

        (await second.RegisterAsync(Chord)).Should().BeTrue();
        await second.UnregisterAsync();
    }
}
