using System;
using System.IO;
using System.Security.Cryptography;

namespace SWGrade.SolidWorks
{
    internal sealed class ModelCopy : IDisposable
    {
        private readonly string folder;
        private readonly DateTime sourceWriteTime;

        internal string SourcePath { get; }
        internal string WorkingPath { get; }
        internal string Hash { get; }

        private ModelCopy(string sourcePath, string folder, string workingPath, string hash, DateTime sourceWriteTime)
        {
            SourcePath = sourcePath;
            this.folder = folder;
            WorkingPath = workingPath;
            Hash = hash;
            this.sourceWriteTime = sourceWriteTime;
        }

        internal static ModelCopy Create(string path)
        {
            string source = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(source), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("当前仅支持 SOLIDWORKS 单零件文件 .SLDPRT。");

            string id = Guid.NewGuid().ToString("N");
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SWGrade", "workspace", id);
            string copy = Path.Combine(folder, "SWGrade-" + id + ".SLDPRT");

            // SOLIDWORKS may keep the original open with write access.
            using (var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var writeTime = File.GetLastWriteTimeUtc(source);
                string hash = ComputeHash(input);
                input.Position = 0;
                Directory.CreateDirectory(folder);
                var work = new ModelCopy(source, folder, copy, hash, writeTime);
                try
                {
                    using (var destination = File.Open(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(destination);

                    using (var copiedFile = File.OpenRead(copy))
                    {
                        if (ComputeHash(copiedFile) != hash || File.GetLastWriteTimeUtc(source) != writeTime)
                            throw new IOException("建立工作副本期间源文件已变化，请在保存完成后重试。");
                    }

                    return work;
                }
                catch
                {
                    work.Dispose();
                    throw;
                }
            }
        }

        internal void VerifySource()
        {
            using (var original = File.Open(SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (ComputeHash(original) != Hash || File.GetLastWriteTimeUtc(SourcePath) != sourceWriteTime)
                    throw new IOException("读取前后的源文件哈希或最后写入时间不同，请检查是否有其他程序修改了源文件。");
            }
        }

        private static string ComputeHash(Stream stream)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose()
        {
            try
            {
                File.Delete(WorkingPath);
                Directory.Delete(folder, false);
            }
            catch (IOException exception)
            {
                throw new IOException("未能清理工作目录 " + folder + "。请确认临时文档已关闭。", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException("无权清理工作目录 " + folder, exception);
            }
        }
    }
}
