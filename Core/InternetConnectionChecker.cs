using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ZaprosiVutm.Core;

namespace ZaprosiVutm
{
    /// <summary>
    /// Определяет, доступен ли сервер обновлений и находимся ли мы внутри локальной сети.
    /// Конкретные адреса задаются в App.config, см. <see cref="AppConfig"/>.
    /// </summary>
    public class InternetConnectionChecker
    {
        public static bool ServerStatus = false;

        public static bool IsInLocal = false;

        /// <summary>
        /// Адрес сервера обновлений: локальный, если мы внутри сети, иначе внешний.
        /// Пустая строка означает, что сервер не настроен.
        /// </summary>
        public static string ServerIP => IsInLocal
            ? AppConfig.UpdateServerLocal
            : AppConfig.UpdateServerPublic;

        public static bool CheckIPAviable(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return false;

            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send(ip, 5000);
                    return reply != null && reply.Status == IPStatus.Success;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"{ip} недоступен: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public static bool CheckPortAvailability(string ip, int port, int timeout = 5000)
        {
            if (string.IsNullOrEmpty(ip))
                return false;

            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(ip, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(timeout);
                    if (!success)
                    {
                        return false; // Порт недоступен
                    }
                    client.EndConnect(result);
                    return true; // Порт доступен
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"{ip}:{port} недоступен: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Выясняет, доступен ли сервер обновлений, и с какой стороны сети мы находимся.
        /// Если сервер не задан в App.config, работаем автономно.
        /// </summary>
        public static void CheckServerStatus()
        {
            string local = AppConfig.UpdateServerLocal;
            string external = AppConfig.UpdateServerPublic;

            if (string.IsNullOrEmpty(local) && string.IsNullOrEmpty(external))
            {
                Logger.Log("Сервер обновлений не настроен, работаем автономно.", LogLevel.Info);
                IsInLocal = false;
                ServerStatus = false;
                return;
            }

            // Локальную сеть определяем по доступности контрольного хоста.
            bool canReachLocal = !string.IsNullOrEmpty(local)
                && CheckIPAviable(AppConfig.LocalProbeHost);

            bool canReachPublic = !string.IsNullOrEmpty(external)
                && CheckPortAvailability(external, AppConfig.UpdateServerPort);

            if (canReachLocal)
            {
                IsInLocal = true;
                ServerStatus = true; // Успешно подключен к локальной сети
            }
            else if (canReachPublic)
            {
                IsInLocal = false;
                ServerStatus = true; // Успешно подключен к внешней сети
            }
            else
            {
                ServerStatus = false; // Не удалось подключиться ни к одному серверу
            }
        }
    }
}
