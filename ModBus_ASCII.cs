using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace System.IO.Ports
{
    public class ModBus_ASCII
    {
        private IConfiguration _config;
        private ILogger _logger;
        
        private object _lock = new object(); // modbus 的傳送/接收時間必須隔開
        private SerialPort port;
        public event EventHandler PortChange;

        public double ReadTimeout = 5000;
        public double RecvComplete_IdleTime = 100; // 多少毫秒之後沒有收到資料就視為接收完成
        public int ComPort = 1;
        public int BaudRate = 38400;
        public Parity Parity = Parity.None;
        public int DataBits = 8;
        public StopBits StopBits = StopBits.One;

        public string PortName => port?.PortName;
        public bool IsOpen => port?.IsOpen == true;

        public ModBus_ASCII(IConfiguration<ModBus_ASCII> config, ILogger<ModBus_ASCII> logger)
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
        public ModBusASCIIResult SendAndGetResponse(string input, string end = "\r")
        {
            byte[] sendBuf = Encoding.ASCII.GetBytes(input + end);

            lock (_lock)
            {
                var port = this.port;
                if (port?.IsOpen != true)
                    return ModBusASCIIResult.PortNotOpen;

                DateTime beginTime = DateTime.Now;
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                port.Write(sendBuf, 0, sendBuf.Length);


                string recvBuf = ""; //接收區 
                //
                //byte[] recvBuf = new byte[16]; //
                TimeSpan t_Elapsed;
                TimeSpan t_Finish = TimeSpan.Zero;
                double readTimeout = this.ReadTimeout;
                while (port.IsOpen)
                {
                    t_Elapsed = DateTime.Now - beginTime;
                    if (t_Elapsed.TotalMilliseconds > readTimeout)
                        break;
                    if (port.BytesToRead > 0)
                    {
                        recvBuf += (char)port.ReadByte();
                        t_Finish = t_Elapsed;
                    }
                    else if (recvBuf.Length > 0)
                    {
                        //string tmp = Encoding.ASCII.GetString(recvBuf, 0, offset);
                        if (recvBuf.EndsWith(end))
                        {
                            return new ModBusASCIIResult
                            {
                                //SendData = sendBuf,
                                //RecvData = Encoding.ASCII.GetBytes(recvBuf),
                                SendString = input,
                                RecvString = recvBuf.Substring(0, recvBuf.Length - end.Length),
                                BeginTime = beginTime,
                                Elapsed = t_Finish.TotalMilliseconds,
                                TotalElapsed = (DateTime.Now - beginTime).TotalMilliseconds,
                            };
                        }
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
                return ModBusASCIIResult.Timeout;
            }
        }

    }

    public class ModBusASCIIResult
    {
        public static readonly ModBusASCIIResult PortNotOpen = new ModBusASCIIResult();
        public static readonly ModBusASCIIResult Timeout = new ModBusASCIIResult();

        public bool IsSuccess => SendString != null && RecvString != null;
        public bool IsTimeout => object.ReferenceEquals(this, Timeout);

        public DateTime BeginTime;
        public double Elapsed;
        public double TotalElapsed;

        //public byte[] SendData;
        //public byte[] RecvData;

        public string SendString;
        public string RecvString;


        public string ReadString(string data)
        {
            return data.Substring(3, data.Length - 3);
        }

    }
}
