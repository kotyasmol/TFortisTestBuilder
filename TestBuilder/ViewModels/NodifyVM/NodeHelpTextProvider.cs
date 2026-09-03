using System;
using System.Collections.Generic;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels.NodifyVM
{
    internal static class NodeHelpTextProvider
    {
        private static readonly IReadOnlyDictionary<Type, string> HelpByType = new Dictionary<Type, string>
        {
            [typeof(StartNodeViewModel)] = """
                Стартовая точка графа.
                Параметры: нет.
                Выход "Выход" запускает следующий шаг. В корневом графе обычно должна быть одна такая нода.
                """,

            [typeof(EndNodeViewModel)] = """
                Завершает текущий граф успешным окончанием.
                Параметры: нет.
                Нужен как понятная финальная точка сценария.
                """,

            [typeof(BodyStartNodeViewModel)] = """
                Технический старт тела цикла For Slaves.
                Параметры: нет.
                Создается автоматически внутри тела цикла и запускает первый шаг одной итерации.
                """,

            [typeof(BodyEndNodeViewModel)] = """
                Технический конец тела цикла For Slaves.
                Параметры: нет.
                Завершает текущую итерацию, после чего цикл переходит к следующему Slave ID.
                """,

            [typeof(LabelNodeViewModel)] = """
                Текстовая метка на графе.
                Text - заметка, которую видно прямо в ноде.
                Размер метки меняется перетаскиванием правого нижнего угла.
                На выполнение теста почти не влияет, нужна для пояснений внутри сценария.
                """,

            [typeof(DelayNodeViewModel)] = """
                Делает паузу между шагами.
                Мс - длительность ожидания в миллисекундах.
                Используй после подачи питания, перезагрузки, сетевых команд и других действий, которым нужно время.
                """,

            [typeof(SubtestNodeViewModel)] = """
                Группирует часть проверки в отдельный вложенный граф.
                Название - имя подтеста и заголовок ноды.
                Описание - текстовое пояснение для сценария.
                Включен - если выключить, тело подтеста пропускается как успешное.
                Стоп - при ошибке внутри подтеста останавливать общий тест.
                Cleanup on failure/stop - запускать подтест как аварийную очистку после провала, остановки или исключения.
                Кнопка "Открыть" открывает вложенный граф подтеста.
                """,

            [typeof(ForEachSlaveNodeViewModel)] = """
                Выполняет вложенный граф для диапазона Modbus Slave ID.
                С - первый Slave ID.
                По - последний Slave ID.
                Шаг - приращение между ID.
                Стоп при ошибке - остановить цикл и тест при провале итерации.
                Внутри тела ноды Modbus можно включать "Slave из цикла", чтобы брать текущий ID автоматически.
                """,

            [typeof(ModbusWriteNodeViewModel)] = """
                Записывает значение в Modbus-регистр.
                Slave ID - устройство, в которое пишем; ID рядом показывает фактическое число.
                Slave из цикла - брать текущий Slave ID из For Slaves.
                Адрес - номер регистра или выбранный регистр из карты устройства.
                Значение - число, которое будет записано.
                Проверка - после записи прочитать регистр и убедиться, что значение применилось.
                """,

            [typeof(CheckRegisterRangeNodeViewModel)] = """
                Проверяет, что значение регистра попадает в диапазон.
                Slave ID - проверяемое устройство; ID рядом показывает фактическое число.
                Slave из цикла - брать текущий Slave ID из For Slaves.
                Live Modbus read - читать регистр прямо при выполнении, а не брать последнее значение мониторинга.
                Адрес - номер регистра или выбранный регистр из карты устройства.
                Минимум и Максимум - допустимые границы значения.
                True идет при успешной проверке, False - при выходе из диапазона.
                """,

            [typeof(CheckRegisterEqualityNodeViewModel)] = """
                Проверяет, что значение регистра равно ожидаемому.
                Slave ID - проверяемое устройство; ID рядом показывает фактическое число.
                Slave из цикла - брать текущий Slave ID из For Slaves.
                Live Modbus read - читать регистр прямо при выполнении, а не брать последнее значение мониторинга.
                Адрес - номер регистра или выбранный регистр из карты устройства.
                Значение - ожидаемое число.
                True идет при совпадении, False - если значение другое.
                """,

            [typeof(WaitUntilNodeViewModel)] = """
                Ждет, пока регистр станет равен ожидаемому значению.
                Slave ID - проверяемое устройство; ID рядом показывает фактическое число.
                Slave из цикла - брать текущий Slave ID из For Slaves.
                Live Modbus read - читать регистр прямо при ожидании.
                Адрес - номер регистра или выбранный регистр из карты устройства.
                Значение - ожидаемое число.
                Таймаут мс - сколько максимум ждать.
                """,

            [typeof(PollRegisterNodeViewModel)] = """
                Несколько раз опрашивает регистр и проверяет стабильный диапазон.
                Slave ID - проверяемое устройство; ID рядом показывает фактическое число.
                Slave из цикла - брать текущий Slave ID из For Slaves.
                Live Modbus read - читать регистр прямо при выполнении.
                Адрес - номер регистра или выбранный регистр из карты устройства.
                Минимум и Максимум - допустимые границы каждого замера.
                Кол-во замеров - сколько чтений сделать.
                """,

            [typeof(SelfTestCheckNodeViewModel)] = """
                Получает selftest-страницу устройства и проверяет найденные параметры.
                URL - адрес страницы selftest/deviceinfo.
                Timeout - общий лимит ожидания ответа и появления данных.
                Prefix - префикс имен переменных, куда сохранить найденные значения.
                Min/Max - правила валидации по строкам: имя параметра и допустимый диапазон.
                Критическая ошибка при провале - останавливать тест исключением, если проверка не прошла.
                """,

            [typeof(CheckVariableEqualityNodeViewModel)] = """
                Проверяет переменную из контекста теста на равенство.
                Variable - имя переменной.
                Expected - ожидаемое значение.
                Type - тип сравнения, например Number или String.
                Fail - сообщение об ошибке, которое попадет в лог при провале.
                True идет при совпадении, False - при отличии.
                """,

            [typeof(CheckVariableRangeNodeViewModel)] = """
                Проверяет, что числовая переменная попадает в диапазон.
                Variable - имя переменной.
                Min и Max - допустимые границы.
                Включая границы - считать значения ровно Min/Max успешными.
                Fail - сообщение об ошибке, которое попадет в лог при провале.
                """,

            [typeof(ClearArpCacheNodeViewModel)] = """
                Очищает ARP-кэш Windows перед сетевыми проверками.
                Run arpd.bat - запускать поставляемый bat-файл очистки.
                Bat path - путь к bat, если нужен свой файл.
                Command и Arguments - команда очистки, если bat выключен.
                Timeout ms - сколько ждать завершения команды.
                Fail on error - считать ошибку очистки провалом теста.
                """,

            [typeof(GetSerialNumberFromServerNodeViewModel)] = """
                Запрашивает серийный номер устройства с сервера.
                Server - хост, /api, /api/Api.svc или полный endpoint; пустое значение берется из Настроек.
                Device - тип устройства для запроса.
                CPU var - имя переменной с CPU ID после Selftest Check; если имя задано, пустой CPU ID блокирует запрос.
                Очисти CPU var только если сервер действительно допускает выдачу номера без CPU ID.
                Output - имя переменной, куда сохранить серийный номер.
                Timeout - лимит одного запроса.
                Retry - количество повторов и задержка между ними.
                Критическая ошибка при провале - останавливать тест, если номер получить не удалось.
                """,

            [typeof(SendUdpSetMacPacketNodeViewModel)] = """
                Отправляет UDP-команду установки MAC на устройство.
                Target IP и Target port - куда отправлять пакет.
                MAC var - переменная, из которой берется MAC-адрес.
                Timeout ms - лимит отправки.
                Repeats - сколько раз повторить отправку.
                Repeat delay - пауза между повторами.
                Fail on send error - считать ошибку отправки провалом теста.
                """,

            [typeof(RunDataTestNodeViewModel)] = """
                Запускает сетевой DataTest и сохраняет результат.
                Mode - режим теста.
                Packet size - размер UDP-пакета.
                UDP port - порт тестового обмена.
                Target wire Mbps - целевая скорость на линии; для этой ноды максимум 100 Mbps.
                Duration ms - длительность замера.
                Warmup ms - прогрев перед учетом результата.
                Pair pause ms - пауза между последовательными парами портов.
                Max loss % - допустимая потеря между подтвержденными TX и RX.
                Max TX deficit % - допустимое отставание генератора от target.
                Output var - переменная для результата.
                Ports - список пар Name,InIp,OutIp[,Mbps]; Mbps ограничивается значением 100.
                Test both directions - проверить оба направления каждой пары.
                Fail on error - считать ошибку DataTest провалом теста.
                """,

            [typeof(GetUpsStatusNodeViewModel)] = """
                Читает статус UPS через HTTP API устройства.
                Base URL - базовый адрес устройства.
                Timeout ms - лимит запроса.
                Output var - переменная, куда сохранить статус.
                Fail on error - считать ошибку запроса провалом теста.
                """,

            [typeof(ReadHttpVariableNodeViewModel)] = """
                Универсально читает одно значение через HTTP GET и сохраняет его в TestContext.
                Base URL - базовый адрес устройства.
                Endpoint - путь API или полный HTTP/HTTPS URL.
                Response type - Integer, Number, Boolean или String.
                Timeout ms - лимит одного запроса.
                Output var - переменная, куда сохранить свежее значение.
                Перед запросом старое значение удаляется; HTTP/parse ошибка идет в False при включенном Fail on error.
                """,

            [typeof(GetUpsVoltageNodeViewModel)] = """
                Читает напряжение UPS через HTTP API устройства.
                Base URL - базовый адрес устройства.
                Timeout ms - лимит запроса.
                Output var - переменная, куда сохранить напряжение.
                Fail on error - считать ошибку запроса провалом теста.
                """,

            [typeof(GetIrpStatusNodeViewModel)] = """
                Читает статус IRP через HTTP API устройства.
                Base URL - базовый адрес устройства.
                Timeout ms - лимит запроса.
                Output var - переменная, куда сохранить статус.
                Fail on error - считать ошибку запроса провалом теста.
                """,

            [typeof(BuildMacFromSerialNodeViewModel)] = """
                Строит MAC-адрес из серийного номера.
                Serial var - переменная с полным серийным номером.
                Serial offset - сдвиг/часть серийника, используемая для расчета.
                MAC prefix - первые байты MAC.
                Short serial var - переменная для укороченного серийника.
                MAC var - переменная, куда сохранить рассчитанный MAC.
                Fail on error - считать ошибку расчета провалом теста.
                """,

            [typeof(CompareVariablesNodeViewModel)] = """
                Сравнивает две переменные из контекста теста.
                Left var и Right var - имена сравниваемых переменных.
                Type - тип сравнения, например Number или String.
                Fail - сообщение об ошибке, которое попадет в лог при провале.
                True идет при совпадении, False - при отличии.
                """,

            [typeof(WaitVariableUntilNodeViewModel)] = """
                Ждет, пока переменная примет ожидаемое значение, при необходимости обновляя ее через HTTP.
                Variable - имя переменной.
                Expected - ожидаемое значение.
                Type - тип сравнения.
                Poll action SelftestSnapshot заново снимает тестовую страницу; HttpGet делает GET одного значения; None не обновляет переменную.
                Base URL - адрес устройства для HTTP-опроса.
                Endpoint - путь API/тестовой страницы или полный URL.
                Response type - как разобрать ответ перед сравнением.
                Request ms - лимит одного запроса.
                Timeout ms - общий лимит ожидания.
                Interval ms - пауза между попытками.
                Fail on timeout - считать таймаут провалом теста.
                """,

            [typeof(BuildTestReportNodeViewModel)] = """
                Собирает отчет по результатам теста в переменную.
                Report var - имя переменной отчета.
                Device name и Device type - данные устройства для отчета.
                Serial var - переменная с серийным номером.
                MAC var - переменная с MAC-адресом.
                Include all variables - добавить в отчет все переменные контекста.
                """,

            [typeof(PrintLabelNodeViewModel)] = """
                Печатает этикетку на принтере.
                Printer - имя принтера Windows.
                Device name и Device type - текст для этикетки.
                Serial var - переменная с серийным номером.
                MAC var - переменная с MAC-адресом.
                Copies - количество копий.
                Include MAC - печатать MAC на этикетке.
                Use equipment field - использовать дополнительное поле оборудования.
                Equipment type и Equipment text - содержимое дополнительного поля.
                Fail on printer error - считать ошибку печати провалом теста.
                """,

            [typeof(SendTestReportNodeViewModel)] = """
                Отправляет отчет на сервер и при необходимости сохраняет копию.
                Server URL - базовый адрес сервера.
                Report var - переменная с готовым отчетом.
                Endpoint - путь API для отправки.
                Timeout ms - лимит одного запроса.
                Retry count и Retry delay ms - повторы отправки.
                Save local copy - сохранять отчет локально.
                Local dir - папка для локальных копий.
                Fail on error - считать ошибку отправки провалом теста.
                """,

            [typeof(OperatorActionNodeViewModel)] = """
                Останавливает выполнение и просит оператора выполнить действие.
                Сообщение - текст, который увидит оператор.
                После подтверждения выполнение идет дальше по выходу "Выход".
                """,
        };

        public static string GetHelp(Type nodeType) =>
            HelpByType.TryGetValue(nodeType, out var help) ? help.Trim() : string.Empty;

        public static string GetSummary(Type nodeType)
        {
            var lines = GetHelpLines(nodeType);
            return lines.Count > 0 ? lines[0] : string.Empty;
        }

        public static IReadOnlyList<string> GetDetails(Type nodeType)
        {
            var lines = GetHelpLines(nodeType);
            if (lines.Count <= 1)
                return Array.Empty<string>();

            var details = new string[lines.Count - 1];
            for (var i = 1; i < lines.Count; i++)
                details[i - 1] = lines[i];

            return details;
        }

        private static IReadOnlyList<string> GetHelpLines(Type nodeType) =>
            GetHelp(nodeType).Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
