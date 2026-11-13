#!/usr/bin/env python3
"""Reproducible, report-focused device profiles; does not modify the four production profiles.

Electrical production limits are deliberately not inferred from another model.
Run from the repository root. ProfileLayout measures the generated files separately.
"""
import copy
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DESTINATION = ROOT / "examples/profiles/Проверка 10 моделей"
BASE = ROOT / "profiles/PSW+UPS-Box 8x2Pro · Параллельная проверка с SFP · 11.11.26.json"
DESCRIPTION = "Диагностика и отчёт · 13.11.26"
# code, base-board selftest type, copper ports, optical ports, protocol
MODELS = [
    (4, 4, 4, 1, "legacy"), (5, 5, 4, 2, "legacy"),
    (6, 6, 6, 2, "legacy"), (7, 7, 8, 2, "legacy"),
    (21, 5, 4, 2, "legacy"), (25, 6, 6, 2, "legacy"),
    (26, 7, 8, 2, "legacy"), (28, 7, 8, 2, "legacy"),
    (32, 0, 8, 2, "pro"), (33, 0, 8, 2, "pro"),
]


def node(kind, **parameters):
    return dict(type=kind, **parameters)


def edge(source, output, target, input_="In"):
    return dict(sourceNodeId=str(source), sourceConnector=output,
                targetNodeId=str(target), targetConnector=input_)


def graph(items, *, continue_errors=False, name="Граф"):
    nodes = [node("Start"), *items, node("End")]
    for index, item in enumerate(nodes):
        item.update(id=str(index), x=80 + (index % 4) * 500, y=80 + (index // 4) * 800)
    connections = []
    for index, item in enumerate(nodes[:-1]):
        output = "Выход" if item["type"].startswith("Start") or item["type"] == "Label" else "True"
        target_input = "Вход" if nodes[index + 1]["type"] in ("End", "Write Register") else "In"
        connections.append(edge(index, output, index + 1, target_input))
        if continue_errors and output == "True":
            connections.append(edge(index, "False", index + 1, target_input))
    return dict(name=name, nodes=nodes, connections=connections)


def stage(name, items, **parameters):
    return node("Subtest", name=name, description="", isEnabled=True, stopOnError=True,
                runOnFailure=False, bodyGraph=graph(items, name=name), **parameters)


def write(address, value=0):
    return node("Write Register", slaveId=23, useCurrentSlaveId=False,
                registerArea="Holding", address=address, value=value, verifyWrite=True)


def field(key, label=None, source="Variable", format_="Text"):
    return dict(key=key, label=label or key, source=source, format=format_)


def report_nodes():
    return [node("Build Test Report", serialVariableName="SerialNumber", reportVariableName="TestReportText",
                 includeAllVariables=False, testType="diagnostic"),
            node("Send Test Report", serverBaseUrl="", reportVariableName="TestReportText",
                 endpoint="/api/Api.svc/result.json", timeoutMs=10000, retryCount=0,
                 retryDelayMs=1000, failOnError=False, saveLocalCopy=True, localReportsDirectory="reports")]


def generate():
    base = json.loads(BASE.read_text(encoding="utf-8"))
    catalog = json.loads((ROOT / "reference/serials/equipment-models.json").read_text(encoding="utf-8"))
    DESTINATION.mkdir(parents=True, exist_ok=True)
    files = []
    portable_models = []
    for code, board_type, copper, optical, family in MODELS:
        model = next(m for m in catalog["models"] if m.get("labelCode") == code)
        series = model["serialSeries"]
        portable_models.append(dict(copy.deepcopy(model), isFullOverride=True))
        url = "http://192.168.0.1" + ("/test.shtml" if family == "legacy" else
              "/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin")
        read = lambda key: node("Read Selftest Field (v1)", url=url, readField=key,
                                timeoutMs=250000, pollIntervalMs=5000, outputPrefix="Dut", failOnError=True)
        preparation = stage("01. Подготовка стенда", [write(1200), write(1201),
            node("Clear ARP Cache", timeoutMs=5000, failOnError=False, runArpdBat=True,
                 arpdBatPath="arpd.bat", command="arp", arguments="-d *")])
        preparation.update(type="Profile Open (v2): Subtest", runOnlyOnFirstRun=True)
        allowed_types = sorted({board_type, code})
        identity = stage("03. Загрузка и тип устройства", [read("cpu_id"), read("dev_type"),
            node("Parse Response", customName="Проверить тип устройства", parseResponse=dict(
                sourceVariableName="Dut.dev_type", parseMode="Regex", valueType="String",
                selector="^(?:" + "|".join(map(str, allowed_types)) + ")$", outputVariableName="VerifiedDeviceType"))])
        identity["description"] = "CPU ID и тип читаются до выдачи номера. Допустимые dev_type: " + ", ".join(map(str, allowed_types))
        serial = stage("04. Серийный номер из справочника", [
            node("Get Serial Number", serverBaseUrl="", deviceType="", cpuIdVariableName="Dut.cpu_id",
                 outputVariableName="SerialNumber", useFixedSerialNumber=False, fixedSerialNumber=0,
                 timeoutMs=30000, retryCount=0, retryDelayMs=1000, failOnError=True)])
        checks = [node("Selftest Check", url=url, timeoutMs=250000, pollIntervalMs=5000,
                       outputPrefix="Dut", validationRules="init_ok=1..1", failOnError=True,
                       psw2G6FHardwareChecks=False)]
        if code == 32:
            checks.append(node("Check Variable Equality", variableName="Dut.ups_det", expectedValue="1",
                               comparisonType="Number", failMessage="Модель с UPS: устройство не обнаружило ИБП"))
        checks.append(node("Compare Variables (capture v1)", leftVariableName="Dut.cpu_id", rightVariableName="SerialNumberCpuId",
                           comparisonType="String", resultVariableName="DeviceIdentity",
                           failMessage="CPU ID изменился после выдачи серийного номера. Проверьте подключённое устройство."))
        selftest = stage("05. Встроенная самопроверка", checks)
        selftest["description"] = "Проверяется init_ok. Версии и измерения включены в отчёт без неподтверждённых производственных допусков."
        pairs = []
        for i in range(copper // 2):
            # Same bench mapping as 8x2Pro: .8/.9 are reserved for SFP.
            ip = [2, 4, 6, 10][i]
            pairs.append(f"Медь {2*i+1}–{2*i+2},192.168.0.{ip},192.168.0.{ip+1},100")
        pairs.append(("SFP 1–2" if optical == 2 else "Гигабит RJ45–SFP") + ",192.168.0.8,192.168.0.9,1000")
        data = stage("06. Передача данных", [node("Run Data Test", mode="SoftwarePcap", expectedPackets=10000,
            packetSizeBytes=1514, udpPort=43962, maxPortTestTimeMs=15000, targetBandwidthMbps=100,
            allowGigabit=True, durationMs=5000, warmupMs=500, interPairDelayMs=5000,
            allowedLossPercent=1, allowedTxDeficitPercent=2, bidirectional=True,
            portsText="\r\n".join(pairs), outputVariableName="DataTest", failOnError=True)])
        data["description"] = "Подключения нужно сверить на стенде. " + (
            "Пара .8/.9: гигабитный RJ45 и SFP на 1000 Мбит/с (руководство PSW-1G4F v13, раздел 2)." if optical == 1 else
            "Медные порты попарно 100 Мбит/с, SFP .8/.9 — 1000 Мбит/с; оба направления.")
        mac = stage("07. Расчёт MAC без записи в устройство", [node("Build MAC From Serial",
            serialVariableName="SerialNumber", serialOffset=series["serialOffset"], macPrefix=series["macPrefix"],
            serialShortVariableName="SerialShort", macVariableName="CalculatedMac", failOnError=True)])
        shutdown = stage("08. Выключение питания", [write(1200), write(1201)])
        shutdown["stopOnError"] = False
        shutdown["bodyGraph"] = graph([write(1200), write(1201)], continue_errors=True, name=shutdown["name"])
        report = stage("09. Сформировать и отправить отчёт", report_nodes())
        items = [preparation, stage("02. Подача питания DUT", [write(1201), write(1200, 1)]),
                 identity, serial, selftest, data, mac, shutdown, report]
        # Use a separate disabled print stage, with catalog model/variant and global printer/layout.
        label = stage("10. Этикетка — включить при необходимости", [node("Print Label", printerName="",
            serialVariableName="SerialNumber", macVariableName="Dut.default_mac", copies=1,
            useQtProZplFormat=True, useDefaultPrinter=True, failOnPrinterError=True)])
        label["isEnabled"] = False
        label["description"] = "Модель/комплектация из справочника, принтер и макет из общих настроек. Расчётный MAC не выдаётся за записанный."
        items.append(label)
        g = graph([])
        g["nodes"] = [node("Start (catalog serials v1)", id="0", x=80, y=80), *items,
                      node("End", id="11", x=80, y=2600)]
        for index, item in enumerate(items, 1):
            item.update(id=str(index), x=80 + ((index-1) % 3) * 650, y=500 + ((index-1) // 3) * 650)
        g["connections"] = [edge(0, "Выход", 1)] + [edge(i, "Success", i+1, "Вход" if i == 10 else "In") for i in range(1, 11)]
        cleanup = stage("Аварийное выключение", [])
        cleanup.update(id="cleanup", x=80, y=3400, runOnFailure=True, stopOnError=False,
                       bodyGraph=graph([write(1200), write(1201)], continue_errors=True, name="Аварийное выключение"))
        failure = stage("Отчёт при ошибке", [
            node("Check Variable Equality", variableName="Execution.SendFailureReport", expectedValue="true", comparisonType="Boolean"),
            node("Check Variable Equality", variableName="ReportDelivery.Status", expectedValue="Sent", comparisonType="String"),
            *report_nodes()])
        failure.update(id="failure-report", x=800, y=3400, runOnFailure=True, stopOnError=False)
        body = failure["bodyGraph"]
        body["connections"] = [edge(0, "Выход", 1), edge(1, "True", 2), edge(1, "False", 5, "Вход"),
            edge(2, "True", 5, "Вход"), edge(2, "False", 3), edge(3, "True", 4),
            edge(3, "False", 5, "Вход"), edge(4, "True", 5, "Вход")]
        g["nodes"] += [cleanup, failure]
        fields = [field("", source="TestType"), field("Dut.dev_type", "Тип устройства (dev_type)"),
                  field("Dut.cpu_id", "CPU ID"), field("самотестирование", source="Entry", format_="BooleanText"),
                  field("Версия прошивки", source="Entry"), field("Версия бутлоадера", source="Entry"),
                  field("MAC адрес", source="Entry"), field("CalculatedMac", "Расчётный MAC (без записи)"),
                  field("SerialShort", "Короткая часть серийного номера"),
                  field("DeviceIdentity.Passed", "CPU ID соответствует выданному номеру", format_="BooleanNumber")]
        for i, pair in enumerate(pairs):
            fields.append(field(f"DataTest.Port{i}.Passed", "Передача данных: " + pair.split(',')[0], format_="BooleanNumber"))
        for name in ["версия платы", "ID микросхемы PoE контроллера", "ID микросхемы Switch контроллера"]:
            fields.append(field(name, source="Entry"))
        for i in range(copper):
            for prefix in ["link", "poe_a_st", "poe_a_v", "poe_a_c"]:
                key = f"{prefix}_{i}"
                fields.append(field(key, source="Entry", format_="FixedSix" if prefix == "poe_a_v" else "Text"))
        if optical == 2:
            for i in range(copper+1, copper+3):
                fields.append(field(f"присутствие SFP{i}", source="Entry"))
        if code == 32:
            fields += [field("Dut.ups_det", "Детекция ИБП", format_="BooleanNumber"),
                       field("напряжение АКБ", source="Entry", format_="FixedSix")]
        hardware = copy.deepcopy(base["hardware"])
        hardware["StandName"] = "АПК-03 · диагностика профилей"
        # No electrical load test: require just the power controller used by the graph.
        hardware["Devices"] = [d for d in hardware["Devices"] if d["SlaveId"] == 23]
        g.update(name=model["name"] + " · " + DESCRIPTION, deviceModel=model["name"], deviceModelId=model["id"],
                 configurationName=DESCRIPTION, productKind="Switch", useCatalogSerials=True, labelVariant=0,
                 updatedAt="2026-11-13T08:00:00+05:00", hardware=hardware,
                 reportTemplate=dict(fields=fields, includeAutomaticEntries=False),
                 pingMonitor=copy.deepcopy(base["pingMonitor"]))
        path = DESTINATION / (g["name"] + ".json")
        path.write_text(json.dumps(g, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        files.append(path)
    local_catalog = DESTINATION / "catalog"
    local_catalog.mkdir(exist_ok=True)
    (local_catalog / "equipment-models.json").write_text(json.dumps(dict(schemaVersion=1,
        revision="2026-11-13-qualification", models=portable_models), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return files


if __name__ == "__main__":
    files = generate()
    for path in files:
        print(path.relative_to(ROOT))
    layout = ["dotnet", "run", "--project", str(ROOT / "tools/ProfileLayout/ProfileLayout.csproj")]
    for index, path in enumerate(files):
        # Bound native resource lifetime during frame export and isolate each
        # profile's headless renderer from the rest of the batch.
        command = layout + ([] if index == 0 else ["--no-build"])
        subprocess.run(command + ["--", "--write", str(path)], cwd=ROOT, check=True)
        subprocess.run(layout + ["--no-build", "--", "--check", str(path)], cwd=ROOT, check=True)
