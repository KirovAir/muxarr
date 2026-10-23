using Muxarr.Core.Config;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class PostProcessingTests
{
    [TestMethod]
    [DataRow("chmod 664 {file}", "chmod 664 \"${MUXARR_FILE}\"")]
    [DataRow("chmod 664 \"{file}\"", "chmod 664 \"${MUXARR_FILE}\"")]
    [DataRow("chmod 664 '{file}'", "chmod 664 ''\"${MUXARR_FILE}\"''")]
    [DataRow("echo {{filename}} in \"{{directory}}\"", "echo \"${MUXARR_FILENAME}\" in \"${MUXARR_DIRECTORY}\"")]
    [DataRow("echo \"say \\\"{file}\\\"\"", "echo \"say \\\"${MUXARR_FILE}\\\"\"")]
    [DataRow("echo $HOME", "echo $HOME")]
    public void BuildShellScript_ReplacesPlaceholdersWithQuotedVariables(string command, string expected)
    {
        var config = new ProcessingConfig { PostProcessingCommand = command };

        Assert.AreEqual(expected, config.BuildShellScript());
    }

    // The path used to be pasted into the command, so "$(...)" in a file name ran.
    [TestMethod]
    [DataRow("printf '%s' {file}")]
    [DataRow("printf '%s' \"{file}\"")]
    [DataRow("printf '%s' '{file}'")]
    public async Task Run_FileNameIsNeverExecuted(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Post-processing runs through /bin/sh.");
        }

        var dir = Path.Combine(Path.GetTempPath(), "muxarr-pp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var marker = Path.Combine(dir, "ran");
            var path = Path.Combine(dir, $"Movie $(touch {marker}) `touch {marker}` \"it's\".mkv");
            var config = new ProcessingConfig { PostProcessingCommand = command };

            var result = await ProcessExecutor.ExecuteProcessAsync("/bin/sh",
                argumentList: ["-c", config.BuildShellScript()],
                environment: ProcessingConfig.ShellEnvironment(path));

            Assert.AreEqual(0, result.ExitCode, result.Error);
            Assert.AreEqual(path, result.Output?.TrimEnd('\n'));
            Assert.IsFalse(File.Exists(marker), "Part of the file name ran as a command.");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
