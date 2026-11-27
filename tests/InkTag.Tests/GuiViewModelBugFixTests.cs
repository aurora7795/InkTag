using System.Collections.Generic;
using System.Threading.Tasks;
using InkTag.Core;
using InkTag.Gui.ViewModels;
using Xunit;

namespace InkTag.Tests;

public class GuiViewModelBugFixTests
{
    [Fact]
    public async Task BulkScrapeQueueViewModel_ApplyMatchedAsync_WithNoEligibleItems_ResetsIsRunning()
    {
        var vm = new BulkScrapeQueueViewModel(System.Array.Empty<string>());
        Assert.False(vm.IsRunning);

        int saved = await vm.ApplyMatchedAsync();

        Assert.Equal(0, saved);
        Assert.False(vm.IsRunning);
        Assert.Equal("No eligible unsaved matched items selected.", vm.ProgressStatus);
    }

    [Fact]
    public void ComicItemViewModel_StatusBadgeText_RaisesPropertyChanged_WithoutDirtyingOnReadError()
    {
        var info = new ComicInfo { Series = "Batman", Title = "Issue 1" };
        var vm = new ComicItemViewModel("/tmp/test.cbz", info, hasEmbeddedXml: true);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsDirty = true;
        Assert.Contains(nameof(ComicItemViewModel.StatusBadgeText), changed);
        Assert.Equal("MODIFIED", vm.StatusBadgeText);

        changed.Clear();
        vm.IsDirty = false;
        vm.HasReadError = true;
        Assert.Contains(nameof(ComicItemViewModel.StatusBadgeText), changed);
        Assert.Equal("ERROR", vm.StatusBadgeText);
        Assert.False(vm.IsDirty);
    }
}
