using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Moonlight.Core
{
    // DNS-SD legacy unicast query: an ephemeral source port asks mDNS responders
    // to reply directly, avoiding contention with the system's port 5353 listener.
    public static class Discovery
    {
        public static async Task<List<Host>> FindAsync()
        {
            var found = new Dictionary<string, Host>(StringComparer.OrdinalIgnoreCase);
            using (var socket = new DatagramSocket())
            {
                socket.MessageReceived += (s, e) =>
                {
                    try
                    {
                        using (var reader = e.GetDataReader())
                        {
                            var bytes = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(bytes);
                            foreach (var host in Parse(bytes, e.RemoteAddress.CanonicalName))
                                lock (found) found[host.Address + ":" + host.HttpPort] = host;
                        }
                    }
                    catch (Exception) { /* A malformed UDP reply must not stop discovery. */ }
                };
                await socket.BindServiceNameAsync("0");
                var query = new List<byte> { 0x4d, 0x4c, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
                foreach (var part in "_nvstream._tcp.local".Split('.')) { query.Add((byte)part.Length); query.AddRange(Encoding.UTF8.GetBytes(part)); }
                query.AddRange(new byte[] { 0, 0, 12, 0, 1 });
                for (int i = 0; i < 3; i++)
                {
                    using (var output = await socket.GetOutputStreamAsync(new HostName("224.0.0.251"), "5353"))
                    using (var writer = new DataWriter(output)) { writer.WriteBytes(query.ToArray()); await writer.StoreAsync(); }
                    await Task.Delay(1000);
                }
            }
            lock (found) return found.Values.ToList();
        }
        private static int U16(byte[] data, int offset)
        {
            if (offset < 0 || offset + 2 > data.Length) throw new FormatException();
            return (data[offset] << 8) | data[offset + 1];
        }
        private static string Name(byte[] data, ref int offset)
        {
            int cursor = offset, end = -1, jumps = 0;
            var labels = new List<string>();
            while (true)
            {
                if (cursor >= data.Length || ++jumps > 128) throw new FormatException();
                int size = data[cursor++];
                if (size == 0) break;
                if ((size & 0xc0) == 0xc0)
                {
                    if (cursor >= data.Length) throw new FormatException();
                    if (end < 0) end = cursor + 1;
                    cursor = ((size & 0x3f) << 8) | data[cursor];
                }
                else
                {
                    if (size > 63 || cursor + size > data.Length) throw new FormatException();
                    labels.Add(Encoding.UTF8.GetString(data, cursor, size)); cursor += size;
                }
            }
            offset = end < 0 ? cursor : end;
            return string.Join(".", labels);
        }
        public static List<Host> Parse(byte[] data, string sender)
        {
            if (data.Length < 12 || (data[2] & 0x80) == 0) return new List<Host>();
            int offset = 12, questions = U16(data, 4), records = U16(data, 6) + U16(data, 8) + U16(data, 10);
            if (questions + records > 1024) throw new FormatException();
            for (int i = 0; i < questions; i++) { Name(data, ref offset); offset += 4; }
            var hosts = new List<Tuple<string, string, int>>();
            var addresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < records; i++)
            {
                string name = Name(data, ref offset);
                int type = U16(data, offset), length = U16(data, offset + 8); offset += 10;
                int end = offset + length;
                if (end > data.Length) throw new FormatException();
                if (type == 33 && length >= 7 && name.EndsWith("._nvstream._tcp.local", StringComparison.OrdinalIgnoreCase))
                {
                    int port = U16(data, offset + 4), p = offset + 6;
                    hosts.Add(Tuple.Create(name.Substring(0, name.Length - "._nvstream._tcp.local".Length), Name(data, ref p), port));
                }
                else if (type == 1 && length == 4) addresses[name] = string.Join(".", data.Skip(offset).Take(4));
                offset = end;
            }
            return hosts.Select(h => new Host { Name = h.Item1, Address = addresses.ContainsKey(h.Item2) ? addresses[h.Item2] : sender,
                HttpPort = h.Item3, HttpsPort = h.Item3 - 5 }).Where(h => h.HttpPort > 0 && h.HttpsPort > 0).ToList();
        }
    }
}
