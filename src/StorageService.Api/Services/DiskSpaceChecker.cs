using StorageService.Api.Storage;

namespace StorageService.Api.Services;

public interface IDiskSpaceChecker
{
    bool HasEnoughFreeSpace(IKvDatabase database, out string message);
}

public sealed class DiskSpaceChecker : IDiskSpaceChecker
{
    private readonly double _thresholdGb;

    public DiskSpaceChecker(double thresholdGb)
    {
        _thresholdGb = thresholdGb;
    }

    public bool HasEnoughFreeSpace(IKvDatabase database, out string message)
    {
        message = "";
        if (_thresholdGb <= 0)
        {
            return true;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(database.Path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            var drive = new DriveInfo(root);
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            if (freeGb >= _thresholdGb)
            {
                return true;
            }

            message = $"disk free space is lower than threshold. freeGb={freeGb:F2}, thresholdGb={_thresholdGb:F2}";
            return false;
        }
        catch (Exception ex)
        {
            message = $"disk free space check failed: {ex.Message}";
            return false;
        }
    }
}
