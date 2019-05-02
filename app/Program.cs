using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        delegate Boolean ConsoleCtrlDelegate(CtrlTypes type);

        static void Main(string[] args)
        {
            try {
                if (!File.Exists("Procfile")) {
                    Console.WriteLine("Procfile not found");
                    return;
                }
                var lines = File.ReadAllLines("Procfile");
                var commands = new Dictionary<string, string>();
                foreach(var line in lines) { 
                    var currentline = line.Trim();
                    if (currentline.StartsWith('#'))
                        continue;
                    var index = currentline.IndexOf(':');
                    if (index <= 0)
                        continue;
                    if (index == currentline.Length - 1)
                        continue;
                    commands.Add(currentline.Substring(0, index), currentline.Substring(index + 1));
                }
                var processes = new List<Process>();
                foreach(var command in commands.Keys)
                {
                    var info = new ProcessStartInfo("cmd.exe", $"/c \"{commands[command]}\"");
                    info.UseShellExecute = false;
                    info.CreateNoWindow = true;
                    info.RedirectStandardError = true;
                    info.RedirectStandardOutput = true;
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
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
