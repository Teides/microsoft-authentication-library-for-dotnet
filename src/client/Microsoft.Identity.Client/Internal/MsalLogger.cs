﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Text;
using Microsoft.Identity.Client.Core;
using Microsoft.Identity.Client.PlatformsCommon.Factories;
using Microsoft.Identity.Client.PlatformsCommon.Interfaces;

namespace Microsoft.Identity.Client.Internal
{
    internal class MsalLogger : ICoreLogger
    {
        private readonly IPlatformLogger _platformLogger;
        private readonly LogCallback _loggingCallback;
        private readonly LogLevel _logLevel;
        private readonly bool _isDefaultPlatformLoggingEnabled;
        private static readonly Lazy<ICoreLogger> s_nullLogger = new Lazy<ICoreLogger>(() => new NullLogger());

        internal MsalLogger(
            Guid correlationId,
            string clientName,
            string clientVersion,
            LogLevel logLevel,
            bool enablePiiLogging,
            bool isDefaultPlatformLoggingEnabled,
            LogCallback loggingCallback)
        {
            CorrelationId = correlationId;
            PiiLoggingEnabled = enablePiiLogging;
            _loggingCallback = loggingCallback;
            _logLevel = logLevel;
            _isDefaultPlatformLoggingEnabled = isDefaultPlatformLoggingEnabled;

            _platformLogger = PlatformProxyFactory.CreatePlatformProxy(null).PlatformLogger;
            ClientName = clientName ?? string.Empty;
            ClientVersion = clientVersion ?? string.Empty;

            ClientInformation = string.Empty;
            if (!string.IsNullOrEmpty(clientName))
            {
                // space is intentional for formatting of the message
                if (string.IsNullOrEmpty(clientVersion))
                {
                    ClientInformation = string.Format(CultureInfo.InvariantCulture, " ({0})", clientName);
                }
                else
                {
                    ClientInformation = string.Format(CultureInfo.InvariantCulture, " ({0}: {1})", clientName, clientVersion);
                }
            }
        }

        public static ICoreLogger Create(
            Guid correlationId,
            IApplicationConfiguration config,
            bool isDefaultPlatformLoggingEnabled = false)
        {
            return new MsalLogger(
                correlationId,
                config?.ClientName ?? string.Empty,
                config?.ClientVersion ?? string.Empty,
                config?.LogLevel ?? LogLevel.Verbose,
                config?.EnablePiiLogging ?? false,
                config?.IsDefaultPlatformLoggingEnabled ?? isDefaultPlatformLoggingEnabled,
                config?.LoggingCallback ?? null);
        }

        public static ICoreLogger NullLogger => s_nullLogger.Value;

        public Guid CorrelationId { get; }

        public bool PiiLoggingEnabled { get; }

        public string ClientName { get; }
        public string ClientVersion { get; }

        internal string ClientInformation { get; }

        public void Info(string messageScrubbed)
        {
            Log(LogLevel.Info, string.Empty, messageScrubbed);
        }

        public void InfoPii(string messageWithPii, string messageScrubbed)
        {
            Log(LogLevel.Info, messageWithPii, messageScrubbed);
        }

        public void InfoPii(Exception exWithPii)
        {
            Log(LogLevel.Info, exWithPii.ToString(), GetPiiScrubbedExceptionDetails(exWithPii));
        }

        public void InfoPiiWithPrefix(Exception exWithPii, string prefix)
        {
            Log(LogLevel.Info, prefix + exWithPii.ToString(), prefix + GetPiiScrubbedExceptionDetails(exWithPii));
        }

        public void Verbose(string messageScrubbed)
        {
            Log(LogLevel.Verbose, string.Empty, messageScrubbed);
        }

        public void VerbosePii(string messageWithPii, string messageScrubbed)
        {
            Log(LogLevel.Verbose, messageWithPii, messageScrubbed);
        }

        public void Warning(string messageScrubbed)
        {
            Log(LogLevel.Warning, string.Empty, messageScrubbed);
        }

        public void WarningPii(string messageWithPii, string messageScrubbed)
        {
            Log(LogLevel.Warning, messageWithPii, messageScrubbed);
        }

        public void WarningPii(Exception exWithPii)
        {
            Log(LogLevel.Warning, exWithPii.ToString(), GetPiiScrubbedExceptionDetails(exWithPii));
        }

        public void WarningPiiWithPrefix(Exception exWithPii, string prefix)
        {
            Log(LogLevel.Warning, prefix + exWithPii.ToString(), prefix + GetPiiScrubbedExceptionDetails(exWithPii));
        }

        public void Error(string messageScrubbed)
        {
            Log(LogLevel.Error, string.Empty, messageScrubbed);
        }

        public void ErrorPii(Exception exWithPii)
        {
            Log(LogLevel.Error, exWithPii.ToString(), GetPiiScrubbedExceptionDetails(exWithPii));
        }

        public void ErrorPiiWithPrefix(Exception exWithPii, string prefix)
        {
            Log(LogLevel.Error, prefix + exWithPii.ToString(), prefix + GetPiiScrubbedExceptionDetails(exWithPii));
        }

        public void ErrorPii(string messageWithPii, string messageScrubbed)
        {
            Log(LogLevel.Error, messageWithPii, messageScrubbed);
        }

        private void Log(LogLevel msalLogLevel, string messageWithPii, string messageScrubbed)
        {
            if (_loggingCallback == null || msalLogLevel > _logLevel)
            {
                return;
            }

            //format log message;
            string correlationId = CorrelationId.Equals(Guid.Empty)
                ? string.Empty
                : " - " + CorrelationId;

            var msalIdParameters = MsalIdHelper.GetMsalIdParameters(this);
            string os = "N/A";
            if (msalIdParameters.TryGetValue(MsalIdParameter.OS, out string osValue))
            {
                os = osValue;
            }

            bool messageWithPiiExists = !string.IsNullOrWhiteSpace(messageWithPii);
            // If we have a message with PII, and PII logging is enabled, use the PII message, else use the scrubbed message.
            bool isLoggingPii = messageWithPiiExists && PiiLoggingEnabled;
            string messageToLog = isLoggingPii ? messageWithPii : messageScrubbed;

            string log = string.Format(CultureInfo.InvariantCulture, "{0} MSAL {1} {2} {3} [{4}{5}]{6} {7}",
                isLoggingPii ? "(True)" : "(False)",
                MsalIdHelper.GetMsalVersion(),
                msalIdParameters[MsalIdParameter.Product],
                os, DateTime.UtcNow, correlationId, ClientInformation, messageToLog);

            if (_isDefaultPlatformLoggingEnabled)
            {
                switch (msalLogLevel)
                {
                    case LogLevel.Error:
                        _platformLogger.Error(log);
                        break;
                    case LogLevel.Warning:
                        _platformLogger.Warning(log);
                        break;
                    case LogLevel.Info:
                        _platformLogger.Information(log);
                        break;
                    case LogLevel.Verbose:
                        _platformLogger.Verbose(log);
                        break;
                }
            }

            _loggingCallback.Invoke(msalLogLevel, log, isLoggingPii);
        }

        internal static string GetPiiScrubbedExceptionDetails(Exception ex)
        {
            var sb = new StringBuilder();
            if (ex != null)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Exception type: {0}", ex.GetType()));

                if (ex is MsalException msalException)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, ", ErrorCode: {0}", msalException.ErrorCode));
                }

                if (ex is MsalServiceException msalServiceException)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "HTTP StatusCode {0}", msalServiceException.StatusCode));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "CorrelationId {0}", msalServiceException.CorrelationId));
                }

                if (ex.InnerException != null)
                {
                    sb.AppendLine("---> Inner Exception Details");
                    sb.AppendLine(GetPiiScrubbedExceptionDetails(ex.InnerException));
                    sb.AppendLine("=== End of inner exception stack trace ===");
                }

                if (ex.StackTrace != null)
                {
                    sb.Append(Environment.NewLine + ex.StackTrace);
                }
            }

            return sb.ToString();
        }
    }
}
