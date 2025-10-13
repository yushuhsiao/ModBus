using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using static System.IO.Ports.ModBus_TCP;

namespace System.IO.Ports
{
    public class ModBus_TCP
    {
        private IConfiguration _config;
        public ILogger _logger;
        public double ReadTimeout = 3000;
        public ModBus_TCP(IConfiguration<ModBus_TCP> config, ILogger<ModBus_TCP> logger)
        {
            _config = config;
            _logger = logger;
        }

        public CommandPacket SendAndRecive(byte[] send, string IP, int Port)
        {
            try
            {
                TcpClient tcp = new TcpClient();
                tcp.Connect(IP, Port);

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
                                try
                                {
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

                                    //檢查 Data
                                    if (header != null && total_length >= Size + header.Value.Length)
                                    {
                                        byte[] buf = s.ToArray();
                                        Array.Copy(buf, Size, packet.Data, 0, header.Value.Length);

                                        _logger.LogDebug(
                                            $"Recv data : {JsonConvert.SerializeObject(packet.Header)}, " +
                                            $"Data = [{packet.Data.ToHexString(",")}]"
                                        );

                                        //收到完整封包，返回 
                                        return packet;
                                    }
                                }
                                catch { break; }
                            }
                        }
                    }
                }
            }
            catch { }
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

        public enum FunctionCode
        {

            ReadHoldingRegisters = 0x03,
            WriteSingleResister = 0x06,
            WriteMultipleRegisters = 0x16,
        }

    }

    public class ModBusTCPStream : MemoryStream
    {
        public static byte[] PDU(ModBus_TCP.FunctionCode FunctionCode, byte Address, byte Quentity)
        {
            using var pduStream = new MemoryStream();
            pduStream.WriteByte((byte)FunctionCode);
            pduStream.WriteByte(Address);
            pduStream.WriteByte(Quentity);
            return pduStream.ToArray();
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
        public ModBusTCPStream(int Transaction, /*Protocol*/ /*Length*/ /*unit*/ byte[] PDU)
        {
            base.WriteByte((byte)Transaction);//Transaction 
            base.WriteByte((byte)0x0000);     //Protocol 
            base.WriteByte((byte)PDU.Length); //Length
            base.WriteByte((byte)0x01);       //Unit
            base.Write(PDU);                  //PDU = Function Code + Data

        }





    }

}
