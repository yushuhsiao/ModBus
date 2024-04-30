using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Threading;

namespace Leader
{
    public class ModBus
    {
        public const string ConfigSection = "ModBus";
        private ILogger _logger;
        private IConfiguration _config;
        /// <summary>
        /// 多少毫秒之後沒有收到資料就視為接收完成
        /// </summary>
        private const int RecvComplete_IdleTime = 100;
        private object _lock = new object(); // modbus 的傳送/接收時間必須隔開
        private SerialPort port;

        public event EventHandler PortChange;

        public ModBus(IServiceProvider service)
        {
            _logger = service.GetService<ILogger<ModBus>>();
            _config = service.GetService<IConfiguration<ModBus>>();
        }

        public double ReadTimeout { get; set; } = 5000;

        public int BaudRate { get; set; } = 9600;

        public Parity Parity { get; set; } = Parity.None;

        public int DataBits { get; set; } = 8;

        public StopBits StopBits { get; set; } = StopBits.One;

        public string PortName { get; set; }

        public bool IsOpen => port?.IsOpen == true;

        public void Open()
        {
            lock (_lock)
            {
                this.Close();
                SerialPort port = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits);
                port.Open();
                this.port = port;
            }
            PortChange?.Invoke(this, EventArgs.Empty);
        }

        public void Close()
        {
            lock (_lock)
            {
                if (port == null) return;
                using (var p = port)
                {
                    try { port?.Close(); }
                    catch { }
                    port = null;
                }
            }
            PortChange?.Invoke(this, EventArgs.Empty);
        }



        public class RTUStream : MemoryStream
        {
            /// <summary>
            /// 寫入通訊站號(address)及功能(functionCode)
            /// </summary>
            /// <param name="address">通訊站號</param>
            /// <param name="functionCode">功能</param>
            public RTUStream(int address, int functionCode)
            {
                base.WriteByte((byte)address);
                base.WriteByte((byte)functionCode);
            }

            public void WriteUInt16(ushort value)
            {
                base.WriteByte((byte)(value / 0x100));  //Hi 
                base.WriteByte((byte)(value % 0x100));  //Lo 
            }

            /// <summary>
            /// 計算 CRC 並且將資料轉換為 byte[]
            /// </summary>
            /// <returns></returns>
            public override byte[] ToArray()
            {
                base.Flush();
                byte[] sendBuf = base.ToArray();
                var crc = Crypto.ModRTU_CRC(sendBuf);
                base.WriteByte((byte)(crc % 0x100));
                base.WriteByte((byte)(crc / 0x100));
                base.Flush();
                return base.ToArray();
            }

            /// <summary>
            /// 傳送資料並且等候回應
            /// </summary>
            /// <param name="sendBuf">要傳送的資料</param>
            /// <returns> null : Timeout</returns>
            public RTUResult SendAndGetResponse(ModBus modBus)
            {
                lock (modBus._lock)
                {
                    var port = modBus.port;
                    if (port?.IsOpen != true)
                        return RTUResult.PortNotOpen;

                    DateTime beginTime = DateTime.Now;
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();

                    byte[] sendBuf = this.ToArray();
                    port.Write(sendBuf, 0, sendBuf.Length);

                    //讀取
                    byte[] recvBuf = new byte[16];
                    TimeSpan t_Elapsed;
                    TimeSpan t_Finish = TimeSpan.Zero;
                    DateTime idle = DateTime.Now;
                    int offset = 0;
                    int recvCount = 0;
                    double readTimeout = this.ReadTimeout;
                    while (port.IsOpen)
                    {
                        t_Elapsed = DateTime.Now - beginTime;
                        if (t_Elapsed.TotalMilliseconds > readTimeout)
                            break;
                        if (port.BytesToRead > 0)
                        {
                            int len = recvBuf.Length - offset;
                            if (len == 0)
                            {
                                Array.Resize(ref recvBuf, recvBuf.Length + 16);
                                len = recvBuf.Length - offset;
                            }
                            int cnt = port.Read(recvBuf, offset, len);
                            //_logger.LogInformation($"RS232 Read: {cnt} {recvBuf.ToHexString()}");
                            offset += cnt;
                            idle = DateTime.Now;
                            recvCount++;
                            t_Finish = t_Elapsed;
                        }
                        else if (recvCount > 0)
                        {
                            TimeSpan idle_time = DateTime.Now - idle;
                            if (idle_time.TotalMilliseconds >= ModBus.RecvComplete_IdleTime)
                            {
                                // address + func code + crc = 4 bytes
                                if (offset >= 4)
                                {
                                    Array.Resize(ref recvBuf, offset);
                                    return new RTUResult
                                    {
                                        SendData = sendBuf,
                                        RecvData = recvBuf,
                                        BeginTime = beginTime,
                                        Elapsed = t_Finish.TotalMilliseconds,
                                        TotalElapsed = (DateTime.Now - beginTime).TotalMilliseconds,
                                        ReadIndex = 3
                                    };
                                }
                                break;
                            }
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }
                    return RTUResult.Timeout;
                }
            }
        }

        public class RTUResult
        {
            public static readonly RTUResult PortNotOpen = new RTUResult();
            public static readonly RTUResult Timeout = new RTUResult();

            public bool IsTimeout => object.ReferenceEquals(this, Timeout);
            public bool IsSuccess => SendData != null && RecvData != null;

            public byte[] SendData;
            public byte[] RecvData;
            public DateTime BeginTime;
            public double Elapsed;
            public double TotalElapsed;

            public int Address_Out => RecvData[0];
            public int Address_In => SendData[0];
            public int FunctionCode_Out => RecvData[1];
            public int FunctionCode_In => SendData[1];
            public ushort CRC_Out => GetCRC(RecvData);
            public ushort CRC_In => GetCRC(SendData);

            private static ushort GetCRC(byte[] buffer)
            {
                if (buffer.Length > 2)
                    return BitConverter.ToUInt16(buffer, buffer.Length - 2);
                return 0;
            }

            public bool VerifyCRC => CRC_Out == CalcCRC();

            public int ReadInt16()
            {
                // 00000000 01010101
                // 00000000 01010111 
                //
                // 01010101 00000000
                // 00000000 01010111 
                short value = this.ReadByte();
                value <<= 8;
                value |= (short)this.ReadByte();
                return value;
            }

            public ushort ReadUInt16()
            {
                try
                {
                    byte[] tmp = new byte[2];
                    Array.Copy(RecvData, ReadIndex, tmp, 0, tmp.Length);
                    ReadIndex += tmp.Length;
                    Array.Reverse(tmp);
                    return BitConverter.ToUInt16(tmp, 0);
                }
                catch { }
                return 0;
            }

            public double ReadFloat64()
            {
                try
                {
                    byte[] tmp = new byte[8];
                    Array.Copy(RecvData, ReadIndex, tmp, 0, tmp.Length);
                    ReadIndex += tmp.Length;
                    Array.Reverse(tmp);
                    return BitConverter.ToDouble(tmp, 0);
                }
                catch { }
                return 0;
            }

            public double ReadFloat64_2()
            {
                try
                {
                    byte[] tmp = new byte[8];
                    Array.Copy(RecvData, ReadIndex, tmp, 0, tmp.Length);
                    ReadIndex += tmp.Length;
                    Array.Reverse(tmp, 0, 2);
                    Array.Reverse(tmp, 2, 2);
                    Array.Reverse(tmp, 4, 2);
                    Array.Reverse(tmp, 6, 2);
                    //Array.Reverse(tmp);
                    return BitConverter.ToDouble(tmp, 0);
                }
                catch { }
                return 0;
            }

            public float ReadFloat32()
            {
                try
                {
                    byte[] tmp = new byte[4];
                    Array.Copy(RecvData, ReadIndex, tmp, 0, tmp.Length);
                    ReadIndex += tmp.Length;
                    Array.Reverse(tmp, 0, 2);
                    Array.Reverse(tmp, 2, 2);
                    return BitConverter.ToSingle(tmp, 0);
                }
                catch { }
                return 0;
            }

            public long ReadInt64()
            {
                try
                {
                    byte[] tmp = new byte[8];
                    Array.Copy(RecvData, ReadIndex, tmp, 0, tmp.Length);
                    ReadIndex += tmp.Length;
                    Array.Reverse(tmp);
                    return BitConverter.ToInt64(tmp, 0);
                }
                catch { }
                return 0;
            }

            public void Skip(int count = 1) => ReadIndex += count;

            public int ReadIndex { get; set; } = 3;

            public byte ReadByte()
            {
                if ((RecvData.Length - 2) > ReadIndex)
                    return RecvData[ReadIndex++];
                return 0;
            }

            public ushort CalcCRC()
            {
                return Crypto.ModRTU_CRC(RecvData, RecvData.Length - 2);
            }

            public override string ToString()
            {
                return $@"{BeginTime}
Send : {SendData.ToHexString(" ")} , CRC = {Crypto.ModRTU_CRC(SendData, SendData.Length - 2).ToString("X")}
Recv : {RecvData.ToHexString(" ")} , CRC = {Crypto.ModRTU_CRC(RecvData, RecvData.Length - 2).ToString("X")}
Time : {Elapsed}ms";
            }
        }
    }
}