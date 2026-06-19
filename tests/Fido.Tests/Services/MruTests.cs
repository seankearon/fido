using Fido.Models;

namespace Fido.Tests.Services;

/// <summary>The MRU list helper — pure, no UI or git.</summary>
public class MruTests
{
    [Test]
    public async Task Add_promotes_to_front_and_dedupes_case_insensitively()
    {
        var list = new List<string> { "beta", "alpha" };

        var changed = Mru.Add(list, "ALPHA");

        await Assert.That(changed).IsTrue();
        await Assert.That(list[0]).IsEqualTo("ALPHA");
        await Assert.That(list.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Re_adding_the_newest_entry_is_a_no_op()
    {
        var list = new List<string> { "alpha", "beta" };

        var changed = Mru.Add(list, "alpha");

        await Assert.That(changed).IsFalse();
    }

    [Test]
    public async Task Blank_values_are_ignored()
    {
        var list = new List<string> { "alpha" };

        await Assert.That(Mru.Add(list, "   ")).IsFalse();
        await Assert.That(Mru.Add(list, null)).IsFalse();
        await Assert.That(list.Count).IsEqualTo(1);
    }

    [Test]
    public async Task The_list_is_capped_at_the_maximum()
    {
        var list = new List<string>();
        for (var i = 0; i < Mru.MaxItems + 5; i++)
            Mru.Add(list, $"item-{i}");

        await Assert.That(list.Count).IsEqualTo(Mru.MaxItems);
        await Assert.That(list[0]).IsEqualTo($"item-{Mru.MaxItems + 4}");   // newest first
    }
}
