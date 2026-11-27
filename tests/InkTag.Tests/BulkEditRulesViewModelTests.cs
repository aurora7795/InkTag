using System.Linq;
using InkTag.Core;
using InkTag.Gui.ViewModels;
using Xunit;

namespace InkTag.Tests;

public class BulkEditRulesViewModelTests
{
    [Fact]
    public void ApplyBulkRulesCommand_AppliesRulesToAllDisplayedComics_WhenNoneSelected()
    {
        var vm = new MainWindowViewModel();
        var item1 = new ComicItemViewModel("/tmp/1.cbz", new ComicInfo { Title = "One", Series = "Batman" });
        var item2 = new ComicItemViewModel("/tmp/2.cbz", new ComicInfo { Title = "Two", Series = "Batman" });
        vm.Comics.Add(item1);
        vm.Comics.Add(item2);

        var rule = vm.BulkEditRules.First();
        rule.SelectedField = rule.AvailableFields.First(f => f.PropertyName == "Publisher");
        rule.SelectedOperation = BulkEditOperation.Set;
        rule.StringValue = "DC Comics";

        vm.ApplyBulkRulesCommand.Execute(null);

        Assert.Equal("DC Comics", item1.Publisher);
        Assert.Equal("DC Comics", item2.Publisher);
        Assert.True(item1.IsDirty);
        Assert.True(item2.IsDirty);
        Assert.Contains("2 items", vm.ProgressText);
    }

    [Fact]
    public void ApplyBulkRulesCommand_AppliesOnlyToSelectedComics_WhenSelectionExists()
    {
        var vm = new MainWindowViewModel();
        var item1 = new ComicItemViewModel("/tmp/1.cbz", new ComicInfo { Title = "One", Publisher = "Old" });
        var item2 = new ComicItemViewModel("/tmp/2.cbz", new ComicInfo { Title = "Two", Publisher = "Old" });
        vm.Comics.Add(item1);
        vm.Comics.Add(item2);
        vm.UpdateSelection(new[] { item1 });

        var rule = vm.BulkEditRules.First();
        rule.SelectedField = rule.AvailableFields.First(f => f.PropertyName == "Publisher");
        rule.SelectedOperation = BulkEditOperation.Set;
        rule.StringValue = "Image";

        vm.ApplyBulkRulesCommand.Execute(null);

        Assert.Equal("Image", item1.Publisher);
        Assert.Equal("Old", item2.Publisher);
        Assert.True(item1.IsDirty);
        Assert.False(item2.IsDirty);
    }
}
