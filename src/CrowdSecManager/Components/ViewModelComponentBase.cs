using CrowdSecManager.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace CrowdSecManager.Components;

public abstract class ViewModelComponentBase<TViewModel> : ComponentBase, IAsyncDisposable
    where TViewModel : ViewModelBase
{
    [Inject]
    protected TViewModel Vm { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Vm.PropertyChanged += OnViewModelChanged;
        if (Vm is IUiDispatcherAware dispatcherAware)
            dispatcherAware.SetDispatcher(action => InvokeAsync(action));
        await Vm.InitializeAsync();
        await base.OnInitializedAsync();
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
        => Vm is IAfterRenderViewModel lifecycle ? lifecycle.OnAfterRenderAsync(firstRender) : Task.CompletedTask;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        => _ = InvokeAsync(StateHasChanged);

    public async ValueTask DisposeAsync()
    {
        Vm.PropertyChanged -= OnViewModelChanged;
        if (Vm is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (Vm is IDisposable disposable)
            disposable.Dispose();
    }
}
