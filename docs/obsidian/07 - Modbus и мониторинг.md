---
tags:
  - testbuilder
  - modbus
  - monitoring
updated: 2026-06-30
---

# Modbus и мониторинг

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
2. Берет `SerialPort.GetPortNames().OrderBy(p => p)`.
3. Для каждого порта вызывает:

```csharp
_modbusService.ConnectAsync(port, 9600, Parity.None, 8, StopBits.One)
```

4. Проверяет порт через `_modbusService.CheckPortAsync()`.
5. Если проверка не прошла, отключается и пробует следующий порт.
6. После успешного порта:
   - сохраняет `SelectedPort`;
   - ставит `IsConnected = true`;
   - запускает `StartMonitoringAsync()`;
   - синхронизирует `SlaveRegistry`;
   - пишет статус и лог.

## Проверка порта

`ModbusService.CheckPortAsync()` делает пробное чтение:

```csharp
ReadRegistersAsync(1, 0, 1)
```

Успех - если вернулся массив длиной `1`.

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

Неизвестные типы логируются в консоль и не попадают в список устройств.

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

Эти ноды работают по `RegisterState`, а не по прямому Modbus-чтению:

- `Check Register Range`;
- `Check Register Equality`;
- `Wait Until`;
- `Poll Register`.

Поэтому для них должны выполняться условия:

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
- Если требуется гарантированно свежее значение регистра, лучше добавить отдельную ноду прямого чтения или доработать существующие register-check ноды.
