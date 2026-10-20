using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Tests.Settings;

public class HotkeyStateTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 20, 9, 30, 15, TimeSpan.Zero);

    private static HotkeyBinding Binding(HotkeyModifiers modifiers, char key) => new(modifiers, key);

    // ---- the text form --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T', "Ctrl+Alt+T")]
    [InlineData(HotkeyModifiers.Windows | HotkeyModifiers.Shift, 'Z', "Shift+Win+Z")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Windows, '7', "Ctrl+Alt+Shift+Win+7")]
    public void The_text_form_lists_modifiers_in_a_fixed_order(HotkeyModifiers modifiers, char key, string expected)
    {
        Binding(modifiers, key).ToText().Should().Be(expected);
    }

    [Theory]
    [InlineData(0x20, "Ctrl+Space")]
    [InlineData(0x87, "Ctrl+F24")]
    [InlineData(0x70, "Ctrl+F1")]
    [InlineData(0x25, "Ctrl+Left")]
    [InlineData(0x2E, "Ctrl+Delete")]
    [InlineData(0x65, "Ctrl+Numpad5")]
    [InlineData(0xBA, "Ctrl+;")]
    [InlineData(0xC0, "Ctrl+`")]
    public void Named_keys_have_names_a_person_can_read(int virtualKey, string expected)
    {
        new HotkeyBinding(HotkeyModifiers.Control, virtualKey).ToText().Should().Be(expected);
    }

    [Fact]
    public void Every_letter_digit_function_and_named_key_survives_a_round_trip()
    {
        var keys = Enumerable.Range('A', 26).Concat(Enumerable.Range('0', 10)).Concat(Enumerable.Range(0x70, 24))
            .Concat([0x20, 0x0D, 0x09, 0x08, 0x1B, 0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28])
            .Concat(Enumerable.Range(0x60, 10))
            .Concat([0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE]);
        foreach (var key in keys)
        {
            var binding = new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, key);

            HotkeyBinding.TryParse(binding.ToText(), out var parsed).Should().BeTrue(binding.ToText());
            parsed.Should().Be(binding);
        }
    }

    [Fact]
    public void A_key_without_a_name_is_written_as_its_code_and_read_back()
    {
        var binding = new HotkeyBinding(HotkeyModifiers.Alt, 0xE9);

        binding.ToText().Should().Be("Alt+Key0xE9");
        HotkeyBinding.TryParse("Alt+Key0xE9", out var parsed).Should().BeTrue();
        parsed.Should().Be(binding);
    }

    [Theory]
    [InlineData("ctrl+alt+t", HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T')]
    [InlineData(" Control + Windows + F5 ", HotkeyModifiers.Control | HotkeyModifiers.Windows, 0x74)]
    [InlineData("WIN+shift+space", HotkeyModifiers.Windows | HotkeyModifiers.Shift, 0x20)]
    public void Reading_ignores_case_spaces_and_the_long_modifier_names(string text, HotkeyModifiers modifiers, int key)
    {
        HotkeyBinding.TryParse(text, out var parsed).Should().BeTrue();

        parsed.Should().Be(new HotkeyBinding(modifiers, key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("T")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+T")]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+T+Alt")]
    [InlineData("Banana+T")]
    [InlineData("Ctrl+Key0xZZ")]
    [InlineData("Ctrl+Key0x1FFFF")]
    public void Text_that_is_not_a_shortcut_is_refused(string? text)
    {
        HotkeyBinding.TryParse(text, out var parsed).Should().BeFalse();

        parsed.Should().Be(default(HotkeyBinding));
    }

    // ---- validity -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_key_alone_has_no_modifier()
    {
        new HotkeyBinding(HotkeyModifiers.None, 'T').Validate().Should().Be(HotkeyReason.NoModifier);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    [InlineData(0xA0)]
    [InlineData(0xA5)]
    public void A_modifier_key_or_no_key_is_not_a_shortcut_key(int virtualKey)
    {
        new HotkeyBinding(HotkeyModifiers.Control, virtualKey).Validate().Should().Be(HotkeyReason.NoKey);
    }

    [Fact]
    public void A_modifier_with_a_key_is_valid()
    {
        new HotkeyBinding(HotkeyModifiers.Control, 'T').Validate().Should().Be(HotkeyReason.None);
    }

    // ---- the persisted state --------------------------------------------------------------------------------------

    [Fact]
    public void By_default_there_is_no_shortcut_as_on_the_Mac()
    {
        HotkeyState.None.Binding.Should().BeNull();
        HotkeyState.None.LastResult.Should().Be(HotkeyStatus.Cleared);
        HotkeyState.None.At.Should().BeNull();
        HotkeyState.None.BindingIsActive.Should().BeFalse();
        HotkeyState.None.HasConflict.Should().BeFalse();
    }

    [Fact]
    public void A_registered_shortcut_survives_storage()
    {
        var state = new HotkeyState(Binding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T'), HotkeyStatus.Registered, At);

        HotkeyState.FromStorage(state.ToStorage()).Should().Be(state);
        state.BindingIsActive.Should().BeTrue();
    }

    [Fact]
    public void A_refused_shortcut_is_remembered_next_to_the_one_still_in_force()
    {
        var state = new HotkeyState(
            Binding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T'), HotkeyStatus.Conflict, At, Binding(HotkeyModifiers.Windows, 'Q'));

        var restored = HotkeyState.FromStorage(state.ToStorage());

        restored.Should().Be(state);
        restored.HasConflict.Should().BeTrue();
        restored.BindingIsActive.Should().BeTrue("the earlier shortcut is still registered; the refused one is another");
    }

    [Fact]
    public void A_stored_shortcut_that_could_not_be_registered_is_not_active()
    {
        var binding = Binding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T');
        var state = new HotkeyState(binding, HotkeyStatus.Conflict, At, binding);

        var restored = HotkeyState.FromStorage(state.ToStorage());

        restored.HasConflict.Should().BeTrue();
        restored.BindingIsActive.Should().BeFalse();
    }

    [Fact]
    public void A_cleared_shortcut_survives_storage()
    {
        var state = new HotkeyState(null, HotkeyStatus.Cleared, At);

        HotkeyState.FromStorage(state.ToStorage()).Should().Be(state);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"binding\":42}")]
    [InlineData("{\"binding\":\"Ctrl+Nonsense\",\"lastResult\":\"Registered\"}")]
    [InlineData("{\"binding\":\"T\",\"lastResult\":\"Registered\"}")]
    [InlineData("{\"binding\":\"Alt\",\"lastResult\":\"Registered\"}")]
    public void Damaged_storage_reads_as_no_shortcut(string? stored)
    {
        HotkeyState.FromStorage(stored).Should().Be(HotkeyState.None);
    }

    [Fact]
    public void A_stored_binding_without_a_modifier_is_dropped_not_registered()
    {
        // A hand-edited value must not make the app grab a bare key.
        HotkeyState.FromStorage("{\"binding\":\"F5\",\"lastResult\":\"Registered\"}").Should().Be(HotkeyState.None);
    }

    [Fact]
    public void Fields_a_later_version_adds_are_ignored_and_an_unknown_result_reads_as_cleared()
    {
        var restored = HotkeyState.FromStorage("{\"binding\":\"Ctrl+Alt+T\",\"lastResult\":\"Sparkling\",\"extra\":{\"a\":1},\"at\":\"2026-10-20T09:30:15+00:00\"}");

        restored.Binding.Should().Be(Binding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T'));
        restored.LastResult.Should().Be(HotkeyStatus.Cleared);
        restored.At.Should().Be(At);
    }

    [Fact]
    public void The_stored_text_holds_the_readable_shortcut()
    {
        new HotkeyState(Binding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T'), HotkeyStatus.Registered, At)
            .ToStorage().Should().Contain("Ctrl+Alt+T");
    }
}
