using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Data.Sqlite;
using SWGrade.Application;

namespace SWGrade.Infrastructure
{
    public sealed class SqliteGradeStore : IGradeStore, IDisposable
    {
        private readonly FileStream workspaceLease;
        private readonly string connectionString;

        public SqliteGradeStore(string folder)
        {
            Directory.CreateDirectory(folder);
            workspaceLease = File.Open(Path.Combine(folder, "teacher-workspace.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(folder, "swgrade.sqlite3"),
                    ForeignKeys = true,
                    Pooling = false
                }.ToString();

                using (var connection = Open())
                using (var command = Command(connection, @"
CREATE TABLE IF NOT EXISTS assignments (
    id TEXT PRIMARY KEY,
    payload TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS submissions (
    id TEXT PRIMARY KEY,
    assignment_id TEXT NOT NULL REFERENCES assignments(id),
    payload TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS submissions_assignment ON submissions(assignment_id);"))
                    command.ExecuteNonQuery();
            }
            catch
            {
                workspaceLease.Dispose();
                throw;
            }
        }

        public IReadOnlyList<Assignment> LoadAssignments() =>
            Read<Assignment>("SELECT payload FROM assignments ORDER BY rowid DESC");

        public IReadOnlyList<Submission> LoadSubmissions(string assignmentId) =>
            Read<Submission>("SELECT payload FROM submissions WHERE assignment_id = $assignment ORDER BY rowid",
                ("$assignment", assignmentId));

        public void AddAssignment(Assignment assignment)
        {
            using (var connection = Open())
            using (var command = Command(connection, "INSERT INTO assignments(id, payload) VALUES($id, $payload)",
                ("$id", assignment.Id), ("$payload", Serialize(assignment))))
                command.ExecuteNonQuery();
        }

        public void AddSubmissions(IReadOnlyList<Submission> submissions)
        {
            if (submissions.Count == 0)
                return;

            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var submission in submissions)
                {
                    using (var command = Command(connection,
                        "INSERT INTO submissions(id, assignment_id, payload) VALUES($id, $assignment, $payload)",
                        ("$id", submission.Id), ("$assignment", submission.AssignmentId), ("$payload", Serialize(submission))))
                    {
                        command.Transaction = transaction;
                        command.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
        }

        public void SaveSubmission(Submission submission)
        {
            using (var connection = Open())
            using (var command = Command(connection,
                "UPDATE submissions SET payload = $payload WHERE id = $id AND assignment_id = $assignment",
                ("$payload", Serialize(submission)), ("$id", submission.Id), ("$assignment", submission.AssignmentId)))
            {
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("无法更新提交，记录已不存在。");
            }
        }

        public void Dispose() => workspaceLease.Dispose();

        private List<T> Read<T>(string sql, params (string Name, object Value)[] parameters)
        {
            var items = new List<T>();
            using (var connection = Open())
            using (var command = Command(connection, sql, parameters))
            using (var row = command.ExecuteReader())
            {
                while (row.Read())
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(row.GetString(0))))
                        items.Add((T)Serializer<T>().ReadObject(stream));
                }
            }
            return items;
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(connectionString);
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static SqliteCommand Command(SqliteConnection connection, string sql,
            params (string Name, object Value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters)
                command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            return command;
        }

        private static DataContractJsonSerializer Serializer<T>() =>
            new DataContractJsonSerializer(typeof(T),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        private static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                Serializer<T>().WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
