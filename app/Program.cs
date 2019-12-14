using NDesk.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace dotnet_foreman
{
    class Program
    {
        enum CtrlTypes : uint
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GenerateConsoleCtrlEvent(CtrlTypes dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern int GetSystemDefaultLCID();

        delegate Boolean ConsoleCtrlDelegate(CtrlTypes type);

        public enum ShellType
        {
            Cmd,
            Wsl,
            Powershell
        }
        public class ForemanProcess
        {
            public string Command;
            public string Name;
            public ConsoleColor Color;
            public Process Process;
            public ShellType ShellType = ShellType.Cmd;

            public ForemanProcess(string name, string command)
            {
                this.Name = name;
                this.Command = command;
            }
        }

        public static object outputSync = new object();
        static int Main(string[] args)
        {
            try {
                int lcid = GetSystemDefaultLCID();
                var ci = System.Globalization.CultureInfo.GetCultureInfo(lcid);
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var shellType = ShellType.Cmd;
                var procfile = "Procfile";
                var help = false;
                var options = new OptionSet {
                    {"f|procfile=", "Procfile", v => procfile = v},
                    {"h|help", v => help = v != null},
                    {"shell-type=", "Default shell: cmd, wsl, powershell", v => {
                            TryGetShellType(v, ref shellType);
                        }
                    },
                    {"wsl", v => {
                        if (v != null)
                            shellType = ShellType.Wsl;
                        }
                    },
                 };
                options.Parse(args);
                if (help) {
                    options.WriteOptionDescriptions(Console.Out);
                    return 0;
                }

                if (!File.Exists(procfile)) {
                    Console.WriteLine($"{procfile} not found");
                    return 1;
                }
                var commands = new List<ForemanProcess>();
                string prevline = null;
                foreach(var line in File.ReadLines(procfile)) {
                    var currentline = line.Trim();
                    if (currentline.StartsWith('#'))
                    {
                        prevline = line;
                        continue;
                    }

                    var index = currentline.IndexOf(':');
                    if (index <= 0)
                    {
                        prevline = line;
                        continue;
                    }

                    if (index == currentline.Length - 1)
                    {
                        prevline = line;
                        continue;
                    }
                    var item = new ForemanProcess(currentline.Substring(0, index).Trim(), currentline.Substring(index + 1).Trim());
                    item.ShellType = shellType;
                    commands.Add(item);
                    if (prevline?.StartsWith("#") == true)
                        TryGetShellType(prevline.TrimStart('#'), ref item.ShellType);
                    prevline = line;
                }
                for (var i = 0; i < commands.Count; i++)
                {
                    commands[i].Color = (ConsoleColor)i + 1;
                }

                foreach(var command in commands)
                {
                    var info = command.ShellType switch
                    {
                        ShellType.Cmd => new ProcessStartInfo("cmd", $"/c \"{command.Command}\""),
                        ShellType.Wsl => new ProcessStartInfo("wsl", $"bash --login -c '{command.Command}'"),
                        ShellType.Powershell => new ProcessStartInfo("powershell", $"{command.Command}"),
                    };
                    info.UseShellExecute = false;
                    info.RedirectStandardError = true;
                    info.RedirectStandardOutput = true;
                    var encoding = Encoding.GetEncoding(ci.TextInfo.OEMCodePage);
                    if (command.Command.IndexOf("iisexpress") >= 0)
                    {
                        encoding = Encoding.GetEncoding(ci.TextInfo.ANSICodePage);
                        encoding = Encoding.GetEncoding(ci.TextInfo.ANSICodePage);
                    }
                    else if (command.ShellType == ShellType.Wsl)
                    {
                        encoding = Encoding.UTF8;
                    }
                    else
                    {
                        encoding = Encoding.GetEncoding(ci.TextInfo.OEMCodePage);
                    }
                    info.CreateNoWindow = true;
                    info.StandardOutputEncoding = encoding;
                    info.StandardErrorEncoding = encoding;
                    var process = Process.Start(info);
                    process.OutputDataReceived += (s, a) =>
                    {
                        if (a.Data == null)
                            return;
                        lock (outputSync)
                        {
                            WriteLogo(command);
                            Console.WriteLine(a.Data);
                        }
                    };
                    process.ErrorDataReceived += (s, a) => {
                        if (a.Data == null)
                            return;

                        lock (outputSync)
                        {
                            WriteLogo(command);
                            Console.WriteLine(a.Data);
                        }
                    };
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    command.Process = process;
                }

                Console.CancelKeyPress += (s, a) => {
                    a.Cancel = true;
                    foreach(var commnad in commands) {
                        FreeConsole();
                        if (AttachConsole((uint)commnad.Process.Id))
                        {
                            GenerateConsoleCtrlEvent(CtrlTypes.CTRL_C_EVENT, 0);
                            FreeConsole();
                        }
                    }
                };
                var tasks = commands.Select(x => Task.Run(() => x.Process.WaitForExit())).ToArray();
                Task.WaitAll(tasks);
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
        }

        private static bool TryGetShellType(string value, ref ShellType type)
        {
            if (value == "wsl")
            {
                type = ShellType.Wsl;
                return true;
            }
            if (value == "cmd")
            {
                type = ShellType.Cmd;
                return true;
            }
            if (value == "powershell")
            {
                type = ShellType.Powershell;
                return true;
            }
            return false;
        }

        private static void WriteLogo(ForemanProcess command)
        {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = command.Color;
            try
            {
                Console.Write(command.Name.PadRight(10) + "|");
            }
            finally
            {
                Console.ForegroundColor = color;
            }
        }
    }
}
