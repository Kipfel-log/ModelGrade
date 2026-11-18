using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SWGrade.Domain;

namespace SWGrade.Application
{
    public sealed class Assignments
    {
        private readonly IGradeStore store;

        public Assignments(IGradeStore store)
        {
            this.store = store;
        }

        public Assignment Create(string name, string standardPath, AssignmentRules rules, string expectedStandardHash = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("请填写作业名称。");
            rules.Validate();
            var path = PartFiles.Normalize(standardPath);
            string actualHash = PartFiles.Hash(path);
            if (expectedStandardHash != null && actualHash != expectedStandardHash)
                throw new InvalidOperationException("标准模型在读取配置后发生变化，请重新读取配置。");
            var assignment = new Assignment
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name.Trim(),
                StandardPath = path,
                StandardHash = actualHash,
                CreatedUtc = Clock.UtcNow(),
                Rules = rules
            };
            store.AddAssignment(assignment);
            return assignment;
        }

        public IReadOnlyList<Submission> Import(Assignment assignment, IEnumerable<string> files)
        {
            var existing = store.LoadSubmissions(assignment.Id);
            var hashes = new HashSet<string>(existing.Select(item => item.FileHash).Where(hash => hash != null),
                StringComparer.Ordinal);
            var imported = new List<Submission>();
            foreach (var file in files)
            {
                var path = PartFiles.Normalize(file);
                var submission = new Submission
                {
                    Id = Guid.NewGuid().ToString("N"),
                    AssignmentId = assignment.Id,
                    FilePath = path,
                    State = TaskState.Pending
                };
                InferStudent(submission);

                try
                {
                    submission.FileHash = PartFiles.Hash(path);
                    if (!hashes.Add(submission.FileHash))
                        submission.ImportNotice = "同内容已导入，保留独立提交记录。";
                    else if (submission.FileHash == assignment.StandardHash)
                        submission.ImportNotice = "文件内容与标准件相同，请核对提交来源。";
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    submission.ImportNotice = "导入时无法读取文件，将在批改时重试。";
                }

                imported.Add(submission);
            }

            store.AddSubmissions(imported);
            return imported;
        }

        private static void InferStudent(Submission submission)
        {
            var fileName = Path.GetFileNameWithoutExtension(submission.FilePath);
            var parts = fileName.Split(new[] { ' ', '-', '_' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].All(char.IsDigit))
            {
                submission.StudentId = parts[0];
                submission.StudentName = parts[1];
            }
        }
    }

    public static class PartFiles
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("请选择 SOLIDWORKS 零件。");
            string fullPath = Path.GetFullPath(path);
            if (!IsPart(fullPath))
                throw new ArgumentException("当前版本仅支持单零件 .SLDPRT。");
            return fullPath;
        }

        public static bool IsPart(string path) =>
            string.Equals(Path.GetExtension(path), ".SLDPRT", StringComparison.OrdinalIgnoreCase);

        public static string Hash(string path)
        {
            using (var source = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create())
            {
                var writeTime = File.GetLastWriteTimeUtc(path);
                string hash = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
                if (File.GetLastWriteTimeUtc(path) != writeTime)
                    throw new IOException("计算 SHA-256 时零件正在保存，请在保存完成后重试。");
                return hash;
            }
        }
    }
}
