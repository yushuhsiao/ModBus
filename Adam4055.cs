using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO.Ports
{
    public class Adam4055 : SerialPortBase
    {
        // Get DI : $036
        // Set DO : #0300FF

        public int Address { get; set; }
        public double ReadTimeout { get; set; } = 15000;

        public Adam4055(IServiceProvider service) : base(service)
        {
        }

        private Interlocked_Int32 _di = new Interlocked_Int32();
        private Interlocked_Int32 _do = new Interlocked_Int32();
        public DateTime ReadTime { get; set; }
        public TimeSpan ReadElapsed { get; set; }
        public DateTime WriteTime { get; set; }
        public TimeSpan WriteElapsed { get; set; }
        public int DI => _di.Value;
        public int DO => _do.Value;

        public bool DI7 => (_di.Value & 0x80) != 0;
        public bool DI6 => (_di.Value & 0x40) != 0;
        public bool DI5 => (_di.Value & 0x20) != 0;
        public bool DI4 => (_di.Value & 0x10) != 0;
        public bool DI3 => (_di.Value & 0x08) != 0;
        public bool DI2 => (_di.Value & 0x04) != 0;
        public bool DI1 => (_di.Value & 0x02) != 0;
        public bool DI0 => (_di.Value & 0x01) != 0;

        public bool DO7
        {
            get => (_do.Value & 0x80) != 0;
            set => SetDO(7, value);
        }
        public bool DO6
        {
            get => (_do.Value & 0x40) != 0;
            set => SetDO(6, value);
        }
        public bool DO5
        {
            get => (_do.Value & 0x20) != 0;
            set => SetDO(5, value);
        }
        public bool DO4
        {
            get => (_do.Value & 0x10) != 0;
            set => SetDO(4, value);
        }
        public bool DO3
        {
            get => (_do.Value & 0x08) != 0;
            set => SetDO(3, value);
        }
        public bool DO2
        {
            get => (_do.Value & 0x04) != 0;
            set => SetDO(2, value);
        }
        public bool DO1
        {
            get => (_do.Value & 0x02) != 0;
            set => SetDO(1, value);
        }
        public bool DO0
        {
            get => (_do.Value & 0x01) != 0;
            set => SetDO(0, value);
        }

        private bool SendAndGetResponse(string cmd, out string res, out TimeSpan elapsed)
        {
            res = null;
            elapsed = TimeSpan.Zero;
            SerialPort port = this.Open(!this.IsOpen, false);
            if (port == null)
                return false;
            if (port.IsOpen == false)
                return false;
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            DateTime beginTime = DateTime.Now;
            port.Write(cmd);
            StringBuilder res_tmp = new StringBuilder();
            for (; ; )
            {
                if (port.BytesToRead > 0)
                {
                    var n = port.ReadChar();
                    if (n == 13) break;
                    res_tmp.Append((char)n);
                }
                else
                    Thread.Sleep(1);
                elapsed = DateTime.Now - beginTime;
                if (elapsed.TotalMilliseconds > ReadTimeout)
                    return false;
            }
            res = res_tmp.ToString();
            elapsed = DateTime.Now - beginTime;
            return true;
        }

        public bool SetDO(int value)
        {
            lock (_lock)
            {
                if (SendAndGetResponse($"#{Address:X2}00{value:X2}\r", out var res, out var elapsed))
                {
                    WriteTime = DateTime.Now;
                    WriteElapsed = elapsed;
                    return true;
                }
            }
            return false;
        }

        public void SetDO(int bit, bool value)
        {
            lock (_lock)
            {
                var n1 = GetStatus();
                if (n1 != null)
                {
                    var _do = n1.Value.Item2;
                    if (value)
                        _do |= 1 << bit;
                    else
                    {
                        var mask = 1 << bit;
                        mask ^= 0xff;
                        _do &= mask;
                    }
                    SetDO(_do);
                }
            }
        }

        public (int, int)? GetStatus()
        {
            lock (_lock)
            {
                if (SendAndGetResponse($"${Address:X2}6\r", out var res, out var elapsed))
                {
                    ReadTime = DateTime.Now;
                    ReadElapsed = elapsed;
                    if (res.StartsWith('!') && res.Length == 7)
                    {
                        var _do = Convert.ToInt32(res.Substring(1, 2), 16);
                        var _di = Convert.ToInt32(res.Substring(3, 2), 16);
                        this._di.Value = _di;
                        this._do.Value = _do;
                        //Console.WriteLine($"{cmd}\t{res}\t DO : {_do.ToString("X2")}, DI :{_di.ToString("X2")}\t{(int)elapsed.TotalMilliseconds}ms");
                        return (_di, _do);
                    }
                }
            }
            return null;
        }
    }
}
