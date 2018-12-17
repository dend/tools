using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace tripled.Kernel
{
    internal class Logger
    {
        internal string CurrentLogFile { get; private set; }

        private TraceLevel _traceLevel;
        private bool _writeToFile = false;
        private StringBuilder _sb = new StringBuilder();

        internal Logger(bool writeToFile, TraceLevel traceLevel)
        {
            _traceLevel = traceLevel;
            _writeToFile = writeToFile;
            if (_writeToFile)
            {
                CurrentLogFile = string.Format("{0}.txt", DateTime.Now.Ticks);
            }
        }

        internal static void InternalLog(string logEntry)
        {
            StackTrace stackTrace = new StackTrace();
            Debug.WriteLine(string.Format("{0}: {1}", stackTrace.GetFrame(1).GetMethod().Name, logEntry));
        }

        internal void Log(string logEntry, TraceLevel level = TraceLevel.Info)
        {
            if (level <= _traceLevel)
            {
                if (_writeToFile)
                {
                    _sb.AppendLine(logEntry);
                }
                Console.WriteLine(logEntry);
            }
        }

        internal void Flush()
        {
            if (_writeToFile)
            {
                File.WriteAllText(CurrentLogFile, _sb.ToString());
            }
        }
    }
}
