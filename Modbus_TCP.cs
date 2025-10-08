using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace System.IO.Ports
{
    internal class Modbus_TCP
    {
        private IConfiguration _config;
        public ILogger _logger;
        public double ReadTimeout = 3000;
        public Modbus_TCP(IConfiguration<Modbus_TCP> config, ILogger<Modbus_TCP> logger)
        {
            _config = config;
            _logger = logger;
        }

        public CommandPacket SendAndRecive(byte[] send)
        {
            TcpClient tcp = new TcpClient();
            tcp.Connect("plc-hmi-ip", 502);

            if (tcp.Connected)
            {
                byte[] data_s;
                using (var s = new MemoryStream())
                {
                    s.WriteAsync(send);
                    //s.CopyTo(tcp.GetStream());
                    s.Flush();
                    data_s = s.ToArray();
                }
                tcp.Client.Send(data_s, 0, data_s.Length, SocketFlags.None);

                byte[] tmp = new byte[16];
                byte[] data_r = null;
                MbapHeader? header = null;
                CommandPacket? packet = null;
                int Size = Marshal.SizeOf<MbapHeader>();

                using (var s = new MemoryStream())
                {
                    var t1 = DateTime.Now;
                    for (; ; )
                    {
                        var t2 = DateTime.Now - t1;
                        if (t2.TotalMilliseconds > ReadTimeout)
                        {
                            // timeout
                            break;
                        }
                        if (tcp.Available > 0)
                        {
                            int cnt = tcp.Client.Receive(tmp);
                            s.Write(tmp, 0, cnt);

                            //當前封包總長度 
                            var total_length = s.Length;

                            //檢查 Heater 
                            if (header == null && total_length >= Size)
                            {
                                byte[] buf = s.ToArray();
                                unsafe
                                {
                                    fixed (byte* ptr = buf)
                                        header = *(MbapHeader*)ptr;
                                }
                                //建立封包結構
                                packet = new CommandPacket
                                {
                                    Header = header.Value,
                                    Data = new byte[header.Value.Length] //初始化 
                                };
                            }

                            //檢查數據
                            if (header != null && total_length >= Size + header.Value.Length)
                            {
                                byte[] buf = s.ToArray();
                                Array.Copy(buf, Size, packet.Data, 0, header.Value.Length);

                                _logger.LogDebug(
                                    $"Recv data : {JsonConvert.SerializeObject(packet.Header)}, " +
                                    $"Data = [{packet.Data.ToHexString(",")}]"
                                );


                                //收到完整封包，跳出 
                                return packet;
                            }
                        }
                    }
                    data_r = s.ToArray();
                }
            }
            return default;
        }

        /// <summary> 同步範例 </summary>
        //public void tcp_demo2()
        //{
        //    TcpClient tcp = new TcpClient();
        //    await tcp.ConnectAsync("plc-hmi-ip", 502);
        //    if (tcp.Connected)
        //    {
        //        byte[] data_s = new byte[1024];
        //        await tcp.Client.SendAsync(data_s, SocketFlags.None);
        //        byte[] data_r = new byte[1024];
        //        var task = tcp.Client.ReceiveAsync(data_r, SocketFlags.None);
        //        if (task.Wait(500))
        //        {
        //            int recv_count = task.Result;
        //        }
        //        else
        //        {
        //            // timeout
        //        }
        //    }
        //}

        private class CommandPacket
        {
            public MbapHeader Header { get; set; }
            public byte[] Data { get; set; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct MbapHeader
        {
            public ushort TransactionId;
            public ushort ProtocolId;
            public ushort Length;
            public byte UnitId;
        }


    }


}
