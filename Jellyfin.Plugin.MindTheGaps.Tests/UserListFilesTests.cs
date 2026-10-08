using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A per-user list that cannot be read or written is never mistaken for an empty one: that would let the next
// change write a near-empty list over the real one. Driven through TodoStore, whose files these are, with the
// failures made real (a lock, a read-only folder, a file that does not parse) rather than simulated.
public class UserListFilesTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-userlist-" + Guid.NewGuid().ToString("N"));

    private static TodoStore Store(string dir) => new(NullLogger<TodoStore>.Instance, dir);

    private static string ListPath(string dir, Guid user) => Path.Combine(dir, "watchlists", user.ToString("N") + ".json");

    private static GapItem Gap(string id, string tmdbId)
        => new()
        {
            Id = id,
            Name = "Film " + tmdbId,
            Pattern = GapPattern.Recommendation,
            Domain = MediaDomain.Movies,
            TargetKind = BaseItemKind.Movie,
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdbId }
        };

    [Fact]
    public void AListThatCannotBeReadJustNow_RefusesAChange_AndIsReadAgainOnceItCanBe()
    {
        var dir = TempDir();
        try
        {
            Store(dir).Add(Owner, [Gap("kept", "1")]);
            var store = Store(dir);

            using (new FileStream(ListPath(dir, Owner), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Throws<UserListUnavailableException>(() => store.Add(Owner, [Gap("new", "2")]));

                // A read still answers, without remembering the empty answer.
                Assert.Empty(store.Load(Owner));
            }

            Assert.Equal(["kept"], store.Load(Owner).Select(e => e.Id));
            store.Add(Owner, [Gap("new", "2")]);
            Assert.Equal(["kept", "new"], Store(dir).Load(Owner).Select(e => e.Id).Order());
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void AListThatDoesNotParse_IsSetAsideRatherThanWrittenOver()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ListPath(dir, Owner))!);
            File.WriteAllText(ListPath(dir, Owner), "{ this is not json");

            Store(dir).Add(Owner, [Gap("new", "2")]);

            Assert.Equal(["new"], Store(dir).Load(Owner).Select(e => e.Id));
            var setAside = Directory.GetFiles(Path.GetDirectoryName(ListPath(dir, Owner))!, "*.unreadable-*");
            Assert.Equal("{ this is not json", File.ReadAllText(Assert.Single(setAside)));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void AChangeThatCannotBeSaved_FailsAndIsNotKept()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var dir = TempDir();
        var folder = Path.GetDirectoryName(ListPath(dir, Owner))!;
        try
        {
            var store = Store(dir);
            store.Add(Owner, [Gap("kept", "1")]);
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            Assert.Throws<UserListUnavailableException>(() => store.Add(Owner, [Gap("lost", "2")]));

            // The same instance does not go on showing a change that never reached the disk.
            Assert.Equal(["kept"], store.Load(Owner).Select(e => e.Id));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Delete(dir);
        }
    }

    [Fact]
    public void DeletingAUser_RemovesTheirSetAsideCopyAndAnUnfinishedWriteToo()
    {
        var dir = TempDir();
        try
        {
            var other = Guid.NewGuid();
            Store(dir).Add(Owner, [Gap("kept", "1")]);
            Store(dir).Add(other, [Gap("kept", "1")]);
            File.WriteAllText(ListPath(dir, Owner) + ".unreadable-20260101000000", "old");
            File.WriteAllText(ListPath(dir, Owner) + ".tmp", "half");

            Assert.True(Store(dir).Delete(Owner));

            Assert.Equal([Path.GetFileName(ListPath(dir, other))], Directory.GetFiles(Path.GetDirectoryName(ListPath(dir, Owner))!).Select(Path.GetFileName));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Pruning_ReachesAGoneUserWhoseOnlyFileIsASetAsideCopy()
    {
        var dir = TempDir();
        try
        {
            var gone = Guid.NewGuid();
            Store(dir).Add(Owner, [Gap("kept", "1")]);
            File.WriteAllText(ListPath(dir, gone) + ".unreadable-20260101000000", "old");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(ListPath(dir, Owner))!, "notes.txt"), "not a user's");

            Assert.Equal(1, Store(dir).Prune(id => id == Owner));

            Assert.Equal(
                new[] { "notes.txt", Path.GetFileName(ListPath(dir, Owner)) }.Order(),
                Directory.GetFiles(Path.GetDirectoryName(ListPath(dir, Owner))!).Select(Path.GetFileName).Order());
        }
        finally
        {
            Delete(dir);
        }
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
