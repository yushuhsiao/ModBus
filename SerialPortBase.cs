using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace System.IO.Ports
{
    public class SerialPortBase
    {
        protected ILogger _logger;
        protected object _lock = new object();
        private SerialPort port;
        public event EventHandler PortChange;

        public int ComPort = 1;
        public int BaudRate = 38400;
        public Parity Parity = Parity.None;
        public int DataBits = 8;
        public StopBits StopBits = StopBits.One;

        public string PortName => port?.PortName;
        public bool IsOpen => port?.IsOpen == true;

        public SerialPortBase(IServiceProvider service) : this(service?.GetService<ILogger<SerialPortBase>>()) { }
        public SerialPortBase(ILogger<SerialPortBase> logger)
        {
            _logger = logger;
        }

        public SerialPort Open(bool force, bool showError = true)
        {
            try
            {
                lock (_lock)
                {
                    if (force) this.Close(showError);
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
    }
}
