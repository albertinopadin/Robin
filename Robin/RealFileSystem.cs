using System.IO;

namespace Robin
{
    internal sealed class RealFileSystem : IFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public string[] GetDirectories(string baseDir, string searchPattern)
            => Directory.GetDirectories(baseDir, searchPattern, SearchOption.TopDirectoryOnly);
    }
}
