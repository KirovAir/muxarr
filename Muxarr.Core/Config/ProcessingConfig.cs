using System.Text;

namespace Muxarr.Core.Config;

public class ProcessingConfig
{
    public int ScanIntervalMinutes { get; set; }
    public int ConversionTimeoutMinutes { get; set; } = 60;
    public bool QueueScannedFiles { get; set; }

    public bool PostProcessingEnabled { get; set; }
    public string PostProcessingCommand { get; set; } = string.Empty;

    private static readonly (string Placeholder, string Variable)[] ShellVariables =
    [
        ("{{filename}}", "MUXARR_FILENAME"),
        ("{{directory}}", "MUXARR_DIRECTORY"),
        ("{{file}}", "MUXARR_FILE"),
        ("{filename}", "MUXARR_FILENAME"),
        ("{directory}", "MUXARR_DIRECTORY"),
        ("{file}", "MUXARR_FILE")
    ];

    public static Dictionary<string, string> ShellEnvironment(string filePath)
    {
        return new Dictionary<string, string>
        {
            ["MUXARR_FILE"] = filePath,
            ["MUXARR_FILENAME"] = Path.GetFileNameWithoutExtension(filePath),
            ["MUXARR_DIRECTORY"] = Path.GetDirectoryName(filePath) ?? string.Empty
        };
    }

    // Placeholders become quoted references to ShellEnvironment's variables, so a file
    // name like "$(touch x).mkv" stays data instead of running as part of the command.
    public string BuildShellScript()
    {
        var command = PostProcessingCommand;
        var script = new StringBuilder(command.Length + 32);
        var quote = '\0';

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (quote != '\'' && c == '\\' && i + 1 < command.Length)
            {
                script.Append(c).Append(command[++i]);
                continue;
            }

            var match = ShellVariables.FirstOrDefault(v =>
                string.CompareOrdinal(command, i, v.Placeholder, 0, v.Placeholder.Length) == 0);
            if (match.Placeholder != null)
            {
                script.Append(quote switch
                {
                    '"' => $"${{{match.Variable}}}",
                    '\'' => $"'\"${{{match.Variable}}}\"'",
                    _ => $"\"${{{match.Variable}}}\""
                });
                i += match.Placeholder.Length - 1;
                continue;
            }

            if (c is '"' or '\'' && (quote == '\0' || quote == c))
            {
                quote = quote == c ? '\0' : c;
            }

            script.Append(c);
        }

        return script.ToString();
    }

    public string ResolveCommand(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var filename = Path.GetFileNameWithoutExtension(filePath);

        return PostProcessingCommand
            .Replace("{{file}}", filePath)
            .Replace("{file}", filePath)
            .Replace("{{filename}}", filename)
            .Replace("{filename}", filename)
            .Replace("{{directory}}", directory)
            .Replace("{directory}", directory);
    }
}
