using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Robin.Tests.Fakes
{
    internal sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> WrittenFiles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> CreatedDirectories { get; } = new List<string>();
        public List<(string BaseDir, string Pattern)> GetDirectoriesCalls { get; } = new List<(string, string)>();
        public Exception ReadException { get; set; }

        public void AddFile(string path, string contents) => files[Normalize(path)] = contents;
        public void AddDirectory(string path) => directories.Add(Normalize(path));

        public bool FileExists(string path) => files.ContainsKey(Normalize(path));

        public string ReadAllText(string path)
        {
            if (ReadException != null) throw ReadException;
            return files[Normalize(path)];
        }

        public void WriteAllText(string path, string contents)
        {
            files[Normalize(path)] = contents;
            WrittenFiles[Normalize(path)] = contents;
        }

        public void CreateDirectory(string path)
        {
            directories.Add(Normalize(path));
            CreatedDirectories.Add(Normalize(path));
        }

        public string[] GetDirectories(string baseDir, string searchPattern)
        {
            GetDirectoriesCalls.Add((baseDir, searchPattern));

            string normalizedBase = Normalize(baseDir);
            if (!directories.Contains(normalizedBase))
            {
                throw new DirectoryNotFoundException($"Could not find a part of the path '{baseDir}'.");
            }

            // The resolver only ever uses "prefix*" patterns.
            string prefix = searchPattern.TrimEnd('*');
            return directories
                .Where(d => string.Equals(Path.GetDirectoryName(d), normalizedBase, StringComparison.OrdinalIgnoreCase))
                .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static string Normalize(string path) => path.TrimEnd('\\', '/');
    }
}
