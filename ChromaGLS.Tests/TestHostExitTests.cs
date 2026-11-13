using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework;

namespace ChromaGLS.Tests;

// A written results XML alone cannot detect BSIPA's foreground logger keeping mono
// alive, so this launches the real runner on one known-fast test and requires a
// clean, bounded exit.
[TestFixture]
[NonParallelizable]
internal sealed class TestHostExitTests
{
    [Test]
    public void ChildRunnerExitsAfterWritingResults()
    {
        string assembly = typeof(Program).Assembly.Location;
        // Output is bin/Release-1.44.1/net48 -> ... -> <tests root> -> repo -> workspace.
        string workspace = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(assembly),
                "..",
                "..",
                "..",
                "..",
                ".."));
        string runDirectory = Path.Combine(workspace, "Scratch", "chroma-managed-host-exit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        string xmlPath = Path.Combine(runDirectory, "results.xml");

        string mono = Process.GetCurrentProcess().MainModule!.FileName;
        var start = new ProcessStartInfo(mono)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = runDirectory,
        };
        string args = Quote(assembly)
            + " --test=" + typeof(ParabolicFloorLightSettingsTests).FullName + "." + nameof(ParabolicFloorLightSettingsTests.GeneratedStorePersistsAllFourteenThroughJsonRoundTrip)
            + " --workers=0 --result=" + Quote(xmlPath) + " --work=" + Quote(runDirectory);
        start.Arguments = args;

        using var child = Process.Start(start)!;
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        bool exited = false;
        try
        {
            child.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stdout.AppendLine(e.Data);
            };
            child.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stderr.AppendLine(e.Data);
            };
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
            exited = child.WaitForExit(10_000);
            if (exited)
            {
                // Finish the asynchronous output callbacks before reading the buffers.
                child.WaitForExit();
                File.WriteAllText(Path.Combine(runDirectory, "stdout.txt"), stdout.ToString());
                File.WriteAllText(Path.Combine(runDirectory, "stderr.txt"), stderr.ToString());
            }
        }
        finally
        {
            if (!child.HasExited)
                child.Kill();
            child.WaitForExit();
        }

        Assert.That(exited, Is.True, $"Child runner did not exit within 10s after writing results; log {runDirectory}");
        Assert.That(child.ExitCode, Is.EqualTo(0), $"Child exit code {child.ExitCode}; log {runDirectory}");
        Assert.That(File.Exists(xmlPath), Is.True, "Child did not write the results XML.");
        var document = new XmlDocument();
        document.Load(xmlPath);
        XmlNode? summary = document.SelectSingleNode("//test-run");
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.Attributes!["passed"]!.Value, Is.EqualTo("1"));
        Assert.That(summary.Attributes["failed"]!.Value, Is.EqualTo("0"));
    }

    private static string Quote(string value) => $"\"{value}\"";
}
