---
tags:
  - testbuilder
  - modbus
  - monitoring
updated: 2026-10-19
---

# Modbus и мониторинг

## Обновление 2026-10-14: конфигурируемые RTU-устройства

Режим без `hardware` использует сканирование известных типов и скорость 9600 8N1.
С 2026-10-19 он также распознаёт RTL-SW/PoE по встроенным картам (типы 104/105).
При наличии `hardware` используются его параметры и только перечисленные slave;
`SlaveManager.ConfigureAsync` проверяет необязательную идентификацию и начальный опрос,
затем атомарно публикует ConfiguredSlaveModel. Типа устройства в C# добавлять не нужно.
COM стенда берётся из локальных настроек; пустое поле оставляет перебор портов.

2026-10-19: в поставляемых RTL-SW/PoE пауза после фонового чтения — 1000 мс,
как в `SwStandMonitorLoop`/`PoeStandMonitorLoop` RTL_v2, вместо прежних 500 мс.
SW читается одним блоком 2300..2385, PoE — 2400..2511. Ожидания К5/VCC
проверяют кэш; их интервалы не создают отдельные запросы на шину. Вкладка
Modbus при управляемом мониторинге не запускает второй цикл. Факт перегрузки
физической шины без аппаратной трассы не установлен.

`ReadAreaAsync`: Holding/Input/Coil/Discrete (FC3/4/1/2). `WriteAreaAsync`: Holding/Coil
(FC6/5, Coil только 0/1). Адреса — смещения протокола с нуля. Значения — uint16 или биты.
ConfiguredSlaveModel группирует соседние адреса внутри области для фонового обмена;
отдельной ноды группового чтения нет. Пропуски адресов не включаются в запрос.

RegisterState индексируется (slave, area, address), хранит время начала опроса.
После команды записи или консоли граф ждёт опроса, начатого после воздействия.
Неполный/ошибочный ответ удаляет значения slave. Для новых карт значение старше
max(10 с, 3 периода опроса) недостоверно. Wait Until учитывает свой таймаут и при
ожидании свежего значения. StopAsync отменяет и дожидается опроса до отключения порта.

Вкладка Modbus переиспользует действующий монитор и карту, не сканирует АПК-03 поверх
профиля платы. Ручные записи заблокированы во время прогона/подключения.
Готовый профиль можно выбрать при подключённом стенде: прежний монитор
останавливается и порт закрывается перед загрузкой, затем выполняется подключение
по карте выбранного профиля. Во время переключения и теста выбор заблокирован.
Редактирование карты и создание пустого профиля требуют отключения.


Modbus-слой отвечает за связь с тестовым стендом, сканирование slave-устройств, чтение/запись Holding Registers и обновление `RegisterState`, который используют регистровые ноды.

## Главные классы

| Класс | Назначение |
|---|---|
| `IModbusService` | Абстракция чтения/записи регистров, проверки порта и подписки на регистры. |
| `ModbusService` | Реальная реализация через `SerialPort` + `NModbus4`. |
| `SlaveManager` | Сканирует устройства и создает модели slave. |
| `SlaveRegistry` | Singleton для UI-ноды: список доступных slave и флаг подключения. |
| `RegisterMonitor` | Фоновый мониторинг всех найденных slave. |
| `RegisterState` | Потокобезопасное хранилище последних значений регистров. |
| `ModbusMonitoringViewModel` | Вкладка `Modbus`: scan, poll, таблицы регистров. |

## Подключение

`TestViewModel.ConnectAsync()`:

1. Ставит статус `Поиск COM-портов...`.
2. Если в настройках задан COM-порт, проверяет его; иначе перебирает доступные
   порты из `SerialPort.GetPortNames()`.
3. Для каждого кандидата вызывает:

```csharp
_modbusService.ConnectAsync(port, 9600, Parity.None, 8, StopBits.One)
```

При наличии `hardware` параметры линии берутся из его карты вместо значений выше.

4. Для старых профилей проверяет порт через `_modbusService.CheckPortAsync()`.
   Для профиля с `hardware` выполняет идентификацию заданного slave и первый
   опрос его регистров по карте профиля.
5. Если порт не открылся, тип slave не совпал, сканирование не нашло устройств
   или опрос завершился ошибкой, отключается и пробует следующий порт.
6. После успешного порта:
   - сохраняет `SelectedPort`;
   - ставит `IsConnected = true`;
   - запускает `StartMonitoringAsync()`;
   - синхронизирует `SlaveRegistry`;
   - пишет статус и лог.

## Проверка порта

При исключении в попытке подключения/сканирования `TestViewModel` сохраняет
полный стек в UI-журнал и диагностический файл, останавливает монитор,
отключает порт и сбрасывает `IsConnected`, `IsMonitoringActive` и состояние
`SlaveRegistry`, прежде чем пробовать следующий порт. Раньше исключение
терялось, а после ошибки сканирования UI мог оставаться «подключённым».
Путь диагностического файла описан в [[10 - Настройки логирование тесты]].

`ModbusService.CheckPortAsync()` для старых профилей делает пробное чтение:

```csharp
ReadRegistersAsync(1, 0, 1)
```

Успех - если вернулся массив длиной `1`.

Для профилей RTL-SW/RTL-PoE карта оборудования задаёт идентификацию на slave 1,
Holding-регистр 0 и ожидаемые значения 104/105 соответственно. RTL_v2 выполняет
такой же поиск по доступным COM-портам, затем проверяет регистр серийного номера
стенда (SW: 2300, PoE: 2400). В TestBuilder первый опрос карты включает этот
регистр. Открытый COM-порт без найденных устройств больше не оставляет
`IsConnected=true`: раньше это также блокировало выбор профиля после неудачного
подключения. Подробности попыток открытия порта остаются в журнале.

## Сканирование slave

`SlaveManager.ScanAsync()` проходит slave ID:

```text
1, 3, 5, ..., 35
```

Для каждого читает регистр `0`. Значение регистра выбирает модель:

| Значение регистра 0 | Модель | DeviceType |
|---:|---|---|
| `1` | `El60Model` | `EL-60` |
| `2` | `PS1Model` | `PS-1` |
| `3` | `PS2Model` | `PS-2` |
| `4` | `El60v5Model` | `EL-60v5` |
| `5` | `IO2Model` | `IO-2` |
| `6` | `StandRpsModel` | `Stand Rps` |
| `7` | `StandPwr180Model` | `PWR-Tester` |
| `8` | `Ps3Model` | `PS-3` |
| `9` | `Simbat24Model` | `SIMBAT` |
| `10` | `Simbat48Model` | `SIMBAT` |
| `104` | `ConfiguredSlaveModel` | `RTL-SW` |
| `105` | `ConfiguredSlaveModel` | `RTL-POE` |

Для типов вне прежних моделей 1..10 сканер ищет совпадение в `HardwarePresets`
по `IdentificationAddress=0` и `IdentificationValue`. Это тот же ресурс
`Assets/hardware-presets.json`, который использует редактор оборудования.
Перед добавлением в список сканер проверяет полное чтение карты; неполный
ответ отклоняется. Типы без модели и без шаблона не попадают в список устройств.

Проверка исходного протокола RTL_v2: `SerialPortStandDiscoveryService.cs`,
`StandModbusAttachService.cs`, `SwRegisterMap.cs` и `docs/hardware/modbus.md`
в репозитории https://github.com/kotyasmol/RTL_v2 (проверено 2026-10-19).

## ModbusService

`ModbusService` хранит:

- `_serialPort`;
- `_master`;
- `_connectionSettings`;
- `_ioLock`;
- `_lastRequestTimeUtc`;
- `_watchers`;
- `IsConnected`;
- `LastError`.

### Последовательный доступ к шине

Все операции чтения/записи проходят через `_ioLock`. Это важно, потому что один COM-порт не должен получать параллельные запросы.

Между запросами выдерживается `_minRequestGap = 150 ms`.

### Чтение

```csharp
ReadRegistersAsync(byte slaveId, ushort address, ushort count)
```

Выполняет `ReadHoldingRegistersAsync`, затем вызывает `NotifyWatchers`.

### Запись

```csharp
WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true)
```

Выполняет `WriteSingleRegisterAsync`. Если `verify = true`, ждет bus gap, читает регистр обратно и сравнивает значение.

Важно: `ModbusWriteStep` передает `verify: false`, потому что у него есть собственная логика read-back verification с тремя попытками.

## Auto reconnect

`ExecuteWithRecoveryAsync` ловит критические ошибки:

- `IOException`;
- `UnauthorizedAccessException`;
- `InvalidOperationException`;
- `ObjectDisposedException`.

При критической ошибке:

1. `IsConnected = false`.
2. Посылается `ReconnectStatusChanged`.
3. Текущее соединение освобождается.
4. До 60 секунд раз в 1 секунду проверяется наличие прежнего COM-порта.
5. При появлении порта соединение открывается заново.
6. Операция повторяется.
7. Если восстановиться не удалось, бросается `IOException`.

`TestViewModel.OnReconnectStatusChanged()` показывает `ModbusReconnectDialog`.

## RegisterMonitor

`RegisterMonitor` запускается после успешного сканирования.

Цикл:

1. Берет snapshot slave-устройств.
2. Последовательно вызывает `slave.PollAsync()`.
3. Для каждого `RegisterItem` обновляет `RegisterState`.
4. Ждет `PollInterval`, по умолчанию 1000 мс.

После 5 последовательных ошибок:

- пишет ошибку;
- вызывает `ConnectionLost`;
- останавливает мониторинг.

`TestViewModel.OnConnectionLost()` пишет предупреждение и говорит пользователю, что восстановление произойдет при следующей Modbus-операции.

При отключении через UI `DisconnectAsync()` останавливает мониторинг, закрывает Modbus-соединение, сбрасывает `IsConnected`/`IsMonitoringActive` и сообщает `SlaveRegistry`, что стенд больше недоступен.

## RegisterState

`RegisterState` - тонкая обертка над:

```csharp
ConcurrentDictionary<RegisterKey, int>
```

Ключ:

```csharp
public readonly record struct RegisterKey(byte SlaveId, int Address);
```

API:

| Метод | Назначение |
|---|---|
| `Update(byte slaveId, int address, int value)` | Обновить последнее значение. |
| `TryGet(byte slaveId, int address, out int value)` | Прочитать последнее значение. |

## Важное следствие для нод

Эти ноды по умолчанию работают по `RegisterState`; с `LiveRead=true`
они выполняют прямое Modbus-чтение:

- `Check Register Range`;
- `Wait Until`.

Для чтения через `RegisterState` должны выполняться условия:

- приложение подключено к стенду;
- slave найден сканером;
- `RegisterMonitor` успел опросить нужный регистр;
- адрес присутствует в `RegisterItems` модели или был обновлен другим шагом.

`Write Register` отличается: он пишет напрямую через `IModbusService`.

## SlaveRegistry

`SlaveRegistry` нужен для UI-ноды, чтобы при подключении показывать:

- список slave в ComboBox;
- список регистров выбранного slave.

`NodeViewModel` подписан на `SlaveRegistry.PropertyChanged`. При изменении `IsConnected` вызывает `SyncSlaves()` и дает дочерним нодам восстановить выбранные `SelectedSlave`/`SelectedRegister`.

## Вкладка Modbus

`ModbusMonitoringViewModel` также умеет запускать свой polling loop для вкладки `Modbus`. Он каждую секунду вызывает `PollAsync()` у всех slave.

Это отдельный UI-механизм просмотра регистров. Основной runtime-граф при запуске использует `RegisterMonitor`, созданный в `TestViewModel`.

## Практические рекомендации

- Перед запуском графа дождаться статуса `Найдено устройств: N`.
- Для регистровых проверок сначала убедиться, что нужный slave виден на вкладке `Modbus`.
- Для сценариев внутри `For Slaves` включать `UseCurrentSlaveId`.
- Для свежего значения включать `LiveRead` у существующих register-check нод;
  `Check Register Range` также поддерживает `ReadAttempts` и `ReadIntervalMs`.
