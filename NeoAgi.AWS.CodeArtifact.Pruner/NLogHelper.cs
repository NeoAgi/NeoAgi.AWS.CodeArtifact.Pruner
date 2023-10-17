using Microsoft.Extensions.Logging;
using NLog.Layouts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner
{
    internal class NLogHelper
    {
        internal static JsonLayout Layout = new JsonLayout
        {
            Attributes =
            {
                new JsonAttribute("timestamp", "${date:universalTime=true:format=o}"),
                new JsonAttribute("level", "${level:upperCase=true}"),
                new JsonAttribute("hostname", "${hostname}"),
                new JsonAttribute("threadId", "${threadId}"),
                new JsonAttribute("logger", "${logger}"),
                new JsonAttribute("message", "${message}"),
                new JsonAttribute("properties", new JsonLayout { IncludeEventProperties = true, MaxRecursionLimit = 2 }, encode: false),
                new JsonAttribute("exception", new JsonLayout
                {
                    Attributes =
                    {
                        new JsonAttribute("type", "${exception:format=type}"),
                        new JsonAttribute("message", "${exception:format=message}"),
                        new JsonAttribute("stacktrace", "${exception:format=tostring}"),
                    }
                },
                encode: false) // don't escape layout
            }
        };

        internal static NLog.LogLevel MapLogLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Critical:
                    return NLog.LogLevel.Fatal;
                case LogLevel.Error:
                    return NLog.LogLevel.Error;
                case LogLevel.Warning:
                    return NLog.LogLevel.Warn;
                case LogLevel.Information:
                    return NLog.LogLevel.Info;
                case LogLevel.Debug:
                    return NLog.LogLevel.Debug;
                case LogLevel.Trace:
                    return NLog.LogLevel.Trace;
                case LogLevel.None:
                default:
                    return NLog.LogLevel.Off;
            }
        }
    }
}
