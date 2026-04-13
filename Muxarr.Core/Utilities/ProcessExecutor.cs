using System.Diagnostics;
using System.Text;

namespace Muxarr.Core.Utilities;

public class ProcessExecutor
{
    public static async Task<ProcessJsonResult<T>> ExecuteJsonProcessAsync<T>(string fileName, string? arguments = null,
        TimeSpan? timeout = null, Action<string, bool>? onOutputLine = null)
    {
        var result = await ExecuteProcessAsync(fileName, arguments, timeout, onOutputLine);
        var json = new ProcessJsonResult<T>(result);

        if (!result.Success || string.IsNullOrEmpty(result.Output))
        {
            return json;
        }

        try
        {
            var res = JsonHelper.Deserialize<T>(result.Output);
            json.Result = res;
        }
        catch (Exception e)
        {
            result.Error = e.ToString();
        }

        return json;
    }

    public static async Task<ProcessResult> ExecuteProcessAsync(string fileName,
        string? arguments = null,
        TimeSpan? timeout = null,
        Action<string, bool>? onOutputLine = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        const string compatLocale = "C.UTF-8";
        process.StartInfo.EnvironmentVariables["LANG"] = compatLocale;
        process.StartInfo.EnvironmentVariables["LC_ALL"] = compatLocale;

        timeout ??= TimeSpan.FromSeconds(30);

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            outputBuilder.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data, false);
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            errorBuilder.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data, true);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource();

        var timeoutTask = Task.Delay(timeout.Value, cts.Token);
        var processTask = process.WaitForExitAsync(cts.Token);

        var completedTask = await Task.WhenAny(processTask, timeoutTask);
        var timedOut = completedTask == timeoutTask;

        if (timedOut)
        {
            process.Kill(true);
        }

        await process.WaitForExitAsync(cts.Token);

        await cts.CancelAsync(); // Cancel possible leftover timeoutTask.

        var exitCode = timedOut ? -1 : process.ExitCode;
        return new ProcessResult
        {
            Output = outputBuilder.ToString(),
            Error = errorBuilder.ToString(),
            TimedOut = timedOut,
            ExitCode = exitCode
        };
    }
}

public class ProcessResult
{
    public string? Output { get; set; }
    public string? Error { get; set; }
    public bool TimedOut { get; set; }
    public int ExitCode { get; set; }

    public bool Success => ExitCode == 0;
}

public class ProcessJsonResult<T> : ProcessResult
{
    public ProcessJsonResult(ProcessResult result)
    {
        Output = result.Output;
        Error = result.Error;
        TimedOut = result.TimedOut;
        ExitCode = result.ExitCode;
    }

    public T? Result { get; set; }
}
