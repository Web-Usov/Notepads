// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2019-2024, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Notepads.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Windows.ApplicationModel;
    using Windows.System;

    public enum NotepadsOperationProtocol
    {
        Unrecognized,
        OpenNewInstance
    }

    public static class NotepadsProtocolService
    {
        internal const string NewInstanceUri = "plainix://newinstance";
        private const string LegacyNewInstanceUri = "notepads://newinstance";

        public static NotepadsOperationProtocol GetOperationProtocol(Uri uri, out string context)
        {
            context = null;

            try
            {
                var operation = uri.ToString().Trim();
                if (operation.EndsWith("/", StringComparison.Ordinal))
                {
                    operation = operation.Substring(0, operation.Length - 1);
                }

                if (string.Equals(operation, NewInstanceUri, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(operation, LegacyNewInstanceUri, StringComparison.OrdinalIgnoreCase))
                {
                    return NotepadsOperationProtocol.OpenNewInstance;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[{nameof(NotepadsProtocolService)}] Failed to parse protocol: {uri}, exception: {ex}");
                AnalyticsService.TrackEvent("NotepadsProtocolService_FailedToParseProtocol", new Dictionary<string, string>()
                {
                    { "Protocol", uri.ToString() },
                    { "Exception", ex.ToString() }
                });
            }

            return NotepadsOperationProtocol.Unrecognized;
        }

        public static async Task<bool> LaunchProtocolAsync(NotepadsOperationProtocol operation)
        {
            try
            {
                if (operation == NotepadsOperationProtocol.Unrecognized)
                {
                    return false;
                }
                else if (operation == NotepadsOperationProtocol.OpenNewInstance)
                {
                    var launchOptions = new LauncherOptions { TargetApplicationPackageFamilyName = Package.Current.Id.FamilyName };
                    return await Launcher.LaunchUriAsync(new Uri(NewInstanceUri), launchOptions);
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[{nameof(NotepadsProtocolService)}] Failed to execute protocol: {operation}, Exception: {ex}");
                AnalyticsService.TrackEvent("NotepadsProtocolService_FailedToExecuteProtocol", new Dictionary<string, string>()
                {
                    { "Protocol", operation.ToString() },
                    { "Exception", ex.Message }
                });
                return false;
            }
        }
    }
}