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

        static int Main(string[] args)
        {
            try {
                int lcid = GetSystemDefaultLCID();
                var ci = System.Globalization.CultureInfo.GetCultureInfo(lcid);
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var procfile = "Procfile";
                var help = false;
                var wsl = false;
                var options = new OptionSet {
                    {"f|procfile=", "Procfile", v => procfile = v},
                    {"h|help", v => help = v != null},
                    {"wsl", v => wsl = v != null},
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
                var commands = new Dictionary<string, string>();
                foreach(var line in File.ReadLines(procfile)) {
                    var currentline = line.Trim();
                    if (currentline.StartsWith('#'))
                        continue;
                    var index = currentline.IndexOf(':');
                    if (index <= 0)
                        continue;
                    if (index == currentline.Length - 1)
                        continue;
                    commands.Add(currentline.Substring(0, index).Trim(), currentline.Substring(index + 1).Trim());
                }
                var processes = new List<Process>();
                foreach(var command in commands.Keys)
                {
                    string cmd = commands[command];
                    Console.WriteLine(cmd);
                    var info = new ProcessStartInfo("cmd", $"/c \"{cmd}\"");
                    if (wsl)
                        info = new ProcessStartInfo("wsl", $"bash --login -c '{cmd}'");
                    info.UseShellExecute = false;
                    info.CreateNoWindow = true;
                    info.RedirectStandardError = true;
                    info.RedirectStandardOutput = true;
                    info.StandardOutputEncoding = Encoding.GetEncoding(ci.TextInfo.OEMCodePage);
                    info.StandardErrorEncoding = Encoding.GetEncoding(ci.TextInfo.OEMCodePage);
                    var process = Process.Start(info);
                    process.OutputDataReceived += (s, a) => {
                        if (a.Data != null)
                            Console.WriteLine(a.Data);
                    };
                    process.ErrorDataReceived += (s, a) => {
                        if (a.Data != null)
                            Console.WriteLine(a.Data);
                    };
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    processes.Add(process);
                }

                Console.CancelKeyPress += (s, a) => {
                    a.Cancel = true;
                    foreach(var process in processes) {
                        FreeConsole();
                        if (AttachConsole((uint)process.Id))
                        {
                            // Disable Ctrl-C handling for our program
                            Console.WriteLine(GenerateConsoleCtrlEvent(CtrlTypes.CTRL_C_EVENT, 0));

                            FreeConsole();
                        }
                    }
                };
                var tasks = processes.Select(x => Task.Run(() => x.WaitForExit())).ToArray();
                Task.WaitAll(tasks);
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
        }
    }
}
