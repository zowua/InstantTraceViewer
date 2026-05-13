using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
#if WINDOWS
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.Dialogs;
#endif

namespace InstantTraceViewerUI
{
    internal static class FileDialog
    {
        private const int BufferLength = 8192;

        public static string? OpenFile(string filter, string? initialDirectory)
        {
            return OpenFile(filter, initialDirectory, _ => { });
        }

        public static unsafe string? OpenFile(string filter, string? initialDirectory, Action<string> persistDirectory)
        {
            if (OperatingSystem.IsMacOS())
            {
                string? path = RunAppleScript(false, false, initialDirectory);
                if (!IsAllowedByFilter(path, filter))
                {
                    return null;
                }

                PersistDirectory(path, persistDirectory);
                return path;
            }

#if WINDOWS
            char[] buffer = new char[BufferLength];
            string reformattedFilter = ReformatFilter(filter);
            fixed (char* bufferPtr = buffer)
            fixed (char* filterPtr = reformattedFilter)
            fixed (char* initialDirPtr = initialDirectory ?? string.Empty)
            {
                OPENFILENAMEW ofn = new()
                {
                    lStructSize = (uint)Marshal.SizeOf<OPENFILENAMEW>(),
                    hwndOwner = new HWND(Win32ImGuiHost.MainWindowHandle),
                    lpstrFilter = filterPtr,
                    lpstrInitialDir = initialDirPtr,
                    lpstrFile = bufferPtr,
                    nMaxFile = BufferLength,
                    Flags = OPEN_FILENAME_FLAGS.OFN_NOCHANGEDIR | OPEN_FILENAME_FLAGS.OFN_FILEMUSTEXIST | OPEN_FILENAME_FLAGS.OFN_EXPLORER,
                };

                if (!PInvoke.GetOpenFileName(ref ofn))
                {
                    return null;
                }

                string path = new(bufferPtr);
                PersistDirectory(path, persistDirectory);
                return path;
            }
#else
            throw new PlatformNotSupportedException("File dialogs are only supported on Windows and macOS.");
#endif
        }

        public static IReadOnlyList<string> OpenMultipleFiles(string filter, string? initialDirectory)
        {
            return OpenMultipleFiles(filter, initialDirectory, _ => { });
        }

        public static unsafe IReadOnlyList<string> OpenMultipleFiles(string filter, string? initialDirectory, Action<string> persistDirectory)
        {
            if (OperatingSystem.IsMacOS())
            {
                string? output = RunAppleScript(false, true, initialDirectory);
                if (string.IsNullOrWhiteSpace(output))
                {
                    return Array.Empty<string>();
                }

                string[] paths = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                paths = paths.Where(path => IsAllowedByFilter(path, filter)).ToArray();
                if (paths.Length > 0)
                {
                    PersistDirectory(paths[0], persistDirectory);
                }
                return paths;
            }

#if WINDOWS
            char[] buffer = new char[BufferLength];
            string reformattedFilter = ReformatFilter(filter);
            fixed (char* bufferPtr = buffer)
            fixed (char* filterPtr = reformattedFilter)
            fixed (char* initialDirPtr = initialDirectory ?? string.Empty)
            {
                OPENFILENAMEW ofn = new()
                {
                    lStructSize = (uint)Marshal.SizeOf<OPENFILENAMEW>(),
                    hwndOwner = new HWND(Win32ImGuiHost.MainWindowHandle),
                    lpstrFilter = filterPtr,
                    lpstrInitialDir = initialDirPtr,
                    lpstrFile = bufferPtr,
                    nMaxFile = BufferLength,
                    Flags = OPEN_FILENAME_FLAGS.OFN_NOCHANGEDIR | OPEN_FILENAME_FLAGS.OFN_FILEMUSTEXIST | OPEN_FILENAME_FLAGS.OFN_EXPLORER | OPEN_FILENAME_FLAGS.OFN_ALLOWMULTISELECT,
                };

                if (!PInvoke.GetOpenFileName(ref ofn))
                {
                    return Array.Empty<string>();
                }

                // Buffer is null-separated; double-null terminates the list.
                List<string> paths = new();
                int start = 0;
                for (int i = 0; i < BufferLength; i++)
                {
                    if (buffer[i] == '\0')
                    {
                        if (i == start)
                        {
                            break;
                        }
                        paths.Add(new string(buffer, start, i - start));
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
                return paths.Skip(1).Select(p => Path.Combine(directoryName, p)).ToList();
            }
#else
            throw new PlatformNotSupportedException("File dialogs are only supported on Windows and macOS.");
#endif
        }

        public static string? SaveFile(string filter, string? initialDirectory, string defaultExtension)
        {
            return SaveFile(filter, initialDirectory, defaultExtension, _ => { });
        }

        public static unsafe string? SaveFile(string filter, string? initialDirectory, string defaultExtension, Action<string> persistDirectory)
        {
            if (OperatingSystem.IsMacOS())
            {
                string? path = RunAppleScript(true, false, initialDirectory);
                if (!string.IsNullOrEmpty(path) && !Path.HasExtension(path))
                {
                    path = Path.ChangeExtension(path, defaultExtension);
                    if (File.Exists(path) && !ConfirmOverwrite(path))
                    {
                        return null;
                    }
                }
                PersistDirectory(path, persistDirectory);
                return path;
            }

#if WINDOWS
            char[] buffer = new char[BufferLength];
            string reformattedFilter = ReformatFilter(filter);
            fixed (char* bufferPtr = buffer)
            fixed (char* filterPtr = reformattedFilter)
            fixed (char* initialDirPtr = initialDirectory ?? string.Empty)
            {
                OPENFILENAMEW ofn = new()
                {
                    lStructSize = (uint)Marshal.SizeOf<OPENFILENAMEW>(),
                    hwndOwner = new HWND(Win32ImGuiHost.MainWindowHandle),
                    lpstrFilter = filterPtr,
                    lpstrInitialDir = initialDirPtr,
                    lpstrFile = bufferPtr,
                    nMaxFile = BufferLength,
                    Flags = OPEN_FILENAME_FLAGS.OFN_NOCHANGEDIR | OPEN_FILENAME_FLAGS.OFN_OVERWRITEPROMPT | OPEN_FILENAME_FLAGS.OFN_EXPLORER,
                };

                if (!PInvoke.GetSaveFileName(ref ofn))
                {
                    return null;
                }

                string path = new(bufferPtr);
                if (!Path.HasExtension(path))
                {
                    path = Path.ChangeExtension(path, defaultExtension);
                }

                PersistDirectory(path, persistDirectory);
                return path;
            }
#else
            throw new PlatformNotSupportedException("File dialogs are only supported on Windows and macOS.");
#endif
        }

        private static string ReformatFilter(string filter) => filter.Replace('|', '\0') + '\0';

        private static bool IsAllowedByFilter(string? path, string filter)
        {
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            string[] filterParts = filter.Split('|');
            for (int i = 1; i < filterParts.Length; i += 2)
            {
                foreach (string pattern in filterParts[i].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (pattern == "*" || pattern == "*.*")
                    {
                        return true;
                    }

                    if (pattern.StartsWith("*.") && path.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
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
            process.WaitForExit();

            return output == "Replace";
        }

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
            process.WaitForExit();

            return string.IsNullOrWhiteSpace(output) ? null : output;
        }

        private static string EscapeAppleScriptString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
