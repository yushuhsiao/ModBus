using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;


namespace System.IO.Ports
{
    public class ModBus_RTU
    {
        private IConfiguration _config;
        private ILogger _logger;
        private object _lock = new object();
        private SerialPort port;
        public event EventHandler PortChange;

        public double ReadTimeout = 5000;
        public double RecvComplete_IdleTime = 3.5; // 接收完成閒置時間
        public int ComPort = 1;
        public int BaudRate = 38400;
        public Parity Parity = Parity.None;
        public int DataBits = 8;
        public StopBits StopBits = StopBits.One;

        public string PortName => port?.PortName;
        public bool IsOpen => port?.IsOpen == true;

        public ModBus_RTU(IConfiguration<ModBus_RTU> config, ILogger<ModBus_RTU> logger)
        {
            _config = config;
            _logger = logger;
        }
        public SerialPort Open(bool force)
        {
            try
            {
                lock (_lock)
                {
                    if (force) this.Close();
                    if (this.port == null)
                    {
                        this.port = new SerialPort($"COM{ComPort}", BaudRate, Parity, DataBits, StopBits);
                        this.port.Open();
                        _logger.LogInformation($"{this.port.PortName}, {BaudRate}, {Parity}, {DataBits}, {StopBits}");
                        PortChange?.Invoke(this, EventArgs.Empty);
                    }
                    return this.port;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                return null;
            }
        }
        public void Close()
        {
            lock (_lock)
            {
                if (port == null) return;
                using (var p = port)
                {
                    _logger.LogInformation($"{p.PortName} Close.");
                    try { port?.Close(); }
                    catch { }
                    port = null;
                }
            }
            PortChange?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 傳送資料並且等候回應
        /// </summary>
        /// <param name="sendBuf">要傳送的資料</param>
        /// <returns> null : Timeout</returns>
        public ModBusResult SendAndGetResponse(ModBusStream stream, byte[] sendBuf)
        {
            SerialPort port = this.Open(false);
            if (port == null)
                return ModBusResult.PortNotOpen;
            if (port.IsOpen == false)
                return ModBusResult.PortNotOpen;

            DateTime beginTime = DateTime.Now;
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            port.Write(sendBuf, 0, sendBuf.Length);

            var result = new ModBusResult() { BeginTime = beginTime, SendData = sendBuf };
            //讀取
            byte[] recvBuf = new byte[16]; //
            TimeSpan t_Elapsed;
            TimeSpan t_Finish = TimeSpan.Zero;
            DateTime idle = DateTime.Now;
            int offset = 0;
            int recvCount = 0;
            double readTimeout = this.ReadTimeout;
            double readComplete_Idle = 1000.0 / (double)this.port.BaudRate * 1000 * RecvComplete_IdleTime;
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
                    offset += cnt;
                    idle = DateTime.Now;
                    recvCount++;
                    t_Finish = t_Elapsed;
                }
                else if (recvCount > 0)
                {
                    TimeSpan idle_time = DateTime.Now - idle;
                    if (idle_time.TotalMilliseconds >= readComplete_Idle)
                    {
                        // address + func code + crc = 4 bytes
                        if (offset >= 4)
                            result.ReadIndex = 3;
                        break;
                    }
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            Array.Resize(ref recvBuf, offset);
            result.RecvData = recvBuf;
            result.Elapsed = t_Finish.TotalMilliseconds;
            result.TotalElapsed = (DateTime.Now - beginTime).TotalMilliseconds;
            return result;
        }

        public enum FunctionCode
        {
            ReadOutputCoils = 0x01,
            ReadDiscreteInputs = 0x02,
            ReadHoldingRegister = 0x03,
            ReadInputRegisters = 0x04,
            WriteSingleOutput = 0x05,
            WriteHoldingRegister = 0x06,
            WriteMultipleOutputs = 0x0f,
            WriteMultipleHoldingRegister = 0x10,
        }
    }


    public class ModBusStream : MemoryStream
    {
        /// <summary>
        /// 寫入通訊站號(address)及功能(functionCode)
        /// </summary>
        /// <param name="address">通訊站號</param>
        /// <param name="functionCode">功能</param>
        public ModBusStream(int address, int functionCode)
        {
            base.WriteByte((byte)address);
            base.WriteByte((byte)functionCode);
        }

        public ModBusStream(int address, ModBus_RTU.FunctionCode functionCode) : this(address, (int)functionCode) { }

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

        public void WriteUInt16(ushort value)
        {
            base.WriteByte((byte)(value / 0x100));  //Hi 
            base.WriteByte((byte)(value % 0x100));  //Lo 
        }

        public void WriteInt32(int write_data)
        {
            base.WriteByte((byte)(write_data >> 24));
            base.WriteByte((byte)(write_data >> 16));
            base.WriteByte((byte)(write_data >> 8));
            base.WriteByte((byte)write_data);
        }

        //public void WriteFloat32(ushort value)
        //{
        //    byte[] floatBytes = new byte[4];
        //    BitConverter.GetBytes(value).CopyTo(floatBytes, 0);
        //    uint floatBits = BitConverter.ToUInt32(floatBytes, 0);
        //    base.WriteByte((byte)(floatBits / 0x100));  //Hi 
        //    base.WriteByte((byte)(floatBits % 0x100));  //Lo 
        //}

        //public void WriteFloat64(ushort value)
        //{
        //    byte[] doubleBytes = new byte[8];
        //    BitConverter.GetBytes(value).CopyTo(doubleBytes, 0);
        //    ulong doubleBits = BitConverter.ToUInt64(doubleBytes, 0);
        //    base.WriteByte((byte)(doubleBits / 0x100));  //Hi 
        //    base.WriteByte((byte)(doubleBits % 0x100));  //Lo 
        //}


    }

    public class ModBusResult
    {
        public static readonly ModBusResult PortNotOpen = new ModBusResult();
        public static readonly ModBusResult Timeout = new ModBusResult();

        public bool IsTimeout => object.ReferenceEquals(this, Timeout) || RecvData.Length == 0;
        public bool IsSuccess => SendData != null && RecvData != null && VerifyCRC;

        public byte[] SendData = Array.Empty<byte>();
        public byte[] RecvData = Array.Empty<byte>();
        public DateTime BeginTime;
        public double Elapsed;
        public double TotalElapsed;

        public int Address_Out => RecvData.Get(0);
        public int Address_In => SendData.Get(0);
        public int FunctionCode_Out => RecvData.Get(1);
        public int FunctionCode_In => SendData.Get(1);
        public ushort CRC_Out => GetCRC(RecvData);
        public ushort CRC_In => GetCRC(SendData);

        private static ushort GetCRC(byte[] buffer)
        {
            if (buffer.Length > 2)
                return BitConverter.ToUInt16(buffer, buffer.Length - 2);
            return 0;
        }

        public bool VerifyCRC => CRC_Out == CalcCRC() && RecvData.Length > 2;

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

        public int ReadInt32()
        {
            int value = 0;
            for (int i = 0; i < 4; i++, value <<=8)
                value |= this.ReadByte();
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
                return BitConverter.ToUInt16(tmp);
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
                return BitConverter.ToDouble(tmp);
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
                return BitConverter.ToDouble(tmp);
            }
            catch { }
            return 0;
        }

        public float ReadFloat32_2()
        {
            try
            {
                byte[] tmp = new byte[4];
                Array.Copy(RecvData, ReadIndex, tmp, 0, tmp.Length);
                ReadIndex += tmp.Length;
                Array.Reverse(tmp, 0, 2);
                Array.Reverse(tmp, 2, 2);
                return BitConverter.ToSingle(tmp);
            }
            catch { }
            return 0;
        }

        public float ReadFloat32()
        {
            try
            {
                byte[] array = new byte[4];
                Array.Copy(RecvData, 3, array, 0, array.Length);
                Array.Reverse(array);
                return BitConverter.ToSingle(array); 
            }
            catch {}
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
                return BitConverter.ToInt64(tmp);
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
            if (RecvData.Length > 2)
                return Crypto.ModRTU_CRC(RecvData, RecvData.Length - 2);
            return 0;
        }

        public override string ToString() => $@"{BeginTime}
Send : {SendData?.ToHexString(" ")} ,CRC = {CRC_In.ToString("X")}
Recv : {RecvData?.ToHexString(" ")} ,CRC = {CRC_Out.ToString("X")}
Time : {Elapsed}ms, Total : {TotalElapsed}ms";
    }
}