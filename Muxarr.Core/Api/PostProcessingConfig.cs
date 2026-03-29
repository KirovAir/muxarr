namespace Muxarr.Core.Api;

public class PostProcessingConfig
{
    public bool Enabled { get; set; }
    public string Command { get; set; } = string.Empty;

    public string ResolveCommand(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var filename = Path.GetFileNameWithoutExtension(filePath);

        return Command
            .Replace("{{file}}", filePath)
            .Replace("{{filename}}", filename)
            .Replace("{{directory}}", directory);
    }
}
