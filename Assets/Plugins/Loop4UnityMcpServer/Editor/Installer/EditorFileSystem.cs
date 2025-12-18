using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace LoopMcpServer.Editor.Installer
{
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        bool FileExists(string path);
        void CreateDirectory(string path);
        void CopyFile(string source, string dest, bool overwrite);
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
        public string[] GetFiles(string path) => Directory.GetFiles(path);
        public string[] GetDirectories(string path) => Directory.GetDirectories(path);
        public string GetFileName(string path) => Path.GetFileName(path);
        public string ReadAllText(string filePath) => File.ReadAllText(filePath);

        public string ComputeFileHash(string filePath)
        {
            string text = File.ReadAllText(filePath);
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
                return string.Concat(hash.Select(b => b.ToString("x2")));
            }
        }
    }
}