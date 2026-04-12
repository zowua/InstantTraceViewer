using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace InstantTraceViewerUI
{
    internal static class FileDialog
    {
        public static string? OpenFile(string filter, string? initialDirectory, Action<string> persistDirectory)
        {
            if (OperatingSystem.IsMacOS())
            {
                string? path = RunAppleScript(false, false, initialDirectory);
                PersistDirectory(path, persistDirectory);
                return path;
            }

            string outFileBuffer = new string('\0', 8192);
            unsafe
            {
                fixed (char* outFilePtr = outFileBuffer)
                {
                    if (OpenFileDialog(ReformatFilter(filter), initialDirectory ?? string.Empty, outFilePtr, outFileBuffer.Length, 0) != 0)
                    {
                        return null;
                    }

                    string outFileTrimmed = new string(outFilePtr);
                    PersistDirectory(outFileTrimmed, persistDirectory);
                    return outFileTrimmed;
                }
            }
        }

        public static IReadOnlyList<string> OpenMultipleFiles(string filter, string? initialDirectory, Action<string> persistDirectory)
        {
            if (OperatingSystem.IsMacOS())
            {
                string? output = RunAppleScript(false, true, initialDirectory);
                if (string.IsNullOrWhiteSpace(output))
                {
                    return Array.Empty<string>();
                }

                string[] paths = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (paths.Length > 0)
                {
                    PersistDirectory(paths[0], persistDirectory);
                }
                return paths;
            }

            string outFileBuffer = new string('\0', 8192);
            unsafe
            {
                fixed (char* outFilePtr = outFileBuffer)
                {
                    if (OpenFileDialog(ReformatFilter(filter), initialDirectory ?? string.Empty, outFilePtr, outFileBuffer.Length, 1) != 0)
                    {
                        return Array.Empty<string>();
                    }

                    List<string> paths = new List<string>();
                    int start = 0;
                    for (int i = 0; i < outFileBuffer.Length; i++)
                    {
                        if (outFileBuffer[i] == '\0')
                        {
                            if (i == start)
                            {
                                break;
                            }
                            paths.Add(outFileBuffer.Substring(start, i - start));
                            start = i + 1;
                        }
                    }

                    if (paths.Count == 1)
                    {
                        PersistDirectory(paths[0], persistDirectory);
                        return paths;
                    }

                    string directoryName = paths[0];
                    persistDirectory(directoryName);
                    List<string> fullPaths = new();
                    for (int i = 1; i < paths.Count; i++)
                    {
                        fullPaths.Add(Path.Combine(directoryName, paths[i]));
                    }
                    return fullPaths;
                }
            }
        }

        public static string? SaveFile(string filter, string? initialDirectory, string defaultExtension, Action<string> persistDirectory)
        {
            if (OperatingSystem.IsMacOS())
            {
                string? path = RunAppleScript(true, false, initialDirectory);
                if (!string.IsNullOrEmpty(path) && !Path.HasExtension(path))
                {
                    path = Path.ChangeExtension(path, defaultExtension);
                }
                PersistDirectory(path, persistDirectory);
                return path;
            }

            string outFileBuffer = new string('\0', 8192);
            unsafe
            {
                fixed (char* outFilePtr = outFileBuffer)
                {
                    if (SaveFileDialog(ReformatFilter(filter), initialDirectory ?? string.Empty, outFilePtr, outFileBuffer.Length) != 0)
                    {
                        return null;
                    }

                    string outFileTrimmed = new string(outFilePtr);
                    if (!Path.HasExtension(outFileTrimmed))
                    {
                        outFileTrimmed = Path.ChangeExtension(outFileTrimmed, defaultExtension);
                    }

                    PersistDirectory(outFileTrimmed, persistDirectory);
                    return outFileTrimmed;
                }
            }
        }

        [DllImport("InstantTraceViewerNative", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        private static unsafe extern int OpenFileDialog(string filter, string initialDirectory, char* outFileBuffer, int outFileBufferLength, int multiSelect);

        [DllImport("InstantTraceViewerNative", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        private static unsafe extern int SaveFileDialog(string filter, string initialDirectory, char* outFileBuffer, int outFileBufferLength);

        private static string ReformatFilter(string filter) => filter.Replace('|', '\0') + '\0';

        private static void PersistDirectory(string? path, Action<string> persistDirectory)
        {
            if (!string.IsNullOrEmpty(path))
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    persistDirectory(directory);
                }
            }
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

            ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/osascript")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(script);

            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return string.IsNullOrWhiteSpace(output) ? null : output;
        }

        private static string EscapeAppleScriptString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
