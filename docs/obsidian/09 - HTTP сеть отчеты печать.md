---
tags:
  - testbuilder
  - http
  - network
  - reports
  - printing
updated: 2026-08-21
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

- до общего timeout повторяет браузерные попытки по одному исходному URL;
- в каждой попытке запускает один headless-процесс Chrome/Edge;
- ждет завершения загрузки по `document.readyState == complete`, затем полные 10 секунд;
- один раз читает DOM через DevTools Protocol как аналог Selenium `driver.PageSource`
  и закрывает браузер этой попытки;
- если снимок не содержит `<selftest>...</selftest>` или совместимый
  `<settings>...</settings>` с `default_mac`, ждет `PollIntervalMs` и пробует снова.

Каждая попытка намеренно повторяет старую отдельную Selenium-утилиту: один `GoToUrl`,
`Thread.Sleep(10000)`, один `PageSource`. В рабочем браузерном режиме нет POST-логина,
обычного HTTP fallback и запроса `/test.shtml`; повторяется только эта целая попытка.
Старые сохраненные профили с `TimeoutMs` до `240000` для DUT selftest автоматически получают
эффективный таймаут `300000`: коммутатор может запускаться до четырех минут.
Для headless Chrome/Edge используется внутреннее чтение DOM через DevTools Protocol после ожидания
загрузки страницы, по смыслу аналогично старому Selenium `driver.PageSource`, но без внешней утилиты.
`PollIntervalMs` задает паузу между полными браузерными попытками.
Извлечение декодирует HTML-, URL- и JS-экранирования, потому что нужный XML может лежать в скрытом DOM/скриптах LuCI.
Проверка `ValidationRules` пишет в лог отдельные строки по каждому параметру и сохраняет
`SelfTest.CheckedRuleCount`, `SelfTest.FailedRuleCount`, `SelfTest.ValidationSummary`.

Операционная причина пятиминутного ожидания, нерабочие альтернативы и порядок диагностики
зафиксированы отдельно: [[18 - Selftest и загрузка DUT]].

Raw XML сохраняется только в `TestContext` текущего запуска как `SelfTestRaw`.
Файл `selftest.txt` больше не создается: он был legacy-артефактом старой консольной утилиты.

`FailOnError` в selftest-нode не переключает результат на `True`; при ошибке step возвращает `False` всегда. Разница только в том, выставляется ли `context.HasCriticalError`, который потом влияет на поле `test_result` в отчете.

## API устройства

Ноды DUT API используют `BaseUrl` и фиксированные endpoint:

| Нода | Endpoint | Ожидаемый ответ |
|---|---|---|
| `Get UPS Status` | `/api/getUpsStatus` | int |
| `Get UPS Voltage` | `/api/getUpsVoltage` | double |
| `Get IRP Status` | `/api/isUps` | int |
| `Wait Variable Until` + `GetUpsStatus` | `/api/getUpsStatus` | int |
| `Wait Variable Until` + `GetUpsVoltage` | `/api/getUpsVoltage` | double |
| `Wait Variable Until` + `GetIrpStatus` | `/api/isUps` | int |

Все эти ноды пишут raw response, success flag и error в `context.Variables`.

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

Плейсхолдеры `SERVER_BASE_URL`, `http://SERVER_BASE_URL` и `server-address`
не отправляются в DNS как реальные хосты. При создании step из GUI они заменяются
общей настройкой `Server base URL`; если настройка пустая, нода завершается
понятной ошибкой `ServerBaseUrl не задан`.

Серийник считается валидным, если из ответа можно получить положительное целое число.

## UDP Set MAC

`SendUdpSetMacPacketStep` отправляет legacy UDP-пакет на устройство.

Пакет:

| Offset | Данные |
|---:|---|
| `0` | ASCII `CONFIG` |
| `10` | ASCII `mw` |
| `12` | 6 байт MAC |
| `18` | ASCII `Kr2` |

Длина пакета - 21 байт.

MAC принимается в разных форматах, потому что parser оставляет только hex-символы:

- `AA:BB:CC:DD:EE:FF`;
- `AA-BB-CC-DD-EE-FF`;
- `AABBCCDDEEFF`.

## Clear ARP Cache

`ClearArpCacheStep` очищает ARP-кэш Windows перед сетевыми обращениями к DUT.
Это нужно, когда устройство перезагрузилось, сменило MAC или стенд быстро
переключает платы с одинаковыми IP: Windows может помнить старую связку
`IP -> MAC`, и запросы уйдут не на то устройство.

По умолчанию step запускает поставляемый рядом с exe `arpd.bat`, затем команду
`arp -d *`. Относительный `arpd.bat` ищется также в `AppContext.BaseDirectory`.
Старое значение аргументов `-d` нормализуется в `-d *`.

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
4. Построить Ethernet/IP/UDP packet заданного размера.
5. Открыть receive и send устройства в promiscuous/max responsiveness.
6. Поставить capture filter:

```text
udp port {UdpPort} and ip dst host {InIp}
```

7. Сделать warmup.
8. В рабочем потоке отправлять пакеты с pacing под `TargetBandwidthMbps`, регулярно
   ограничивая непрерывную пачку, чтобы не блокировать UI.
9. Посчитать RX/TX/loss/tx deficit.
10. Сравнить с `AllowedLossPercent`.
11. Закрыть обе карты и выдержать `InterPairDelayMs` перед следующей парой.

Пары никогда не запускаются параллельно. При `1000 Mbps` и кадре 1514 байт одна
пара требует примерно 82,5 тысячи отправок в секунду; длинный тест всё равно
создаёт существенную нагрузку, поэтому фактическую достижимую скорость нужно
проверять на стендовой машине.

### Формат PortsText

```text
port0-1,192.168.0.2,192.168.0.3,1000
port2-3,192.168.0.4,192.168.0.5,1000
port4-5,192.168.0.6,192.168.0.7,1000
port6-7,192.168.0.8,192.168.0.9,1000
port8-9,192.168.0.10,192.168.0.11,1000
```

Поля:

1. Название пары.
2. `InIp` - receive/destination IP.
3. `OutIp` - send/source IP.
4. Опциональный target bandwidth для конкретной пары.

## Сбор отчета

`BuildTestReportStep` собирает JSON:

```json
{
  "test_result": 1,
  "profile": "profile name",
  "device_name": "PSW+UPS-Box 8x2Pro",
  "device_type": 32,
  "serial_num": "12345",
  "mac": "C0:11:A6:20:00:01",
  "created_at": "2026-06-30T...",
  "variables": {}
}
```

`test_result` зависит от `context.HasCriticalError`.

Если `IncludeAllVariables = true`, в отчет попадает отсортированная копия всех переменных контекста. Это удобно для диагностики, но может сделать отчет большим.

`BuildTestReportStep` не отправляет отчет и не пишет файл; он только формирует JSON-строку в переменной. Отправка и локальная копия выполняются отдельной нодой `Send Test Report`.

## Отправка отчета

`SendTestReportStep`:

1. Читает JSON из `ReportVariableName`.
2. Строит URL `ServerBaseUrl + Endpoint`.
3. Если `SaveLocalCopy = true`, пишет файл:

```text
{LocalReportsDirectory}/result-yyyyMMdd-HHmmss-fff.json
```

4. Делает POST multipart form-data:

```text
field name: file
file name: result.json
content-type: application/json
```

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
  -> Send UDP Set MAC
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
