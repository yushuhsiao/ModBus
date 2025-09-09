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
            DI = new DI_Value();
            DO = new DO_Value(this);
        }

        public DI_Value DI { get; }
        public DO_Value DO { get; }
        public DateTime ReadTime { get; set; }
        public TimeSpan ReadElapsed { get; set; }
        public DateTime WriteTime { get; set; }
        public TimeSpan WriteElapsed { get; set; }

        public class DI_Value
        {
            internal Interlocked_Int32 _value = new Interlocked_Int32();
            public int Value => _value.Value;

            internal DI_Value() { }

            public bool this[int bit] => (_value.Value & 1 << bit) != 0;
        }

        public class DO_Value
        {
            private Adam4055 _src;
            internal Interlocked_Int32 _value = new Interlocked_Int32();
            public int Value => _value.Value;

            internal DO_Value(Adam4055 src) { _src = src; }

            public bool this[int bit]
            {
                get => (_value.Value & 1 << bit) != 0;
                set => _src.SetDO(bit, value);
            }
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
                        this.DI._value.Value = _di;
                        this.DO._value.Value = _do;
                        //Console.WriteLine($"{cmd}\t{res}\t DO : {_do.ToString("X2")}, DI :{_di.ToString("X2")}\t{(int)elapsed.TotalMilliseconds}ms");
                        return (_di, _do);
                    }
                }
            }
            return null;
        }
    }
}