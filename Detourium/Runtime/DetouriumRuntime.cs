﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Detourium.Runtime
{
    using Logging;

    public static class DetouriumRuntime
    {
        public static string PluginsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Detourium", "plugins");

        public static int CurrentProcessId =>
            Process.GetCurrentProcess().Id;

        public static List<DetouriumPlugin> PluginsLoaded =
            new List<DetouriumPlugin>();
        
        public static ConsoleLogger ConsoleLogger;

        public static int EntryPoint(string initialDLL)
        {
            NativeMethods.AllocConsole();

            ConsoleLogger = new ConsoleLogger();
            ConsoleLogger.LogLevel = LogLevel.Debug;

            ConsoleLogger.Log(Banner.BannerText);
            ConsoleLogger.LogInfo($"Runtime initialized. (pid: {CurrentProcessId})");

            if (!string.IsNullOrEmpty(initialDLL))
                ConsoleLogger.LogInfo("Plugin DLL GUID: " + initialDLL);

            // Reference the latest version of Detourium.Plugins.
            AppDomain.CurrentDomain.AssemblyResolve += (s, args) => (!args.Name.Contains("Detourium.Plugins")) ? null :
                Assembly.LoadFrom(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Detourium", "Detourium.Plugins.dll"));
            
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                ConsoleLogger.LogError($"[Runtime] An unhandled exception occurred in the current domain. (fatal: {e.IsTerminating})", (Exception)e.ExceptionObject);

            Directory.CreateDirectory(PluginsDirectory);

            // Upon startup, delete any existing, conflicting files in the plugins directory.
            new DirectoryInfo(PluginsDirectory)
                .EnumerateFiles($"{CurrentProcessId}-*.tmp", SearchOption.TopDirectoryOnly)
                .Where(file => !string.IsNullOrEmpty(initialDLL) ? !file.Name.Contains(initialDLL) : true).ToList()
                .ForEach((file) => file.Delete());

            while (true)
            {
                try
                {
                    DiscoverPlugins();
                }
                catch (Exception exception)
                {
                    ConsoleLogger.LogError($"[Runtime] An unhandled exception occurred whilst discovering plugin(s).", exception);
                }

                Thread.Sleep(1000);
            }
        }

        public static void DiscoverPlugins()
        {
            foreach (var file in Directory.GetFiles(PluginsDirectory, $"{CurrentProcessId}-*.tmp", SearchOption.TopDirectoryOnly).ToList())
            {
                try
                {
                    var assembly = Assembly.Load(File.ReadAllBytes(file));
                                   File.Delete(file);

                    var plugins = assembly.GetTypes().Where(p => typeof(DetouriumPlugin).IsAssignableFrom(p) && p.IsClass)
                                          .Select(plugin => (DetouriumPlugin)Activator.CreateInstance(plugin));

                    if (!(plugins.Any()))
                    {
                        ConsoleLogger.LogWarning(Globalization<DetouriumPlugin>.PluginInterfaceNotFound());
                    }

                    foreach (var plugin in plugins)
                    {
                        try
                        {
                            if (!plugin.Configuration.PluginEnabled)
                            {
                                ConsoleLogger.LogWarning(Globalization<DetouriumPlugin>.PluginDisabledMessage(plugin));
                                continue;
                            }

                            var duplicate = PluginsLoaded.Find(p => p.PluginName == plugin.PluginName);

                            if (duplicate != null)
                            {
                                if (!duplicate.Configuration.PluginEnabled)
                                {
                                    ConsoleLogger.LogWarning(Globalization<DetouriumPlugin>.PluginDisabledMessage(duplicate));
                                    continue;
                                }

                                if (duplicate.Configuration.EnforceVersionPriority && plugin.PluginVersion < duplicate.PluginVersion)
                                {
                                    ConsoleLogger.LogWarning(Globalization<DetouriumPlugin>.PluginVersionPriorityMessage(plugin, duplicate));

                                    continue;
                                }
                                
                                ConsoleLogger.LogDebug(Globalization<DetouriumPlugin>.PluginUnloadedMessage(duplicate));

                                PluginsLoaded.Remove(duplicate);
                            }

                            ConsoleLogger.LogDebug(Globalization<DetouriumPlugin>.PluginLoadedMessage(plugin));

                            if (PluginsLoaded.Count == 0 && !plugin.Configuration.DisplayConsole)
                                NativeMethods.FreeConsole();

                            PluginsLoaded.Add(plugin);
                            plugin.OnInstalled();
                        }
                        catch (Exception exception)
                        {
                            ConsoleLogger.LogError($"[Runtime] An unhandled exception occurred while loading plugin(s).", exception);
                        }
                    }
                }
                catch (ReflectionTypeLoadException exception)
                {
                    ConsoleLogger.LogError($"[Runtime] An unhandled reflection exception occurred while loading plugin(s).", exception);
                }
                catch (Exception exception)
                {
                    ConsoleLogger.LogError($"[Runtime] An unhandled exception occurred while loading plugin(s).", exception);
                }
            }
        }

        internal static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AllocConsole();

            [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
            public static extern bool FreeConsole();
        }
    }
}
