using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace StoreSystem.Api
{
    /// <summary>
    /// Desktop ilovasi serverni avtomatik topishi uchun UDP "discovery" xizmati.
    /// Bir tarmoqdagi (WiFi/LAN) Desktop ilova "JESKO_DISCOVERY_V1?" so'rovini
    /// broadcast qiladi, server esa o'z manzili (port) bilan javob beradi.
    /// Shu tufayli mijoz IP manzilni qo'lda yozishi shart emas.
    ///
    /// Bu xizmat IXTIYORIY qulaylik: agar biror sabab bilan ishlamasa,
    /// server baribir oddiy tarzda (qo'lda IP yozish orqali) ishlayveradi.
    /// </summary>
    public sealed class DiscoveryService : IDisposable
    {
        // Discovery uchun maxsus UDP port (HTTP portidan alohida).
        public const int DiscoveryPort = 51999;

        // Javob oldidagi belgi — Desktop shu belgidan keyingi JSON'ni o'qiydi.
        private const string ReplyPrefix = "JESKO_DISCOVERY_V1!";

        private readonly int _httpPort;
        private UdpClient? _udp;
        private Thread? _thread;
        private volatile bool _running;

        public DiscoveryService(int httpPort)
        {
            _httpPort = httpPort;
        }

        public void Start()
        {
            if (_running) return;

            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.EnableBroadcast = true;
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "JESKO-Discovery" };
            _thread.Start();
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var data = _udp!.Receive(ref remote); // bloklab kutadi
                    var text = Encoding.UTF8.GetString(data);

                    if (text.StartsWith("JESKO_DISCOVERY", StringComparison.OrdinalIgnoreCase))
                    {
                        var payload = ReplyPrefix + JsonSerializer.Serialize(new
                        {
                            app = "JESKO",
                            name = Environment.MachineName,
                            httpPort = _httpPort,
                            version = "1.0.0"
                        });
                        var reply = Encoding.UTF8.GetBytes(payload);
                        _udp.Send(reply, reply.Length, remote);
                    }
                }
                catch
                {
                    // Socket yopilganda yoki vaqtinchalik xatoda — bir oz kutib davom etamiz.
                    // Xatolar server ishini to'xtatmaydi.
                    Thread.Sleep(150);
                }
            }
        }

        public void Stop()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            try { _udp?.Dispose(); } catch { }
            _udp = null;
        }

        public void Dispose() => Stop();
    }
}
