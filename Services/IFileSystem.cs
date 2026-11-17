using System;
using System.IO;

namespace DeskNest
{
    // Abstraction minimale du système de fichiers utilisée par Storage.
    // Elle permet de tester la persistance sans toucher au profil de l'utilisateur.
    public interface IFileSystem
    {
        bool FileExists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        void Copy(string sourcePath, string destinationPath, bool overwrite);
        void Move(string sourcePath, string destinationPath);
        void Delete(string path);
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        string[] GetFiles(string path, string searchPattern, SearchOption searchOption);
        string[] GetDirectories(string path, string searchPattern, SearchOption searchOption);
        DateTime GetLastWriteTimeUtc(string path);
        void Replace(string sourcePath, string destinationPath, string destinationBackupPath, bool ignoreMetadataErrors);
    }

    public sealed class PhysicalFileSystem : IFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
        public void Copy(string sourcePath, string destinationPath, bool overwrite) => File.Copy(sourcePath, destinationPath, overwrite);
        public void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);
        public void Delete(string path) => File.Delete(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public string[] GetFiles(string path, string searchPattern, SearchOption searchOption) => Directory.GetFiles(path, searchPattern, searchOption);
        public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption) => Directory.GetDirectories(path, searchPattern, searchOption);
        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
        public void Replace(string sourcePath, string destinationPath, string destinationBackupPath, bool ignoreMetadataErrors) =>
            File.Replace(sourcePath, destinationPath, destinationBackupPath, ignoreMetadataErrors);
    }
}
