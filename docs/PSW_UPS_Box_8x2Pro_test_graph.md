# PSW+UPS-Box 8x2Pro: актуальный граф тестирования по старому профилю

Источник профиля: `PSW+UPS-Box 8x2Pro (4).json`.

Источник старой логики: `kotyasmol/QTstand_old`, основной порядок в `TestThread::process()`, проверки в `SelfTestStage`, `HeaterTestStage`, `PoeTest`, `UpsTestPS2`, `SetMac`, `PrintLabelStage`.

## 0. Что важно перед сборкой графа

В профиле включены:

- `firmware_chek = 1`, требуемая версия `1.1.0`.
- `buildin_test = 1`.
- `poe_test = 1`.
- `poe_line_test[0..15] = 1`.
- `poe_line_power[0..15] = 15000`.
- `poe_line_min[0..15] = 53`, `poe_line_max[0..15] = 56`.
- `data_test = 1`.
- `data_test_ports[0..9] = 1`, остальные выключены.
- `send_mac = 1`.
- `test_ups = 1`.
- `test_heating = 1`, ток `150..1000`.
- `test_heating2 = 1`, ток `150..1000`.
- `print_label = 1`, `label_num = 4`.
- `start_delay = 160` секунд.
- `use_ac1 = 1`, `use_ac2 = 0`.
- `charge_CC_test = 0`, `charge_CV_test = 0`, поэтому CC/CV зарядные тесты не выполняются.

Плейсхолдеры, которые нужно заменить при сборке:

- `PS_SLAVE_ID` - реальный slave ID блока питания/PS-2/PS-3 в GUI.
- `SIMBAT_SLAVE_ID` - реальный slave ID SIMBAT24. По старой логике выбран SIMBAT24, потому что `charge_CV_voltage_min = 23000 < 40000`.
- `EL60_FIRST`, `EL60_LAST`, `EL60_STEP` - диапазон slave ID нагрузок EL60v5. Если они не идут ровным шагом, цикл `For` использовать нельзя, узлы надо развернуть вручную.
- `SERVER_BASE_URL` - адрес старого API выдачи серийника и приема отчета. В JSON его нет.
- `PRINTER_NAME` - имя принтера этикеток. В JSON его нет.
- `DUT_BASE_URL = http://192.168.0.1`.
- `DUT_SELFTEST_URL = http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin`.
- `SET_MAC_UDP_PORT = 43962`, если в настройках стенда не задан другой порт.

Критическое ограничение текущих нод:

- `Проверка диапазона`, `Проверка равенства`, `Ожидание значения` и `Опрос регистра` читают не Modbus напрямую, а текущее состояние `RegisterState`.
- Значит, во время выполнения графа должен работать мониторинг Modbus, и нужные slave должны быть обнаружены в GUI.
- Если мониторинг не работает или slave не найден, эти ноды будут проверять пустое/устаревшее значение.

## 1. Каких нод не хватает для полного повторения старого теста

1. Нода `Set Variable / Expression`.

   Нужна для вычисления:

   - `SerialShort = SerialNumber - 3200000`, потому что `model_num_mac_print = 32`, а старая формула была `serial - 100000 * model_num_mac_print`.
   - `Dut.NewMac = C0:11:A6:20:XX:XX`, где `XX:XX` - старшие/младшие байты `SerialShort`.
   - `TestReportJson` перед отправкой отчета.

   Без нее вместо этих шагов ставить `Метка`.

2. Нода `Build MAC from Serial`.

   Можно заменить общей `Set Variable / Expression`, но лучше отдельная нода:

   - вход: `SerialNumber`, `model_num_mac_print = 32`;
   - выходы: `SerialShort`, `Dut.NewMac`;
   - формат MAC: `C0:11:A6:20:XX:XX`.

3. Нода `Variable Compare Variable`.

   Текущая `Check Variable Equality` сравнивает переменную только с литералом. Для старого теста нужно сравнить:

   - `Dut.default_mac` с `Dut.NewMac`;
   - иногда результат HTTP/Modbus с вычисленным ожиданием.

4. Нода `Firmware Version >=`.

   Старый тест проверял `firmvare_vers >= 1.1.0`, а не строгое равенство. Текущая нода умеет сравнение с литералом, но для версии надежнее отдельная проверка `>=`.

5. Нода `Wait/Retry HTTP Selftest`.

   Старый тест до `start_delay = 160` секунд перечитывал страницу устройства, пока устройство не поднялось и selftest не стал валидным. Сейчас `Selftest Check` делает один HTTP-запрос. Нужно либо встроить retry, либо добавить условный цикл.

6. Нода `Wait Variable Until`.

   Нужна для ожиданий:

   - `Dut.ups_rez = 1` после выключения AC, максимум 160 секунд;
   - `Dut.ups_rez = 0` после включения AC, максимум 160 секунд;
   - `Dut.akb_det = 1`, максимум 30 секунд.

   Текущая `Ожидание значения` работает только по регистру, не по переменной.

7. Нода `Loop Until / While`.

   Текущий `For` умеет только цикл по slave ID. Он подходит для повторяющихся EL60v5, но не подходит для HTTP retry, ожидания UPS-статуса и цикла по портам данных.

8. Нода `Real Data Test`.

   Текущая `Run Data Test` в коде является заглушкой и для `SoftwarePcap` возвращает ошибку интеграции. Для полного теста нужна реальная реализация старого `data_test_thread`:

   - проверка портов `0..9`;
   - скорость `1000`;
   - пары `0-1`, `2-3`, `4-5`, `6-7`, `8-9`;
   - режим SoftwarePcap при `switch_state = 0`;
   - режим hard/chain/telnet, если в настройках стенда включен switch state.

9. Нода `Build Test Report`.

   Текущая `Send Test Report` отправляет уже готовую переменную `TestReportJson`, но ее никто не собирает. В старом тесте отчет собирался автоматически из всех ошибок, серийника, MAC, статистики и имени теста.

10. Нода `Get IRP Status`.

   В `UpsTestPS2()` старая логика при некорректном `ups_det` делала `GetIrpStatus()`. В текущем списке есть `Get UPS Status` и `Get UPS Voltage`, но нет прямого аналога `GetIrpStatus`.

11. Нода `Device Presence / Stand Type Check`.

   В старом тесте проверялось, что подключены PS2/PS3 и SIMBAT. Сейчас это нужно либо делать через регистры вручную, либо отдельной нодой.

12. Нода `Direct Modbus Read`.

   Нужна, если не полагаться на фоновый мониторинг. Сейчас `Poll Register` не читает Modbus сам, а только берет последнее значение из `RegisterState`.

13. Нода `Switch Telnet Config`.

   Старый тест умел переводить управляемый switch в нормальную конфигурацию перед тестом. В профиле этого параметра нет, но если в настройках стенда `switch_state = 1`, без этой ноды полный тест не повторить.

## 2. Полный граф

Ниже шаги в формате для ручной сборки. Если указан `Метка`, это место, где текущих нод не хватает или нужно вручную зафиксировать недостающую логику.

## Подтест 1 "Инициализация стенда и профиля"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Инициализация стенда и профиля`
- `Description`: `PSW+UPS-Box 8x2Pro, профиль 0.1.0`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Метка`.

В GUI:

- `Text`: `Профиль: PSW+UPS-Box 8x2Pro; start_delay=160s; AC1=true; AC2=false; PoE lines 0..15; data ports 0..9; UPS=true; heaters=true; print labels=4`

Шаг 3: добавить ноду `Clear ARP Cache`.

В GUI:

- `RunArpdBat`: `true`
- `ArpdBatPath`: `arpd.bat`
- `Command`: `arp`
- `Arguments`: `-d`
- `TimeoutMs`: `5000`
- `FailOnError`: `false`

Шаг 4: добавить ноду `Метка`.

В GUI:

- `Text`: `Если EL60v5 нагрузки еще не настроены, перейти к подтесту настройки PoE-нагрузок. Если уже настроены и включены, этот подтест все равно безопасно повторить.`

## Подтест 2 "Настройка PoE-нагрузок EL60v5"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Настройка PoE-нагрузок EL60v5`
- `Description`: `Установить мощность 15000 и включить обе линии A/B для всех 16 PoE-линий`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: обернуть в цикл `Цикл For`, если EL60v5 идут ровным диапазоном slave ID.

В GUI цикла:

- `FromSlaveId`: `EL60_FIRST`
- `ToSlaveId`: `EL60_LAST`
- `Step`: `EL60_STEP`
- `StopOnError`: `true`

Внутри цикла использовать `UseCurrentSlaveId = true`.

Шаг 3: внутри цикла добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: любое значение, потому что используется текущий slave
- `UseCurrentSlaveId`: `true`
- `Address`: `1412`
- `Value`: `15000`
- `VerifyWrite`: `true`

Назначение: `EL60v5 LoadSetA = 15000`.

Шаг 4: внутри цикла добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `100`

Шаг 5: внутри цикла добавить ноду `Запись регистра`.

В GUI:

- `UseCurrentSlaveId`: `true`
- `Address`: `1413`
- `Value`: `15000`
- `VerifyWrite`: `true`

Назначение: `EL60v5 LoadSetB = 15000`.

Шаг 6: внутри цикла добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `100`

Шаг 7: внутри цикла добавить ноду `Запись регистра`.

В GUI:

- `UseCurrentSlaveId`: `true`
- `Address`: `1414`
- `Value`: `1`
- `VerifyWrite`: `true`

Назначение: `EL60v5 LoadEnableA = 1`.

Шаг 8: внутри цикла добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `100`

Шаг 9: внутри цикла добавить ноду `Запись регистра`.

В GUI:

- `UseCurrentSlaveId`: `true`
- `Address`: `1415`
- `Value`: `1`
- `VerifyWrite`: `true`

Назначение: `EL60v5 LoadEnableB = 1`.

Шаг 10: внутри цикла добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `400`

Шаг 11: если EL60v5 не идут ровным диапазоном slave ID, вместо шага 2 добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ ГИБКОГО ЦИКЛА ПО СПИСКУ SLAVE ID. Развернуть вручную для каждого EL60v5: записать 1412=15000, 1413=15000, 1414=1, 1415=1, задержки 100/100/100/400 мс.`

## Подтест 3 "Подключение АКБ/разряда и включение AC1"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Подключение АКБ/разряда и включение AC1`
- `Description`: `Начальное состояние для PSW+UPS-Box 8x2Pro`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1210`
- `Value`: `1`
- `VerifyWrite`: `true`

Назначение: `set_stand_discharge_key(1)`, ключ разряда/АКБ включен.

Шаг 3: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `300`

Шаг 4: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `500`

Назначение: старая `Pause(500)` перед включением питания.

Шаг 5: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1200`
- `Value`: `1`
- `VerifyWrite`: `true`

Назначение: `AC1 ON`.

Шаг 6: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `200`

Шаг 7: добавить ноду `Метка`.

В GUI:

- `Text`: `AC2 по профилю выключен: use_ac2=0, поэтому регистр AC2 не писать.`

## Подтест 4 "Ожидание загрузки устройства"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Ожидание загрузки устройства`
- `Description`: `Старая waitingStartDevice(start_delay=160s), затем пауза 2s`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Wait/Retry HTTP Selftest. Нужно до 160 секунд перечитывать DUT_SELFTEST_URL, пока устройство отвечает и selftest XML парсится. Текущий Selftest Check делает один запрос.`

Шаг 3: добавить ноду `Selftest Check`.

В GUI:

- `Url`: `http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin`
- `TimeoutMs`: `160000`
- `OutputPrefix`: `Dut`
- `ValidationRules`: пусто или `init_ok=0..1`
- `FailOnError`: `true`

Шаг 4: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `2000`

## Подтест 5 "Selftest устройства"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Selftest устройства`
- `Description`: `Проверки test.shtml/deviceinfo из QTstand_old`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Selftest Check`.

В GUI:

- `Url`: `http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin`
- `TimeoutMs`: `30000`
- `OutputPrefix`: `Dut`
- `ValidationRules`: `init_ok=1..1`
- `FailOnError`: `true`

Шаг 3: добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.init_ok`
- `ExpectedValue`: `1`
- `ComparisonType`: `Number`
- `FailMessage`: `Selftest init_ok должен быть 1`

Шаг 4: добавить ноду `Метка`.

В GUI:

- `Text`: `Тип устройства в старом тесте допускает model_num=0 или model_num_mac_print=32. Текущая Check Variable Equality не умеет OR, поэтому собрать ветвление: Dut.dev_type==0 ИЛИ Dut.dev_type==32.`

Шаг 5: добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.dev_type`
- `ExpectedValue`: `0`
- `ComparisonType`: `Number`
- `FailMessage`: `Если Dut.dev_type не 0, проверить альтернативу 32`

Шаг 6: добавить альтернативную ноду `Check Variable Equality` на ветке ошибки шага 5.

В GUI:

- `VariableName`: `Dut.dev_type`
- `ExpectedValue`: `32`
- `ComparisonType`: `Number`
- `FailMessage`: `Dut.dev_type должен быть 0 или 32`

Шаг 7: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Firmware Version >=. Нужно проверить Dut.firmvare_vers >= 1.1.0. Если текущая переменная хранится строкой, временно можно поставить строгое сравнение с 1.1.0, но это уже не полное соответствие старому тесту.`

Шаг 8: временно добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.firmvare_vers`
- `ExpectedValue`: `1.1.0`
- `ComparisonType`: `Version`
- `FailMessage`: `Версия прошивки должна быть не ниже 1.1.0`

Шаг 9: добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.ups_det`
- `ExpectedValue`: `1`
- `ComparisonType`: `Number`
- `FailMessage`: `UPS/АКБ должны быть обнаружены`

Шаг 10: добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.ups_rez`
- `ExpectedValue`: `0`
- `ComparisonType`: `Number`
- `FailMessage`: `До UPS-теста устройство должно быть от сети: ups_rez=0`

Шаг 11: добавить ноду `Check Variable Range`.

В GUI:

- `VariableName`: `Dut.akb_voltage`
- `Min`: `12`
- `Max`: `27`
- `Inclusive`: `true`
- `FailMessage`: `Напряжение АКБ должно быть в диапазоне 12..27 В`

Шаг 12: добавить ноду `Метка`.

В GUI:

- `Text`: `В старом Selftest также проверялись внутренние напряжения ADC 2.5/1.0/1.8/1.2/1.5, PoE detect по парам и SFP. Для полного соответствия нужны точные имена полей из XML и правила ValidationRules по каждому полю.`

## Подтест 6 "Получение серийного номера"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Получение серийного номера`
- `Description`: `Нужно для MAC, этикетки и отчета`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Get Serial Number`.

В GUI:

- `ServerBaseUrl`: `SERVER_BASE_URL`
- `DeviceType`: `PSW+UPS-Box 8x2Pro`
- `CpuIdVariableName`: `Dut.cpu_id`
- `TimeoutMs`: `30000`
- `RetryCount`: `1`
- `RetryDelayMs`: `1000`
- `OutputVariableName`: `SerialNumber`
- `FailOnError`: `true`

Шаг 3: добавить ноду `Check Variable Range`.

В GUI:

- `VariableName`: `SerialNumber`
- `Min`: `3200000`
- `Max`: `3299999`
- `Inclusive`: `true`
- `FailMessage`: `Серийный номер для model_num_mac_print=32 должен попадать в диапазон 3200000..3299999`

## Подтест 7 "Проверка нагревателя 1"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Проверка нагревателя 1`
- `Description`: `test_heating=1, ток 150..1000`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Опрос регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1216`
- `Min`: `0`
- `Max`: `50`
- `SampleCount`: `3`

Назначение: ток нагревателя 1 до включения должен быть `0..50`.

Шаг 3: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1215`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: реле нагревателя 1 выключено.

Шаг 4: добавить ноду `Ожидание значения`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1215`
- `ExpectedValue`: `0`
- `TimeoutMs`: `3000`

Шаг 5: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1215`
- `Value`: `1`
- `VerifyWrite`: `true`

Назначение: реле нагревателя 1 включено.

Шаг 6: добавить ноду `Ожидание значения`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1215`
- `ExpectedValue`: `1`
- `TimeoutMs`: `3000`

Шаг 7: добавить ноду `Опрос регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1216`
- `Min`: `150`
- `Max`: `1000`
- `SampleCount`: `3`

Назначение: ток нагревателя 1 после включения должен быть `150..1000`.

Шаг 8: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1215`
- `Value`: `0`
- `VerifyWrite`: `true`

Шаг 9: добавить ноду `Ожидание значения`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1215`
- `ExpectedValue`: `0`
- `TimeoutMs`: `3000`

## Подтест 8 "Проверка нагревателя 2"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Проверка нагревателя 2`
- `Description`: `test_heating2=1, ток 150..1000`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Опрос регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1221`
- `Min`: `0`
- `Max`: `50`
- `SampleCount`: `3`

Назначение: ток нагревателя 2 до включения должен быть `0..50`.

Шаг 3: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1220`
- `Value`: `1`
- `VerifyWrite`: `true`

Шаг 4: добавить ноду `Ожидание значения`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1220`
- `ExpectedValue`: `1`
- `TimeoutMs`: `3000`

Шаг 5: добавить ноду `Опрос регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1221`
- `Min`: `150`
- `Max`: `1000`
- `SampleCount`: `3`

Назначение: ток нагревателя 2 после включения должен быть `150..1000`.

Шаг 6: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1220`
- `Value`: `0`
- `VerifyWrite`: `true`

Шаг 7: добавить ноду `Ожидание значения`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1220`
- `ExpectedValue`: `0`
- `TimeoutMs`: `3000`

## Подтест 9 "PoE линии 0..15"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `PoE линии 0..15`
- `Description`: `poe_line_test[0..15]=1, напряжение 53..56 В, мощность нагрузки 15000`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: обернуть в цикл `Цикл For` по EL60v5 slave ID.

В GUI:

- `FromSlaveId`: `EL60_FIRST`
- `ToSlaveId`: `EL60_LAST`
- `Step`: `EL60_STEP`
- `StopOnError`: `true`

Внутри цикла один slave EL60v5 покрывает две линии: канал B и канал A.

Шаг 3: внутри цикла добавить ноду `Опрос регистра`.

В GUI:

- `UseCurrentSlaveId`: `true`
- `Address`: `1403`
- `Min`: `53000`
- `Max`: `56000`
- `SampleCount`: `3`

Назначение: напряжение канала B. В старом коде четные линии проверялись через `MB_EL60V5_VOLTAGE_B`.

Шаг 4: внутри цикла добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `200`

Шаг 5: внутри цикла добавить ноду `Опрос регистра`.

В GUI:

- `UseCurrentSlaveId`: `true`
- `Address`: `1402`
- `Min`: `53000`
- `Max`: `56000`
- `SampleCount`: `3`

Назначение: напряжение канала A. В старом коде нечетные линии проверялись через `MB_EL60V5_VOLTAGE_A`.

Шаг 6: внутри цикла добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `200`

Шаг 7: добавить ноду `Метка` после цикла.

В GUI:

- `Text`: `Для красивого отчета по линиям 0..15 не хватает цикла с индексом линии. Текущий For дает только slaveId, поэтому подписи line0/line1/... придется вести вручную или добавить ноду For по целому индексу.`

Шаг 8: если EL60v5 не идут ровным диапазоном slave ID, вместо шага 2 добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ ГИБКОГО ЦИКЛА ПО СПИСКУ SLAVE ID. Развернуть вручную для каждой пары линий: Poll 1403 53000..56000, Delay 200, Poll 1402 53000..56000, Delay 200.`

## Подтест 10 "Data test портов 0..9"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Data test портов 0..9`
- `Description`: `data_test=1, data_test_ports[0..9]=1, скорость 1000`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ РЕАЛЬНОЙ НОДЫ Data Test. Текущая Run Data Test в коде является заглушкой и не выполняет SoftwarePcap/Bercut/telnet тест. Для полного соответствия старому QTstand_old нужно проверить пары портов 0-1, 2-3, 4-5, 6-7, 8-9 на 1000 Мбит.`

Шаг 3: если все равно хочется поставить текущую ноду как заготовку, добавить `Run Data Test`.

В GUI:

- `Mode`: `SoftwarePcap`
- `ExpectedPackets`: `10000`
- `PacketSizeBytes`: `1514`
- `UdpPort`: `43962`
- `MaxPortTestTimeMs`: `15000`
- `PortsText`:

```text
pair0: port0-port1, CARD_IP_0, CARD_IP_1, speed=1000
pair1: port2-port3, CARD_IP_2, CARD_IP_3, speed=1000
pair2: port4-port5, CARD_IP_4, CARD_IP_5, speed=1000
pair3: port6-port7, CARD_IP_6, CARD_IP_7, speed=1000
pair4: port8-port9, CARD_IP_8, CARD_IP_9, speed=1000
```

- `OutputVariableName`: `DataTest`
- `FailOnError`: `true`

Шаг 4: добавить ноду `Метка`.

В GUI:

- `Text`: `CARD_IP_0..CARD_IP_9 взять из настроек сетевых карт стенда. В JSON профиля этих IP нет. Если switch_state=1, нужна другая ветка старой логики: telnet/switch normal config или data_test_chain.`

## Подтест 11 "UPS: подготовка и проверка наличия"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `UPS: подготовка и проверка наличия`
- `Description`: `Начало UpsTestPS2`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Device Presence / Stand Type Check. Старый тест проверял наличие PS2/PS3 и SIMBAT24. Здесь вручную убедиться, что PS_SLAVE_ID и SIMBAT_SLAVE_ID обнаружены монитором Modbus.`

Шаг 3: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `3000`

Шаг 4: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1219`
- `Value`: `1`
- `VerifyWrite`: `false`

Назначение: очистка min/max PS2 перед UPS-тестом.

Шаг 5: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1210`
- `Value`: `1`
- `VerifyWrite`: `true`

Назначение: включить ключ разряда/АКБ.

Шаг 6: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `15000`

Шаг 7: добавить ноду `Selftest Check`.

В GUI:

- `Url`: `http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin`
- `TimeoutMs`: `30000`
- `OutputPrefix`: `Dut`
- `ValidationRules`: пусто
- `FailOnError`: `true`

Шаг 8: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Get IRP Status. Если Dut.ups_det не 0 и не 1, старый тест ждал до 160 секунд и вызывал GetIrpStatus().`

Шаг 9: добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.ups_det`
- `ExpectedValue`: `1`
- `ComparisonType`: `Number`
- `FailMessage`: `UPS должен быть обнаружен: ups_det=1`

Шаг 10: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Wait Variable Until. Нужно до 30 секунд ждать Dut.akb_det=1, периодически вызывая Get UPS Voltage. Если akb_det не стал 1, проверить напряжение АКБ 12..27 В.`

Шаг 11: добавить ноду `Get UPS Voltage`.

В GUI:

- `BaseUrl`: `http://192.168.0.1`
- `TimeoutMs`: `5000`
- `OutputVariableName`: `Dut.akb_voltage`
- `FailOnError`: `true`

Шаг 12: добавить ноду `Check Variable Range`.

В GUI:

- `VariableName`: `Dut.akb_voltage`
- `Min`: `12`
- `Max`: `27`
- `Inclusive`: `true`
- `FailMessage`: `Напряжение АКБ после Get UPS Voltage должно быть 12..27 В`

Шаг 13: добавить ноду `Get UPS Status`.

В GUI:

- `BaseUrl`: `http://192.168.0.1`
- `TimeoutMs`: `5000`
- `OutputVariableName`: `Dut.ups_rez`
- `FailOnError`: `true`

Шаг 14: добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.ups_rez`
- `ExpectedValue`: `0`
- `ComparisonType`: `Number`
- `FailMessage`: `Перед отключением AC устройство должно быть от сети: ups_rez=0`

Шаг 15: добавить ноду `Метка`.

В GUI:

- `Text`: `charge_CC_test=0 и charge_CV_test=0, поэтому CC/CV зарядные проверки пропустить. test_charging=1 в JSON старой веткой UpsTestPS2 напрямую не используется.`

## Подтест 12 "UPS: переход на АКБ"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `UPS: переход на АКБ`
- `Description`: `Отключение AC1 и ожидание ups_rez=1`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1204`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: charge key off.

Шаг 3: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `500`

Шаг 4: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `300`

Шаг 5: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1200`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: `AC1 OFF`.

Шаг 6: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `500`

Шаг 7: добавить ноду `Метка`.

В GUI:

- `Text`: `AC2 по профилю выключен: use_ac2=0. Старый код после AC2-ветки все равно делал задержку 200 мс.`

Шаг 8: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `200`

Шаг 9: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1215`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: heater1 off.

Шаг 10: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1220`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: heater2 off.

Шаг 11: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1204`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: повторный charge key off.

Шаг 12: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `500`

Шаг 13: добавить ноду `Метка`.

В GUI:

- `Text`: `ОБЕРНУТЬ В ЦИКЛ ОЖИДАНИЯ: каждые 5000 мс вызвать Get UPS Status и проверить Dut.ups_rez=1, максимум 160 секунд. Текущий For не подходит, потому что он циклит slaveId, а не условие/время.`

Шаг 14: добавить ноду `Get UPS Status`.

В GUI:

- `BaseUrl`: `http://192.168.0.1`
- `TimeoutMs`: `5000`
- `OutputVariableName`: `Dut.ups_rez`
- `FailOnError`: `true`

Шаг 15: добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.ups_rez`
- `ExpectedValue`: `1`
- `ComparisonType`: `Number`
- `FailMessage`: `После отключения AC устройство должно перейти на АКБ: ups_rez=1`

Шаг 16: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `2000`

## Подтест 13 "UPS: измерение разряда SIMBAT24"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `UPS: измерение разряда SIMBAT24`
- `Description`: `Проверка SIMBAT discharge voltage/current`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Опрос регистра`.

В GUI:

- `SlaveId`: `SIMBAT_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1707`
- `Min`: `1`
- `Max`: `65535`
- `SampleCount`: `3`

Назначение: `MB_SIMBAT_DISCHARGE_VOLTAGE`, значение должно быть живым, не ноль.

Шаг 3: добавить ноду `Опрос регистра`.

В GUI:

- `SlaveId`: `SIMBAT_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1708`
- `Min`: `1`
- `Max`: `65535`
- `SampleCount`: `3`

Назначение: `MB_SIMBAT_DISCHARGE_CURRENT`, значение должно быть живым, не ноль.

Шаг 4: добавить ноду `Метка`.

В GUI:

- `Text`: `В старом коде check_stand_param для SIMBAT вызывался с 1..2, но затем в отчет писались реальные voltage/current. Для практического графа лучше проверять, что значения больше 0.`

## Подтест 14 "UPS: возврат на AC1"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `UPS: возврат на AC1`
- `Description`: `Включение AC1 и ожидание ups_rez=0`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1200`
- `Value`: `1`
- `VerifyWrite`: `true`

Назначение: `AC1 ON`.

Шаг 3: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `3000`

Шаг 4: добавить ноду `Метка`.

В GUI:

- `Text`: `ОБЕРНУТЬ В ЦИКЛ ОЖИДАНИЯ: каждые 5000 мс вызвать Get UPS Status и проверить Dut.ups_rez=0, максимум 160 секунд. Текущий For не подходит.`

Шаг 5: добавить ноду `Get UPS Status`.

В GUI:

- `BaseUrl`: `http://192.168.0.1`
- `TimeoutMs`: `5000`
- `OutputVariableName`: `Dut.ups_rez`
- `FailOnError`: `true`

Шаг 6: добавить ноду `Check Variable Equality`.

В GUI:

- `VariableName`: `Dut.ups_rez`
- `ExpectedValue`: `0`
- `ComparisonType`: `Number`
- `FailMessage`: `После включения AC устройство должно вернуться на сеть: ups_rez=0`

Шаг 7: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1210`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: discharge key off.

Шаг 8: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1204`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: charge key off.

## Подтест 15 "Расчет и запись MAC"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Расчет и запись MAC`
- `Description`: `send_mac=1, MAC C0:11:A6:20:XX:XX`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Build MAC from Serial. Нужно вычислить SerialShort = SerialNumber - 3200000; Dut.NewMac = C0:11:A6:20:XX:XX, где XX:XX это SerialShort в двух байтах.`

Шаг 3: добавить ноду `Send UDP Set MAC`.

В GUI:

- `TargetIp`: `192.168.0.1`
- `TargetPort`: `43962`
- `MacVariableName`: `Dut.NewMac`
- `TimeoutMs`: `1000`
- `RepeatCount`: `1`
- `DelayBetweenRepeatsMs`: `200`
- `FailOnSendError`: `true`

Шаг 4: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `7000`

Шаг 5: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1200`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: `StandOff`, AC1 off.

Шаг 6: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1210`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: `StandOff`, discharge key off.

Шаг 7: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `10000`

Шаг 8: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1200`
- `Value`: `1`
- `VerifyWrite`: `true`

Назначение: снова включить AC1.

Шаг 9: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `100`

Шаг 10: добавить ноду `Clear ARP Cache`.

В GUI:

- `RunArpdBat`: `true`
- `ArpdBatPath`: `arpd.bat`
- `Command`: `arp`
- `Arguments`: `-d`
- `TimeoutMs`: `5000`
- `FailOnError`: `false`

Шаг 11: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Wait/Retry HTTP Selftest. После перезапуска ждать устройство до 160 секунд, затем пауза 2 секунды.`

Шаг 12: добавить ноду `Selftest Check`.

В GUI:

- `Url`: `http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin`
- `TimeoutMs`: `160000`
- `OutputPrefix`: `Dut`
- `ValidationRules`: пусто
- `FailOnError`: `true`

Шаг 13: добавить ноду `Задержка`.

В GUI:

- `Milliseconds`: `2000`

Шаг 14: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Variable Compare Variable. Нужно проверить Dut.default_mac == Dut.NewMac. Текущая Check Variable Equality сравнивает только с литералом.`

## Подтест 16 "Печать этикеток"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Печать этикеток`
- `Description`: `print_label=1, label_num=4`
- `IsEnabled`: `true`
- `StopOnError`: `true`

Шаг 2: добавить ноду `Метка`.

В GUI:

- `Text`: `Если SerialShort и Dut.NewMac еще не вычислены, печать невозможна. Нужна нода Build MAC from Serial / Set Variable.`

Шаг 3: добавить ноду `Print Label`.

В GUI:

- `PrinterName`: `PRINTER_NAME`
- `DeviceName`: `PSW+UPS-Box 8x2Pro`
- `DeviceType`: `32`
- `SerialVariableName`: `SerialShort`
- `MacVariableName`: `Dut.NewMac`
- `Copies`: `4`
- `IncludeMac`: `true`
- `EquipmentFieldUse`: `false`
- `EquipmentType`: `0`
- `EquipmentText`: пусто
- `FailOnPrinterError`: `true`

## Подтест 17 "Финальная статистика и отчет"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Финальная статистика и отчет`
- `Description`: `Финальный запрос страницы, сбор отчета, отправка результата`
- `IsEnabled`: `true`
- `StopOnError`: `false`

Шаг 2: добавить ноду `Selftest Check`.

В GUI:

- `Url`: `http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin`
- `TimeoutMs`: `30000`
- `OutputPrefix`: `DutFinal`
- `ValidationRules`: пусто
- `FailOnError`: `false`

Шаг 3: добавить ноду `Метка`.

В GUI:

- `Text`: `НЕДОСТАЕТ НОДЫ Build Test Report. Нужно собрать TestReportJson из имени теста, SerialNumber, SerialShort, Dut.NewMac, всех ошибок, PoE/Data/UPS/Heater результатов и финальной статистики DutFinal.`

Шаг 4: добавить ноду `Send Test Report`.

В GUI:

- `ServerBaseUrl`: `SERVER_BASE_URL`
- `ReportVariableName`: `TestReportJson`
- `Endpoint`: `/api/Api.svc/result.json`
- `TimeoutMs`: `10000`
- `RetryCount`: `1`
- `RetryDelayMs`: `1000`
- `SaveLocalCopy`: `true`
- `LocalReportsDirectory`: `reports`
- `FailOnError`: `false`

Шаг 5: добавить ноду `Действие оператора`.

В GUI:

- `Message`: `Вставьте разъемы`

## Подтест 18 "Безопасное выключение стенда"

Шаг 1: добавить ноду `Подтест`.

В GUI:

- `Name`: `Безопасное выключение стенда`
- `Description`: `Финальный StandOff`
- `IsEnabled`: `true`
- `StopOnError`: `false`

Шаг 2: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1200`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: AC1 off.

Шаг 3: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1210`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: discharge key off.

Шаг 4: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1204`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: charge key off.

Шаг 5: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1215`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: heater1 off.

Шаг 6: добавить ноду `Запись регистра`.

В GUI:

- `SlaveId`: `PS_SLAVE_ID`
- `UseCurrentSlaveId`: `false`
- `Address`: `1220`
- `Value`: `0`
- `VerifyWrite`: `true`

Назначение: heater2 off.

## 3. Короткая карта регистров из этого графа

- `1200` - AC1 relay/state.
- `1204` - charge key.
- `1210` - discharge/AKB key.
- `1215` - heater1 relay.
- `1216` - heater1 current.
- `1219` - clear PS2 min/max.
- `1220` - heater2 relay.
- `1221` - heater2 current.
- `1402` - EL60v5 voltage A.
- `1403` - EL60v5 voltage B.
- `1412` - EL60v5 load set A.
- `1413` - EL60v5 load set B.
- `1414` - EL60v5 load enable A.
- `1415` - EL60v5 load enable B.
- `1707` - SIMBAT discharge voltage.
- `1708` - SIMBAT discharge current.

## 4. Что я бы добавил в проект в первую очередь

1. `SetVariableStep` с арифметикой и форматированием.
2. `BuildMacFromSerialStep`.
3. `WaitVariableUntilStep` с retry-действием внутри.
4. Настоящий `RunDataTestStep`.
5. `BuildTestReportStep`.
6. `CompareVariablesStep`.
7. `DirectReadRegisterStep` или исправить `PollRegisterStep`, чтобы он сам читал Modbus, а не только `RegisterState`.

