using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZaprosiVutm.Core;

namespace ZaprosiVutm
{
    internal static class Program
    {
        private static readonly string _currentVersion = "1.0.9"; // Текущая версия приложения

        // Вычисляются лениво: адрес сервера известен только после CheckServerStatus().
        private static string VersionUrl =>
            $"http://{InternetConnectionChecker.ServerIP}:{AppConfig.UpdateServerPort}/rv2/version.txt";

        private static string UpdateUrl =>
            $"http://{InternetConnectionChecker.ServerIP}:{AppConfig.UpdateServerPort}/rv2/ZaprosiVutm.zip";

        private static bool _isUpdating = false;

        // Получаем имя приложения без расширения
        public static readonly string _appName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName);

        // Основная папка для логов
        public static readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _appName, "Logs");

        // Основная папка для файлов
        public static readonly string _directoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _appName, "UTM");

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string currentDirectory = Directory.GetCurrentDirectory();
            Logger.Log($"Текущая рабочая директория приложения: {currentDirectory}", LogLevel.Info);

            Logger.Log($"Текущая рабочая директория логов: {_logDirectory}", LogLevel.Info);
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            Logger.Log($"Текущая рабочая директория файлов: {_directoryPath}", LogLevel.Info);
            if (!Directory.Exists(_directoryPath))
                Directory.CreateDirectory(_directoryPath);

            // Путь к файлу, который будет хранить дату последнего запуска
            string lastRunFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lastrun.txt");

            // Проверяем, существует ли файл и соответствует ли дата
            if (File.Exists(lastRunFilePath))
            {
                string lastRunDate = File.ReadAllText(lastRunFilePath);
                if (DateTime.TryParse(lastRunDate, out DateTime lastRun) && lastRun.Date == DateTime.Today)
                {
                    // Приложение уже запускалось сегодня, завершаем выполнение
                    Logger.Log("Приложение уже запускалось сегодня.", LogLevel.Info);
                    return;
                }
            }
            // Записываем текущую дату в файл
            File.WriteAllText(lastRunFilePath, DateTime.Now.ToString());

            InternetConnectionChecker.CheckServerStatus();
            Logger.Log($"Сервер -> {InternetConnectionChecker.ServerStatus}", LogLevel.Info);

            // Удаляем старую версию, если она существует
            string currentProcessPath = Process.GetCurrentProcess().MainModule.FileName;
            string backupFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileNameWithoutExtension(currentProcessPath) + ".bak");
            DeletePathIfExists(backupFilePath);

            // Удаляем папку update, если она существует
            string extractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update");
            DeletePathIfExists(extractPath);

            // Автообновление отключено по умолчанию, см. README, раздел "Безопасность".
            if (AppConfig.EnableAutoUpdate && InternetConnectionChecker.ServerStatus)
            {
                CheckForUpdatesAsync().GetAwaiter().GetResult();
            }

            // Автозапуск отключён по умолчанию: программа не должна молча прописывать себя в реестр.
            if (AppConfig.EnableAutostart)
            {
                SetToAutostartup();
            }

            if (!WaitForUtmService())
            {
                return;
            }

            Application.Run(new MainForm());
        }

        /// <summary>
        /// Ждёт запуска службы УТМ: без неё запросы к ЕГАИС смысла не имеют.
        /// </summary>
        private static bool WaitForUtmService()
        {
            string serviceName = AppConfig.UtmServiceName;

            try
            {
                ServiceController service = new ServiceController(serviceName);
                while (service.Status != ServiceControllerStatus.Running)
                {
                    Logger.Log($"Ожидание запуска службы {serviceName}...", LogLevel.Info);
                    Thread.Sleep(60000); // Ждем 1 минуту перед следующей проверкой
                    service.Refresh(); // Обновляем статус службы
                }
                return true;
            }
            catch (InvalidOperationException ex)
            {
                // Службы с таким именем нет — проверьте UtmServiceName в App.config.
                Logger.Log($"Служба '{serviceName}' не найдена: {ex.Message}", LogLevel.Fatal);
                return false;
            }
        }

        private static void DeletePathIfExists(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                    Console.WriteLine($"Файл '{path}' удален.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при удалении файла '{path}': {ex.Message}");
                }
            }
            else if (Directory.Exists(path))
            {
                try
                {
                    Directory.Delete(path, true);
                    Console.WriteLine($"Папка '{path}' удалена.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при удалении папки '{path}': {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"Путь '{path}' не существует.");
            }
        }

        private static async Task CheckForUpdatesAsync()
        {
            if (_isUpdating)
            {
                Console.WriteLine("Обновление уже в процессе.");
                return;
            }

            _isUpdating = true; // Устанавливаем флаг обновления

            string latestVersionString = await GetLatestVersionAsync();

            if (latestVersionString != null)
            {
                Version latestVersion = new Version(latestVersionString);
                Version currentVersion = new Version(_currentVersion);

                if (latestVersion > currentVersion)
                {
                    Console.WriteLine("Найдена новая версия: " + latestVersion);
                    await DownloadUpdateAsync();
                }
                else if (latestVersion < currentVersion)
                {
                    Console.WriteLine("У вас последняя версия.");
                }
                else
                {
                    Console.WriteLine("Вы используете актуальную версию.");
                }
            }
            else
            {
                Console.WriteLine("Не удалось получить информацию о последней версии.");
            }
        }

        private static async Task<string> GetLatestVersionAsync()
        {
            using (WebClient client = new WebClient())
            {
                try
                {
                    return await client.DownloadStringTaskAsync(VersionUrl);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка при получении версии: " + ex.Message);
                    return null;
                }
            }
        }

        private static async Task DownloadUpdateAsync()
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "update.zip");
            string extractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update"); // Папка для распаковки
            Directory.CreateDirectory(extractPath); // Создаем папку для распаковки

            using (WebClient client = new WebClient())
            {
                try
                {
                    Console.WriteLine("Скачивание обновления...");
                    await client.DownloadFileTaskAsync(UpdateUrl, tempFilePath);
                    Console.WriteLine("Обновление скачано.");

                    // Распаковываем архив
                    ZipFile.ExtractToDirectory(tempFilePath, extractPath);

                    // Находим исполняемый файл в распакованной папке
                    string[] files = Directory.GetFiles(extractPath, "*.exe");
                    if (files.Length == 0)
                    {
                        Console.WriteLine("Не найден исполняемый файл в обновлении.");
                        return;
                    }

                    // Путь к текущему исполняемому файлу
                    string currentProcessPath = Process.GetCurrentProcess().MainModule.FileName;

                    // Переименовываем текущую версию в .bak
                    string backupFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileNameWithoutExtension(currentProcessPath) + ".bak");
                    if (File.Exists(backupFilePath))
                    {
                        File.Delete(backupFilePath); // Удаляем старый бэкап, если он существует
                    }
                    File.Move(currentProcessPath, backupFilePath);

                    // Перемещаем новый файл в текущую папку
                    string newFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileName(files[0]));
                    File.Move(files[0], newFilePath);

                    // Запускаем новую версию
                    Process newProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = newFilePath,
                            UseShellExecute = true
                        }
                    };
                    newProcess.Start();

                    // Ждем, пока новая версия запустится
                    newProcess.WaitForInputIdle();

                    // Закрываем старую версию
                    Process currentProcess = Process.GetCurrentProcess();
                    currentProcess.Kill();

                    // Удаляем временные файлы
                    try
                    {
                        // Удаляем папку для распаковки
                        if (Directory.Exists(extractPath))
                        {
                            Directory.Delete(extractPath, true);
                        }
                        // Удаляем временный zip файл
                        if (File.Exists(tempFilePath))
                        {
                            File.Delete(tempFilePath);
                        }
                        // Удаляем старую версию (файл .bak)
                        if (File.Exists(backupFilePath))
                        {
                            File.Delete(backupFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Ошибка при удалении временных файлов: " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка при скачивании обновления: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Прописывает программу в автозагрузку текущего пользователя.
        /// Вызывается только если EnableAutostart включён в App.config.
        /// </summary>
        static void SetToAutostartup()
        {
            // Получаем путь к исполняемому файлу
            string exePath = Process.GetCurrentProcess().MainModule.FileName;

            // Получаем имя приложения
            string appName = Path.GetFileNameWithoutExtension(exePath);

            // Открываем ключ реестра
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
                if (key != null)
                {
                    // Проверяем, существует ли уже запись
                    if (key.GetValue(appName) == null)
                    {
                        // Если записи нет, добавляем её
                        key.SetValue(appName, exePath);
                        Console.WriteLine($"Программа '{appName}' добавлена в автозагрузку.");
                    }
                    else
                    {
                        Console.WriteLine($"Программа '{appName}' уже добавлена в автозагрузку.");
                    }
                }
            }
        }
    }
}
