using WinCare.Application.Storage;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class StorageExplorerTests
{
    [Fact]
    public void Browse_lists_actual_children_without_guessing_folder_sizes()
    {
        string root = Path.Combine(Path.GetTempPath(), "wincare-browse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Subfolder"));
            File.WriteAllBytes(Path.Combine(root, "photo.bin"), [1, 2, 3, 4]);
            StorageBrowserListing listing = StorageExplorer.Browse(root);
            Assert.Equal(2, listing.Items.Count);
            Assert.True(listing.Items[0].IsFolder);
            Assert.Null(listing.Items[0].Bytes);
            Assert.Equal("—", listing.Items[0].SizeLabel);
            Assert.Equal(4L, listing.Items[1].Bytes);
            Assert.Equal("4 B", listing.Items[1].SizeLabel);
            Assert.False(listing.Truncated);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Browse_sets_truncation_flag_without_reading_entire_directory()
    {
        string root = Path.Combine(Path.GetTempPath(), "wincare-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "b.txt"), "b");
            StorageBrowserListing listing = StorageExplorer.Browse(root, maxEntries: 1);
            Assert.Single(listing.Items);
            Assert.True(listing.Truncated);
            Assert.Throws<ArgumentOutOfRangeException>(() => StorageExplorer.Browse(root, 0));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Browse_rejects_missing_paths_without_changing_disk()
    {
        string missing = Path.Combine(Path.GetTempPath(), "wincare-not-here-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() => StorageExplorer.Browse(missing));
    }
}
