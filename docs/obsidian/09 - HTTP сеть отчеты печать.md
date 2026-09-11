---
tags:
  - testbuilder
  - http
  - network
  - reports
  - printing
updated: 2026-09-11
---

# HTTP, сеть, отчеты и печать

Эта страница описывает инфраструктуру, которой пользуются HTTP-, сетевые, отчетные и печатные ноды.

## HttpRequestService

`HttpRequestService` реализует `IHttpRequestService`:

```csharp
Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
```

Что делает:

1. Проверяет, что URL не пустой.
2. Проверяет, что URL абсолютный и схема `http` или `https`.
3. Создает linked timeout token.
4. Делает GET через `HttpClient`.
5. Возвращает `HttpRequestResult.Success` или `Failure`.

`HttpRequestResult` содержит:

| Поле | Назначение |
|---|---|
| `IsSuccessStatusCode` | HTTP 2xx. |
| `StatusCode` | Код ответа или `null`. |
| `Body` | Тело ответа. |
| `ErrorMessage` | Нормализованная ошибка. |
| `Elapsed` | Время выполнения. |

## Selftest HTTP

`SelfTestCheckStep` имеет особую логику получения страницы:

- до общего timeout раз в `PollIntervalMs` параллельно проверяет ICMP и TCP-порт URL;
- пока DUT не отвечает, Chrome/Edge не запускается;
- после появления DUT запускает одну фоновую browser-попытку с чистым профилем
  и передаёт ей исходный URL прямо в строке запуска;
- ждет завершения загрузки по `document.readyState == complete`, затем полные 10 секунд;
- один раз читает DOM через DevTools Protocol как аналог Selenium `driver.PageSource`
  и закрывает браузер этой попытки;
- если снимок не содержит `<selftest>...</selftest>` или совместимый
  `<settings>...</settings>` с `default_mac`, ждет `PollIntervalMs` и пробует снова.

Каждая browser-попытка намеренно повторяет старую отдельную Selenium-утилиту: один `GoToUrl`,
`Thread.Sleep(10000)`, один `PageSource`. В рабочем браузерном режиме нет POST-логина,
обычного HTTP fallback и запроса `/test.shtml`; повторяется только эта целая попытка.
После попытки Chrome получает `Browser.close`; выход процесса ожидается до 2 секунд.
Для зависшего Windows Chrome принудительное завершение вынесено в отдельный
`taskkill /PID <PID своего Chrome> /T /F` с ожиданием до 3 секунд. Ошибка завершения
останавливает выполнение, чтобы не накапливать процессы. Следующая попытка начинается
после выхода предыдущего Chrome. Удаление его уникального временного профиля выполняется
отдельно в фоне и не задерживает возврат уже прочитанного DOM. Все эти операции идут вне UI.
В логах отдельно отмечаются запуск, DevTools, готовность документа, чтение DOM, выход Chrome
и обнаружение XML; таймаут включает название этапа. Это диагностика Windows-стенда,
а не подтверждение устранения зависания без повторного аппаратного прогона.
Старые сохраненные профили с `TimeoutMs` до `160000` для DUT selftest автоматически получают
эффективный таймаут `180000`. Рабочий профиль ждёт DUT и страницу до трёх минут,
проверяя готовность каждые пять секунд.
Для headless Chrome/Edge используется внутреннее чтение DOM через DevTools Protocol после ожидания
загрузки страницы, по смыслу аналогично старому Selenium `driver.PageSource`, но без внешней утилиты.
`PollIntervalMs` задает паузу между полными браузерными попытками.
Извлечение декодирует HTML-, URL- и JS-экранирования, потому что нужный XML может лежать в скрытом DOM/скриптах LuCI.
Проверка `ValidationRules` пишет в лог отдельные строки по каждому параметру и сохраняет
`SelfTest.CheckedRuleCount`, `SelfTest.FailedRuleCount`, `SelfTest.ValidationSummary`.

Операционная причина трёхминутного ожидания, нерабочие альтернативы и порядок диагностики
зафиксированы отдельно: [[18 - Selftest и загрузка DUT]].

Raw XML сохраняется только в `TestContext` текущего запуска как `SelfTestRaw`.
Файл `selftest.txt` больше не создается: он был legacy-артефактом старой консольной утилиты.
Полный перечень исторических XML-полей, формула фактического количества,
legacy-алиасы и служебные ключи описаны в [[19 - Поля тестовой страницы DUT]].

`FailOnError` в selftest-нode не переключает результат на `True`; при ошибке step возвращает `False` всегда. Разница только в том, выставляется ли `context.HasCriticalError`, который потом влияет на поле `test_result` в отчете.

## API устройства

Для новых графов DUT API использует две универсальные ноды:

| Нода | Назначение |
|---|---|
| `Read HTTP Variable` | Один GET, строгий разбор ответа и запись свежего значения. |
| `Wait Variable Until` + `HttpGet` | Повторный GET до совпадения значения или обязательного таймаута. |
| `Wait Variable Until` + `SelftestSnapshot` | Повторный снимок всей тестовой страницы и сравнение целевого поля. |

Обе ноды принимают произвольные `BaseUrl`, `Endpoint`, output variable и
`ResponseType`: `Integer`, `Number`, `Boolean`, `String`. `Endpoint` может быть
относительным путем или полным HTTP/HTTPS URL. Перед запросом старое output
value удаляется, поэтому оставшееся от selftest значение не может пройти как
результат свежего чтения.

Рабочий UPS-алгоритм:

1. На SIMBAT выполняется `slave 17 / 1706 ← 1` с read-back-проверкой.
2. `SelftestSnapshot` ждёт `Dut.akb_voltage` в диапазоне `20..27` В при
   включённом AC1.
3. На PS-3 выполняется `slave 23 / 1200 ← 0` с проверкой: AC1 выключен.
4. Новый снимок снова должен дать `akb_voltage = 20..27` В. Доступная страница
   и нормальное напряжение при выключенном AC1 подтверждают работу от АКБ.
5. AC1 возвращается записью `slave 23 / 1200 ← 1` с проверкой.
6. Третий свежий снимок должен дать `akb_voltage = 20..27` В при работе от сети.
7. Имитатор отключается записью `slave 17 / 1706 ← 0` с read-back-проверкой.

Все три ожидания используют `SelftestSnapshot`, `ComparisonType = Number` и
диапазон `ExpectedValue = 20..27`; request timeout `30000` мс, общий timeout
`160000` мс, интервал `5000` мс и `FailOnTimeout = true`. Фиксированных
задержек нет: старое `Dut.akb_voltage` удаляется, снимается новая страница, а
значение вне диапазона приводит к следующей попытке. После отключения SIMBAT
диапазон не проверяется, потому что требования к напряжению в этом состоянии
не определены.

Предыдущие варианты UPS-графа опирались на артефакты старого оборудования:
тракт PS-3 `1210`, `ups_rez`, `/api/getUpsStatus` и косвенные проверки
`1707/1708`. В актуальном стенде подключением имитатора управляет записываемый
регистр SIMBAT `1706`. Поле `akb_det` на фактической прошивке не меняется при
`1706 = 1`, поэтому признаком исправной батарейной цепи служит
`akb_voltage = 20..27` В. Исторические API-ноды остаются только для
совместимости профилей.

Специализированные `Get UPS Status`, `Get UPS Voltage` и `Get IRP Status`
оставлены в runtime и десериализации только для старых JSON, но убраны из
палитры. Старые poll actions также поддерживаются.

Регистр пути IRP важен для части прошивок. В официальном PDF одновременно
встречаются `isUps` в заголовке команды и `isUPS` в примере запроса, поэтому
runtime сначала использует `/api/isUPS`, затем только при `404`/`Not Found`
пробует `/api/isUps`. HTML-страница ошибки не парсится как статус.

Legacy `Get IRP Status` перед запросом удаляет прежнюю output variable и принимает
только `0` или `1`. Результат диагностики сохраняется в
`GetIrpStatus.Url`, `GetIrpStatus.StatusCode`, `GetIrpStatus.Attempts`,
`GetIrpStatus.RawResponse`, `GetIrpStatus.Success`, `GetIrpStatus.Error`.
Это исключает ложный успех по старому `Dut.ups_det`, ранее полученному из
selftest. Legacy `Wait Variable Until` с `GetIrpStatus` использует тот же алгоритм и
пишет последний URL/status/raw response в `WaitVariable.*`.

## Получение серийного номера

`GetSerialNumberFromServerStep` строит URL двумя способами.

Если `ServerBaseUrl` похож на полный endpoint:

```text
https://server/api/api.svc/getSerialNum
```

то добавляется только query:

```text
?devType=...&cpuId=...
```

Если `ServerBaseUrl` - хост или базовый URL:

```text
server.local
http://server
```

то строится:

```text
http://server/api/api.svc/getSerialNum?devType=...&cpuId=...
```

Также можно указать `http://server/api` или `http://server/api/Api.svc`: нода
достроит только недостающую часть пути и не создаст ошибочный `/api/api/api.svc`.

Плейсхолдеры `SERVER_BASE_URL`, `http://SERVER_BASE_URL` и `server-address`
не отправляются в DNS как реальные хосты. При создании step из GUI они заменяются
общей настройкой `Server base URL`; если настройка пустая, нода завершается
понятной ошибкой `ServerBaseUrl не задан`.

Если `CpuIdVariableName` задан, нода требует непустую переменную CPU ID до
сетевого запроса. Имя ищется без учета регистра, чтобы `Dut.cpu_id` работал и с
XML-тегом `CPU_ID`. Запрос без `cpuId` остается доступен только при явно пустом
`CpuIdVariableName`, как в низкоуровневом методе старого Qt-стенда.

Перед запуском ноды прежние `SerialNumber`/`NetTest.SerialNumber` удаляются.
Серийник считается валидным только как положительное целое число в plain text
или JSON scalar; случайная последовательность цифр в HTML/тексте не принимается.
Для диагностики сохраняются URL, CPU ID, число попыток, последний HTTP status,
elapsed, raw response и текст ошибки.

Для стендовой отладки есть явный режим `UseFixedSerialNumber`. В нём серверный
endpoint не вызывается ни при каких условиях, а в `SerialNumber` и
`NetTest.SerialNumber` записывается `FixedSerialNumber`. В рабочем PSW-профиле
режим включён со значением `3200428`; источник виден в
`SerialNumberSource = FixedDebug`.

## Set Pro MAC

`SetProMacStep` заменяет неподходящую для Pro-коммутаторов legacy UDP-команду.
Он запускает предоставленный рабочий `set_mac_pro.bat` с тремя аргументами:

1. полный MAC из `Dut.NewMac`, нормализованный как `AA:BB:CC:DD:EE:FF`;
2. точное имя модели/`boardversion`, в рабочем профиле `PSW+UPS-Box 8x2Pro`;
3. текущий Unix timestamp в секундах.

Bat-файл через `WinSCP.com` подключается к `192.168.0.1` и выполняет команды
прошивки: `fw_setenv ethaddr`, `fw_setenv boardversion`, `macset.sh` и запись
`RTC_TIMESTAMP`. Секреты подключения остаются внутри поставляемого bat-файла и
не переносятся в C# или JSON-профиль.

Нода работает только в Windows. Относительный `BatchPath` ищется в текущей
папке и рядом с `TestBuilder.exe`; абсолютный путь также поддерживается.
stdout/stderr читаются параллельно в OEM-кодировке, а вызов полностью
асинхронный, поэтому ожидание WinSCP не блокирует UI. По таймауту завершается
всё дерево процессов — это также закрывает зависший `pause`, если WinSCP не
найден.

Успешным считается только сочетание exit code `0` и маркера `SUCCESSFUL` в
stdout рабочего bat-файла. Ненулевой код, отсутствие маркера, таймаут или
ошибка запуска ведут по `False` при `FailOnError = true`. Диагностика доступна
в `SetMac.Started`, `SetMac.TimedOut`, `SetMac.ExitCode`, `SetMac.StdOut`,
`SetMac.StdErr` и `SetMac.Error`; фактические аргументы — в `SetMac.Mac`,
`SetMac.BoardVersion`, `SetMac.Timestamp`, `SetMac.BatchPath`.

Успех процесса означает, что WinSCP принял команды, но не доказывает сохранение
MAC. Рабочий граф после этого перезапускает DUT, очищает ARP, получает новый
selftest и сравнивает `Dut.default_mac` с `Dut.NewMac`.

## Clear ARP Cache

`ClearArpCacheStep` очищает ARP-кэш Windows перед сетевыми обращениями к DUT.
Это нужно, когда устройство перезагрузилось, сменило MAC или стенд быстро
переключает платы с одинаковыми IP: Windows может помнить старую связку
`IP -> MAC`, и запросы уйдут не на то устройство.

По умолчанию step запускает поставляемый рядом с exe `arpd.bat`, затем команду
`arp -d *`. Относительный `arpd.bat` ищется также в `AppContext.BaseDirectory`.
Старое значение аргументов `-d` нормализуется в `-d *`.
На Windows перехваченные stdout/stderr читаются в OEM-кодировке текущей
системы, а не как UTF-8; русский stderr `arpd.bat` попадает в лог без mojibake.
Непустой stderr считается ошибкой даже при exit code `0`, поэтому отсутствие
прав администратора больше не сопровождается ложным сообщением об успешной
очистке. При `FailOnError = false` ошибка по-прежнему только диагностическая.

## DataTest через SharpPcap

Десять тестовых карт на рабочей Windows-машине настраиваются вручную и постоянно:

| Порт коммутатора | IP карты |
|---:|---|
| 0 | `192.168.0.2/24` |
| 1 | `192.168.0.3/24` |
| 2 | `192.168.0.4/24` |
| 3 | `192.168.0.5/24` |
| 4 | `192.168.0.6/24` |
| 5 | `192.168.0.7/24` |
| 6 | `192.168.0.8/24` |
| 7 | `192.168.0.9/24` |
| 8 | `192.168.0.10/24` |
| 9 | `192.168.0.11/24` |

Приложение не перенастраивает сетевые адаптеры. `Run Data Test` только находит
уже настроенные pcap-устройства по этим IP и проверяет пары `0-1`, `2-3`,
`4-5`, `6-7`, `8-9`.

`RunDataTestStep` делает программный сетевой тест.

Поддерживаемые режимы:

- `SoftwarePcap`;
- `Pcap`;
- `Software`;
- `TYPE_SOFT_GEN`.

Все эти значения ведут в одну текущую реализацию software/pcap. Если указать другой режим, step вернет ошибку с рекомендацией использовать `SoftwarePcap`.

Основные этапы:

1. Получить список `CaptureDeviceList.Instance`.
2. Последовательно, по одной строке `PortsText`, найти receive/send сетевые карты по IP.
3. Получить MAC-адреса адаптеров.
4. Построить Ethernet/IP/UDP packet заданного размера с ID запуска и sequence.
5. Открыть receive и send устройства в promiscuous/max responsiveness.
6. Поставить capture filter:

```text
ether src {OutMac} and ether dst {InMac}
and udp src port {UdpPort} and udp dst port {UdpPort}
and src host {OutIp} and dst host {InIp}
```

7. Сделать warmup с отдельным ID и дождаться очистки хвоста capture.
8. На Windows/Npcap заранее собрать синхронизированные send queues по 500 мс и
   передать их драйверу. Это уменьшает context switches и делает скорость
   стабильнее. Если native send queue недоступна, используется paced `SendPacket`.
9. Считать только уникальные sequence текущего ID; отдельно учитывать duplicates,
   unexpected packets и Npcap capture drops.
10. Посчитать RX/TX/loss/tx deficit и скорости на линии с учетом 24 байт overhead
    на кадр: preamble/SFD 8, FCS 4, inter-packet gap 12.
11. Сравнить loss с `AllowedLossPercent`, а deficit генератора — с отдельным
    `AllowedTxDeficitPercent`.
12. При инфраструктурной ошибке генератора/захвата повторить направление один раз.
    Потеря при нормальном TX и без capture drops считается результатом DUT и не
    скрывается retry.
13. Если `Bidirectional = true`, поменять send/receive местами и проверить обратное
    направление. Затем закрыть карты и выдержать `InterPairDelayMs`.

Пары никогда не запускаются параллельно. Нода предназначена для 100-Мбит портов:
глобальный и парный target ограничены `100 Mbps`, а legacy `1000` при загрузке
профиля мигрирует на `100`. Для кадра 1514 байт target 100 Mbps соответствует
примерно 8127 кадрам/с с учетом физического overhead Ethernet.

### Значение метрик

| Метрика | Значение |
|---|---|
| `target` | Требуемая скорость на физической Ethernet-линии. |
| `expected` | Сколько кадров должно пройти за `DurationMs` при target. |
| `TX` | Сколько нумерованных кадров генератор подтвердил как отправленные. |
| `RX` | Сколько уникальных кадров этого запуска поймано на карте назначения. |
| `loss` | `(TX - RX) / TX`; характеризует путь через DUT, если capture drops равны нулю. |
| `tx deficit` | Насколько фактическая TX wire speed ниже target; характеризует генератор/ПК. |
| `tx speed` | Фактическая скорость отправки на линии с Ethernet overhead. |
| `rx speed` | Фактическая скорость уникальных принятых кадров на той же временной базе. |

### Формат PortsText

```text
port0-1,192.168.0.2,192.168.0.3,100
port2-3,192.168.0.4,192.168.0.5,100
port4-5,192.168.0.6,192.168.0.7,100
port6-7,192.168.0.8,192.168.0.9,100
port8-9,192.168.0.10,192.168.0.11,100
```

Поля:

1. Название пары.
2. `InIp` - receive/destination IP.
3. `OutIp` - send/source IP.
4. Опциональный target bandwidth для конкретной пары.

## Сбор отчета

`BuildTestReportStep` повторяет формат `QTstand_old`:

```text
test_result=true=1
stand_id=true=123
serial_num=true=3200123
session=true=<session-id>
Тип проверки=true=production
самотестирование=true=true
напряжение АКБ=true=24.5
```

`test_result` зависит от `context.HasCriticalError`.
`stand_id` читается из `AppSettings.StandId` (дефолт `123`), а `session` — из первого аргумента
запуска приложения и пропускается, если аргумент отсутствует. `serial_num` —
полный серверный `SerialNumber`, не короткий номер этикетки.

Каждая строка имеет формат `name=true|false=value\r\n`. `SubtestStep` сохраняет
исход составного подтеста в `TestContext.ReportEntries`. Если
`IncludeAllVariables = true`, в отчет также попадает отсортированная копия
диагностических переменных контекста.

`BuildTestReportStep` не отправляет отчет и не пишет файл; он только формирует
текст в переменной. Отправка и локальная копия выполняются отдельной нодой
`Send Test Report`.

## Отправка отчета

`SendTestReportStep`:

1. Читает построчный отчет из `ReportVariableName`.
2. Строит URL `ServerBaseUrl + Endpoint`.
3. Если `SaveLocalCopy = true`, пишет файл:

```text
{LocalReportsDirectory}/result-yyyyMMdd-HHmmss-fff.txt
```

4. Делает POST с побайтно совместимым multipart-телом `QTstand_old`:

```text
field: action (empty)
field: updatefile; filename: result.json; content-type: application/octet-stream
field: result; filename: result.json (empty)
```

Legacy-тело намеренно сохраняет двойную строку boundary перед полем `result` и
не добавляет стандартный закрывающий суффикс `--`: именно такие байты формирует
старый `MainWindow::set_test_result`. Стандартный `MultipartFormDataContent`
давал тот же набор полей, но сервер фактически отвечал `HTTP 200` с текстом
`error`.

5. Успех: HTTP 2xx и response начинается с `Ok`.
6. При ошибке делает retry.

## Печать этикетки

`PrintLabelStep` строит ZPL:

- заголовок устройства;
- SN;
- MAC, если `IncludeMac = true`;
- barcode.

Если `EquipmentFieldUse = true`, serial number для печати получает суффикс:

```text
{serial}-{EquipmentType}{EquipmentText}
```

RAW-печать реализована через Windows API:

- `OpenPrinter`;
- `StartDocPrinter`;
- `StartPagePrinter`;
- `WritePrinter`;
- `EndPagePrinter`;
- `EndDocPrinter`;
- `ClosePrinter`.

Ограничение: на не-Windows ОС нода возвращает ошибку `RAW-печать поддерживается только в Windows`.

## Типичные цепочки

### Selftest и проверки

```text
Selftest Check
  -> Check Variable Equality (Dut.init_ok == 1)
  -> Check Variable Range (Dut.akb_voltage 12..27)
```

### Серийник, MAC, установка MAC

```text
Get Serial Number
  -> Build MAC From Serial
  -> Set Pro MAC
  -> Delay
  -> Clear ARP Cache
  -> Selftest Check
  -> Compare Variables (Dut.default_mac == Dut.NewMac)
```

### Отчет и отправка

```text
Build Test Report
  -> Send Test Report
```
