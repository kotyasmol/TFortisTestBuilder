---
tags:
  - testbuilder
  - json
  - serialization
---

# JSON профили

Профили тестирования сохраняются в JSON через `GraphSerializer`. Формат ориентирован на человекочитаемое хранение графа: список нод, координаты, параметры и связи.

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
| `Build MAC From Serial` | `BuildMacFromSerialNodeViewModel` |
| `Compare Variables` | `CompareVariablesNodeViewModel` |
| `Wait Variable Until` | `WaitVariableUntilNodeViewModel` |
| `Build Test Report` | `BuildTestReportNodeViewModel` |
| `Print Label` | `PrintLabelNodeViewModel` |
| `Send Test Report` | `SendTestReportNodeViewModel` |
| `Subtest` | `SubtestNodeViewModel` |
| `For Slaves` | `ForEachSlaveNodeViewModel` |

Deserializer также принимает часть русских и legacy-имен, например `Старт`, `Конец`, `WriteRegister`, `SELFTEST_CHECK`, `GET_UPS_STATUS`.

## Поля параметров

`NodeDto` содержит объединенную схему всех нод. Поля записываются только когда нужны конкретному типу.

| Группа | Поля |
|---|---|
| Delay | `milliseconds` |
| Label | `text`, `labelWidth`, `labelHeight` |
| Subtest | `name`, `description`, `isEnabled`, `stopOnError`, `bodyGraph` |
| Modbus | `slaveId`, `useCurrentSlaveId`, `address`, `value`, `verifyWrite`, `min`, `max`, `expectedValue`, `durationMs`, `sampleCount` |
| Selftest/HTTP | `url`, `timeoutMs`, `outputPrefix`, `validationRules`, `baseUrl`, `outputVariableName`, `failOnError` |
| Variables | `variableName`, `leftVariableName`, `rightVariableName`, `comparisonType`, `failMessage`, `inclusive` |
| Serial/MAC | `serverBaseUrl`, `deviceType`, `cpuIdVariableName`, `serialVariableName`, `serialOffset`, `macPrefix`, `serialShortVariableName`, `macVariableName` |
| UDP | `targetIp`, `targetPort`, `repeatCount`, `delayBetweenRepeatsMs`, `failOnSendError` |
| DataTest | `mode`, `expectedPackets`, `packetSizeBytes`, `udpPort`, `maxPortTestTimeMs`, `targetBandwidthMbps`, `durationMs`, `warmupMs`, `allowedLossPercent`, `portsText`, `ports` |
| Print Label | `printerName`, `deviceName`, `copies`, `includeMac`, `equipmentFieldUse`, `equipmentType`, `equipmentText`, `failOnPrinterError` |
| Report | `reportVariableName`, `endpoint`, `retryCount`, `retryDelayMs`, `saveLocalCopy`, `localReportsDirectory`, `includeAllVariables` |
| For Slaves | `fromSlaveId`, `toSlaveId`, `step`, `stopOnError`, `body` |
| Wait Variable | `pollAction`, `requestTimeoutMs`, `intervalMs`, `failOnTimeout` |
| Clear ARP | `runArpdBat`, `arpdBatPath`, `command`, `arguments` |

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
| `Get UPS Status` | `baseUrl` | `http://192.168.0.1` |
| `Build MAC From Serial` | `serialOffset` | `3200000` |
| `Build MAC From Serial` | `macPrefix` | `C0:11:A6:20` |
| `Print Label` | `copies` | `4` |
| `For Slaves` | `fromSlaveId`, `toSlaveId`, `step` | `1`, `20`, `1` |
| `Send Test Report` | `endpoint` | `/api/Api.svc/result.json` |

Полный список дефолтов описан в [[05 - Справочник нод]].

## Совместимость и риски

- JSON хранит связи по тексту коннектора. Нельзя без миграции менять `Title` коннекторов.
- В старых/битых профилях могут встречаться строки коннекторов с mojibake или `????`; такие связи deserializer пропустит, если не найдет коннектор.
- `FindConnector` содержит специальную совместимость для `For Slaves`: `Success` сопоставляется с `True`, `Error` - с `False`.
- `ExpectedValue` и `DeviceType` хранятся как `object?`, поэтому deserializer содержит преобразования из `JsonElement`, строк и чисел.

