﻿using Nethereum.Hex.HexTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SoliditySHA3Miner
{
    public class DevFee
    {
        public const string Address = "0x9172ff7884CEFED19327aDaCe9C470eF1796105c";
        public const float Percent = 2.0F;
        public const float MinimumPercent = 1.5F;

        public static float UserPercent
        {
            get => (m_UserPercent < MinimumPercent) ? Percent : m_UserPercent;
            set => m_UserPercent = value;
        }

        private static float m_UserPercent = Percent;
    }

    internal class Program
    {
        #region closing handler

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private delegate bool EventHandler(CtrlType sig);

        private static EventHandler m_handler;

        private static bool Handler(CtrlType sig)
        {
            if (m_handler == null)
                m_handler += new EventHandler(Handler);

            lock (m_handler)
            {
                if (m_allMiners != null)
                    m_allMiners.AsParallel()
                                .ForAll(miner =>
                                {
                                    try { if (miner != null) miner.Dispose(); }
                                    catch (Exception ex) { Print(ex.Message); }
                                });

                if (m_waitCheckTimer != null) m_waitCheckTimer.Stop();
                if (m_manualResetEvent != null) m_manualResetEvent.Set();

                return true;
            }
        }

        #endregion closing handler

        public static readonly DateTime LaunchTime = DateTime.Now;

        public static Config Config { get; private set; }

        public static ulong WaitSeconds { get; private set; }

        public static string LogFileFormat => $"{DateTime.Today:yyyy-MM-dd}.log";

        public static string AppDirPath => Path.GetDirectoryName(typeof(Program).Assembly.Location);

        public static string GetApplicationName() => typeof(Program).Assembly.GetName().Name;

        public static string GetApplicationVersion() => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

        public static string GetApplicationYear() => File.GetCreationTime(typeof(Program).Assembly.Location).Year.ToString();

        public static string GetAppConfigPath() => Path.Combine(AppDirPath, GetApplicationName() + ".conf");

        public static string GetCurrentTimestamp() => string.Format("{0:s}", DateTime.Now);

        public static void Print(string message, bool excludePrefix = false)
        {
            new TaskFactory().StartNew(() =>
            {
                message = message.Replace("Accelerated Parallel Processing", "APP").Replace("\n", Environment.NewLine);
                if (!excludePrefix) message = string.Format("[{0}] {1}", GetCurrentTimestamp(), message);

                if (Config.isLogFile)
                {
                    var logFilePath = Path.Combine(AppDirPath, "Log", LogFileFormat);

                    lock (m_manualResetEvent)
                    {
                        try
                        {
                            if (!Directory.Exists(Path.GetDirectoryName(logFilePath))) Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));

                            using (var logStream = File.AppendText(logFilePath))
                            {
                                logStream.WriteLine(message);
                                logStream.Close();
                            }
                        }
                        catch (Exception)
                        {
                            Console.WriteLine(string.Format("[ERROR] Failed writing to log file '{0}'", logFilePath));
                        }
                    }
                }

                Console.WriteLine(message);

                if (message.Contains("Mining stopped")) m_manualResetEvent.Set();
                if (message.Contains("Kernel launch failed")) m_isKernelLaunchFailed = true;
            });
        }

        private static ManualResetEvent m_manualResetEvent = new ManualResetEvent(false);
        private static bool m_isKernelLaunchFailed;
        private static System.Timers.Timer m_waitCheckTimer;
        private static Miner.CPU m_cpuMiner;
        private static Miner.CUDA m_cudaMiner;
        private static Miner.OpenCL m_openCLMiner;
        private static Miner.IMiner[] m_allMiners;
        private static API.Json m_apiJson;

        private static string GetHeader()
        {
            return "\n" +
                "*** " + GetApplicationName() + " " + GetApplicationVersion() + " beta by lwYeo@github (" + GetApplicationYear() + ") ***\n" +
                "*** Built with .NET Core 2.1 SDK, VC++ 2017, gcc 4.8.5, nVidia CUDA SDK 9.2 64-bits, and AMD APP SDK v3.0.130.135 (OpenCL)\n" +
                "\n" +
                "Donation addresses:\n" +
                "ETH (or any ERC 20/918 tokens)	: 0x9172ff7884cefed19327adace9c470ef1796105c\n" +
                "BTC                             : 3GS5J5hcG6Qcu9xHWGmJaV5ftWLmZuR255\n" +
                "LTC                             : LbFkAto1qYt8RdTFHL871H4djendcHyCyB\n";
        }

        private static void Main(string[] args)
        {
            if (File.Exists(GetAppConfigPath()))
            {
                Config = Utils.Json.DeserializeFromFile<Config>(GetAppConfigPath());
                if (Config == null)
                {
                    Console.WriteLine(string.Format("[ERROR] Failed to read config file at {0}", GetAppConfigPath()));
                    if (args.Any())
                        Config = new Config();
                    else
                        Environment.Exit(1);
                }
            }
            else
            {
                Config = new Config();
                if (Utils.Json.SerializeToFile(Config, GetAppConfigPath()))
                {
                    Console.WriteLine(string.Format("[INFO] Config file created at {0}", GetAppConfigPath()));
                    if (!args.Any())
                    {
                        Console.WriteLine("[INFO] Update the config file, especially miner details.");
                        Console.WriteLine("[INFO] Exiting application...");
                        Environment.Exit(0);
                    }
                }
                else
                {
                    Console.WriteLine(string.Format("[ERROR] Failed to write config file at {0}", GetAppConfigPath()));
                    if (!args.Any())
                        Environment.Exit(1);
                }
            }
            
            foreach (var arg in args)
            {
                try
                {
                    switch (arg.Split('=')[0])
                    {
                        case "logFile":
                            Config.isLogFile = bool.Parse(arg.Split('=')[1]);
                            break;
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("[ERROR] Failed parsing argument: " + arg);
                    Environment.Exit(1);
                }
            }

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Console.Title = string.Format("{0} {1} beta by lwYeo@github ({2})", GetApplicationName(), GetApplicationVersion(), GetApplicationYear());

            Print(GetHeader(), excludePrefix: true);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                m_handler += new EventHandler(Handler);
                SetConsoleCtrlHandler(m_handler, true);
            }
            else
            {
                AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
                {
                    Handler(CtrlType.CTRL_CLOSE_EVENT);
                };
                Console.CancelKeyPress += (s, ev) =>
                {
                    Handler(CtrlType.CTRL_C_EVENT);
                };
            }
            
            var config = Config;
            Config.ParseArgumentsToConfig(args, ref config);

            if (!Utils.Json.SerializeToFile(Config, GetAppConfigPath()))
                Print(string.Format("[ERROR] Failed to write config file at {0}", GetAppConfigPath()));

            try
            {
                Config.networkUpdateInterval = Config.networkUpdateInterval < 1000 ? Config.Defaults.NetworkUpdateInterval : Config.networkUpdateInterval;
                Config.hashrateUpdateInterval = Config.hashrateUpdateInterval < 1000 ? Config.Defaults.HashrateUpdateInterval : Config.hashrateUpdateInterval;

                Miner.Work.SetKingAddress(Config.kingAddress);
                Miner.Work.SetSolutionTemplate(Miner.CPU.GetNewSolutionTemplate(Miner.Work.GetKingAddressString()));

                var web3Interface = new NetworkInterface.Web3Interface(Config.web3api, Config.contractAddress, Config.minerAddress, Config.privateKey, Config.gasToMine,
                                                                       Config.abiFile, Config.networkUpdateInterval, Config.hashrateUpdateInterval);

                HexBigInteger tempMaxTarget = null;
                if (Config.overrideMaxTarget.Value > 0u)
                {
                    Print("[INFO] Override maximum difficulty: " + Config.overrideMaxTarget.HexValue);
                    tempMaxTarget = Config.overrideMaxTarget;
                }
                else tempMaxTarget = web3Interface.GetMaxTarget();

                if (Config.customDifficulty > 0)
                    Print("[INFO] Custom difficulity: " + Config.customDifficulty.ToString());

                var secondaryPoolInterface = string.IsNullOrWhiteSpace(Config.secondaryPool)
                                           ? null
                                           : new NetworkInterface.PoolInterface(Config.minerAddress, Config.secondaryPool, Config.maxScanRetry, -1, -1,
                                                                                Config.customDifficulty, tempMaxTarget);

                var primaryPoolInterface = new NetworkInterface.PoolInterface(Config.minerAddress, Config.primaryPool, Config.maxScanRetry,
                                                                              Config.networkUpdateInterval, Config.hashrateUpdateInterval,
                                                                              Config.customDifficulty, tempMaxTarget, secondaryPoolInterface);

                var mainNetworkInterface = (string.IsNullOrWhiteSpace(Config.privateKey))
                                           ? primaryPoolInterface
                                           : (NetworkInterface.INetworkInterface)web3Interface;

                if (Config.cpuMode)
                {
                    if (Config.cpuDevices.Any())
                        m_cpuMiner = new Miner.CPU(mainNetworkInterface, Config.cpuDevices, Config.submitStale, Config.pauseOnFailedScans);
                }
                else
                {
                    if (Config.cudaDevices.Any())
                        m_cudaMiner = new Miner.CUDA(mainNetworkInterface, Config.cudaDevices, Config.submitStale, Config.pauseOnFailedScans);

                    var openCLdevices = Config.intelDevices.Union(Config.amdDevices).ToArray();
                    if (openCLdevices.Any())
                        m_openCLMiner = new Miner.OpenCL(mainNetworkInterface, openCLdevices, Config.submitStale, Config.pauseOnFailedScans);
                }
                m_allMiners = new Miner.IMiner[] { m_openCLMiner, m_cudaMiner, m_cpuMiner }.Where(m => m != null).ToArray();

                if (!m_allMiners.Any() || m_allMiners.All(m => !m.HasAssignedDevices))
                {
                    Console.WriteLine("[ERROR] No miner assigned.");
                    Environment.Exit(1);
                }
       
                if (!Utils.Json.SerializeToFile(Config, GetAppConfigPath())) // Write again to update GPU intensity etc.
                    Print(string.Format("[ERROR] Failed to write config file at {0}", GetAppConfigPath()));

                m_apiJson = new API.Json(m_allMiners);
                if (m_apiJson.IsSupported) m_apiJson.Start(Config.minerJsonAPI);

                API.Ccminer.StartListening(Config.minerCcminerAPI, m_allMiners);

                if (Config.cpuMode)
                {
                    if (m_cpuMiner.HasAssignedDevices)
                        m_cpuMiner.StartMining(Config.networkUpdateInterval, Config.hashrateUpdateInterval);
                }
                else
                {
                    if (m_openCLMiner != null && m_openCLMiner.HasAssignedDevices)
                        m_openCLMiner.StartMining(Config.networkUpdateInterval, Config.hashrateUpdateInterval);

                    if (m_cudaMiner != null && m_cudaMiner.HasAssignedDevices)
                        m_cudaMiner.StartMining(Config.networkUpdateInterval, Config.hashrateUpdateInterval);
                }

                m_waitCheckTimer = new System.Timers.Timer(1000);
                m_waitCheckTimer.Elapsed +=
                    delegate
                    {
                        if (m_allMiners.All(m => m != null && (!m.IsMining || m.IsPaused))) WaitSeconds++;
                    };
                m_waitCheckTimer.Start();
                WaitSeconds = (ulong)(LaunchTime - DateTime.Now).TotalSeconds;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.ToString());
                if (ex.InnerException != null)
                    Console.WriteLine(ex.InnerException.ToString());

                Environment.Exit(1);
            }

            m_manualResetEvent.WaitOne();
            Console.WriteLine("[INFO] Exiting application...");

            API.Ccminer.StopListening();
            m_waitCheckTimer.Stop();

            if (m_isKernelLaunchFailed)
                Environment.Exit(22);
            else
                Environment.Exit(0);
        }
    }
}