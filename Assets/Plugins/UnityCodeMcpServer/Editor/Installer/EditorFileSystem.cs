using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace UnityCodeMcpServer.Editor.Installer
{
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        bool FileExists(string path);
        void CreateDirectory(string path);
        void CopyFile(string source, string dest, bool overwrite);
        void DeleteFile(string path);
        void DeleteDirectory(string path, bool recursive);
        string[] GetFiles(string path);
        string[] GetDirectories(string path);
        string GetFileName(string path);
        string ComputeFileHash(string filePath);
        string ReadAllText(string filePath);
    }

    public class EditorFileSystem : IFileSystem
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public bool FileExists(string path) => File.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void CopyFile(string source, string dest, bool overwrite) => File.Copy(source, dest, overwrite);
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
        public string[] GetFiles(string path) => Directory.GetFiles(path);
        public string[] GetDirectories(string path) => Directory.GetDirectories(path);
        public string GetFileName(string path) => Path.GetFileName(path);
        public string ReadAllText(string filePath) => File.ReadAllText(filePath);

        public string ComputeFileHash(string filePath)
        {
            string text = File.ReadAllText(filePath);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
                return string.Concat(hash.Select(b => b.ToString("x2")));
            }
        }
    }
}
