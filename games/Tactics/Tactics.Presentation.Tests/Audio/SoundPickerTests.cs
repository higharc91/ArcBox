using Tactics.Presentation.Audio;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Audio;

/// <summary><see cref="SoundPicker"/>: a sound with several files rotates through them, the same way every time, and never twice running.</summary>
public sealed class SoundPickerTests
{
    private static readonly Lazy<SoundBank> Bank = new(() => SoundBank.Load(Pack.Root));

    [Fact]
    public void A_sound_never_plays_the_same_file_twice_running()
    {
        var picker = new SoundPicker(Bank.Value);

        foreach (var id in Bank.Value.Ids.Where(i => Bank.Value.Sound(i)!.Files.Length > 1))
        {
            var files = Enumerable.Range(0, 20).Select(_ => picker.Next(id)!).ToArray();

            Assert.All(files.Zip(files.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
        }
    }

    [Fact]
    public void A_sound_goes_through_all_its_files()
    {
        var picker = new SoundPicker(Bank.Value);
        var files = Bank.Value.Sound("blade_flesh")!.Files;

        Assert.Equal(files.Order(), Enumerable.Range(0, files.Length).Select(_ => picker.Next("blade_flesh")!).Order());
    }

    [Fact]
    public void Two_pickers_pick_alike()
    {
        var (one, two) = (new SoundPicker(Bank.Value), new SoundPicker(Bank.Value));

        foreach (var id in Enumerable.Repeat("step_grass", 7).Concat(Enumerable.Repeat("blade_flesh", 5)))
            Assert.Equal(one.Next(id), two.Next(id));
    }

    [Fact]
    public void One_sound_rotating_leaves_the_others_where_they_were()
    {
        var (quiet, busy) = (new SoundPicker(Bank.Value), new SoundPicker(Bank.Value));
        busy.Next("step_grass");

        Assert.Equal(quiet.Next("blade_flesh"), busy.Next("blade_flesh"));
    }

    [Fact]
    public void A_sound_with_one_file_just_plays_it()
    {
        var picker = new SoundPicker(Bank.Value);

        Assert.Equal(picker.Next("battle_start"), picker.Next("battle_start"));
    }

    [Fact]
    public void A_sound_the_pack_lacks_is_nothing() => Assert.Null(new SoundPicker(Bank.Value).Next("no_such_sound"));
}
