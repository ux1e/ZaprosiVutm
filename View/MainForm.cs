using System;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Linq;
using System.Net.Http;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Net;
using System.Text;
using ZaprosiVutm.Core;
using System.Diagnostics;

namespace ZaprosiVutm
{
    public partial class MainForm : Form
    {
        private NotifyIcon notifyIcon;

        public string currentCN;

        private readonly EgaisRestWorker _egaisWorker = new EgaisRestWorker();
        private readonly ManualResetEventSlim _requestMutex = new ManualResetEventSlim(true);
        //private readonly string _directoryPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        private static readonly string[] _fileNames = new string[]
        {
            "ReplyRests_v2.xml",
            "ActWriteOff_v3.xml",
            "ParsedBeer.xml"
        };

        private readonly string _selectedIP;
        private int _priceForLiter;
        private DateTime _lastRequestTime;

        public MainForm()
        {
            _selectedIP = AppConfig.UtmHost;
            GetCnForSelectedIP();

            InitializeComponent();
            tbPriceForLiter.Text = AppConfig.PricePerLiter.ToString();
            InitializeNotifyIcon();

            // Скрываем форму и минимизируем ее
            this.WindowState = FormWindowState.Minimized; // Сначала минимизируем
            this.Hide(); // Затем скрываем
            Icon = Properties.Resources.AppIcon;

            if (InternetConnectionChecker.ServerStatus)
            {
                SendHelloMessageToServer();
            }

            WriteLog("Робот инициализирован", LogLevel.Info);
            StartPivo();
        }

        private async void SendHelloMessageToServer()
        {
            await EgaisRestWorker.GetCnValueAsync(_selectedIP);

            SendMessage($"{InternetConnectionChecker.ServerIP}:{AppConfig.UpdateServerPort}", "Key", "начал работать" + "~~" + currentCN);
        }

        public void SendMessage(string webservice, string type, string data)
        {
            string url = $"http://{webservice}/mon/hs/Exchange/query";
            string postData = $"qtype={Uri.EscapeDataString(type)}&client={Uri.EscapeDataString(currentCN)}&data={Uri.EscapeDataString(data)}";

            try
            {
                // Создаем запрос
                WebRequest request = WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                byte[] byteArray = Encoding.UTF8.GetBytes(postData);
                request.ContentLength = byteArray.Length;

                // Отправляем данные
                using (Stream dataStream = request.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);
                }

                // Получаем ответ
                using (WebResponse response = request.GetResponse())
                {
                    WriteLog(((HttpWebResponse)response).StatusDescription, LogLevel.Trace);

                    using (Stream dataStream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(dataStream))
                    {
                        string responseFromServer = reader.ReadToEnd();
                        WriteLog(responseFromServer, LogLevel.Info);
                    }
                }
            }
            catch (WebException ex)
            {
                // Обработка исключений, связанных с веб-запросами
                WriteLog($"Ошибка при отправке сообщения: {ex.Message}", LogLevel.Fatal);
                if (ex.Response != null)
                {
                    using (var errorResponse = (HttpWebResponse)ex.Response)
                    {
                        WriteLog($"Статус ошибки: {errorResponse.StatusCode}", LogLevel.Fatal);
                    }
                }
            }
            catch (Exception ex)
            {
                // Обработка других исключений
                WriteLog($"Произошла ошибка: {ex.Message}", LogLevel.Fatal);
            }
        }

        private void InitializeNotifyIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Icon = Properties.Resources.AppIcon,
                Visible = true,
                Text = "НЕ ЗАКРЫВАТЬ МЕНЯ"
            };

            // Создаем контекстное меню
            ContextMenuStrip contextMenu = new ContextMenuStrip();

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += ExitItem_Click;

            ToolStripMenuItem startItem = new ToolStripMenuItem("Списать пиво");
            startItem.Click += bStart_Click;

            ToolStripMenuItem keyInfoItem = new ToolStripMenuItem("Инфа о ключе");
            keyInfoItem.Click += KeyInfo_Click;

            ToolStripMenuItem debugItem = new ToolStripMenuItem("ДЕБАГ");
            debugItem.Click += Debug_Click;


            contextMenu.Items.Add(startItem);
            contextMenu.Items.Add(keyInfoItem);
            contextMenu.Items.Add(exitItem);
            contextMenu.Items.Add(debugItem);

            notifyIcon.ContextMenuStrip = contextMenu;

            // Обработчик события двойного клика по иконке
            notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        }

        private async void Debug_Click(object sender, EventArgs e)
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            ShowMessage($"Директории \n" +
                $"приложения: {currentDirectory} \n" +
                $"логов: {Program._logDirectory} \n" +
                $"файлов: {Program._directoryPath} ");

            //Process.Start("explorer.exe", Logger._logDirectory);
        }

        private async void KeyInfo_Click(object sender, EventArgs e)
        {
            string url = $"{AppConfig.UtmBaseUrl}/api/info/list";

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    // Выполняем GET-запрос
                    var response = await client.GetStringAsync(url);

                    // Десериализуем JSON-ответ
                    var serializer = new JavaScriptSerializer();
                    var jsonResponse = serializer.Deserialize<Dictionary<string, object>>(response);

                    // Извлекаем rsa и gost как словари
                    var rsa = (Dictionary<string, object>)jsonResponse["rsa"];
                    var gost = (Dictionary<string, object>)jsonResponse["gost"];

                    // Извлекаем нужные данные
                    string rsaStartDate = rsa["startDate"].ToString();
                    string rsaExpireDate = rsa["expireDate"].ToString();
                    string gostStartDate = gost["startDate"].ToString();
                    string gostExpireDate = gost["expireDate"].ToString();

                    // Преобразуем строки в DateTime
                    DateTime rsaExpireDateTime = DateTime.Parse(rsaExpireDate);
                    DateTime gostExpireDateTime = DateTime.Parse(gostExpireDate);
                    DateTime currentDate = DateTime.Now;

                    // Вычисляем количество оставшихся дней
                    int rsaDaysLeft = (rsaExpireDateTime - currentDate).Days;
                    int gostDaysLeft = (gostExpireDateTime - currentDate).Days;

                    // Формируем сообщение для отображения
                    string message = $"RSA: \n" +
                                     $"Кончится: {rsaExpireDate}\n" +
                                     $"Осталось дней: {rsaDaysLeft}\n" +
                                     $"----------------------------------------------\n" +
                                     $"GOST:\n" +
                                     $"GOST Кончится: {gostExpireDate}\n" +
                                     $"Осталось дней: {gostDaysLeft}";

                    // Выводим сообщение в MessageBox
                    MessageBox.Show(message, "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExitItem_Click(object sender, EventArgs e)
        {
            notifyIcon.Visible = false; // Скрываем иконку
            Application.Exit(); // Выход из приложения
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal)
                Hide();
            else Show();

            WindowState = WindowState == FormWindowState.Normal ? FormWindowState.Minimized : FormWindowState.Normal;
        }

        private async void StartPivo()
        {
            //await GetCnForSelectedIP();
            await Task.Delay(TimeSpan.FromMinutes(1)); // ну так надо, что бы зачилился минутку

            // Проверяем, удалось ли получить currentCN
            if (string.IsNullOrEmpty(currentCN))
            {
                WriteLog("Не удалось получить CN. Ожидание 20 минут перед повторной попыткой...", LogLevel.Warn);

                // Ожидаем 20 минут
                await Task.Delay(TimeSpan.FromMinutes(20));

                // Повторная попытка получения currentCN
                await GetCnForSelectedIP();

                // Проверяем снова, удалось ли получить currentCN
                if (string.IsNullOrEmpty(currentCN))
                {
                    WriteLog("Не удалось получить CN после повторной попытки.", LogLevel.Error);
                    return; // Завершаем выполнение метода, если CN все еще не получен
                }
            }

            if (tbPriceForLiter.Text.All(char.IsDigit))
            {
                // Пытаемся преобразовать текст в целое число
                if (int.TryParse(tbPriceForLiter.Text, out _priceForLiter))
                {
                    // Проверяем, что число не меньше 100
                    if (_priceForLiter >= 100)
                    {
                        WriteLog("Успешно преобразовано в число: " + _priceForLiter, LogLevel.Info);
                    }
                    else
                    {
                        WriteLog("Ошибка: число должно быть не меньше 100.", LogLevel.Warn);
                        return;
                    }
                }
            }
            else
            {
                WriteLog("Ошибка: введенное значение содержит недопустимые символы.", LogLevel.Error);
                return;
            }

            if (!_requestMutex.Wait(0))
            {
                WriteLog("Запрос уже выполняется. Пожалуйста, подождите.", LogLevel.Warn);
                return;
            }

            foreach (var fileName in _fileNames)
            {
                string filePath = Path.Combine(Program._directoryPath, fileName);
                using (FileStream fs = File.Create(filePath)) { }
                WriteLog("Пустой файл успешно создан: " + filePath, LogLevel.Info);
            }

            try
            {
                _lastRequestTime = DateTime.Now;
                await Task.Run(() => SendAndCheckReply(_selectedIP));
                WriteLog("А на сегодня все", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка в методе списания: {ex.Message}", LogLevel.Fatal);
            }
            finally
            {
                _requestMutex.Set();
            }
        }

        private async void bStart_Click(object sender, EventArgs e)
        {
            StartPivo();
        }

        private void ShowMessage(string message)
        {
            // Используем Invoke, чтобы обновить UI из другого потока
            if (InvokeRequired)
            {
                Invoke(new Action(() => ShowMessage(message)));
            }
            else
            {
                MessageBox.Show(message, "Робот", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void WriteLog(string message, LogLevel level, bool showMessageBox = false)
        {
            Logger.Log(message, level);

            if (!showMessageBox)
                return;

            ShowMessage(message);
        }

        private async Task GetCnForSelectedIP()
        {
            currentCN = await EgaisRestWorker.GetCnValueAsync(_selectedIP);
            WriteLog($"CN: {currentCN}", LogLevel.Info);
        }

        private async Task SendAndCheckReply(string ip)
        {
            WriteLog("Начало", LogLevel.Trace);

            bool waitForTicket = false;
            string ticketUrl = await _egaisWorker.GetTicketForRests(ip); // Смотрим есть ли тикет с остатками
            if (string.IsNullOrEmpty(ticketUrl))
            {   // Если нету - запрашиваем
                await _egaisWorker.SendQueryRests(ip, currentCN); // Отправляем запрос остатков
                WriteLog("Отправлен запрос на получение остатков, ждем 5 минут", LogLevel.Info);
                waitForTicket = true;
            }
            try
            {
                if (waitForTicket == true)
                {   // Ждем 5 минут что бы тикет пришел
                    await Task.Delay(5 * 60 * 1000);
                    WriteLog("5 минут прошло, идем дальше", LogLevel.Info);
                }

                if ((DateTime.Now - _lastRequestTime) > TimeSpan.FromHours(1))
                {
                    WriteLog("Время ожидания истекло.", LogLevel.Error);
                    return; // Если прошел час, то всё
                }
                if (waitForTicket == true)
                {
                    ticketUrl = await _egaisWorker.GetTicketForRests(ip); // Получаем опять ответ остатков
                }
                WriteLog($"Получен ответ остатков: {ticketUrl}", LogLevel.Info);

                if (!string.IsNullOrEmpty(ticketUrl))
                {
                    EgaisRestWorker.ProcessReplyRests(_fileNames[0], _fileNames[2], currentCN); // Обрабатываем полученный XML
                    WriteLog("Обработали xml", LogLevel.Info);
                    EgaisRestWorker.ProcessProductsToActWriteOff( // Делаем документ для списания
                        _fileNames[2], // Документ с пивом
                        _fileNames[1], // Новый документ
                        _priceForLiter, // Цена за 1 литр
                        currentCN);

                    if (EgaisRestWorker.EmptyPosition)
                    {
                        WriteLog("EmptyPosition, закругляемся", LogLevel.Info);
                        return;
                    }

                    WriteLog("Сделали документ для списания", LogLevel.Trace);

                    _egaisWorker.SendActWriteRequest(_fileNames[1], ip); // Отправляем акт списания
                    WriteLog("Акт списания выполнен", LogLevel.Info);

                    return;
                }

                await Task.Delay(5 * 60 * 1000); // 5 минут

                WriteLog("Не получен ответ от сервера.", LogLevel.Error);
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка в SendAndCheckReply: {ex.Message}", LogLevel.Fatal);
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            this.Hide();
        }
    }
}
