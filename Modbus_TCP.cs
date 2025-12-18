using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using static System.IO.Ports.ModBus_TCP;

namespace System.IO.Ports
{
    public class ModBus_TCP
    {
        private IConfiguration _config;
        public ILogger _logger;
        
        public ModBus_TCP(IConfiguration<ModBus_TCP> config, ILogger<ModBus_TCP> logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool IsConnected(TcpClient client) => client?.Connected == true;

        public (bool, ModBusTCPStream.CommandPacket) WriteAndRead(TcpClient _tcp, string _ip, int _port, short _address, short _quantity)
        {
            byte[] PDU = ModBusTCPStream.PDU(ModBus_TCP.FunctionCode.ReadHoldingRegisters, _address, _quantity);
            using (ModBusTCPStream w1 = new ModBusTCPStream(PDU.Length/*PDU*/ + 1/*Unit*/))
            {
                w1.Write(PDU);
                byte[] sendBuf = w1.ToArray();
                var packet = w1.SendAndReceive(_tcp, _ip, _port, sendBuf, _logger);
                if (packet == null || packet.Data == null || packet.Data.Length < 8)
                    return (false, new ModBusTCPStream.CommandPacket());
                return (true, packet);
            }
        }

        /// 同步範例 
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

        public enum FunctionCode
        {
            ReadHoldingRegisters = 0x03,
            WriteSingleResister = 0x06,
            WriteMultipleRegisters = 0x16,
        }

    }

    /// <summary>
    /// Modbus TCP 協定使用 Big Endian（高位在前），而 C# 預設是 Little Endian
    /// </summary>
    public class ModBusTCPStream : MemoryStream
    {
        public class CommandPacket
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

        public static byte[] PDU(ModBus_TCP.FunctionCode FunctionCode, short Address, short Quentity)
        {
            using var pdu = new MemoryStream();
            pdu.WriteByte((byte)FunctionCode);

            var address = BitConverter.GetBytes(Address);
            var quantity = BitConverter.GetBytes(Quentity);
            if (BitConverter.IsLittleEndian)
            {
                address = address.Reverse().ToArray();
                quantity = quantity.Reverse().ToArray();
            }
            pdu.WriteByte(address[0]);
            pdu.WriteByte(address[1]);
            pdu.WriteByte(quantity[0]);
            pdu.WriteByte(quantity[1]);

            return pdu.ToArray();
        }

        /// <summary>
        ///┌────────────────────────────┐ <br />
        ///│ Transaction Identifier│(2 bytes) <br />
        ///│ Protocol Identifier   │(2 bytes) 固定 0x0000 <br />
        ///│ Length                │(2 bytes) 從 Unit Identifier 開始的長度 <br />
        ///│ Unit Identifier       │(1 byte)  通常為 0xFF 或 0x01，用於區分多設備（Modbus TCP 通常固定）<br />
        ///│ Function Code         │(1 byte)  定義操作類型（如讀取、寫入）<br />
        ///│ Data                  │(N bytes) (adddress + quentity) <br />
        ///└────────────────────────────┘ <br />
        /// Length = 1 (Unit Identifier) + 1 (Function Code) + Data <br />
        /// PDU = Function Code + Data
        /// </summary>
        public ModBusTCPStream( /*Transaction*/ /*Protocol*/ /*Length*/ /*unit*/ int Length)
        {
            base.WriteByte((byte)0x00);                        //Transaction 
            base.WriteByte((byte)0x01);                        //
            base.WriteByte((byte)0x00);                        //Protocol 
            base.WriteByte((byte)0x00);                        //
            //                                                 //
            short _Length = (short)Length;                     //Length
            var lengthBytes = BitConverter.GetBytes(_Length);  //
            if (BitConverter.IsLittleEndian)                   //
                lengthBytes = lengthBytes.Reverse().ToArray(); //
            base.WriteByte(lengthBytes[0]);                    //
            base.WriteByte(lengthBytes[1]);                    //
            //                                                 //
            base.WriteByte((byte)0x01);                        //Unit

        }

        public CommandPacket SendAndReceive(TcpClient tcp, string _ip, int _port, byte[] send, ILogger _logger, int _ReadTimeout = 3000)
        {
            if (send == null) throw new ArgumentNullException(nameof(send));
            try
            {
                if (!tcp.Connected)
                {
                    _logger.LogError("Modbus_TCP 連線失敗");
                    return default;
                }

                var stream = tcp.GetStream();
                tcp.ReceiveTimeout = _ReadTimeout; // ReadTimeout 改為常數或傳入參數
                tcp.SendTimeout = 5000;
                //Write
                stream.Write(send, 0, send.Length);

                //Read 
                byte[] tmp = new byte[1024];
                int headerSize = Marshal.SizeOf<MbapHeader>();
                MbapHeader? header = null;
                var sw = Stopwatch.StartNew();
                using (var s = new MemoryStream())
                {
                    while (sw.Elapsed.TotalMilliseconds <= _ReadTimeout)
                    {
                        if (stream.DataAvailable)
                        {
                            var cnt = stream.Read(tmp, 0, tmp.Length);
                            if (cnt == 0) break;

                            s.Write(tmp, 0, cnt);
                            var total_length = s.Length;

                            if (header == null && total_length >= headerSize)
                            {
                                byte[] buf = s.GetBuffer();
                                header = new MbapHeader
                                {
                                    TransactionId = (ushort)((buf[0] << 8) | buf[1]),
                                    ProtocolId = (ushort)((buf[2] << 8) | buf[3]),
                                    Length = (ushort)((buf[4] << 8) | buf[5]),
                                    UnitId = buf[6]
                                };

                                var pdulength = header.Value.Length - 1;
                                if (pdulength < 0)
                                {
                                    _logger.LogWarning("Modbus_TCP 負的 PDU 長度: {PDU}", pdulength);
                                    break;
                                }

                                if (total_length >= headerSize + pdulength)
                                {
                                    var packet = new CommandPacket
                                    {
                                        Header = header.Value,
                                        Data = new byte[pdulength]
                                    };
                                    Array.Copy(buf, headerSize, packet.Data, 0, packet.Data.Length);
                                    return packet;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception /*ex*/)
            {
                _logger.LogError("Modbus_TCP 連線異常");
                try { tcp?.Close(); tcp?.Dispose(); } catch { }
                tcp = null;
            }
            return default;
        }
        public static void DisconnectSafely(TcpClient tcp)
        {
            try { tcp?.Close(); tcp?.Dispose(); } catch { }
            tcp = null;
        }

    }

}
