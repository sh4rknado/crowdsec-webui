using CrowdSecManager.ViewModels;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class ViewModelPrimitiveTests
{
    private sealed class TestViewModel : ViewModelBase
    {
        public string Value
        {
            get;
            private set => SetProperty(ref field, value);
        } = "";

        public string Computed => $"value:{Value}";

        public void SetValue(string value) => Value = value;

        public void NotifyComputed() => RaiseComputedPropertyChanged(nameof(Computed));
    }

    [Fact]
    public void SetProperty_WhenValueChanges_RaisesPropertyChanged()
    {
        var viewModel = new TestViewModel();
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        viewModel.SetValue("updated");

        Assert.Equal([nameof(TestViewModel.Value)], notifications);
        Assert.Equal("updated", viewModel.Value);
    }

    [Fact]
    public void SetProperty_WhenValueIsUnchanged_DoesNotRaisePropertyChanged()
    {
        var viewModel = new TestViewModel();
        var notificationCount = 0;
        viewModel.PropertyChanged += (_, _) => notificationCount++;

        viewModel.SetValue("");

        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public void RaiseComputedPropertyChanged_RaisesNotificationForComputedProperty()
    {
        var viewModel = new TestViewModel();
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        viewModel.NotifyComputed();

        Assert.Equal([nameof(TestViewModel.Computed)], notifications);
    }

    [Theory]
    [InlineData("Cmd", "cmd", true)]
    [InlineData("Out", "out", false)]
    [InlineData("Err", "err", false)]
    [InlineData("Muted", "muted", false)]
    public void TerminalLineFactories_CreateExpectedPresentationLine(
        string factoryName, string expectedClass, bool expectedPrompt)
    {
        const string text = "sample";
        var line = factoryName switch
        {
            "Cmd" => TerminalLine.Cmd(text),
            "Out" => TerminalLine.Out(text),
            "Err" => TerminalLine.Err(text),
            "Muted" => TerminalLine.Muted(text),
            _ => throw new ArgumentOutOfRangeException(nameof(factoryName))
        };

        Assert.Equal(new TerminalLine(text, expectedClass, expectedPrompt), line);
    }
}
