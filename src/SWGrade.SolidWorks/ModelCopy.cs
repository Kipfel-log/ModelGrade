using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace SWGrade.SolidWorks
{
    internal sealed class ModelCopy : IDisposable
    {
        private static readonly ConcurrentDictionary<string, string> Pending = new ConcurrentDictionary<string, string>();
        private readonly string folder;
        private readonly DateTime sourceWriteTime;
        private readonly Action<string> report;
        private bool preserveCopy;
        private bool disposed;

        internal string SourcePath { get; }
        internal string WorkingPath { get; }
        internal string Hash { get; }

        private ModelCopy(string sourcePath, string folder, string workingPath, string hash, DateTime sourceWriteTime, Action<string> report)
        {
            SourcePath = sourcePath;
            this.folder = folder;
            WorkingPath = workingPath;
            Hash = hash;
            this.sourceWriteTime = sourceWriteTime;
            this.report = report;
        }

        internal static ModelCopy Create(string path, Action<string> report = null)
        {
            string source = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(source), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("当前仅支持 SOLIDWORKS 单零件文件 .SLDPRT。");

            RetryPending(report);
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
                var work = new ModelCopy(source, folder, copy, hash, writeTime, report);
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

        internal void PreserveForCad()
        {
            preserveCopy = true;
            report?.Invoke("工作副本的关闭状态未确认，保留文件：" + WorkingPath);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (preserveCopy)
                return;

            var error = TryClean(WorkingPath, folder);
            if (error == null)
                return;

            // CAD and its file watcher can hold the directory briefly after CloseDoc.
            Thread.Sleep(80);
            error = TryClean(WorkingPath, folder);
            if (error != null)
            {
                Pending.TryAdd(WorkingPath, folder);
                report?.Invoke("工作副本清理已延后：" + folder + "。 " + error.Message);
            }
        }

        internal static void RetryPending(Action<string> report)
        {
            foreach (var entry in Pending)
            {
                if (TryClean(entry.Key, entry.Value) == null && Pending.TryRemove(entry.Key, out _))
                    report?.Invoke("已补清理临时工作目录：" + entry.Value);
            }
        }

        private static Exception TryClean(string path, string folder)
        {
            try
            {
                // Delete only the copy this process created. Keep unknown files and non-empty folders.
                File.Delete(path);
                Directory.Delete(folder, false);
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return exception;
            }
        }
    }
}
