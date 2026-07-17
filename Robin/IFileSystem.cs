namespace Robin
{
    /// <summary>
    /// The filesystem primitives used by FFmpegPathResolver, abstracted so the
    /// resolver's cache and WinGet-scan logic can be unit-tested in memory.
    /// </summary>
    internal interface IFileSystem
    {
        bool FileExists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        void CreateDirectory(string path);
        string[] GetDirectories(string baseDir, string searchPattern);
    }
}
