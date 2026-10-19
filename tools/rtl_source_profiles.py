"""Apply the original RTL_v1 settings to generated TestBuilder board graphs.

Source files are preserved verbatim under RtlProfileMigration/SourceProfiles.
Firmware executables/paths and physical RUN are deliberately not imported.
This is a build-time migration, not an automatic rewrite of user profiles.
"""
import json
from pathlib import Path

SOURCES = Path(__file__).parent / 'RtlProfileMigration' / 'SourceProfiles'
SW_WRITES = {2329: 'k5_test_delay', 2332: 'vcc_start_delay'}
for address, prefix in [(2333, 'k5_52V'), (2336, 'k5_55V'),
                        (2339, 'vout_vmain'), (2341, 'vout_vres'),
                        (2344, 'vref'), (2347, 'v12'), (2350, 'vcc_3V3'),
                        (2353, 'vcc_1V5'), (2356, 'vcc_1V1'),
                        (2359, 'cr2032'), (2362, 'cr2032_cpu'),
                        (2365, 'dut_tamper_status'), (2368, 'dut_tamper_led')]:
    SW_WRITES[address] = prefix + '_min'
    SW_WRITES[address + 1] = prefix + '_max'


def walk(graph):
    for node in graph['nodes']:
        yield node
        for key in ('body', 'bodyGraph'):
            if key in node:
                yield from walk(node[key])


def apply_original(graph, board):
    source = json.loads((SOURCES / f'RTL_{board}_v1.json').read_text())
    graph['configurationName'] = 'Проверка платы · параметры RTL_v1'
    graph['name'] = graph['deviceModel'] + ' · ' + graph['configurationName']
    graph['hardware']['PollIntervalMs'] = 1000
    sw_flags = {'CR2032 без питания': '0. Проверка CR2032',
                'K5 — пять режимов питания': '1.Проверка узла K5',
                'VCC — две попытки': '2.Проверка VCC',
                'DUT self-test': 'dut_selftest', 'SENSOR1 — 0 → 1 → 0': 'dut_sensor1_test',
                'SENSOR2 — 0 → 1 → 0': 'dut_sensor2_test', 'RELAY — 0 → 1 → 0': 'dut_relay_test',
                'TAMPER — 0 → 1 → 0': 'dut_tamper_test', 'RS485': 'dut_rs485_test',
                'I²C': 'dut_i2c_test', 'PoE info': 'dut_poe_test'}
    poe_flags = {'Напряжение 3,3 В': '3.3_test', 'Версия платы': 'board_version_test',
                 'Подача PoE — 16 линий': 'poe_test', 'Светодиоды — 16 каналов': 'led_test',
                 'UART — запуск и статус': 'uart_test', 'UART — напряжения 16 каналов': 'uart_test'}
    for node in walk(graph):
        name, typ, address = node.get('name', ''), node['type'], node.get('address')
        if typ == 'Subtest':
            flags = sw_flags if board == 'SW' else poe_flags
            if name in flags:
                node['isEnabled'] = source[flags[name]]
            if name.startswith('Отчёт'):
                node['isEnabled'] = source['6. отправка отчёта' if board == 'SW' else 'server_report']
            if name.startswith('Этикетка') and board == 'SW':
                node['isEnabled'] = source['7. печать этикетки']
            if board == 'POE' and name.startswith('Подача PoE порт '):
                node['isEnabled'] = source[f'port{name.split()[-1]}_test']
            if name == 'Перед началом':
                next(n for n in walk(node['bodyGraph']) if n['type'] == 'Operator Action')['text'] = 'Установите прошитую плату в стенд. Проверьте подключение питания и COM-портов.'
            if name == 'Подготовка стенда':
                node['description'] = 'Параметры RTL_SW_v1: задержки K5 и VCC — 10 (значения регистров). Коэффициенты калибровки не изменяются.'
            if name == 'Питание и reset':
                node['description'] = 'Пауза загрузки из RTL_POE_v1: 15 секунд.'
                [n for n in walk(node['bodyGraph']) if n['type'] == 'Delay'][-1]['milliseconds'] = source['startup_time'] * 1000
            if name in ('DUT self-test', 'PoE info', 'Версия платы', 'Запуск DUT после ручной прошивки'):
                node['description'] = 'Параметры из RTL_' + board + '_v1.json.'
        if board == 'SW':
            if typ == 'Write Register' and address in SW_WRITES:
                node['value'] = source[SW_WRITES[address]]
            if typ == 'Check Register Range' and address in (2317, 2318, 2319, 2320):
                prefix = {2317: 'cr2032_NoPWR', 2318: 'cr2032_NoPWR_cpu',
                          2319: 'dut_tamper_status', 2320: 'dut_tamper_led'}[address]
                node.update(min=source[prefix + '_min'], max=source[prefix + '_max'])
            if typ == 'Check Variable Equality':
                field = {'McuVersion': 'firmware_swd_vers', 'FirmwareVersion': 'firmware_vers',
                         'PoeFirmware': 'firmware', 'PoeMcu': 'mcu'}.get(node.get('variableName'))
                if field:
                    node['expectedValue'] = source[field]
            if typ == 'Print Label':
                node['copies'] = source['label_num']
        else:
            if typ == 'Check Register Range':
                if address == 2404:
                    node.update(min=source['3.3VMin'], max=source['3.3Vmax'])
                elif address == 2486:
                    node.update(min=source['board_version'], max=source['board_version'])
                elif address is not None and 2495 <= address <= 2510:
                    prefix = f'uart_ch{address - 2494}_voltage'
                    node.update(min=source[prefix + '_min'], max=source[prefix + '_max'])
            if typ == 'Check LED Color':
                node['checkLedColor']['expectedColor'] = source['led_colour']
    return graph


if __name__ == '__main__':
    for board in ('SW', 'POE'):
        path = Path('profiles') / f'RTL_{board}_board.json'
        graph = apply_original(json.loads(path.read_text()), board)
        path.write_text(json.dumps(graph, ensure_ascii=False, indent=2) + '\n')
