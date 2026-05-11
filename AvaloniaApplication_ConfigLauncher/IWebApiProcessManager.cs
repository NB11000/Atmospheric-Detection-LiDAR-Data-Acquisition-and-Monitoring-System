using System.Diagnostics;
using System.Threading.Tasks;

namespace AvaloniaApplication_ConfigLauncher;

/// <summary>
/// Interface for WebApiProcessManager to enable unit testing of ViewModels.
/// </summary>
public interface IWebApiProcessManager
{
    bool IsProcessRunning { get; }
    Process Start();
    Task<bool> WaitUntilReadyAsync(string baseUrl, int timeoutSec = 30);
    Task<bool> StopAsync(string baseUrl, int timeoutSec = 10);
}
