namespace CrowdSecManager.ViewModels;

public interface IAfterRenderViewModel
{
    Task OnAfterRenderAsync(bool firstRender);
}