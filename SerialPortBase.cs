using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace System.IO.Ports
{
    public class SerialPortBase
    {
        protected ILogger _logger;
        protected object _lock = new object();
        private SerialPort port;
        public event EventHandler PortChange;

        public virtual int ComPort { get; set; } = 1;
        public virtual int BaudRate { get; set; } = 38400;
        public virtual Parity Parity { get; set; } = Parity.None;
        public virtual int DataBits { get; set; } = 8;
        public virtual StopBits StopBits { get; set; } = StopBits.One;

        public string PortName => port?.PortName;
        public bool IsOpen => port?.IsOpen == true;

        public SerialPortBase(IServiceProvider service)
        {
            _logger = service.GetService(typeof(ILogger<>).MakeGenericType(this.GetType())) as ILogger;
        }
        public SerialPortBase(ILogger logger) { _logger = logger; }

        public SerialPort Open(bool force, bool showError = true)
        {
            try
            {
                lock (_lock)
                {
                    if (force) this.Close(showError);
                    if (this.port == null)
                    {
                        if (this.ComPort <= 0)
                            return null;
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
                if (showError)
                    _logger.LogError(ex, ex.Message);
                this.Close(showError);
                return null;
            }
        }

        public void Close(bool showMessage = true)
        {
            lock (_lock)
            {
                if (port == null) return;
                using (var p = port)
                {
                    if (showMessage)
                        _logger.LogInformation($"{p.PortName} Close.");
                    try { port?.Close(); }
                    catch { }
                    port = null;
                }
            }
            PortChange?.Invoke(this, EventArgs.Empty);
        }



        private static TimeCounter _comPorts_timer = new TimeCounter();
        private static Interlocked<IEnumerable<string>> _comports = new Interlocked<IEnumerable<string>>() { Value = Array.Empty<string>() };
        public static IEnumerable<string> ComPorts
        {
            get
            {
                yield return "Disabled";
                if (_comPorts_timer.IsTimeout(2000, true))
                    _comports.Value = SerialPort.GetPortNames().Distinct().OrderBy(p => p);
                var ports = _comports.Value;
                foreach (var s in ports)
                    yield return s;
            }
        }

        private static readonly int[] _baudRates = new[] { 9600, 19200, 38400, 57600, 115200 };
        public static IEnumerable<int> BaudRates => _baudRates;
    }
}
