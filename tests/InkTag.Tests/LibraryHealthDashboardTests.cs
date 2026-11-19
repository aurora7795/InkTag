using System;
using System.IO;
using System.Linq;
using InkTag.Core;
using InkTag.Core.Backup;
using InkTag.Gui.ViewModels;
using Xunit;

namespace InkTag.Tests;

[Collection("ProcessStateTests")]
public class LibraryHealthDashboardTests : IDisposable
{
    private readonly string _testBackupDir;

    public LibraryHealthDashboardTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), "InkTagDashboardTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBackupDir);
        MetadataBackupService.SetGlobalCustomBackupDirectory(_testBackupDir);
    }

    public void Dispose()
    {
        MetadataBackupService.SetGlobalCustomBackupDirectory(null);
        if (Directory.Exists(_testBackupDir))
        {
            try { Directory.Delete(_testBackupDir, true); } catch { }
        }
    }

    private ComicItemViewModel CreateTestComic(string title, string series, string publisher, bool isUntagged = false, bool hasError = false)
    {
        var model = new ComicInfo
        {
            Title = isUntagged ? null : title,
            Series = isUntagged ? null : series,
            Publisher = publisher
        };
        var vm = new ComicItemViewModel(
            filePath: $"/comics/{series}/{title}.cbz",
            model: model,
            hasEmbeddedXml: !isUntagged);

        if (hasError)
        {
            vm.HasReadError = true;
            vm.ReadErrorMessage = "Simulated corrupt archive header";
        }

        return vm;
    }

    [Fact]
    public void ViewModes_SwitchBetweenOverviewAndSpreadsheet()
    {
        var vm = new MainWindowViewModel();

        // Default should be Overview
        Assert.Equal(MainWindowViewMode.Overview, vm.ActiveViewMode);
        Assert.True(vm.IsOverviewView);
        Assert.False(vm.IsSpreadsheetView);

        // Switch to Spreadsheet
        vm.ShowSpreadsheetViewCommand.Execute(null);
        Assert.Equal(MainWindowViewMode.Spreadsheet, vm.ActiveViewMode);
        Assert.False(vm.IsOverviewView);
        Assert.True(vm.IsSpreadsheetView);

        // Switch back to Overview
        vm.ShowOverviewViewCommand.Execute(null);
        Assert.Equal(MainWindowViewMode.Overview, vm.ActiveViewMode);
        Assert.True(vm.IsOverviewView);
        Assert.False(vm.IsSpreadsheetView);
    }

    [Fact]
    public void NavRail_TogglesVisibility()
    {
        var vm = new MainWindowViewModel();

        Assert.True(vm.IsRailVisible);

        vm.ToggleRailCommand.Execute(null);
        Assert.False(vm.IsRailVisible);

        vm.ToggleRailCommand.Execute(null);
        Assert.True(vm.IsRailVisible);
    }

    [Fact]
    public void HealthMetrics_CalculatesTaggedUntaggedModifiedAndErrorsAccurately()
    {
        var vm = new MainWindowViewModel();

        var c1 = CreateTestComic("Issue 1", "Sandman", "DC/Vertigo"); // Tagged
        var c2 = CreateTestComic("Issue 2", "Sandman", "DC/Vertigo"); // Tagged
        var c3 = CreateTestComic("Unknown", "Unknown", "Unknown", isUntagged: true); // Untagged
        var c4 = CreateTestComic("Corrupt", "Hellblazer", "DC/Vertigo", hasError: true); // Error

        vm.Comics.Add(c1);
        vm.Comics.Add(c2);
        vm.Comics.Add(c3);
        vm.Comics.Add(c4);

        // Simulate an unsaved edit on c1
        c1.Title = "Issue 1 Special Edition";
        c1.IsDirty = true;

        vm.UpdateCounts();

        Assert.Equal(4, vm.Comics.Count);
        Assert.Equal(2, vm.TaggedCount);     // c1 and c2 are tagged
        Assert.Equal(1, vm.UntaggedCount);   // c3
        Assert.Equal(1, vm.ModifiedCount);   // c1 is dirty
        Assert.Equal(1, vm.ReadErrorCount);  // c4 has error
        Assert.Equal(50, vm.TaggedPercentage); // 2 of 4 = 50%

        Assert.Equal("All (4)", vm.AllFilterLabel);
        Assert.Equal("Untagged (1)", vm.UntaggedFilterLabel);
        Assert.Equal("Modified (1)", vm.ModifiedFilterLabel);
        Assert.Equal("Errors (1)", vm.ErrorsFilterLabel);
    }

    [Fact]
    public void NavRail_PopulatesPublisherAndSeriesGroups()
    {
        var vm = new MainWindowViewModel();

        vm.Comics.Add(CreateTestComic("Issue 1", "Spawn", "Image"));
        vm.Comics.Add(CreateTestComic("Issue 2", "Spawn", "Image"));
        vm.Comics.Add(CreateTestComic("Issue 1", "Savage Dragon", "Image"));
        vm.Comics.Add(CreateTestComic("Issue 1", "Batman", "DC Comics"));

        Assert.Equal(2, vm.PublisherGroups.Count);
        var dcGroup = vm.PublisherGroups.FirstOrDefault(p => p.Name == "DC Comics");
        var imageGroup = vm.PublisherGroups.FirstOrDefault(p => p.Name == "Image");

        Assert.NotNull(dcGroup);
        Assert.Equal(1, dcGroup.Count);

        Assert.NotNull(imageGroup);
        Assert.Equal(3, imageGroup.Count);

        Assert.Equal(3, vm.SeriesGroups.Count);
        var spawnGroup = vm.SeriesGroups.FirstOrDefault(s => s.Name == "Spawn");
        Assert.NotNull(spawnGroup);
        Assert.Equal(2, spawnGroup.Count);
    }

    [Fact]
    public void NavRail_FilterBySeriesAndPublisher_AppliesFilterAndBreadcrumbs()
    {
        var vm = new MainWindowViewModel();

        vm.Comics.Add(CreateTestComic("Issue 1", "Spawn", "Image"));
        vm.Comics.Add(CreateTestComic("Issue 2", "Spawn", "Image"));
        vm.Comics.Add(CreateTestComic("Issue 1", "Batman", "DC Comics"));

        Assert.Equal(3, vm.DisplayedComics.Count);
        Assert.False(vm.HasActiveRailFilter);

        // Filter by Series "Spawn"
        vm.FilterBySeriesCommand.Execute("Spawn");

        Assert.Equal("Spawn", vm.SelectedSeriesFilter);
        Assert.Null(vm.SelectedPublisherFilter);
        Assert.True(vm.HasActiveRailFilter);
        Assert.Equal("Series: Spawn", vm.ActiveRailFilterDescription);
        Assert.Equal(2, vm.DisplayedComics.Count);
        Assert.True(vm.IsSpreadsheetView); // Automatically switches to grid view

        // Clear Rail Filter
        vm.ClearRailFilterCommand.Execute(null);

        Assert.Null(vm.SelectedSeriesFilter);
        Assert.False(vm.HasActiveRailFilter);
        Assert.Equal(3, vm.DisplayedComics.Count);

        // Filter by Publisher "DC Comics"
        vm.FilterByPublisherCommand.Execute("DC Comics");

        Assert.Equal("DC Comics", vm.SelectedPublisherFilter);
        Assert.Null(vm.SelectedSeriesFilter);
        Assert.True(vm.HasActiveRailFilter);
        Assert.Equal("Publisher: DC Comics", vm.ActiveRailFilterDescription);
        Assert.Single(vm.DisplayedComics);
    }

    [Fact]
    public void NeedsAttentionItems_GathersIssuesAndSupportsInspect()
    {
        var vm = new MainWindowViewModel();

        var normal = CreateTestComic("Issue 1", "Spawn", "Image");
        var untagged = CreateTestComic("Unknown", "Unknown", "Unknown", isUntagged: true);
        var corrupt = CreateTestComic("Damaged", "Batman", "DC Comics", hasError: true);

        vm.Comics.Add(normal);
        vm.Comics.Add(untagged);
        vm.Comics.Add(corrupt);

        Assert.Equal(2, vm.NeedsAttentionTotalCount);
        Assert.Equal(2, vm.NeedsAttentionItems.Count);

        // Verify status badge text
        Assert.Equal("TAGGED", normal.StatusBadgeText);
        Assert.Equal("UNTAGGED", untagged.StatusBadgeText);
        Assert.Equal("ERROR", corrupt.StatusBadgeText);

        // Test InspectComic
        vm.InspectComicCommand.Execute(corrupt);

        Assert.Equal(corrupt, vm.ActiveComic);
        Assert.True(vm.IsSpreadsheetView);
        Assert.True(vm.IsInspectorVisible);

        // Test ViewAllNeedsAttention
        vm.ViewAllNeedsAttentionCommand.Execute(null);
        Assert.Equal(ComicFilterMode.Untagged, vm.FilterMode);
        Assert.True(vm.IsSpreadsheetView);
    }

    [Fact]
    public void LastBatchJob_PopulatesDetailsAndHandlesRollback()
    {
        var backupService = new MetadataBackupService(_testBackupDir);
        string batchId = "batch_test_last_job_123";

        // Create a fake backup entry under this batch ID
        backupService.CreateBackup(
            archivePath: "/comics/Test/Issue1.cbz",
            originalXml: "<ComicInfo><Title>Original</Title></ComicInfo>",
            operationType: "BulkAutoTag",
            batchJobId: batchId);

        var vm = new MainWindowViewModel();
        vm.RefreshLastBatchJob();

        Assert.True(vm.HasLastBatchJob);
        Assert.NotNull(vm.LastBatchJob);
        Assert.Equal(batchId, vm.LastBatchJob.BatchJobId);
        Assert.Equal(batchId, vm.LastBatchJobSummaryText);
        Assert.Contains("BulkAutoTag", vm.LastBatchJobDetailsText);
    }
}
