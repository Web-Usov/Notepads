namespace Notepads.SyntaxExtensions
{
    using System.Globalization;
#if WINDOWS_UWP
    using Windows.ApplicationModel.Resources;
#else
    using System.Resources;
#endif

    internal static class ExtensionMessages
    {
#if WINDOWS_UWP
        private static readonly ResourceLoader Resources = ResourceLoader.GetForViewIndependentUse("ExtensionMessages");
#else
        private static readonly ResourceManager Resources = new ResourceManager(
            "Notepads.ExtensionMessages", typeof(ExtensionMessages).Assembly);
#endif

        public static string Get(string key, params object[] values)
        {
            string text = Resources.GetString(key);
            return values.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, values);
        }
    }
}
