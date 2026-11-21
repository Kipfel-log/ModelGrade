using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SWGrade.Domain;

namespace SWGrade.Classroom
{
    public static class ClassroomFiles
    {
        internal const int Limit = 64 * 1024;
        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(Part3Report));

        public static byte[] Encode(Part3Report report)
        {
            Validate(report);
            using (var stream = new MemoryStream())
            {
                Serializer().WriteObject(stream, report);
                if (stream.Length > Limit)
                    throw new InvalidDataException("成绩文件过大。");
                return stream.ToArray();
            }
        }

        public static Part3Report Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > Limit)
                throw new InvalidDataException("成绩文件大小不符合要求。");
            using (var stream = new MemoryStream(bytes))
            {
                var report = Serializer().ReadObject(stream) as Part3Report;
                Validate(report);
                return report;
            }
        }

        public static Part3Report Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length <= 0 || stream.Length > Limit)
                    throw new InvalidDataException("请选择零件 3 专项成绩 JSON 文件。");
                using (var reader = new BinaryReader(stream))
                    return Decode(reader.ReadBytes((int)stream.Length));
            }
        }

        private static void Validate(Part3Report report)
        {
            if (report == null || report.Rubric != Part3Report.Version ||
                !Label(report.StudentId, 32) || !Label(report.StudentName, 40) || !Label(report.ModelName, 150) ||
                report.ModelHash?.Length != 64 || !report.ModelHash.All(Uri.IsHexDigit) || report.CreatedUtc == default ||
                report.Lines == null || report.Lines.Length < 2 || report.Lines.Length > 24 ||
                report.Lines.Any(line => line == null || !Label(line.Step, 36) || !Label(line.Criterion, 42) ||
                    !Label(line.Detail, 1000) || line.Maximum < 1 || line.Maximum > 20 || line.Points < 0 || line.Points > line.Maximum) ||
                report.Lines.Sum(line => line.Maximum) != 100)
                throw new InvalidDataException("成绩内容不符合零件 3 专项格式。");
        }

        private static bool Label(string value, int max) => !string.IsNullOrWhiteSpace(value) &&
            value.Length <= max && !value.Any(char.IsControl);
    }

    public sealed class ClassroomTransport : IDisposable
    {
        internal const int Port = 47936;
        private const string Query = "SWGrade Part3 discovery v1";
        private const string Reply = "SWGrade Part3 display v1";
        private readonly UdpClient discovery;
        private readonly TcpListener listener;
        private readonly Action<Part3Report> receive;
        public string Code { get; }

        public ClassroomTransport(Action<Part3Report> receive)
        {
            this.receive = receive;
            var random = new byte[4];
            using (var generator = RandomNumberGenerator.Create())
                generator.GetBytes(random);
            Code = (BitConverter.ToUInt32(random, 0) % 1000000).ToString("D6");
            listener = new TcpListener(IPAddress.Any, Port);
            listener.Start(8);
            try { discovery = new UdpClient(Port); }
            catch { listener.Stop(); throw; }
            _ = AnnounceAsync();
            _ = ReceiveAsync();
        }

        private async Task AnnounceAsync()
        {
            try
            {
                while (true)
                {
                    var request = await discovery.ReceiveAsync().ConfigureAwait(false);
                    if (request.Buffer.Length != Query.Length || Encoding.ASCII.GetString(request.Buffer) != Query)
                        continue;
                    var bytes = Encoding.ASCII.GetBytes(Reply);
                    await discovery.SendAsync(bytes, bytes.Length, request.RemoteEndPoint).ConfigureAwait(false);
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task ReceiveAsync()
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
                using (client)
                {
                    client.ReceiveTimeout = client.SendTimeout = 3500;
                    using (var stream = client.GetStream())
                    using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                    {
                        try
                        {
                            string code = Encoding.ASCII.GetString(reader.ReadBytes(6));
                            if (code != Code) { stream.WriteByte(2); continue; }
                            int size = reader.ReadInt32();
                            if (size <= 0 || size > ClassroomFiles.Limit) { stream.WriteByte(3); continue; }
                            var data = reader.ReadBytes(size);
                            if (data.Length != size) { stream.WriteByte(3); continue; }
                            receive(ClassroomFiles.Decode(data));
                            stream.WriteByte(1);
                        }
                        catch (Exception)
                        {
                            try { stream.WriteByte(3); } catch (IOException) { }
                        }
                    }
                }
            }
        }

        public static async Task<IPAddress[]> FindAsync()
        {
            var found = new HashSet<IPAddress>();
            using (var socket = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true })
            {
                // Loopback also allows testing both windows on the same computer.
                var query = Encoding.ASCII.GetBytes(Query);
                foreach (var address in new[] { IPAddress.Broadcast, IPAddress.Loopback })
                    await socket.SendAsync(query, query.Length, new IPEndPoint(address, Port));
                var deadline = Task.Delay(1350);
                while (!deadline.IsCompleted)
                {
                    UdpReceiveResult packet;
                    try
                    {
                        var response = socket.ReceiveAsync();
                        if (await Task.WhenAny(deadline, response) != response)
                            break;
                        packet = await response;
                    }
                    catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        // Windows reports an unreachable UDP destination as a reset; other destinations can still reply.
                        continue;
                    }
                    if (packet.RemoteEndPoint.Port == Port && packet.Buffer.Length == Reply.Length &&
                        Encoding.ASCII.GetString(packet.Buffer) == Reply)
                        found.Add(packet.RemoteEndPoint.Address);
                }
            }
            return found.ToArray();
        }

        public static async Task SendAsync(IPAddress address, string code, Part3Report report)
        {
            if (code?.Length != 6 || code.Any(c => c < '0' || c > '9'))
                throw new ArgumentException("请输入教师大屏上的六位课堂码。");
            var data = ClassroomFiles.Encode(report);
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                var connection = client.ConnectAsync(address, Port);
                if (await Task.WhenAny(connection, Task.Delay(3500)) != connection)
                    throw new IOException("无法连接教师大屏，请核对局域网地址，或导出成绩文件。");
                await connection;
                await Task.Run(() =>
                {
                    client.ReceiveTimeout = client.SendTimeout = 5000;
                    using (var stream = client.GetStream())
                    using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                    {
                        writer.Write(Encoding.ASCII.GetBytes(code));
                        writer.Write(data.Length);
                        writer.Write(data);
                        writer.Flush();
                        int result = stream.ReadByte();
                        if (result != 1)
                            throw new IOException(result == 2 ? "课堂码不正确。" : "大屏未确认接收成绩。");
                    }
                });
            }
        }

        public void Dispose()
        {
            discovery.Close();
            listener.Stop();
        }
    }
}
