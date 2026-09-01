---
tags:
  - testbuilder
  - json
  - serialization
updated: 2026-09-01
---

# JSON профили

Профили тестирования сохраняются в JSON через `GraphSerializer`. Формат ориентирован на человекочитаемое хранение графа: список нод, координаты, параметры и связи.

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
| `Send UDP Set MAC` | `SendUdpSetMacPacketNodeViewModel` |
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

Deserializer также принимает часть русских и legacy-имен, например `Старт`, `Конец`, `WriteRegister`, `SELFTEST_CHECK`, `GET_UPS_STATUS`.

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
| Serial/MAC | `serverBaseUrl`, `deviceType`, `cpuIdVariableName`, `serialVariableName`, `serialOffset`, `macPrefix`, `serialShortVariableName`, `macVariableName` |
| UDP | `targetIp`, `targetPort`, `repeatCount`, `delayBetweenRepeatsMs`, `failOnSendError` |
| DataTest | `mode`, `expectedPackets`, `packetSizeBytes`, `udpPort`, `maxPortTestTimeMs`, `targetBandwidthMbps`, `durationMs`, `warmupMs`, `interPairDelayMs`, `allowedLossPercent`, `allowedTxDeficitPercent`, `bidirectional`, `portsText`, `ports` |
| Print Label | `printerName`, `deviceName`, `copies`, `includeMac`, `equipmentFieldUse`, `equipmentType`, `equipmentText`, `failOnPrinterError` |
| Report | `reportVariableName`, `endpoint`, `retryCount`, `retryDelayMs`, `saveLocalCopy`, `localReportsDirectory`, `includeAllVariables` |
| For Slaves | `fromSlaveId`, `toSlaveId`, `step`, `stopOnError`, `body` |
| Wait Variable | `pollAction`, `baseUrl`, `endpoint`, `responseType`, `requestTimeoutMs`, `timeoutMs`, `intervalMs`, `failOnTimeout` |
| Clear ARP | `runArpdBat`, `arpdBatPath`, `command`, `arguments` |

Для `Clear ARP Cache` дефолтный `arguments` - `-d *`. Старые профили с
`arguments: "-d"` при выполнении нормализуются в `-d *`, если `command` равен
`arp`.

В `PSW_UPS_Box_8x2Pro_full_algorithm_polling.json` `Run Data Test` использует
пять постоянных пар: `.2/.3`, `.4/.5`, `.6/.7`, `.8/.9`, `.10/.11` в сети
`192.168.0.0/24`. Адреса назначаются вручную в Windows и профилем не изменяются.
Target рабочего профиля — `100 Mbps`, `bidirectional = true`. При загрузке и
сохранении `targetBandwidthMbps`, `ports[].bandwidthMbps` и четвертое поле каждой
строки `portsText` ограничиваются диапазоном `1..100`; это автоматически мигрирует
ошибочные legacy-значения `1000` на `100`. Для старых профилей без новых полей
используются `allowedTxDeficitPercent = 2.0` и `bidirectional = true`.

## Вложенные графы

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
| `Print Label` | `copies` | `4` |
| `For Slaves` | `fromSlaveId`, `toSlaveId`, `step` | `1`, `20`, `1` |
| `Send Test Report` | `endpoint` | `/api/Api.svc/result.json` |
| `Subtest` | `runOnFailure` | `false` |
| `Check Register Range` / `Check Register Equality` / `Wait Until` / `Poll Register` | `liveRead` | `false` |
| Любая нода | `color` | `blue` |

Полный список дефолтов описан в [[05 - Справочник нод]].

Старые `Wait Variable Until` без `endpoint`/`responseType` сохраняют поведение:
`GetUpsStatus`, `GetUpsVoltage` и `GetIrpStatus` автоматически получают прежние
endpoint и тип ответа. Специализированные типы `Get UPS Status`,
`Get UPS Voltage`, `Get IRP Status` также десериализуются, но новые графы должны
использовать `Read HTTP Variable` и `Wait Variable Until` + `HttpGet`.

## Совместимость и риски

- JSON хранит связи по тексту коннектора. Нельзя без миграции менять `Title` коннекторов.
- В старых/битых профилях могут встречаться строки коннекторов с mojibake или `????`; такие связи deserializer пропустит, если не найдет коннектор.
- `FindConnector` содержит специальную совместимость для `For Slaves`: `Success` сопоставляется с `True`, `Error` - с `False`.
- `ExpectedValue` и `DeviceType` хранятся как `object?`, поэтому deserializer содержит преобразования из `JsonElement`, строк и чисел.
- В `For Slaves` старые коннекторы `Success`/`Error` при загрузке сопоставляются с текущими `True`/`False`.
- `Subtest` при загрузке принимает и `bodyGraph`, и старое поле `body`; при сохранении использует `bodyGraph`.
- Старые профили без `runOnFailure` и `liveRead` загружаются как раньше: оба флага получают `false`.
