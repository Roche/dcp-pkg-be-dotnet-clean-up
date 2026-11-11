using OpenDCP.Framework.Cleanup.Constants;

namespace OpenDCP.Framework.Cleanup.Interfaces
{
    public interface ICleanupHook
    {
        CleanupInterval Interval { get; }
        Task ExecuteCleanupAsync(CancellationToken cancellationToken);
    }
}
