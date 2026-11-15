using CrowdSecManager.Services;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class AcquisitionCreateViewModel(IConfigFileService config, ILogger<AcquisitionCreateViewModel> log) : ViewModelBase
{
    private readonly IConfigFileService _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<AcquisitionCreateViewModel> _log = log ?? throw new ArgumentNullException(nameof(log));

    public string FileName { get; set => SetProperty(ref field, value); } = "my-app.yaml";
    public string Content { get; set => SetProperty(ref field, value); } = "";
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }

    public async Task CreateAcquisitionAsync()
    {
        IsBusy = true;
        Message = null;
        HasError = false;
        try
        {
            if (string.IsNullOrWhiteSpace(FileName))
            {
                HasError = true;
                Message = "A file name is required.";
                return;
            }

            var normalized = FileName.Trim();
            if (!normalized.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) &&
                !normalized.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                normalized += ".yaml";
            }

            if (!normalized.StartsWith("acquis.d/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "acquis.d/" + normalized;
            }

            if (string.IsNullOrWhiteSpace(Content))
            {
                HasError = true;
                Message = "Acquisition content is required.";
                return;
            }

            var (valid, error) = _config.ValidateYaml(Content);
            if (!valid)
            {
                HasError = true;
                Message = error;
                return;
            }

            var (saved, saveError) = _config.Save(normalized, Content);
            if (!saved)
            {
                HasError = true;
                Message = saveError ?? "Could not create the acquisition file.";
                return;
            }

            FileName = normalized;
            Message = $"Acquisition file '{normalized}' created successfully. Reload CrowdSec to apply.";
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Could not create acquisition file {FileName}", FileName);
            HasError = true;
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Clear()
    {
        FileName = "my-app.yaml";
        Content = "";
        Message = null;
        HasError = false;
    }
}
