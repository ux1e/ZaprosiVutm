using System;
using System.IO;

namespace ZaprosiVutm.Core
{
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warn,
        Error,
        Fatal
    }

    public static class Logger
    {
        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            // Получаем текущую дату и создаем имя папки
            string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
            string logFolderPath = Path.Combine(Program._logDirectory, dateFolder);

            // Создаем папку для логов, если она не существует
            Directory.CreateDirectory(logFolderPath);

            // Формируем путь к файлу лога
            string logFilePath = Path.Combine(logFolderPath, "log.txt");

            // Формируем запись лога
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {level.ToString().ToUpper()} | {message}";

            // Вывод в консоль
            Console.WriteLine(logEntry);

            // Запись в файл
            File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
        }
    }
}
