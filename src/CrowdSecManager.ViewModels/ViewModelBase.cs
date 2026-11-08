using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrowdSecManager.ViewModels;

public interface IAfterRenderViewModel
{
    Task OnAfterRenderAsync(bool firstRender);
}

public interface IUiDispatcherAware
{
    void SetDispatcher(Func<Func<Task>, Task> dispatcher);
}

/// <summary>Provides observable property helpers for view models.</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual Task InitializeAsync() => Task.CompletedTask;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaiseComputedPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
