using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Moonlight.Core
{
    public static class WakeOnLan
    {
        public static async Task SendAsync(Host host)
        {
            if (string.IsNullOrEmpty(host.Mac)) throw new InvalidOperationException("The MAC address is not available yet. Connect to the PC while it is on first.");
            var mac = Crypto.Unhex(host.Mac.Replace(":", "").Replace("-", ""));
            if (mac.Length != 6 || mac.All(b => b == 0)) throw new InvalidOperationException("The PC did not provide a valid MAC address.");
            byte[] packet = new byte[102];
            for (int i = 0; i < 6; i++) packet[i] = 255;
            for (int i = 0; i < 16; i++) Array.Copy(mac, 0, packet, 6 + i * 6, 6);
            using (var socket = new DatagramSocket())
            {
                foreach (var address in new[] { "255.255.255.255", host.Address })
                foreach (var port in new[] { "9", "47998" })
                using (var output = await socket.GetOutputStreamAsync(new HostName(address), port))
                using (var writer = new DataWriter(output)) { writer.WriteBytes(packet); await writer.StoreAsync(); }
            }
        }
    }
}
