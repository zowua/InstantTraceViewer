using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace InstantTraceViewerUI
{
    internal sealed class MacFileDialog : IFileDialogBackend
    {
        public string? OpenFile(string filter, string? initialDirectory)
        {
            string? path = RunAppleScript(saveDialog: false, allowMultiple: false, initialDirectory);
            return path != null && FileDialog.IsAllowedByFilter(path, filter) ? path : null;
        }

        public IReadOnlyList<string> OpenMultipleFiles(string filter, string? initialDirectory)
        {
            string? output = RunAppleScript(saveDialog: false, allowMultiple: true, initialDirectory);
            if (string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<string>();
            }

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(path => FileDialog.IsAllowedByFilter(path, filter))
                .ToArray();
        }

        public string? SaveFile(string filter, string? initialDirectory, string defaultExtension)
        {
            string? path = RunAppleScript(saveDialog: true, allowMultiple: false, initialDirectory);
            if (!string.IsNullOrEmpty(path) && !Path.HasExtension(path))
            {
                path = Path.ChangeExtension(path, defaultExtension);
                if (File.Exists(path) && !ConfirmOverwrite(path))
                {
                    return null;
                }
            }

            return path;
        }

        private static bool ConfirmOverwrite(string path)
        {
            string script =
                "try\n" +
                $"display dialog \"{EscapeAppleScriptString(Path.GetFileName(path))} already exists. Do you want to replace it?\" buttons {{\"Cancel\", \"Replace\"}} default button \"Replace\" cancel button \"Cancel\" with icon caution\n" +
                "return \"Replace\"\n" +
                "on error number -128\n" +
                "return \"\"\n" +
                "end try";

            return RunAppleScriptCommand(script) == "Replace";
        }

        private static string? RunAppleScript(bool saveDialog, bool allowMultiple, string? initialDirectory)
        {
            string command = saveDialog ? "choose file name" : "choose file";
            string multipleClause = allowMultiple ? " with multiple selections allowed true" : string.Empty;
            string locationClause = Directory.Exists(initialDirectory) ?
                $" default location POSIX file \"{EscapeAppleScriptString(initialDirectory!)}\"" :
                string.Empty;

            string script =
                "try\n" +
                $"set selectionResult to {command}{multipleClause}{locationClause}\n" +
                "if class of selectionResult is list then\n" +
                "set outputLines to {}\n" +
                "repeat with currentItem in selectionResult\n" +
                "set end of outputLines to POSIX path of currentItem\n" +
                "end repeat\n" +
                "set AppleScript's text item delimiters to linefeed\n" +
                "return outputLines as string\n" +
                "else\n" +
                "return POSIX path of selectionResult\n" +
                "end if\n" +
                "on error number -128\n" +
                "return \"\"\n" +
                "end try";

            string? output = RunAppleScriptCommand(script);

            return string.IsNullOrWhiteSpace(output) ? null : output;
        }

        private static string? RunAppleScriptCommand(string script)
        {
            ProcessStartInfo startInfo = new("/usr/bin/osascript")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(script);

            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Debug.WriteLine($"osascript failed with exit code {process.ExitCode}: {error}");
                return null;
            }

            return output;
        }

        private static string EscapeAppleScriptString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
