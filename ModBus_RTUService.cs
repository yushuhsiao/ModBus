using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace Leader.Services
{
    public abstract class ModBus_RTUService<T> where T : ModBus_RTU
    {
        public BusyState Busy { get; } = new BusyState();
        protected readonly ILogger _logger;
        protected readonly IConfiguration _config;
        protected abstract T Device { get; }

        public ModBus_RTUService(IServiceProvider service)
        {
            _logger = (ILogger)service.GetService(typeof(ILogger<>).MakeGenericType(this.GetType()));
            _config = (IConfiguration)service.GetService(typeof(IConfiguration<>).MakeGenericType(this.GetType()));
        }

        private static TimeCounter _comPorts_timer = new TimeCounter();
        private static IEnumerable<string> _comports = Array.Empty<string>();
        public IEnumerable<string> ComPorts
        {
            get
            {
                yield return "Disabled";
                if (_comPorts_timer.IsTimeout(2000, true))
                    _comports = SerialPort.GetPortNames().Distinct().OrderBy(p => p);
                var ports = _comports;
                foreach (var s in ports)
                    yield return s;
            }
        }

        private static readonly int[] _baudRates = new[] { 9600, 19200, 38400, 57600, 115200 };
        public IEnumerable<int> BaudRates => _baudRates;

        public string ComPortName
        {
            get
            {
                if (this.ComPort == 0)
                    return "Disabled";
                return $"COM{this.ComPort}";
            }
            set
            {
                if (value?.StartsWith("COM", StringComparison.OrdinalIgnoreCase) == true)
                    this.ComPort = value.Substring(3).ToInt32() ?? 0;
                else
                    this.ComPort = 0;
            }
        }

        public abstract string ConfigSectionName { get; }

        [AppSetting]
        public virtual int ComPort
        {
            get => _config.GetValue<int>(sectionName: ConfigSectionName);
            set => _config.SetValue(ConfigChange(value), sectionName: ConfigSectionName);
        }

        [AppSetting]
        public virtual int BaudRate
        {
            get => _config.GetValue<int>(sectionName: ConfigSectionName);
            set => _config.SetValue(ConfigChange(value), sectionName: ConfigSectionName);
        }

        [AppSetting, DefaultValue(Parity.None)]
        public virtual Parity Parity
        {
            get => _config.GetValue<Parity>(sectionName: ConfigSectionName);
            set => _config.SetValue(ConfigChange(value), sectionName: ConfigSectionName);
        }

        [AppSetting, DefaultValue(8)]
        public virtual int DataBits
        {
            get => _config.GetValue<int>(sectionName: ConfigSectionName);
            set => _config.SetValue(ConfigChange(value), sectionName: ConfigSectionName);
        }

        [AppSetting, DefaultValue(StopBits.One)]
        public virtual StopBits StopBits
        {
            get => _config.GetValue<StopBits>(sectionName: ConfigSectionName);
            set => _config.SetValue(ConfigChange(value), sectionName: ConfigSectionName);
        }


        [AppSetting, DefaultValue(1)]
        public int SiteId1
        {
            get => _config.GetValue<int>(sectionName: ConfigSectionName);
            set => _config.SetValue(value, sectionName: ConfigSectionName);
        }

        [AppSetting, DefaultValue(2)]
        public int SiteId2
        {
            get => _config.GetValue<int>(sectionName: ConfigSectionName);
            set => _config.SetValue(value, sectionName: ConfigSectionName);
        }

        [AppSetting, DefaultValue(3)]
        public int SiteId3
        {
            get => _config.GetValue<int>(sectionName: ConfigSectionName);
            set => _config.SetValue(value, sectionName: ConfigSectionName);
        }

        [AppSetting, DefaultValue(4)]
        public int SiteId4
        {
            get => _config.GetValue<int>(sectionName: ConfigSectionName);
            set => _config.SetValue(value, sectionName: ConfigSectionName);
        }

        private readonly Interlocked_Bool _config_change = new Interlocked_Bool(true);
        private TValue ConfigChange<TValue>(TValue value)
        {
            _config_change.Value = true;
            return value;
        }
        protected bool IsConfigChanged() => _config_change.Exchange(false);

        protected void ConfigChange()
        {
            if (IsConfigChanged() || Device.IsOpen == false)
            {
                using (Busy.Enter(out var busy))
                {
                    Device.Close();
                    Device.ComPort = ComPort;
                    Device.BaudRate = BaudRate;
                    Device.Parity = Parity;
                    Device.DataBits = DataBits;
                    Device.StopBits = StopBits;
                }
            }
        }

        protected bool Execute<TValue>(TValue value, Func<TValue, bool> cb)
        {
            try
            {
                using (Busy.Enter(out var busy))
                {
                    if (busy)
                        return false;
                    ConfigChange();
                    return cb(value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
            }
            return false;
        }

        private Dictionary<int, Dictionary<string, ModBus_RTU.Data>> msgs = new Dictionary<int, Dictionary<string, ModBus_RTU.Data>>();
        protected void SetMsg(int siteId, string key, ModBus_RTU.Data value)
        {
            if (!msgs.TryGetValue(siteId, out var dict))
                msgs[siteId] = dict = new Dictionary<string, ModBus_RTU.Data>();
            dict[key] = value;
        }
        public ModBus_RTU.Data GetMsg(int siteId, string key)
        {
            if (msgs.TryGetValue(siteId, out var dict) && dict.TryGetValue(key, out var value))
                return value;
            return null;
        }
        public void ClearMsg()
        {
            foreach (var dict in msgs.Values)
                dict.Clear();
        }
        public ModBus_RTU.Data GetMsg1(string key) => GetMsg(SiteId1, key);
        public ModBus_RTU.Data GetMsg2(string key) => GetMsg(SiteId2, key);
    }
}
