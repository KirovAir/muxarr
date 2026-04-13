using Microsoft.VisualStudio.TestTools.UnitTesting;

// MkvMerge.KillExistingProcesses / FFmpeg.KillExistingProcesses enumerate by
// process name across the whole OS, so parallel test runs aren't safe.
[assembly: DoNotParallelize]
