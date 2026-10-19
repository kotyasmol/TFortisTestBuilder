"""Build a portable Russian handoff report from real RTL simulation evidence.

Run after dotnet test --filter FullyQualifiedName~RtlFullProfileSimulationTests.
Usage: python3 tools/rtl_simulation_report.py artifacts/rtl-simulation
"""
from pathlib import Path
import hashlib
import html
import json
import shutil
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
folder = Path(sys.argv[1]).resolve()
results = sorted((folder / "evidence").glob("*/result.json"))
rows = [json.loads(p.read_text(encoding="utf-8")) for p in results]
if len(rows) != 36 or not all(r["verified"] for r in rows):
    raise SystemExit(f"Expected 36 verified scenarios; got {len(rows)}, verified {sum(bool(r['verified']) for r in rows)}")
esc = lambda v: html.escape(str(v))
labels = {
    "none": "Исправная плата", "write-once": "Одна неудачная запись: повтор восстанавливает работу",
    "write": "Команда записи не выполняется", "serial": "Сервер вернул неверный серийный номер",
    "server": "Сервер отчётов возвращает HTTP 503", "cancel": "Оператор остановил проверку",
    "disconnect": "Нет ответов на чтение; запись ещё доступна", "bus-lost": "Полная потеря Modbus: чтение и запись недоступны",
    "short-read": "Контроллер возвращает неполный набор регистров", "cleanup": "Один выход не отключается",
    "as-shipped": "SW с исходными заглушками версий", "battery": "Пониженное напряжение CR2032",
    "k5": "Отказ K5 VMAIN; остальные режимы проверяются", "sensor": "Датчик не меняет состояние",
    "console": "Таймаут консоли", "json": "Неверный JSON в ответе консоли", "printer": "Принтер не принимает этикетку",
    "voltage": "Питание 3,3 В ниже допуска", "poe-line": "Отказ PoE 1A; остальные линии проверяются",
    "led": "Неверный цвет LED 1A; остальные каналы проверяются", "uart": "Контроллер вернул отказ UART",
    "uart-voltage": "Напряжение последнего канала UART ниже допуска", "print-enabled": "Включена опциональная печать PoE",
}
profile_dir = folder / "profiles"
profile_dir.mkdir(exist_ok=True)
manifest = {}
for name in ["RTL_SW_board.json", "RTL_POE_board.json"]:
    original = root / "profiles" / name
    digest = hashlib.sha256(original.read_bytes()).hexdigest().upper()
    if any(r["sourceSha256"] != digest for r in rows if r["profile"] == name):
        raise SystemExit(f"Profile changed after simulation: {name}")
    shutil.copy2(original, profile_dir / name)
    manifest[name] = digest
(folder / "profiles-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
regression = folder / "test-results" / "regression.trx"
counts = ET.parse(regression).find(".//{*}Counters").attrib
if counts.get("failed") != "0" or counts.get("passed") != counts.get("total"):
    raise SystemExit("Regression test run did not pass")
total_seconds = sum(r["seconds"] for r in rows)
table = ""
for r in rows:
    target = "evidence/" + r["scenario"]
    outcome = "Проверка пройдена" if r["successful"] else "Ожидаемый отказ подтверждён"
    if r["fault"] == "server": outcome = "Изделие проверено; отчёт не отправлен — успешная приёмка не показана"
    table += f'''<tr><td>RTL-{esc(r['board'])}</td><td>{esc(labels[r['fault']])}<small>{'Исходные паузы и опрос 500 мс' if r['originalTiming'] else 'Ускоренный прогон'}</small></td><td>{esc(outcome)}<small>{esc(r['Headline'])}</small></td><td>{r['seconds']:.2f} с</td><td><a href="{target}/result.json">Результат</a> · <a href="{target}/trace.jsonl">Журнал</a></td></tr>'''
timed = "".join(f"<li>RTL-{esc(r['board'])}: {r['seconds']:.1f} с, {r['ReadCount']} обращений к чтению, {r['WriteAttempts']} попыток записи.</li>" for r in rows if r['originalTiming'])
doc = f'''<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Проверка RTL-SW и RTL-PoE — передача инженеру</title>
<style>body{{font:16px/1.6 system-ui,sans-serif;color:#182333;background:#edf0f4;margin:0}}main{{max-width:1180px;margin:32px auto;padding:32px;background:#fafbfc}}h1{{font-size:30px;line-height:1.2}}h2{{font-size:23px;margin-top:36px}}a{{color:#185c99}}.note{{background:#fff2cc;padding:16px 20px;border-left:4px solid #996400}}.ok{{background:#e4efe9;padding:16px 20px;border-left:4px solid #31654c}}small{{display:block;color:#536071;font-size:13px}}table{{width:100%;border-collapse:collapse;font-size:14px}}td,th{{text-align:left;vertical-align:top;border-bottom:1px solid #c8d0da;padding:10px}}th{{background:#e5e9ef}}pre{{white-space:pre-wrap;background:#e8ecf1;padding:16px;font-size:13px}}li{{margin-bottom:8px}}.scroll{{overflow:auto}}@media print{{body,main{{background:white;margin:0;padding:12px}}a{{color:inherit}}tr{{break-inside:avoid}}}}</style>
<main><p>TFortisTestBuilder · 15 октября 2026</p><h1>Проверка RTL-SW и RTL-PoE перед испытанием на стенде</h1>
<div class="ok">Подтверждены {len(rows)} сценариев имитации и {esc(counts['passed'])} остальных тестов приложения. Полные графы обеих плат прошли с исходными паузами и периодом фонового опроса 500 мс.</div>
<p>Это проверка программного алгоритма. Она даёт основание передать сборку инженеру для испытания на оборудовании. Физические платы, COM-драйвер, настоящий сервер и принтер в этих прогонах не подключались.</p>
<div class="note"><b>Профили пока не утверждены для приёмки продукции.</b> Допуски временные; для SW не заданы производственные версии PoE-прошивки и MCU. Исходный SW-профиль с заглушками получил ожидаемый отказ. Для успешных испытаний изменялись только копии в памяти: SIM-POE-1.0 и SIM-MCU-3. Эти строки нельзя переносить в производственный профиль.</div>
<h2>Что именно проверено</h2><p>JSON-профиль загружался штатным загрузчиком. Штатные SlaveManager и ConfiguredSlaveModel читали имитируемое устройство; RegisterMonitor обновлял RegisterState. GraphCompiler и TestExecutor выполняли настоящие ноды, включая разбор консоли, получение серийника, формирование отчёта и этикетки. Результат проходил через модель операторского пульта и его обработчики выполнения.</p>
<p>Имитатор содержит самостоятельную карту поведения контроллера: запись запускает аппаратный тест, статус проходит «выполняется → успех/отказ», питание и reset меняют измерения PoE, а команды датчиков и реле меняют соответствующие ответы. Значения не берутся из ожидаемых результатов нод. Проверены переходы пяти режимов K5, VCC, датчики 0 → 1 → 0, 16 линий PoE, 16 каналов цвета и 16 напряжений UART. Запись калибровочных коэффициентов SW не выполнялась.</p>
<p>Отчёт действительно формируется и сохраняется локально; HTTP-запрос перехватывается до сети. Этикетка EPL с полным серийником 123456 и одной копией доходит до имитатора принтера. В исходном PoE-профиле печать выключена: отдельно проверены включение печати и её отказ.</p>
<p>Завершающие подтесты выполняются в порядке RunOnFailure с новым токеном отмены. Тестовый запуск воспроизводит эту политику; нажатие кнопки запуска, открытие физического порта и операторские диалоги в этих сценариях не автоматизированы. Они не подменяются утверждением о физическом испытании.</p>
<h2>Результаты полных прогонов</h2><ul>{timed}</ul><p>Это время работы имитатора на данном компьютере. Оно не является нормативом для производства. В остальных сценариях паузы сокращены до 50 мс, интервалы повторов до 15 мс, опрос выполняется каждые 10 мс. Условия проверок, число попыток и связи графа сохранены.</p>
<h2>Найденная и исправленная ошибка</h2><p>При отказе отключения питания завершающий подтест, соединённый с основным графом, не учитывался как аварийное завершение в итоговом сообщении. Теперь пульт показывает «Проверьте состояние стенда». Тесты подтверждают, что приложение пытается отключить каждый выход даже после неудачи одного из них.</p>
<p>Отдельно проверена полная потеря Modbus: команды отключения не принимаются, питание в модели остаётся включённым, успешный результат не показывается. Программа не может физически снять питание через оборванную связь; порядок ручного отключения должен быть определён на рабочем месте.</p>
<h2>Порядок испытания на настоящем стенде</h2><ol>
<li>Используйте сборку, сопровождающую этот отчёт. Если рядом есть архив Windows, распакуйте его целиком в отдельную папку, затем запустите TestBuilder.exe. Не заменяйте рабочую сборку до проверки. Исходные профили приложены в папке <a href="profiles/RTL_SW_board.json">profiles (SW)</a> и <a href="profiles/RTL_POE_board.json">profiles (PoE)</a>; контрольные суммы — в <a href="profiles-sha256.json">манифесте</a>.</li>
<li>В настройках укажите порт стенда, отдельный порт консоли SW, адрес сервера, Stand ID и имя установленного принтера. Успешный виртуальный ответ сервера не подтверждает совместимость производственного API.</li>
<li>Откройте нужный профиль в редакторе. В оборудовании проверьте slave 1, 9600 бод, 8N1, опрос 500 мс. Ожидаемые типы контроллеров: SW — 104, PoE — 105, регистр 0. Сверьте с реальной версией контроллера.</li>
<li>До подачи питания инженер должен утвердить пределы напряжений, аппаратную версию PoE, ожидаемые цвета и версии прошивок. В SW замените SET_PRODUCTION_FIRMWARE и SET_PRODUCTION_MCU на реальные значения. Минимальные версии 0.0.0 также временные. Сохраните изменения отдельным профилем.</li>
<li>Возьмите заведомо исправные, вручную прошитые SW и PoE. Запустите полную проверку каждой платы. Для PoE введите настоящий UID из 24 шестнадцатеричных символов. Подтвердите предусмотренные операторские действия. Запишите фактические значения и время проверки.</li>
<li>Сверьте результат с измерениями стенда: K5/VCC и датчики SW, питание PoE всех 16 линий, цвета всех 16 каналов, UART. Убедитесь, что подсветка относится к правильным элементам платы.</li>
<li>Проверьте принятый сервером отчёт, UID и серийник. Для SW проверьте реальную этикетку 30×10 мм, читаемость Code128, полный серийный номер и одну копию. Включать печать PoE нужно только если она предусмотрена на участке.</li>
<li>По безопасной методике участка проверьте остановку оператором, обрыв связи, известную неисправную плату и недоступность сервера/принтера. Убедитесь, что отказ не показан как успешная приёмка, а отключение питания подтверждено фактически. Не создавайте коротких замыканий ради проверки ПО.</li>
<li>Передайте разработчику имя и файл профиля, версию сборки, модель/версию контроллера, журнал запуска, серийник тестовой платы, ожидаемый и фактический результат. Если есть расхождение измерений — приложите показания стенда.</li></ol>
<h2>Протокол для инженера</h2><p>Можно распечатать этот раздел или заполнить копию в отдельном документе.</p><table><tr><th>Поле</th><th>Значение</th></tr>{''.join('<tr><td>'+s+'</td><td>_______________________________________</td></tr>' for s in ['Дата, участок, ответственный','Сборка и профиль (контрольная сумма)','Плата, аппаратная версия, UID, серийник','Прошивки DUT / PoE / MCU','Утверждённые допуски, кем и когда','Исправная SW / PoE: результат и время','Неисправная плата: результат и подсветка','Остановка / обрыв связи / отключение питания','Отчёт на сервере / этикетка','Замечания и решение о допуске'])}</table>
<h2>Все сценарии</h2><p>«Ожидаемый отказ подтверждён» означает, что тест успешно обнаружил заданную неисправность. Журнал содержит обращения к регистрам, ответы, консольные команды и переходы нод.</p><div class="scroll"><table><thead><tr><th>Плата</th><th>Сценарий</th><th>Подтверждённый результат</th><th>Время</th><th>Данные</th></tr></thead><tbody>{table}</tbody></table></div>
<h2>Повторный запуск разработчиком</h2><p>В рабочем дереве с этими изменениями и установленным .NET 8:</p><pre>dotnet build TestBuilder.Tests/TestBuilder.Tests.csproj --no-restore --disable-build-servers -m:1
TFORTIS_RTL_EVIDENCE="$PWD/artifacts/rtl-simulation/evidence" dotnet test TestBuilder.Tests/TestBuilder.Tests.csproj --no-build --no-restore -m:1 --filter FullyQualifiedName~RtlFullProfileSimulationTests
python3 tools/rtl_simulation_report.py artifacts/rtl-simulation</pre><p>Команда с переменной окружения показана для macOS/Linux; в PowerShell задайте $env:TFORTIS_RTL_EVIDENCE перед dotnet test. Для формирования отчёта также нужен результат общего прогона test-results/regression.trx. Базовая ревизия Git сама по себе не содержит всех изменений рабочего дерева.</p>
<p>Исходники имитатора и тестов: TestBuilder.Tests/Simulation. Сверка аппаратного контракта — по RTL_v2, ревизия 86f7dbd7cadf4c72f7a3e0763ce0fc4d72123687. Протоколы xUnit не заменяют испытания электроники; RTU-кадры, CRC, реальные скорости загрузки, аналоговые переходные процессы, нагрев и качество контактов этой моделью не воспроизводятся.</p></main></html>'''
(folder / "Откройте — результаты и порядок проверки.html").write_text(doc, encoding="utf-8")
print(f"Report: {folder}, {len(rows)} scenarios, {counts['passed']} regression tests")
