using System;

namespace Clockify_IFTTT
{


    public static class Logger
    {
        private static bool doDebug = true;

        public static void info(string message)
        {

            Console.WriteLine($"{DateTime.Now.ToString("HH:mm:ss")} - [INFO] > {message}");
        }

        public static void fatal(string message)
        {
            Console.WriteLine($"{DateTime.Now.ToString("HH:mm:ss")} - [FATAL] > {message}");
        }
    }
}