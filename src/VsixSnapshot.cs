using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace VsixTreeViewer
{
    internal static class VsixSnapshot
    {
        private const int MaxAttempts = 10;

        public static string Create(string vsixPath)
        {
            if (string.IsNullOrWhiteSpace(vsixPath) || !File.Exists(vsixPath))
            {
                return null;
            }

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                string snapshotPath = null;

                try
                {
                    string sourceStamp = GetStamp(vsixPath);
                    string sourceHash = GetHash(vsixPath);
                    if (!string.Equals(sourceStamp, GetStamp(vsixPath), StringComparison.Ordinal))
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    string snapshotDirectory = VsixTemporaryFiles.GetSnapshotDirectory(vsixPath);
                    snapshotPath = Path.Combine(snapshotDirectory, sourceHash + ".vsix");
                    Directory.CreateDirectory(snapshotDirectory);

                    if (File.Exists(snapshotPath))
                    {
                        return snapshotPath;
                    }

                    string temporaryPath = snapshotPath + "." + Path.GetRandomFileName();
                    try
                    {
                        File.Copy(vsixPath, temporaryPath, overwrite: true);

                        if (!string.Equals(sourceStamp, GetStamp(vsixPath), StringComparison.Ordinal)
                            || !string.Equals(sourceHash, GetHash(temporaryPath), StringComparison.Ordinal)
                            || !string.Equals(sourceHash, GetHash(vsixPath), StringComparison.Ordinal))
                        {
                            File.Delete(temporaryPath);
                            Thread.Sleep(250);
                            continue;
                        }

                        File.Move(temporaryPath, snapshotPath);
                        return snapshotPath;
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                }
                catch (IOException)
                {
                    if (!string.IsNullOrWhiteSpace(snapshotPath) && File.Exists(snapshotPath))
                    {
                        return snapshotPath;
                    }

                    if (attempt >= MaxAttempts)
                    {
                        throw;
                    }

                    Thread.Sleep(250);
                }
            }

            return null;
        }

        private static string GetStamp(string vsixPath)
        {
            FileInfo fileInfo = new(vsixPath);
            return $"{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}";
        }

        private static string GetHash(string path)
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);

            foreach (byte value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
