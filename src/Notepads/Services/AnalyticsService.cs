// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2019-2024, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Notepads.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Keeps legacy telemetry call sites source-compatible without transmitting diagnostics.
    /// Standalone Plainix releases must not forward document paths, exception text or
    /// stack traces to Store Services without informed user consent.
    /// </summary>
    public static class AnalyticsService
    {
        public static void TrackEvent(string eventName, IDictionary<string, string> properties = null)
        {
            // Intentionally disabled for the standalone fork.
        }

        public static void TrackError(Exception exception, IDictionary<string, string> properties = null)
        {
            // Intentionally disabled for the standalone fork.
        }
    }
}
