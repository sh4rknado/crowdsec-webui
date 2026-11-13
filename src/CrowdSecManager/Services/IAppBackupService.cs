namespace CrowdSecManager.Services;

public interface IAppBackupService
{
    Task WriteBackupAsync(Stream destination, CancellationToken cancellationToken = default);
    Task RestoreAsync(Stream archiveStream, CancellationToken cancellationToken = default);
}
