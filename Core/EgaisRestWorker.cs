using System;
using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Xml;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using System.Reflection;
using ZaprosiVutm.Core;

namespace ZaprosiVutm
{
    public class EgaisRestWorker
    {
        public string HashCode;
        public static bool EmptyPosition = false;

        // Пивные коды которые будем списывать
        private static readonly HashSet<string> _validProductCodes = new HashSet<string>
        {
            "500",  // Пиво до 8.6%
            "510",  // Пиво выше 8.6%
            "520",  // Пивные напитки
            "2606", // Сидр фруктовый 
            "2607", // Сидр фруктовый ароматизированный (старый, вероятно можно убрать)
            "261",  // Сидр
            "2611", // Сидр ароматизированный
            "2613", // Сидр фруктовый ароматизированный
            "262",  // Пуаре
            "263"   // Медовуха
        }; // Актуальные коды можно взять с сайта fsrar.gov.ru  /files/25787

        static readonly XNamespace prefNs = "http://fsrar.ru/WEGAIS/ProductRef_v2";
        static readonly XNamespace rstNs = "http://fsrar.ru/WEGAIS/ReplyRests_v2";
        static readonly XNamespace awrNs = "http://fsrar.ru/WEGAIS/ActWriteOff_v3";
        static readonly XNamespace orefNs = "http://fsrar.ru/WEGAIS/ClientRef_v2";
        static readonly XNamespace nsNs = "http://fsrar.ru/WEGAIS/WB_DOC_SINGLE_01";

        public async Task SendQueryRests(string ip, string FSRARID)
        {
            string queryRestsXml = CreateQueryRestsXml(FSRARID);
            string queryRestsResponse = await SendRequest($"http://{ip}:{AppConfig.UtmPort}/opt/in/QueryRests_v2", queryRestsXml);
        }

        private string CreateQueryRestsXml(string FSRARID)
        {
            XNamespace qp = "http://fsrar.ru/WEGAIS/QueryParameters";
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

            XDocument doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(nsNs + "Documents",
                    new XAttribute("Version", "1.0"),
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(XNamespace.Xmlns + "ns", nsNs),
                    new XAttribute(XNamespace.Xmlns + "qp", qp),
                    new XElement(nsNs + "Owner",
                        new XElement(nsNs + "FSRAR_ID", FSRARID)
                    ),
                    new XElement(nsNs + "Document",
                       new XElement(nsNs + "QueryRests_v2")
                    )
                )
            );
            return doc.ToString();
        }

        private async Task<string> SendRequest(string url, string xmlData = null)
        {
            string boundary = "----------------------------" + DateTime.Now.Ticks.ToString("x");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.Method = "POST";

            string header = "Content-Disposition: form-data; name=\"xml_file\"; filename=hophey.xml\r\nContent-Type: text/xml; charset=UTF-8\r\n\r\n";

            using (MemoryStream stream = new MemoryStream())
            {
                byte[] boundaryBytes = Encoding.UTF8.GetBytes("\r\n--" + boundary + "\r\n");
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                byte[] xmlDataBytes = Encoding.UTF8.GetBytes(xmlData ?? "");
                byte[] endBoundaryBytes = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--");

                await stream.WriteAsync(boundaryBytes, 0, boundaryBytes.Length);
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                await stream.WriteAsync(xmlDataBytes, 0, xmlDataBytes.Length);
                await stream.WriteAsync(endBoundaryBytes, 0, endBoundaryBytes.Length);

                request.ContentLength = stream.Length;

                try
                {
                    using (Stream requestStream = await request.GetRequestStreamAsync())
                    {
                        stream.Position = 0;
                        await stream.CopyToAsync(requestStream);
                    }

                    using (WebResponse response = await request.GetResponseAsync())
                    {
                        using (Stream responseStream = response.GetResponseStream())
                        {
                            using (StreamReader reader = new StreamReader(responseStream))
                            {
                                return await reader.ReadToEndAsync();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при отправке запроса: {ex.Message}");
                    Logger.Log($"Ошибка при отправке запроса: {ex.Message}", LogLevel.Fatal);
                    return null;
                }
            }
        }

        public async Task<string> GetTicket(string url)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";

                using (HttpWebResponse response = (HttpWebResponse)await Task.Run(() => request.GetResponse()))
                {
                    using (Stream responseStream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                        {
                            string responseBody = await reader.ReadToEndAsync();
                            return responseBody;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine($"Ошибка при получении тикета: {ex.Message}");
                Logger.Log($"Ошибка при получении тикета: {ex.Message}", LogLevel.Fatal);
                return null;
            }
        }

        public async Task<string> GetTicketForRests(string baseUrl)
        {
            baseUrl = $"http://{baseUrl}:{AppConfig.UtmPort}/opt/out/";
            string responseBody = await GetTicket(baseUrl);
            if (responseBody == null) return null;

            try
            {
                string[] lines = responseBody.Split('\n');

                string latestReplyUrl = null;
                int highestId = int.MinValue;

                foreach (string line in lines)
                {
                    if (line.Contains("ReplyRests_v2/"))
                    {
                        string url;

                        //  Обработка возможного некорректного формата строки
                        if (line.Contains("<url replyId") && line.Contains(">"))
                        {
                            int start = line.IndexOf(">") + 1;
                            int end = line.IndexOf("</url");
                            if (start > 0 && end > start)
                            {
                                url = line.Substring(start, end - start).Trim();
                            }
                            else
                            {
                                Console.WriteLine($"Не удалось извлечь URL из строки: {line}");
                                Logger.Log($"Не удалось извлечь URL из строки: {line}", LogLevel.Error);
                                continue; // Пропускаем эту строку
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Неверный формат строки для URL: {line}");
                            Logger.Log($"Неверный формат строки для URL: {line}", LogLevel.Error);
                            continue; // Пропускаем строку
                        }

                        int currentId;
                        if (int.TryParse(url.Split('/').Last(), out currentId))
                        {
                            Console.WriteLine($"Проверяем тикет: {url}");
                            Logger.Log($"Проверяем тикет: {url}", LogLevel.Info);

                            if (currentId > highestId)
                            {
                                highestId = currentId;
                                latestReplyUrl = url;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Не удалось преобразовать ID из URL: {url}");
                            Logger.Log($"Не удалось преобразовать ID из URL: {url}", LogLevel.Error);
                        }
                    }
                }

                Console.WriteLine($"Наш тикет: {latestReplyUrl}");
                Logger.Log($"Наш тикет: {latestReplyUrl}", LogLevel.Info);

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(60); // Тайм-аут на 60 минут для скачивания файла ( ну уж если интернет совсем плохой )
                    try
                    {
                        using (var response = await client.GetAsync(latestReplyUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();

                            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                            //var filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ReplyRests_v2.xml");
                            var path = Path.Combine(Program._directoryPath, "ReplyRests_v2.xml");
                            Logger.Log($"[GetTicketForRests] ReplyRests_v2: {path}");
                            var filePath = path;

                            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                var buffer = new byte[8192]; // Размер буфера
                                long totalReadBytes = 0;
                                int readBytes;

                                using (var contentStream = await response.Content.ReadAsStreamAsync())
                                {
                                    while ((readBytes = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                    {
                                        await fs.WriteAsync(buffer, 0, readBytes);
                                        totalReadBytes += readBytes;

                                        if (totalBytes != -1)
                                        {
                                            var progress = (double)totalReadBytes / totalBytes * 100;
                                            Console.WriteLine($"Прогресс: {progress:0}%");  //F2  Console.WriteLine($"Прогресс: {progress:0:#.##}%");
                                            //Logger.Log($"Прогресс: {progress:0}%", LogLevel.Info);
                                            // Немного засирает лог, но хотя бы можно будет увидеть на какой стадии если что прервалось, а так можно спокойно убрать
                                        }
                                    }
                                }
                            }

                            Console.WriteLine("Файл успешно скачан!");
                            Logger.Log("Файл успешно скачан!", LogLevel.Info);
                        }
                    }
                    catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested == false)
                    {
                        Console.WriteLine("Запрос был отменен из-за тайм-аута.");
                        Logger.Log("Запрос был отменен из-за тайм-аута.", LogLevel.Fatal);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Ошибка при скачивании файла: " + ex.Message);
                        Logger.Log("Ошибка при скачивании файла: " + ex.Message, LogLevel.Fatal);
                    }
                }

                return latestReplyUrl;
            }
            catch (XmlException ex)
            {
                Console.WriteLine($"Ошибка парсинга XML: {ex.Message}");
                Logger.Log($"Ошибка парсинга XML: {ex.Message}", LogLevel.Error);
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке тикета: {ex.Message}");
                Logger.Log($"Ошибка при обработке тикета: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        public static void ProcessReplyRests(string inputXmlPath, string outputXmlPath, string FSRARID)
        {
            List<XElement> stockPositions = new List<XElement>();

            try
            {
                string path1 = Path.Combine(Program._directoryPath, inputXmlPath);
                Logger.Log($"[ProcessReplyRests] inputXmlPath: {path1}", LogLevel.Trace);
                XDocument xDoc = XDocument.Load(path1);

                // Извлекаем StockPosition
                var stockPositionsElements = xDoc.Descendants(rstNs + "StockPosition");

                foreach (var stockPosition in stockPositionsElements)
                {
                    // Находим элемент Product внутри StockPosition
                    var productElement = stockPosition.Element(rstNs + "Product");
                    if (productElement != null)
                    {
                        var productVCodeElement = productElement.Element(prefNs + "ProductVCode");
                        if (productVCodeElement != null && !string.IsNullOrWhiteSpace(productVCodeElement.Value))
                        {
                            string productCode = productVCodeElement.Value;
                            if (_validProductCodes.Contains(productCode))
                            {
                                // Клонируем элемент StockPosition и добавляем его в список
                                stockPositions.Add(new XElement(rstNs + "StockPosition",
                                    new XAttribute(XNamespace.Xmlns + "pref", prefNs),
                                    new XAttribute(XNamespace.Xmlns + "oref", orefNs),
                                    new XAttribute(XNamespace.Xmlns + "rst", rstNs),
                                    new XAttribute(XNamespace.Xmlns + "ns", nsNs),
                                    new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                                    new XElement(rstNs + "Quantity", stockPosition.Element(rstNs + "Quantity")?.Value),
                                    new XElement(rstNs + "InformF1RegId", stockPosition.Element(rstNs + "InformF1RegId")?.Value),
                                    new XElement(rstNs + "InformF2RegId", stockPosition.Element(rstNs + "InformF2RegId")?.Value),
                                    new XElement(rstNs + "Product",
                                        new XElement(prefNs + "FullName", productElement.Element(prefNs + "FullName")?.Value),
                                        new XElement(prefNs + "AlcCode", productElement.Element(prefNs + "AlcCode")?.Value),
                                        new XElement(prefNs + "Capacity", productElement.Element(prefNs + "Capacity")?.Value),
                                        new XElement(prefNs + "UnitType", productElement.Element(prefNs + "UnitType")?.Value),
                                        new XElement(prefNs + "AlcVolume", productElement.Element(prefNs + "AlcVolume")?.Value),
                                        new XElement(prefNs + "ProductVCode", productCode),
                                        new XElement(prefNs + "Producer", productElement.Element(prefNs + "Producer")?.Elements())
                                    )
                                ));
                            }
                        }
                    }
                }

                // Создаем новый XML-документ с нужной структурой
                XDocument newXmlDoc = new XDocument(
                    new XDeclaration("1.0", "UTF-8", "no"),
                    new XElement(nsNs + "Documents",
                        new XAttribute(XNamespace.Xmlns + "rst", rstNs),
                        new XAttribute(XNamespace.Xmlns + "ns", nsNs),
                        new XElement(nsNs + "Owner",
                            new XElement(nsNs + "FSRAR_ID", FSRARID)
                        ),
                        new XElement(nsNs + "Document",
                            new XElement(nsNs + "ReplyRests_v2",
                                new XElement(rstNs + "RestsDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff")),
                                new XElement(rstNs + "Products", stockPositions)
                            )
                        )
                    )
                );

                // Сохраняем новый XML-документ
                string path2 = Path.Combine(Program._directoryPath, outputXmlPath);
                Logger.Log($"[ProcessReplyRests] outputXmlPath: {path2}", LogLevel.Trace);

                newXmlDoc.Save(path2);
                Console.WriteLine($"Новый XML-файл сохранен: {outputXmlPath}");
                Logger.Log($"Новый XML-файл сохранен: {outputXmlPath}", LogLevel.Info);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"Ошибка: Файл {inputXmlPath} не найден.");
                Logger.Log($"Ошибка: Файл {inputXmlPath} не найден.", LogLevel.Error);
            }
            catch (XmlException ex)
            {
                Console.WriteLine($"Ошибка парсинга XML: {ex.Message}");
                Logger.Log($"Ошибка парсинга XML: {ex.Message}", LogLevel.Error);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Непредвиденная ошибка: {ex.Message}");
                Logger.Log($"Непредвиденная ошибка: {ex.Message}", LogLevel.Error);
            }
        }

        public static void ProcessProductsToActWriteOff(string inputXmlPath, string outputXmlPath, double pricePerLiter, string FSRARID)
        {
            EmptyPosition = false;
            List<XElement> positions = new List<XElement>();

            try
            {
                string path1 = Path.Combine(Program._directoryPath, inputXmlPath);
                Logger.Log($"[ProcessProductsToActWriteOff] inputXmlPath: {path1}", LogLevel.Trace);
                XDocument xDoc = XDocument.Load(path1);

                // Извлекаем StockPosition
                var stockPositions = xDoc.Descendants(rstNs + "StockPosition");

                int identityCounter = 1; // Счетчик для Identity

                foreach (var stockPosition in stockPositions)
                {
                    var productElement = stockPosition.Element(rstNs + "Product");
                    if (productElement != null)
                    {
                        var productVCodeElement = productElement.Element(prefNs + "ProductVCode");
                        var productCapacityElement = productElement.Element(prefNs + "Capacity");

                        double productCapacity = double.Parse(productCapacityElement.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                        var quantityElement = stockPosition.Element(rstNs + "Quantity");
                        var informF2RegIdElement = stockPosition.Element(rstNs + "InformF2RegId");

                        if (productVCodeElement != null && _validProductCodes.Contains(productVCodeElement.Value) &&
                            quantityElement != null && informF2RegIdElement != null)
                        {
                            // Проверяем, что количество можно преобразовать в double
                            if (double.TryParse(quantityElement.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double quantity))
                            {
                                double sumSale = (pricePerLiter * productCapacity) * quantity; // Рассчитываем сумму как цена за литр * объем

                                // Создаем элемент Position
                                positions.Add(new XElement(awrNs + "Position",
                                    new XElement(awrNs + "Identity", identityCounter++),
                                    new XElement(awrNs + "Quantity", quantity),
                                    new XElement(awrNs + "SumSale", Math.Round(sumSale, 2)),
                                    new XElement(awrNs + "InformF1F2",
                                        new XElement(awrNs + "InformF2",
                                            new XElement(prefNs + "F2RegId", informF2RegIdElement.Value)
                                        )
                                    )
                                ));
                            }
                            else
                            {
                                Console.WriteLine($"Ошибка: Неверный формат количества '{quantityElement.Value}' в StockPosition.");
                                Logger.Log($"Ошибка: Неверный формат количества '{quantityElement.Value}' в StockPosition.", LogLevel.Fatal);
                            }
                        }
                    }
                }

                // Маленький костыль
                if (positions.Count.Equals(0))
                {
                    EmptyPosition = true;
                    Logger.Log("Ничего не нашлось для списания", LogLevel.Info);
                    return;
                }

                // Генерируем случайный номер акта
                Random random = new Random();
                string actNumber = random.Next(100000000, 999999999).ToString("D9");

                // Получаем сегодняшнюю дату
                string actDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

                // Создаем новый XML-документ с нужной структурой
                XDocument newXmlDoc = new XDocument(
                    // new XDeclaration("1.0", "UTF-8", null),
                    new XElement(nsNs + "Documents",
                        new XAttribute("Version", "1.0"),
                        new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                        new XAttribute(XNamespace.Xmlns + "ns", nsNs),
                        new XAttribute(XNamespace.Xmlns + "pref", prefNs),
                        new XAttribute(XNamespace.Xmlns + "awr", awrNs),
                        new XAttribute(XNamespace.Xmlns + "ce", "http://fsrar.ru/WEGAIS/CommonV3"),
                        new XElement(nsNs + "Owner",
                            new XElement(nsNs + "FSRAR_ID", FSRARID)
                                ),
                        new XElement(nsNs + "Document",
                            new XElement(nsNs + "ActWriteOff_v3",
                                new XElement(awrNs + "Identity", Guid.NewGuid().ToString()), // Генерируем уникальный идентификатор
                        new XElement(awrNs + "Header",
                            new XElement(awrNs + "ActNumber", actNumber), // Случайный номер акта
                            new XElement(awrNs + "ActDate", actDate), // Сегодняшняя дата
                            new XElement(awrNs + "TypeWriteOff", "Реализация"), // Причина списывания
                            new XElement(awrNs + "Note")
                        ),
                        new XElement(awrNs + "Content", positions))))
                );

                // Сохраняем новый XML-документ

                string path2 = Path.Combine(Program._directoryPath, outputXmlPath);
                Logger.Log($"[ProcessProductsToActWriteOff] outputXmlPath: {path2}", LogLevel.Trace);

                newXmlDoc.Save(path2);
                Console.WriteLine($"Новый XML-файл сохранен: {outputXmlPath}");
                Logger.Log($"Новый XML-файл сохранен: {outputXmlPath}", LogLevel.Info);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"Ошибка: Файл {inputXmlPath} не найден.");
                Logger.Log($"Ошибка: Файл {inputXmlPath} не найден.", LogLevel.Error);
            }
            catch (XmlException ex)
            {
                Console.WriteLine($"Ошибка парсинга XML: {ex.Message}");
                Logger.Log($"Ошибка парсинга XML: {ex.Message}", LogLevel.Error);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Непредвиденная ошибка: {ex.Message}");
                Logger.Log($"Непредвиденная ошибка: {ex.Message}", LogLevel.Error);
            }
        }

        public async void SendActWriteRequest(string xmlFilePath, string ip)
        {
            string path3 = Path.Combine(Program._directoryPath, xmlFilePath);
            Logger.Log($"[SendActWriteRequest] xmlFilePath : {path3}", LogLevel.Trace);

            // Убедитесь, что файл существует
            if (!File.Exists(path3))
            {
                Console.WriteLine($"Файл не найден: {xmlFilePath}");
                Logger.Log($"Файл не найден: {xmlFilePath}", LogLevel.Error);
                return;
            }

            // Создаем HttpClient
            var client = new HttpClient();

            // Создаем запрос
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://{ip}:{AppConfig.UtmPort}/opt/in/ActWriteOff_v3");

            // Создаем содержимое запроса
            var content = new MultipartFormDataContent();
            //content.Add(new StreamContent(File.OpenRead(xmlFilePath)), "xml_file", Path.GetFileName(xmlFilePath));
            content.Add(new StreamContent(File.OpenRead(path3)), "xml_file", Path.GetFileName(path3));

            // Устанавливаем содержимое запроса
            request.Content = content;

            try
            {
                // Отправляем запрос
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode(); // Проверяем успешный статус ответа

                // Читаем и выводим ответ
                string responseBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Ответ от сервера:");
                Console.WriteLine(responseBody);
                Logger.Log("Ответ от сервера:", LogLevel.Trace);
                Logger.Log(responseBody, LogLevel.Trace);
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Ошибка при отправке запроса: {e.Message}");
                Logger.Log($"Ошибка при отправке запроса: {e.Message}", LogLevel.Error);
            }
        }

        public static async Task<string> GetCnValueAsync(string url)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    // Загрузка XML-данных с сайта
                    var xml = await httpClient.GetStringAsync($"http://{url}:{AppConfig.UtmPort}/diagnosis");

                    // Создание XmlDocument
                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xml);

                    // Поиск элемента CN
                    var cnNode = xmlDoc.SelectSingleNode("//CN");

                    // Возврат значения элемента CN
                    return cnNode?.InnerText.Trim(); // Возвращаем текстовое содержимое элемента
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                    Logger.Log($"Ошибка: {ex.Message}", LogLevel.Fatal);
                    return null;
                }
            }
        }
    }
}
