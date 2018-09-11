﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SoliditySHA3Miner.API
{
    public class Json : IDisposable
    {
        public bool IsSupported { get; }

        private Miner.IMiner[] m_miners;
        private HttpListener m_Listener;
        private bool m_isOngoing;

        public Json(params Miner.IMiner[] miners)
        {
            IsSupported = HttpListener.IsSupported;
            if (!IsSupported)
            {
                Program.Print("[ERROR] Obsolete OS detected, JSON-API will not start.");
                return;
            }
            m_miners = miners;
        }

        public void Start(string apiBind)
        {
            m_isOngoing = false;

            var httpBind = apiBind.ToString();
            if (string.IsNullOrWhiteSpace(httpBind))
            {
                Program.Print("[INFO] minerJsonAPI is null or empty, using default...");
                httpBind = Defaults.JsonAPIPath;
            }
            else if (apiBind == "0")
            {
                Program.Print("[INFO] JSON-API is disabled.");
                return;
            }

            if (!httpBind.StartsWith("http://") || httpBind.StartsWith("https://")) httpBind = "http://" + httpBind;
            if (!httpBind.EndsWith("/")) httpBind += "/";

            if (!int.TryParse(httpBind.Split(':')[2].TrimEnd('/'), out int port))
            {
                Program.Print("[ERROR] Invalid port provided for JSON-API.");
                return;
            }

            var tempIPAddress = httpBind.Split(new string[] { "//" }, StringSplitOptions.None)[1].Split(':')[0];
            if (!IPAddress.TryParse(tempIPAddress, out IPAddress ipAddress))
            {
                Program.Print("[ERROR] Invalid IP address provided for JSON-API.");
                return;
            }

            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                try { socket.Bind(new IPEndPoint(ipAddress, port)); }
                catch (Exception)
                {
                    Program.Print("[ERROR] JSON-API failed to bind to: " + (string.IsNullOrEmpty(apiBind) ? Defaults.JsonAPIPath : apiBind));
                    return;
                }
            };

            try
            {
                m_Listener = new HttpListener();
                m_Listener.Prefixes.Add(httpBind);

                Task.Run(() => Process(m_Listener));
                m_isOngoing = true;
            }
            catch (Exception ex)
            {
                Program.Print("[ERROR] An error has occured while starting JSON-API: " + ex.Message);
                return;
            }
        }

        public void Stop()
        {
            if (!m_isOngoing) return;
            try
            {
                m_isOngoing = false;
                Program.Print("[INFO] JSON-API service stopping...");
                m_Listener.Stop();
            }
            catch (Exception ex)
            {
                Program.Print("[ERROR] An error has occured while stopping JSON-API: " + ex.Message);
            }
        }

        private void Process(HttpListener listener)
        {
            listener.Start();
            Program.Print(string.Format("[INFO] JSON-API service started at {0}...", listener.Prefixes.ElementAt(0)));
            while (m_isOngoing)
            {
                HttpListenerResponse response = listener.GetContext().Response;

                var api = new JsonAPI();
                float divisor = 1;
                ulong totalHashRate = 0ul;

                foreach (var miner in m_miners)
                    totalHashRate += miner.GetTotalHashrate();

                var sTotalHashRate = totalHashRate.ToString();
                if (sTotalHashRate.Length > 12 + 1)
                {
                    divisor = 1000000000000;
                    api.HashRateUnit = "TH/s";
                }
                else if (sTotalHashRate.Length > 9 + 1)
                {
                    divisor = 1000000000;
                    api.HashRateUnit = "GH/s";
                }
                else if (sTotalHashRate.Length > 6 + 1)
                {
                    divisor = 1000000;
                    api.HashRateUnit = "MH/s";
                }
                else if (sTotalHashRate.Length > 3 + 1)
                {
                    divisor = 1000;
                    api.HashRateUnit = "KH/s";
                }

                api.EffectiveHashRate = (float)m_miners.Select(m => m.NetworkInterface).
                                                        FirstOrDefault(m => m != null)?.GetEffectiveHashrate() / divisor;
                api.TotalHashRate = totalHashRate / divisor;

                foreach (var miner in m_miners)
                {
                    foreach (var device in miner.Devices.Where(d => d.DeviceID > -1))
                    {
                        JsonAPI.Miner newMiner = null;
                        if (miner.HasMonitoringAPI)
                        {
                            switch (device.Type)
                            {
                                case "CUDA":
                                    {
                                        var tempValue = 0;
                                        var tempSize = 0ul;
                                        var tempStr = new StringBuilder(1024);
                                        var instancePtr = ((Miner.CUDA)miner).m_instance;

                                        newMiner = new JsonAPI.CUDA_Miner()
                                        {
                                            Type = device.Type,
                                            DeviceID = device.DeviceID,
                                            ModelName = device.Name,
                                            HashRate = miner.GetHashrateByDevice(device.Platform, device.DeviceID) / divisor,
                                            LatencyMS = miner.NetworkInterface.LastLatency,
                                            HasMonitoringAPI = miner.HasMonitoringAPI,
                                            SettingIntensity = device.Intensity
                                        };

                                        Miner.CUDA.Solver.GetDeviceSettingMaxCoreClock(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).SettingMaxCoreClockMHz = tempValue;

                                        Miner.CUDA.Solver.GetDeviceSettingMaxMemoryClock(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).SettingMaxMemoryClockMHz = tempValue;

                                        Miner.CUDA.Solver.GetDeviceSettingPowerLimit(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).SettingPowerLimitPercent = tempValue;

                                        Miner.CUDA.Solver.GetDeviceSettingThermalLimit(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).SettingThermalLimitC = tempValue;

                                        Miner.CUDA.Solver.GetDeviceSettingFanLevelPercent(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).SettingFanLevelPercent = tempValue;

                                        Miner.CUDA.Solver.GetDeviceCurrentFanTachometerRPM(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).CurrentFanTachometerRPM = tempValue;

                                        Miner.CUDA.Solver.GetDeviceCurrentTemperature(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).CurrentTemperatureC = tempValue;

                                        Miner.CUDA.Solver.GetDeviceCurrentCoreClock(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).CurrentCoreClockMHz = tempValue;

                                        Miner.CUDA.Solver.GetDeviceCurrentMemoryClock(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).CurrentMemoryClockMHz = tempValue;

                                        Miner.CUDA.Solver.GetDeviceCurrentUtilizationPercent(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).CurrentUtilizationPercent = tempValue;

                                        Miner.CUDA.Solver.GetDeviceCurrentPstate(instancePtr, device.DeviceID, ref tempValue);
                                        ((JsonAPI.CUDA_Miner)newMiner).CurrentPState = tempValue;

                                        Miner.CUDA.Solver.GetDeviceCurrentThrottleReasons(instancePtr, device.DeviceID, tempStr, ref tempSize);
                                        ((JsonAPI.CUDA_Miner)newMiner).CurrentThrottleReasons = tempStr.ToString();
                                    }
                                    break;

                                case "OpenCL":
                                    {
                                        var tempValue = 0;
                                        var tempStr = new StringBuilder(1024);
                                        var instancePtr = ((Miner.OpenCL)miner).m_instance;

                                        newMiner = new JsonAPI.AMD_Miner()
                                        {
                                            Type = device.Type,
                                            DeviceID = device.DeviceID,
                                            ModelName = device.Name,
                                            HashRate = miner.GetHashrateByDevice(device.Platform, device.DeviceID) / divisor,
                                            LatencyMS = miner.NetworkInterface.LastLatency,
                                            HasMonitoringAPI = miner.HasMonitoringAPI,
                                            Platform = device.Platform,
                                            SettingIntensity = device.Intensity
                                        };

                                        Miner.OpenCL.Solver.GetDeviceSettingMaxCoreClock(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).SettingMaxCoreClockMHz = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceSettingMaxMemoryClock(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).SettingMaxMemoryClockMHz = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceSettingPowerLimit(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).SettingPowerLimitPercent = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceSettingThermalLimit(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).SettingThermalLimitC = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceSettingFanLevelPercent(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).SettingFanLevelPercent = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceCurrentFanTachometerRPM(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).CurrentFanTachometerRPM = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceCurrentTemperature(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).CurrentTemperatureC = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceCurrentCoreClock(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).CurrentCoreClockMHz = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceCurrentMemoryClock(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).CurrentMemoryClockMHz = tempValue;

                                        Miner.OpenCL.Solver.GetDeviceCurrentUtilizationPercent(instancePtr, new StringBuilder(device.Platform), device.DeviceID, ref tempValue);
                                        ((JsonAPI.AMD_Miner)newMiner).CurrentUtilizationPercent = tempValue;
                                    }
                                    break;
                            }
                        }
                        else
                        {
                            switch (device.Type)
                            {
                                case "OpenCL":
                                    newMiner = new JsonAPI.OpenCLMiner()
                                    {
                                        Type = device.Type,
                                        DeviceID = device.DeviceID,
                                        ModelName = device.Name,
                                        HashRate = miner.GetHashrateByDevice(device.Platform, device.DeviceID) / divisor,
                                        LatencyMS = miner.NetworkInterface.LastLatency,
                                        HasMonitoringAPI = miner.HasMonitoringAPI,

                                        Platform = device.Platform,
                                        SettingIntensity = device.Intensity
                                    };
                                    break;

                                default:
                                    newMiner = new JsonAPI.Miner()
                                    {
                                        Type = device.Type,
                                        DeviceID = device.DeviceID,
                                        ModelName = device.Name,
                                        HashRate = miner.GetHashrateByDevice(device.Platform, (device.Type == "CPU")
                                                                                     ? Array.IndexOf(miner.Devices, device)
                                                                                     : device.DeviceID) / divisor,
                                        LatencyMS = miner.NetworkInterface.LastLatency,
                                        HasMonitoringAPI = miner.HasMonitoringAPI
                                    };
                                    break;
                            }
                        }

                        if (newMiner != null) api.Miners.Add(newMiner);
                    }
                }

                byte[] buffer = Encoding.UTF8.GetBytes(Utils.Json.SerializeFromObject(api, Utils.Json.BaseClassFirstSettings));
                response.ContentLength64 = buffer.Length;

                using (var output = response.OutputStream)
                    if (buffer != null) output.Write(buffer, 0, buffer.Length);

                response.Close();
            }
        }

        public void Dispose()
        {
            m_isOngoing = false;
            if (m_Listener != null) m_Listener.Close();
        }

        public class JsonAPI
        {
            public DateTime SystemDateTime => DateTime.Now;
            public float EffectiveHashRate { get; set; }
            public float TotalHashRate { get; set; }
            public string HashRateUnit { get; set; }
            public List<Miner> Miners { get; set; }

            public JsonAPI() => Miners = new List<Miner>();

            public class Miner
            {
                public string Type { get; set; }
                public int DeviceID { get; set; }
                public string ModelName { get; set; }
                public float HashRate { get; set; }
                public int LatencyMS { get; set; }
                public bool HasMonitoringAPI { get; set; }
            }

            public class CUDA_Miner : Miner
            {
                public float SettingIntensity { get; set; }
                public int SettingMaxCoreClockMHz { get; set; }
                public int SettingMaxMemoryClockMHz { get; set; }
                public int SettingPowerLimitPercent { get; set; }
                public int SettingThermalLimitC { get; set; }
                public int SettingFanLevelPercent { get; set; }

                public int CurrentFanTachometerRPM { get; set; }
                public int CurrentTemperatureC { get; set; }
                public int CurrentCoreClockMHz { get; set; }
                public int CurrentMemoryClockMHz { get; set; }
                public int CurrentUtilizationPercent { get; set; }
                public int CurrentPState { get; set; }
                public string CurrentThrottleReasons { get; set; }
            }

            public class OpenCLMiner : Miner
            {
                public string Platform { get; set; }
                public float SettingIntensity { get; set; }
            }

            public class AMD_Miner : OpenCLMiner
            {
                public int SettingMaxCoreClockMHz { get; set; }
                public int SettingMaxMemoryClockMHz { get; set; }
                public int SettingPowerLimitPercent { get; set; }
                public int SettingThermalLimitC { get; set; }
                public int SettingFanLevelPercent { get; set; }

                public int CurrentFanTachometerRPM { get; set; }
                public int CurrentTemperatureC { get; set; }
                public int CurrentCoreClockMHz { get; set; }
                public int CurrentMemoryClockMHz { get; set; }
                public int CurrentUtilizationPercent { get; set; }
            }
        }
    }
}