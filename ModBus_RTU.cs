using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;


namespace System.IO.Ports
{
    public class ModBus_RTU : SerialPortBase
    {
        public double ReadTimeout = 5000;
        public double RecvComplete_IdleTime = 3.5; // 接收完成閒置時間

        public ModBus_RTU(IServiceProvider service) : base(service) { }
        public ModBus_RTU(ILogger logger) : base(logger) { }




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
            double readComplete_Idle = port.BaudRate > 19200 ? 1.75 : (((double)port.DataBits + 3.0) * RecvComplete_IdleTime * 1000.0) / port.BaudRate;
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

        public bool SendAndGetResponse(Data data)
        {
            DateTime beginTime = data.BeginTime = DateTime.Now;
            var sendBuf = data.GetSendBuffer();

            var port = this.Open(false);
            if (port == null)
            {
                data.PortNotOpen = true;
                return false;
            }
            if (port.IsOpen == false)
            {
                data.PortNotOpen = true;
                return false;
            }

            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            port.Write(sendBuf, 0, sendBuf.Length);

            //讀取
            var recvBuf = ArrayPool<byte>.Shared.Rent(256);
            TimeSpan t_Elapsed;
            TimeSpan t_Finish = TimeSpan.Zero;
            try
            {
                DateTime idle = DateTime.Now;
                int offset = 0;
                int recvCount = 0;
                double readTimeout = this.ReadTimeout;
                //double readComplete_Idle = 1000.0 / (double)port.BaudRate * 1000.0 * RecvComplete_IdleTime;
                double readComplete_Idle = port.BaudRate > 19200 ? 1.75 : (((double)port.DataBits + 3.0) * RecvComplete_IdleTime * 1000.0) / port.BaudRate;
                while (port.IsOpen)
                {
                    t_Elapsed = DateTime.Now - beginTime;
                    if (t_Elapsed.TotalMilliseconds > readTimeout)
                        break;
                    if (port.BytesToRead > 0)
                    {
                        int readLen = recvBuf.Length - offset;
                        if (readLen <= 0)
                            break;
                        int cnt = port.Read(recvBuf, offset, readLen);
                        offset += cnt;
                        idle = DateTime.Now;
                        recvCount++;
                        t_Finish = t_Elapsed;
                    }
                    else if (recvCount > 0)
                    {
                        var idle_time = DateTime.Now - idle;
                        if (idle_time.TotalMilliseconds >= readComplete_Idle)
                        {
                            // address + func code + crc = 4 bytes
                            if (offset >= 4)
                            {
                                data.ReadIndex = 3;
                                data.RecvData = recvBuf.AsSpan(0, offset).ToArray();
                                return true;
                            }
                            break;
                        }
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(recvBuf);
                data.Elapsed = t_Finish.TotalMilliseconds;
                data.TotalElapsed = (DateTime.Now - beginTime).TotalMilliseconds;
            }
            return false;
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

        public class Data
        {
            public Data(int address, ModBus_RTU.FunctionCode functionCode) : this(address, (int)functionCode) { }
            public Data(int address, int functionCode)
            {
                SendData = ArrayPool<byte>.Shared.Rent(256);
                this.WriteByte((byte)address);
                this.WriteByte((byte)functionCode);
            }

            public bool PortNotOpen { get; internal set; }
            public bool IsTimeout { get; internal set; }
            public bool IsSuccess => VerifyCRC;

            public byte[] SendData { get; internal set; } = Array.Empty<byte>();
            public byte[] RecvData { get; internal set; } = Array.Empty<byte>();
            public DateTime BeginTime { get; internal set; }
            public double Elapsed { get; internal set; }
            public double TotalElapsed { get; internal set; }

            public int Address_Out => RecvData.Get(0);
            public int Address_In => SendData.Get(0);
            public int FunctionCode_Out => RecvData.Get(1);
            public int FunctionCode_In => SendData.Get(1);
            public ushort CRC_Out => GetCRC(RecvData);
            public ushort CRC_In => GetCRC(SendData);
            public bool VerifyCRC => CRC_Out == CalcCRC(RecvData);

            private static ushort GetCRC(byte[] buffer)
            {
                if (buffer.Length > 2)
                    return BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(buffer.Length - 2, 2));
                return 0;
            }
            public ushort CalcCRC(byte[] buffer)
            {
                if (buffer.Length > 2)
                    return Crypto.ModRTU_CRC(buffer, buffer.Length - 2);
                return 0;
            }

            // for write

            private int send_index = 0;

            public void WriteByte(byte value)
            {
                if (send_index == -1)
                    return;
                SendData[send_index++] = value;
            }

            public void WriteUInt16(int value) => WriteUInt16((ushort)value);
            public void WriteUInt16(ushort value)
            {
                if (send_index == -1)
                    return;
                BinaryPrimitives.WriteUInt16BigEndian(SendData.AsSpan(send_index, sizeof(ushort)), value);
                send_index += sizeof(ushort);
            }

            public void WriteInt32(int write_data)
            {
                if (send_index == -1)
                    return;
                BinaryPrimitives.WriteInt32BigEndian(SendData.AsSpan(send_index, sizeof(int)), write_data);
                send_index += sizeof(int);
            }

            internal byte[] GetSendBuffer()
            {
                if (send_index == -1)
                    return SendData;
                byte[] sendBuf = new byte[send_index + sizeof(ushort)];
                SendData.AsSpan(0, send_index).CopyTo(sendBuf);
                ArrayPool<byte>.Shared.Return(SendData);
                this.SendData = sendBuf;
                send_index = -1;

                var crc = CalcCRC(sendBuf);
                BinaryPrimitives.WriteUInt16LittleEndian(sendBuf.AsSpan(sendBuf.Length - sizeof(ushort), sizeof(ushort)), crc);
                return sendBuf;
            }


            // for read

            public int ReadIndex { get; set; } = 3;

            private delegate T ReadPrimitivesDelegate<T>(ReadOnlySpan<byte> span);

            private T ReadPrimitives<T>(int size, ReadPrimitivesDelegate<T> readFunc)
            {
                var readIndex = this.ReadIndex + size;
                if (RecvData.Length - 2 >= readIndex)
                {
                    try
                    {
                        var value = readFunc(RecvData.AsSpan(ReadIndex, size));
                        ReadIndex = readIndex;
                        return value;
                    }
                    catch { }
                }
                return default;
            }

            public void Skip(int count = 1) => ReadIndex += count;
            public byte ReadByte()
            {
                if ((RecvData.Length - 2) > ReadIndex)
                    return RecvData[ReadIndex++];
                return 0;
            }
            public short ReadInt16() => ReadPrimitives(sizeof(short), BinaryPrimitives.ReadInt16BigEndian);
            public ushort ReadUInt16() => ReadPrimitives(sizeof(ushort), BinaryPrimitives.ReadUInt16BigEndian);
            public int ReadInt32() => ReadPrimitives(sizeof(int), BinaryPrimitives.ReadInt32BigEndian);
            public uint ReadUInt32() => ReadPrimitives(sizeof(uint), BinaryPrimitives.ReadUInt32BigEndian);
            public long ReadInt64() => ReadPrimitives(sizeof(long), BinaryPrimitives.ReadInt64BigEndian);
            public ulong ReadUInt64() => ReadPrimitives(sizeof(ulong), BinaryPrimitives.ReadUInt64BigEndian);

            public override string ToString() => $@"{BeginTime}
Send : {SendData?.ToHexString(" ")} ,CRC = {CRC_In.ToString("X")}
Recv : {RecvData?.ToHexString(" ")} ,CRC = {CRC_Out.ToString("X")}
Time : {Elapsed}ms, Total : {TotalElapsed}ms";
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
                return BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(buffer.Length - 2, 2));
            //return BitConverter.ToUInt16(buffer, buffer.Length - 2);
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
            //short value = this.ReadByte();
            //value <<= 8;
            //value |= (short)this.ReadByte();
            //return value;
            try
            {
                var value = BinaryPrimitives.ReadInt16BigEndian(RecvData.AsSpan(ReadIndex, sizeof(short)));
                ReadIndex += sizeof(short);
                return value;
            }
            catch { }
            return 0;
        }

        public int ReadInt32()
        {
            try
            {
                var value = BinaryPrimitives.ReadInt32BigEndian(RecvData.AsSpan(ReadIndex, sizeof(int)));
                ReadIndex += sizeof(int);
                return value;
            }
            catch { }
            return 0;
            //int value = 0;
            //for (int i = 0; i < 4; i++, value <<= 8)
            //    value |= this.ReadByte();
        }

        public ushort ReadUInt16()
        {
            try
            {
                var value = BinaryPrimitives.ReadUInt16BigEndian(RecvData.AsSpan(ReadIndex, sizeof(ushort)));
                ReadIndex += sizeof(ushort);
                return value;
                //ushort value = this.ReadByte();
                //value <<= 8;
                //value |= this.ReadByte();
                //return value;

                //byte[] tmp = new byte[2];
                //Array.Copy(RecvData, ReadIndex, tmp, 0, tmp.Length);
                //ReadIndex += tmp.Length;
                //Array.Reverse(tmp);
                //return BitConverter.ToUInt16(tmp);
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
            catch { }
            return 0;
        }


        public long ReadInt64()
        {
            try
            {
                var value = BinaryPrimitives.ReadInt64BigEndian(RecvData.AsSpan(ReadIndex, sizeof(long)));
                ReadIndex += sizeof(long);
                return value;
                //byte[] tmp = new byte[8];
                //Array.Copy(RecvData, ReadIndex, tmp, 0, tmp.Length);
                //ReadIndex += tmp.Length;
                //Array.Reverse(tmp);
                //return BitConverter.ToInt64(tmp);
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