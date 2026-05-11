using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AvaloniaApplication_ConfigLauncher;

/// <summary>
/// Manages the WebAPI process lifecycle (start, wait-until-ready, graceful shutdown).
/// Issue 03 Part B.
/// </summary>
public class WebApiProcessManager : IWebApiProcessManager
{
    private readonly string _webApiDirectory;
    private readonly HttpClient _httpClient;
    private Process? _process;

    /// <summary>
    /// Issue 04 Part E: True when the WebAPI process is running (not null and not exited).
    /// </summary>
    public bool IsProcessRunning => _process != null && !_process.HasExited;

    public WebApiProcessManager(string webApiDirectory)
    {
        _webApiDirectory = webApiDirectory;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>
    /// Internal constructor for testing with a provided HttpClient.
    /// </summary>
    internal WebApiProcessManager(string webApiDirectory, HttpClient httpClient)
    {
        _webApiDirectory = webApiDirectory;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Starts the WebAPI.exe process. Returns the Process reference.
    /// Throws InvalidOperationException if WebAPI.exe is not found.
    /// </summary>
    public Process Start()
    {
        var exePath = Path.Combine(_webApiDirectory, "WebAPI.exe");
        if (!File.Exists(exePath))
        {
            throw new InvalidOperationException(
                $"未找到 WebAPI.exe，请确保启动器与 WebAPI 位于同一目录。\n预期路径: {exePath}");
        }

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _webApiDirectory,
                UseShellExecute = false
            }
        };

        _process.Start();
        return _process;
    }

    /// <summary>
    /// Polls GET {baseUrl}/ every 1 second until 200 OK or timeoutSec elapses.
    /// Returns true if the server becomes ready, false on timeout.
    /// </summary>
    public async Task<bool> WaitUntilReadyAsync(string baseUrl, int timeoutSec = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await _httpClient.GetAsync(baseUrl + "/");
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // Server not reachable yet, wait and retry
            }

            await Task.Delay(1000);
        }

        return false;
    }

    /// <summary>
    /// Gracefully stops the WebAPI via POST /api/system/shutdown, then waits for
    /// the process to exit. Falls back to Process.Kill() if HTTP is unreachable
    /// or the process does not exit within timeoutSec.
    /// Returns true if the process exited normally, false if Kill() was needed.
    /// </summary>
    public async Task<bool> StopAsync(string baseUrl, int timeoutSec = 10)
    {
        bool httpReachable;
        try
        {
            var response = await _httpClient.PostAsync(
                baseUrl + "/api/system/shutdown", null);
            httpReachable = response.IsSuccessStatusCode;
        }
        catch
        {
            httpReachable = false;
        }

        if (_process == null || _process.HasExited)
        {
            // No process to manage — if HTTP was reachable we consider it success
            return httpReachable;
        }

        if (!httpReachable)
        {
            // HTTP unreachable (server may have crashed) — kill directly
            try { _process.Kill(); } catch { /* process may already be dead */ }
            return false;
        }

        var exited = _process.WaitForExit(timeoutSec * 1000);
        if (exited)
        {
            return true;
        }

        // Timeout — force kill
        try { _process.Kill(); } catch { /* process may already be dead */ }
        return false;
    }
}
