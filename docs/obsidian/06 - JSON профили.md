---
tags:
  - testbuilder
  - json
  - serialization
updated: 2026-09-23
---

# JSON профили

Профили тестирования сохраняются в JSON через `GraphSerializer`. Формат ориентирован на человекочитаемое хранение графа: список нод, координаты, параметры и связи.

В терминологии стенда **старые коммутаторы** — модели без `Pro`; **новые** —
модели с `Pro`. Первая добавленная в TestBuilder модель `PSW+UPS-Box 8x2Pro`
относится к новым. Профили разных семейств нельзя смешивать:

| Семейство | Пример | Страница selftest | Запись MAC |
|---|---|---|---|
| Старые, без `Pro` | `PSW-2G6F+` | `/test.shtml` | `Set PSW MAC (UDP)` |
| Новые, с `Pro` | `PSW+UPS-Box 8x2Pro` | LuCI `/cgi-bin/luci/admin/statistics/deviceinfo` | `Set Pro MAC` через WinSCP |

Это соответствие подтверждено указанными профилями и стендовым логом для
`PSW-2G6F+`; особенности других моделей без `Pro` нужно брать из их профилей.

`GraphSerializer` использует `WriteIndented = true`, case-insensitive чтение свойств и `UnsafeRelaxedJsonEscaping`, поэтому русские строки в профиле сохраняются читаемо, без `\uXXXX`.

## Верхний уровень

```json
{
  "name": "Example profile",
  "nodes": [],
  "connections": []
}
```

| Поле | Тип | Назначение |
|---|---|---|
| `name` | string | Имя профиля или вложенного графа. |
| `nodes` | array | Ноды графа. |
| `connections` | array | Связи между коннекторами. |

## Нода

Для `Check Register Range` необязательные поля `readAttempts` и
`readIntervalMs` задают число live-чтений и паузу перед каждым чтением в
миллисекундах. Без них сохраняется прежнее поведение: одна проверка без паузы.
В профиле `PSW-2G6F+` они равны 3 и 600 для регистров PoE 1403/1402.

Общие поля:

```json
{
  "id": "0",
  "type": "Start",
  "x": 64,
  "y": 96
}
```

| Поле | Тип | Назначение |
|---|---|---|
| `id` | string | Локальный ID внутри одного графа. При сохранении назначается по индексу. |
| `type` | string | Канонический английский тип ноды. |
| `NodeType` | string? | Совместимость со старым форматом; читается, если `type` пустой. |
| `x`, `y` | double | Координаты на рабочем поле. |
| `color` | string? | Цвет рамки ноды: `blue`, `turquoise`, `green`, `yellow`, `orange`, `red` или `purple`. |

## Связь

```json
{
  "sourceNodeId": "0",
  "sourceConnector": "Выход",
  "targetNodeId": "1",
  "targetConnector": "Вход"
}
```

| Поле | Назначение |
|---|---|
| `sourceNodeId` | ID исходной ноды. |
| `sourceConnector` | Текст заголовка исходного коннектора. |
| `targetNodeId` | ID целевой ноды. |
| `targetConnector` | Текст заголовка целевого коннектора. |

Важно: коннекторы ищутся по `Title`. Поэтому переименование коннекторов в коде влияет на совместимость JSON.

## Канонические типы нод

`GraphSerializer.GetNodeType` сохраняет такие `type`:

| Type | ViewModel |
|---|---|
| `Start` | `StartNodeViewModel` |
| `End` | `EndNodeViewModel` |
| `Body Start` | `BodyStartNodeViewModel` |
| `Body End` | `BodyEndNodeViewModel` |
| `Delay` | `DelayNodeViewModel` |
| `Label` | `LabelNodeViewModel` |
| `Write Register` | `ModbusWriteNodeViewModel` |
| `Check Register Range` | `CheckRegisterRangeNodeViewModel` |
| `Check Register Equality` | `CheckRegisterEqualityNodeViewModel` |
| `Wait Until` | `WaitUntilNodeViewModel` |
| `Poll Register` | `PollRegisterNodeViewModel` |
| `Operator Action` | `OperatorActionNodeViewModel` |
| `Selftest Check` | `SelfTestCheckNodeViewModel` |
| `Check Variable Equality` | `CheckVariableEqualityNodeViewModel` |
| `Check Variable Range` | `CheckVariableRangeNodeViewModel` |
| `Clear ARP Cache` | `ClearArpCacheNodeViewModel` |
| `Get Serial Number` | `GetSerialNumberFromServerNodeViewModel` |
| `Set Pro MAC` | `SetProMacNodeViewModel` |
| `Set PSW MAC (UDP)` | `SetPswMacNodeViewModel` |
| `Update PSW Firmware` | `UpdatePswFirmwareNodeViewModel` |
| `Run Data Test` | `RunDataTestNodeViewModel` |
| `Get UPS Status` | `GetUpsStatusNodeViewModel` |
| `Get UPS Voltage` | `GetUpsVoltageNodeViewModel` |
| `Get IRP Status` | `GetIrpStatusNodeViewModel` |
| `Read HTTP Variable` | `ReadHttpVariableNodeViewModel` |
| `Build MAC From Serial` | `BuildMacFromSerialNodeViewModel` |
| `Compare Variables` | `CompareVariablesNodeViewModel` |
| `Wait Variable Until` | `WaitVariableUntilNodeViewModel` |
| `Build Test Report` | `BuildTestReportNodeViewModel` |
| `Print Label` | `PrintLabelNodeViewModel` |
| `Send Test Report` | `SendTestReportNodeViewModel` |
| `Subtest` | `SubtestNodeViewModel` |
| `For Slaves` | `ForEachSlaveNodeViewModel` |

Deserializer также принимает часть русских и legacy-имен, например `Старт`, `Конец`, `WriteRegister`, `SELFTEST_CHECK`, `GET_UPS_STATUS`. Старые типы `Send UDP Set MAC`, `SEND_UDP_SET_MAC_PACKET` и `UDP установка MAC` загружаются как `Set Pro MAC`; их UDP-поля игнорируются, а таймаут меньше 10 секунд заменяется безопасным значением 60000 мс.

Канонические типы при сохранении всегда английские. Русские и legacy-имена нужны только для загрузки старых профилей.

## Поля параметров

`NodeDto` содержит объединенную схему всех нод. Поля записываются только когда нужны конкретному типу.

| Группа | Поля |
|---|---|
| Delay | `milliseconds` |
| Label | `text`, `labelWidth`, `labelHeight` |
| Subtest | `name`, `description`, `isEnabled`, `stopOnError`, `runOnFailure`, `bodyGraph` |
| Modbus | `slaveId`, `useCurrentSlaveId`, `address`, `value`, `verifyWrite`, `min`, `max`, `expectedValue`, `durationMs`, `sampleCount`, `liveRead` |
| Selftest/HTTP | `url`, `timeoutMs`, `outputPrefix`, `validationRules`, `baseUrl`, `endpoint`, `responseType`, `outputVariableName`, `failOnError` |
| Variables | `variableName`, `leftVariableName`, `rightVariableName`, `comparisonType`, `failMessage`, `inclusive` |
| Serial/MAC | `serverBaseUrl`, `deviceType`, `cpuIdVariableName`, `useFixedSerialNumber`, `fixedSerialNumber`, `serialVariableName`, `serialOffset`, `macPrefix`, `serialShortVariableName`, `macVariableName`, `batchPath`, `boardVersion` |
| Set PSW MAC (UDP) | `destinationIp`, `localIp`, `localPort`, `udpPort`, `macVariableName`, `timeoutMs`, `failOnError` |
| Update PSW Firmware | `baseUrl`, `firmwarePath`, `targetVersion`, `versionVariable`, `forceUpdate` |
| DataTest | `mode`, `expectedPackets`, `packetSizeBytes`, `udpPort`, `maxPortTestTimeMs`, `targetBandwidthMbps`, `allowGigabit`, `durationMs`, `warmupMs`, `interPairDelayMs`, `allowedLossPercent`, `allowedTxDeficitPercent`, `bidirectional`, `portsText`, `ports` |
| Print Label | `printerName`, `serialVariableName`, `serialShortVariableName`, `macVariableName`, `useManualSerialNumber`, `manualSerialNumber`, `manualMacAddress`, `copies`, `useQtProZplFormat`, `labelModel`, `failOnPrinterError` |
| Report | `reportVariableName`, `testType`, `endpoint`, `retryCount`, `retryDelayMs`, `saveLocalCopy`, `localReportsDirectory`, `includeAllVariables` |
| For Slaves | `fromSlaveId`, `toSlaveId`, `step`, `stopOnError`, `body` |
| Wait Variable | `pollAction`, `baseUrl`, `endpoint`, `responseType`, `requestTimeoutMs`, `timeoutMs`, `intervalMs`, `failOnTimeout` |
| Clear ARP | `runArpdBat`, `arpdBatPath`, `command`, `arguments` |

Для браузерного исполнения `Selftest Check` дополнительные JSON-поля не нужны:
`timeoutMs` остаётся общим сроком ожидания DUT и тестовой страницы, а
`pollIntervalMs` — интервалом ICMP/TCP-проверок готовности. Браузер запускается
только после положительной проверки; внутренний предел одной браузерной попытки
— 30000 мс или меньше при близком общем deadline. В стартовом подтесте рабочего
профиля используются `timeoutMs: 300000` и `pollIntervalMs: 5000`; последующие
проверки уже загруженного DUT сохраняют `timeoutMs: 180000`.

Датчики собираются обычными нодами, без специализированного типа:
`Write Register` (`slaveId: 21`, `useCurrentSlaveId: false`, `verifyWrite: true`)
сначала обнуляет `1500/1501`. В PSW-2G6F+ после исходного `Selftest Check`
команда `1500=1` замыкает сухой контакт Sensor1; `Wait Variable Until` с
`pollAction: SelftestSnapshot` ждёт свежий `Dut.sensor_1=1`, затем `1500=0`
размыкает контакт. Как в старом C++, исходное значение лишь считывается,
а после размыкания новый веб-снимок не требуется. `sensor_0` и `sensor_2`
в PSW-профилях не проверяются. Профиль Pro сохраняет проверку Sensor1/2.
Каждый снимок проверяет готовность (`init_ok=1`, числовой `dev_type`),
но не валидирует версии прошивки и загрузчика: на DUT они могут приходить
шестнадцатеричными строками. В PSW-2G6F+ версию отдельно проверяет
`Update PSW Firmware` после первого selftest.
Параметры ожидания: `requestTimeoutMs: 30000`, `timeoutMs: 60000`,
`intervalMs: 1000`, `failOnTimeout: true`. PSW-2G6F+ читает `/test.shtml`,
Pro — LuCI `deviceinfo`. Аварийный `runOnFailure`-подтест также сбрасывает оба
выхода, продолжая попытки при `False` отдельной записи.

Реле Pro проверяется обычными `Read HTTP Variable` (команды
`/test.shtml?set_mb_output=0/1`), `Write Register` (сброс `1507=0` после
выключения) и `Wait Until` (`1507=1`, `liveRead: true`). В PSW-2G6F+ этих
шагов нет. Старый тип `Check IO-2 Sensors and Relay` и его aliases теперь
отклоняются с указанием импортировать обновлённый профиль; проверка не
пропускается молча. Поля специализированной ноды удалены из DTO.

Рабочий подтест `проверка акб (упс)` не использует `akb_det`: на фактической
прошивке поле не меняется после включения SIMBAT. Подтест выполняет четыре
проверяемые записи: `slave 17 / 1706 ← 1`, `slave 23 / 1200 ← 0`,
`1200 ← 1`, `slave 17 / 1706 ← 0`. Пока SIMBAT подключён, после первых трёх
управляющих действий `Wait Variable Until` + `SelftestSnapshot` ждёт
`Dut.akb_voltage` в включительном диапазоне `20..27` В.

Диапазон хранится как `comparisonType: "Number"` и
`expectedValue: "20..27"`. У каждого ожидания request timeout `30000`, общий
timeout `160000`, интервал `5000` мс и `failOnTimeout: true`. Проверка после
`1706 ← 0` не выполняется, поскольку допустимый диапазон напряжения при
отключённом имитаторе не задан; отключение подтверждается Modbus read-back.

Для `Clear ARP Cache` дефолтный `arguments` - `-d *`. Старые профили с
`arguments: "-d"` при выполнении нормализуются в `-d *`, если `command` равен
`arp`.

В рабочем PSW-профиле `Get Serial Number` содержит
`useFixedSerialNumber: false`, а `fixedSerialNumber` отсутствует. Нода выполняет
production-запрос с фактическим `Dut.cpu_id`; успешный запрос может выдать новый
серийный номер. Отладочный режим остаётся доступен в редакторе ноды, но в этом
профиле не используется. Запрос выполняется через `getSerialNum`, как в
производственном пути старого Qt; `getExistsSerialNum` не используется.
`Set Pro MAC` использует
`batchPath: "set_mac_pro.bat"`, `boardVersion: "PSW+UPS-Box 8x2Pro"`,
`macVariableName: "Dut.NewMac"`, `timeoutMs: 60000` и `failOnError: true`.
Относительный путь означает bat рядом с exe; его содержимое и учётные данные
не сериализуются в профиль. Третий аргумент, Unix timestamp, создаётся во время
запуска и также не хранится в JSON.

В `PSW_UPS_Box_8x2Pro_full_algorithm_polling.json` `Run Data Test` использует
пять постоянных пар: `.2/.3`, `.4/.5`, `.6/.7`, `.8/.9`, `.10/.11` в сети
`192.168.0.0/24`. Адреса назначаются вручную в Windows и профилем не изменяются.
Target рабочего профиля — `100 Mbps`, `bidirectional = true`. При загрузке и
сохранении `targetBandwidthMbps`, `ports[].bandwidthMbps` и четвертое поле каждой
строки `portsText` ограничиваются диапазоном `1..100` при `allowGigabit=false`
(дефолт); это автоматически мигрирует ошибочные legacy-значения `1000` на `100`.
Явный `allowGigabit=true` расширяет диапазон до `1..1000`, сохраняется и
учитывается при загрузке, клонировании и выполнении. Для старых профилей без новых полей
используются `allowedTxDeficitPercent = 2.0` и `bidirectional = true`.

`Build Test Report` сохраняет `testType` (`production` по умолчанию) и строит
построчный отчет протокола `QTstand_old`, а не JSON. В рабочем профиле общий
`reportVariableName` у сборки и отправки — `TestReportText`. Старые профили со
значением `serialVariableName: "SerialShort"` автоматически переключаются на
полный `SerialNumber`, требуемый сервером отчетов.

## Вложенные графы

### Профиль PSW-2G6F+ с прошивкой 0.2.13

Пошаговое описание: [[20 - PSW-2G6F+ тестирование]]. Пошаговое описание
другой модели: [[21 - PSW+UPS-Box 8x2Pro тестирование]].

Рабочий `PSW_2G6F_plus_full_algorithm.json` должен оставаться валидным JSON без
маркеров слияния Git. Проверка серийного номера выполняется через сервер:
`useFixedSerialNumber: false`; отладочное поле `fixedSerialNumber` не нужно в
поставляемом профиле. Сохранение через редактор может менять цвета и координаты
нод, но не должно менять эти рабочие параметры.

`profiles/PSW_2G6F_plus_full_algorithm.json` — единственный поставляемый профиль
этой модели; диагностический черновик удалён. Selftest — `/test.shtml`, модель 6; только сухой контакт Sensor1 через IO-2 slave 21 без релейного теста;
12 линий PoE; три медные пары по 100 и SFP `.8/.9` по 1000 Мбит/с.
Профиль предварительно сбрасывает `21:1500/1501=0`, затем проверяет
только `sensor_1=1` после `1500=1` и завершают проверку записью `1500=0`.
Проверок `sensor_0`, `sensor_2` и веб-условия после `1500=0` нет.
Первый `Selftest Check` обращается к `/test.shtml` и проверяет только
`init_ok=1`, `dev_type=6`: прежний URL
`/cgi-bin/luci/.../deviceinfo` относится к профилю нового коммутатора `Pro` и
не совпадал с подтверждённым для PSW-2G6F+ адресом. `firmvare_vers` у этого
DUT может быть строкой вроде `20c`.
Серийники `600000..665535`, MAC-префикс `C0:11:A6:06`; отдельный тип
`Set PSW MAC (UDP)` не пересекается с legacy-алиасами `Set Pro MAC`.
После перезапуска MAC проверяется в отдельном префиксе `DutAfterMac`.

`Print Label.labelModel` — строковый enum `PswUpsBox8x2Pro` (дефолт) или
`Psw2G6FPlus`; применяется при `useQtProZplFormat=true`. Для модели 6 —
четыре этикетки, штрихкод `006SSSSS`. После первого selftest проверяется версия
ПО, при более старой версии загружается `sw407-0.2.13-05.09.2025.img` и затем
повторно читается страница. Образ берётся с Windows-стенда по пути из
`firmwarePath`; SHA-256 не проверяется. После удачной установки 0.2.13
`forceUpdate=false`, так что повторная загрузка не выполняется. DFU не входит
в автоматический граф; `testType` — `production (прошивка 0.2.13, без DFU)`.
Адреса стенда и требуемое оборудование описаны в
[`PSW_2G6F_plus_migration.md`](../PSW_2G6F_plus_migration.md).

### Хранение вложенных графов

`For Slaves` сохраняет вложенный граф в поле `body`:

```json
{
  "type": "For Slaves",
  "body": {
    "name": "Тело цикла For Slaves",
    "nodes": [],
    "connections": []
  }
}
```

`Subtest` сохраняет вложенный граф в поле `bodyGraph`:

```json
{
  "type": "Subtest",
  "name": "Selftest",
  "runOnFailure": false,
  "bodyGraph": {
    "name": "Selftest",
    "nodes": [],
    "connections": []
  }
}
```

Для совместимости `CreateSubtestNode` умеет читать и `bodyGraph`, и `body`.

## Значения по умолчанию при загрузке

Если поле отсутствует, deserializer подставляет дефолт из кода. Примеры:

| Нода | Поле | Дефолт |
|---|---|---|
| `Delay` | `milliseconds` | `1000` |
| `Label` | `text` | `Этап` |
| `Label` | `labelWidth`, `labelHeight` | `300`, `120` |
| `Selftest Check` | `url` | `SelfTestCheckStep.DefaultUrl` |
| `Selftest Check` | `pollIntervalMs` | `SelfTestCheckStep.DefaultPollIntervalMs` |
| `Get UPS Status` | `baseUrl` | `http://192.168.0.1` |
| `Read HTTP Variable` | `baseUrl`, `endpoint`, `responseType` | `http://192.168.0.1`, `/api/getUpsStatus`, `Integer` |
| `Wait Variable Until` | `pollAction`, `endpoint`, `responseType` | `HttpGet`, `/api/getUpsStatus`, `Integer` |
| `Build MAC From Serial` | `serialOffset` | `3200000` |
| `Build MAC From Serial` | `macPrefix` | `C0:11:A6:20` |
| `Print Label` | `printerName`, `serialVariableName`, `serialShortVariableName`, `macVariableName`, `useManualSerialNumber`, `manualSerialNumber`, `manualMacAddress`, `copies`, `useQtProZplFormat` | `TSC TE310`, `SerialNumber`, `SerialShort`, `Dut.default_mac`, `false`, `""`, `""`, `4`, `false` |
| `For Slaves` | `fromSlaveId`, `toSlaveId`, `step` | `1`, `20`, `1` |
| `Send Test Report` | `endpoint` | `/api/Api.svc/result.json` |
| `Subtest` | `runOnFailure` | `false` |
| `Check Register Range` / `Check Register Equality` / `Wait Until` / `Poll Register` | `liveRead` | `false` |
| Любая нода | `color` | `blue` |

Полный список дефолтов описан в [[05 - Справочник нод]].

У старых `Print Label` допустимы лишние поля `deviceName`, `deviceType`,
`includeMac`, `equipmentFieldUse`, `equipmentType` и `equipmentText`:
JSON-deserializer проигнорирует их как неизвестные для актуальной ноды.
`macVariableName` теперь является действующим полем Pro-ZPL-режима.
При следующем сохранении эти legacy-поля исчезнут. Если старый профиль не
содержит `serialVariableName`, используется полный `SerialNumber`.

Поставляемый `manual_label_printing.json` содержит только структурные
`Start`/`End` и один исполняемый `Print Label`. В нём
`useManualSerialNumber: true`, пустое `manualSerialNumber` заполняется оператором
в UI перед запуском вместе с обязательным `manualMacAddress`; `copies: 4` и
`useQtProZplFormat: true`. Поэтому он печатает четыре Qt/ZPL-этикетки
`PSW+UPS-Box 8x2Pro` с введённым MAC, коротким SN и составным штрихкодом.
Поскольку Modbus-нод нет, профиль разрешено запускать без подключения к стенду.

Основной рабочий профиль также включает Pro-ZPL-режим, но использует только
значения контекста: `SerialNumber`, `SerialShort` и фактически прочитанный после
перезапуска `Dut.default_mac`. Отсутствующие или несогласованные данные ведут в
`False`; Print Label не рассчитывает MAC и не использует `SetMac.Timestamp`.

Перед штатным и аварийным `Send Test Report` рабочий профиль содержит
`Operator Action` с шаблонами `{SerialNumber}`, `{Dut.default_mac}` и
`{Dut.NewMac}`. Ветка `Отмена` пропускает HTTP POST. Аварийный отчёт дополнительно
защищён `Check Variable Range` для `SerialNumber = 3200000..3299999`.

Старые `Wait Variable Until` без `endpoint`/`responseType` сохраняют поведение:
`GetUpsStatus`, `GetUpsVoltage` и `GetIrpStatus` автоматически получают прежние
endpoint и тип ответа. Специализированные типы `Get UPS Status`,
`Get UPS Voltage`, `Get IRP Status` также десериализуются, но новые графы должны
использовать `Read HTTP Variable` и `Wait Variable Until` + `HttpGet`.
Для прошивок без скалярного DUT API поддерживается `pollAction = SelftestSnapshot`:
`endpoint` указывает на тестовую страницу, а каждая попытка обновляет все
переменные указанного output prefix.

## Совместимость и риски

- JSON хранит связи по тексту коннектора. Нельзя без миграции менять `Title` коннекторов.
- В старых/битых профилях могут встречаться строки коннекторов с mojibake или `????`; такие связи deserializer пропустит, если не найдет коннектор.
- `FindConnector` содержит специальную совместимость для `For Slaves`: `Success` сопоставляется с `True`, `Error` - с `False`.
- `ExpectedValue` и `DeviceType` хранятся как `object?`, поэтому deserializer содержит преобразования из `JsonElement`, строк и чисел.
- В `For Slaves` старые коннекторы `Success`/`Error` при загрузке сопоставляются с текущими `True`/`False`.
- `Subtest` при загрузке принимает и `bodyGraph`, и старое поле `body`; при сохранении использует `bodyGraph`.
- Старые профили без `runOnFailure` и `liveRead` загружаются как раньше: оба флага получают `false`.
