namespace CrowdSecManager.ViewModels;

public interface IUiDispatcherAware
{
    void SetDispatcher(Func<Func<Task>, Task> dispatcher);
}
