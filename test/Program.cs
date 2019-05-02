using System;
using System.Threading;

namespace test
{
    class Program
    {
        static void Main(string[] args)
        {
            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, a) => {
                exitEvent.Set();
            };
            Console.WriteLine("Waiting for ctrl+c");
            exitEvent.WaitOne();
        }
    }
}
