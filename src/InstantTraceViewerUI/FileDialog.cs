using System;
using System.Collections.Generic;
using System.IO;

namespace InstantTraceViewerUI
{
    internal interface IFileDialogBackend
    {
        string? OpenFile(string filter, string? initialDirectory);

        IReadOnlyList<string> OpenMultipleFiles(string filter, string? initialDirectory);

        string? SaveFile(string filter, string? initialDirectory, string defaultExtension);
    }

    internal static class FileDialog
    {
        private static readonly IFileDialogBackend s_backend = CreateBackend();

        public static string? OpenFile(string filter, string? initialDirectory)
        {
            return OpenFile(filter, initialDirectory, _ => { });
        }

        public static string? OpenFile(string filter, string? initialDirectory, Action<string> persistDirectory)
        {
            string? path = s_backend.OpenFile(filter, initialDirectory);
            PersistDirectory(path, persistDirectory);
            return path;
        }

        public static IReadOnlyList<string> OpenMultipleFiles(string filter, string? initialDirectory)
        {
            return OpenMultipleFiles(filter, initialDirectory, _ => { });
        }

        public static IReadOnlyList<string> OpenMultipleFiles(string filter, string? initialDirectory, Action<string> persistDirectory)
        {
            IReadOnlyList<string> paths = s_backend.OpenMultipleFiles(filter, initialDirectory);
            if (paths.Count > 0)
            {
                PersistDirectory(paths[0], persistDirectory);
            }

            return paths;
        }

        public static string? SaveFile(string filter, string? initialDirectory, string defaultExtension)
        {
            return SaveFile(filter, initialDirectory, defaultExtension, _ => { });
        }

        public static string? SaveFile(string filter, string? initialDirectory, string defaultExtension, Action<string> persistDirectory)
        {
            string? path = s_backend.SaveFile(filter, initialDirectory, defaultExtension);
            PersistDirectory(path, persistDirectory);
            return path;
        }

        internal static bool IsAllowedByFilter(string path, string filter)
        {
            if (string.IsNullOrEmpty(filter))
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

        private static IFileDialogBackend CreateBackend()
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                return new WindowsFileDialog();
            }
#endif

            if (OperatingSystem.IsMacOS())
            {
                return new MacFileDialog();
            }

            return new UnsupportedFileDialogBackend();
        }

        private static void PersistDirectory(string? path, Action<string> persistDirectory)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                persistDirectory(directory);
            }
        }

        private sealed class UnsupportedFileDialogBackend : IFileDialogBackend
        {
            public string? OpenFile(string filter, string? initialDirectory)
            {
                throw CreateException();
            }

            public IReadOnlyList<string> OpenMultipleFiles(string filter, string? initialDirectory)
            {
                throw CreateException();
            }

            public string? SaveFile(string filter, string? initialDirectory, string defaultExtension)
            {
                throw CreateException();
            }

            private static PlatformNotSupportedException CreateException()
            {
                return new PlatformNotSupportedException("File dialogs are only supported on Windows and macOS.");
            }
        }
    }
}
