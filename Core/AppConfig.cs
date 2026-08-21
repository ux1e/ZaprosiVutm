using System.Configuration;

namespace ZaprosiVutm.Core
{
    /// <summary>
    /// Настройки приложения из App.config.
    /// Все адреса вынесены сюда, чтобы конкретная инфраструктура не хранилась в коде.
    /// </summary>
    internal static class AppConfig
    {
        public static string UtmHost => Get("UtmHost", "127.0.0.1");

        public static int UtmPort => GetInt("UtmPort", 8080);

        public static string UtmServiceName => Get("UtmServiceName", "Transport");

        public static int PricePerLiter => GetInt("PricePerLiter", 100);

        public static string UpdateServerLocal => Get("UpdateServerLocal", string.Empty);

        public static string UpdateServerPublic => Get("UpdateServerPublic", string.Empty);

        public static int UpdateServerPort => GetInt("UpdateServerPort", 4401);

        public static string LocalProbeHost => Get("LocalProbeHost", string.Empty);

        public static bool EnableAutostart => GetBool("EnableAutostart", false);

        public static bool EnableAutoUpdate => GetBool("EnableAutoUpdate", false);

        /// <summary>Базовый адрес УТМ вида http://host:port</summary>
        public static string UtmBaseUrl => $"http://{UtmHost}:{UtmPort}";

        private static string Get(string key, string fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int GetInt(string key, int fallback)
        {
            return int.TryParse(Get(key, null), out int value) ? value : fallback;
        }

        private static bool GetBool(string key, bool fallback)
        {
            return bool.TryParse(Get(key, null), out bool value) ? value : fallback;
        }
    }
}
