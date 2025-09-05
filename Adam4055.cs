using Microsoft.Extensions.Configuration;
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
        public int DI => _di.Value;
        public int DO => _do.Value;


        public async Task<bool> GetStatus()
        {
            SerialPort port = this.Open(false);
            if (port == null)
                return false;
            if (port.IsOpen == false)
                return false;
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            string cmd = $"${Address:00}6\r";
            //var buf = Encoding.UTF8.GetBytes(cmd);
            //port.Write(buf, 0, buf.Length);
            DateTime beginTime = DateTime.Now;
            TimeSpan elapsed = TimeSpan.Zero;
            port.Write(cmd);
            StringBuilder res_tmp = new StringBuilder();
            for (; ; )
            {
                if (port.BytesToRead > 0)
                {
                    var n = port.ReadChar();
                    //Console.Write(n.ToString("X2"));
                    //Console.Write(" ");
                    if (n == 13) break;
                    res_tmp.Append((char)n);
                }
                else
                    await Task.Delay(1);
                elapsed = DateTime.Now - beginTime;
                if (elapsed.TotalMilliseconds > ReadTimeout)
                    return false;
            }
            var res = res_tmp.ToString();
            elapsed = DateTime.Now - beginTime;
            if (res.StartsWith('!') && res.Length == 7)
            {
                var _do = Convert.ToInt32(res.Substring(1, 2), 16);
                var _di = Convert.ToInt32(res.Substring(3, 2), 16);
                this._di.Value = _di;
                this._do.Value = _do;
                Console.WriteLine($"{cmd}\t{res}\t DO : {_do.ToString("X2")}, DI :{_di.ToString("X2")}\t{(int)elapsed.TotalMilliseconds}ms");
                return true;
            }
            return false;
        }
    }
}
