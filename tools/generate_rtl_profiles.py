"""Generate editable starter profiles from RTL_v2 source + converted source diagram.
Usage: python3 tools/generate_rtl_profiles.py <RTL_v2 checkout> <poe-board.json>
Production values come from the original RTL_v1 profiles supplied by the owner.
"""
from pathlib import Path
import json, sys, base64, re, copy, subprocess, xml.etree.ElementTree as ET
from rtl_source_profiles import apply_original, SOURCES, SW_WRITES
sw_source=json.loads((SOURCES / "RTL_SW_v1.json").read_text())
poe_source=json.loads((SOURCES / "RTL_POE_v1.json").read_text())
rtl=Path(sys.argv[1]); out=Path('profiles'); presets=json.loads(Path('TestBuilder/Assets/hardware-presets.json').read_text())
generated_profiles=[]
def n(t,**kw):
 kw.setdefault("runOnlyOnFirstRun",False)
 return dict(type=t,**kw)
def inp(x): return 'In' if x['type'] in ['Subtest','Serial Console','Run Program','Parse Response','Check LED Color','For Slaves (repeat v1)','Check Variable Equality','Check Variable Range','Get Serial Number','Print Label','Build Test Report','Send Test Report'] else 'Вход'
def success(x):
 t=x['type']
 return 'Success' if t=='Subtest' else ('Продолжить' if t in ['Operator Action','Operator Input (v1)'] else ('Выход' if t in ['Start','Body Start','Delay','Label'] else 'True'))
def error(x): return 'Error' if x['type']=='Subtest' else ('Отмена' if x['type'].startswith('Operator ') else 'False')
def graph(name,items,body=False,continue_errors=False):
 nodes=[n('Body Start' if body else 'Start')]+items+[n('Body End' if body else 'End')];links=[]
 # These coordinates only describe row/order intent. ProfileLayout measures the
 # actual Avalonia templates and replaces them before generation is complete.
 for i,x in enumerate(nodes): x.update(id=str(i),x=100+(i%6)*410,y=120+(i//6)*520)
 for a,b in zip(nodes,nodes[1:]):
  links.append(dict(sourceNodeId=a['id'],sourceConnector=success(a),targetNodeId=b['id'],targetConnector=inp(b)))
  if continue_errors and a['type']=='Subtest': links.append(dict(sourceNodeId=a['id'],sourceConnector='Error',targetNodeId=b['id'],targetConnector=inp(b)))
 return dict(name=name,nodes=nodes,connections=links)
def sub(name,items,regions='',**kw): return n('Subtest',name=name,description=kw.pop('description',''),isEnabled=kw.pop('isEnabled',True),stopOnError=True,boardElements=regions,bodyGraph=graph(name,items),**kw)
def repeat(items,count=2,delay=0,until=True): return n('For Slaves (repeat v1)',repeatOnSameSlave=True,repeatCount=count,repeatDelayMs=delay,indexVariable='attempt',untilSuccess=until,stopOnError=True,body=graph('Повтор',items,body=True))
def collect(name,subs,regions=''):
 f=repeat([],count=1,until=False);f['body']=graph(name,subs,body=True,continue_errors=True)
 return sub(name,[f],regions)
def delay(ms):return n('Delay',milliseconds=ms)
def write(a,v):return repeat([n('Write Register',slaveId=1,address=a,value=v,verifyWrite=False)],count=3,delay=1000)
def check(a,mi,ma=None,report_name=''): return n('Check Register Range',slaveId=1,address=a,min=mi,max=mi if ma is None else ma,liveRead=False,reportName=report_name)
def wait(a,v,ms=60000): return n('Wait Until',slaveId=1,address=a,expectedValue=v,durationMs=ms,liveRead=False)
def wait_range(a,mi,ma,interval=500,count=120):
 node=check(a,mi,ma);node.update(readAttempts=count,readIntervalMs=interval);return node
def console(command,output='Console.Response',timeout=10000):return n('Serial Console',serialConsole=dict(portName='',baudRate=115200,command=command,responseTerminator='',quietMs=500,timeoutMs=timeout,outputVariableName=output))
def parse(selector,var='Parsed.Value',source='Console.Response',typ='String',mode='Json'):return n('Parse Response',parseResponse=dict(sourceVariableName=source,parseMode=mode,selector=selector,valueType=typ,outputVariableName=var))
def eq(var,expected,typ='String'):return n('Check Variable Equality',variableName=var,expectedValue=str(expected),comparisonType=typ)
def param(name):return "ubus call tf_hwsys getParam '"+json.dumps(dict(name=name),separators=(',',':'))+"'"
def paramcheck(name,value,tries=1,interval=0):
 steps=[console(param(name)),parse(name,name),eq(name,value)]
 return [repeat(steps,tries,interval)] if tries>1 else steps
def prompt(text,var=''):return n('Operator Input (v1)' if var else 'Operator Action',text=text,outputVariableName=var)
def program_uid(): return [n('Run Program',runProgram=dict(programPath='read_mcu_id.bat',arguments='',workingDirectory='',timeoutMs=30000,expectedExitCode=0,outputPrefix='Mcu')),parse(r'^\s*(?<value>[0-9A-Fa-f]{24})\s*$','CpuId','Mcu.Output',mode='Regex')]
def serial(kind,cpu='CpuId'):return n('Get Serial Number',serverBaseUrl='',deviceType=kind,cpuIdVariableName=cpu,outputVariableName='SerialNumber',timeoutMs=10000,retryCount=2,retryDelayMs=1000,failOnError=True)
def report():return [n('Build Test Report',reportVariableName='TestReportText',serialVariableName='SerialNumber',testType='production',includeAllVariables=False),n('Send Test Report',serverBaseUrl='',reportVariableName='TestReportText',endpoint='/api/Api.svc/result.json',timeoutMs=10000,retryCount=2,retryDelayMs=1000,saveLocalCopy=True,localReportsDirectory='reports',failOnError=False)]
def failure_report(g):
 # Detached RunOnFailure stage is called by TestBuilder only after its power cleanup.
 items=[n('Check Variable Range',variableName='SerialNumber',min=1,max=2147483647,inclusive=True),eq('ReportDelivery.Status','Sent')]+report()
 stage=sub('Отчёт при отказе — если есть серийный номер',items,runOnFailure=True,description='После снятия питания. Без серийного номера сохраняется журнал; уже отправленный отчёт повторно не отправляется.')
 body=stage['bodyGraph'];end=body['nodes'][-1]
 body['connections']=[c for c in body['connections'] if c['sourceNodeId']!='2']
 body['connections'] += [dict(sourceNodeId='1',sourceConnector='False',targetNodeId=end['id'],targetConnector='Вход'),dict(sourceNodeId='2',sourceConnector='True',targetNodeId=end['id'],targetConnector='Вход'),dict(sourceNodeId='2',sourceConnector='False',targetNodeId='3',targetConnector='In')]
 stage.update(id='failure-report',x=100,y=2500);g['nodes'].append(stage)
def label():return n('Print Label',printerName='TSC TE310',serialVariableName='SerialNumber',copies=1,useQtProZplFormat=False,failOnPrinterError=True)
def hardware(kind):return dict(BaudRate=9600,Parity=0,DataBits=8,StopBits=1,PollIntervalMs=1000,Devices=[copy.deepcopy(next(p for p in presets if p['Name']==kind))])
board_names = {'ConsoleStatus': 'Консоль', 'CrStatus': 'Батарейка CR2032', 'FlashStatus': 'Флеш-память', 'I2CStatus': 'Датчик I²C', 'K5TestStatus': 'Узел K5', 'PoeStatus': 'Контроллер PoE', 'RelayStatus': 'Реле', 'Rs485Status': 'Интерфейс RS-485', 'RtlStatus': 'Коммутатор', 'Sensor1Status': 'Датчик 1', 'Sensor2Status': 'Датчик 2', 'TamperStatus': 'Датчик вскрытия', 'V11Status': 'Питание 1,1 В', 'V15Status': 'Питание 1,5 В', 'V33Status': 'Питание 3,3 В', '3V3': 'Питание 3,3 В', 'PoeTotal1_4': 'Питание PoE портов 1–4', 'PoeTotal5_8': 'Питание PoE портов 5–8', 'Spi_swd_uart93': 'Интерфейсы SPI, SWD и UART', 'Led1A': 'Светодиод порта 1A', 'Poe1A': 'Питание PoE порта 1A', 'Led1B': 'Светодиод порта 1B', 'Poe1B': 'Питание PoE порта 1B', 'Led2A': 'Светодиод порта 2A', 'Poe2A': 'Питание PoE порта 2A', 'Led2B': 'Светодиод порта 2B', 'Poe2B': 'Питание PoE порта 2B', 'Led3A': 'Светодиод порта 3A', 'Poe3A': 'Питание PoE порта 3A', 'Led3B': 'Светодиод порта 3B', 'Poe3B': 'Питание PoE порта 3B', 'Led4A': 'Светодиод порта 4A', 'Poe4A': 'Питание PoE порта 4A', 'Led4B': 'Светодиод порта 4B', 'Poe4B': 'Питание PoE порта 4B', 'Led5A': 'Светодиод порта 5A', 'Poe5A': 'Питание PoE порта 5A', 'Led5B': 'Светодиод порта 5B', 'Poe5B': 'Питание PoE порта 5B', 'Led6A': 'Светодиод порта 6A', 'Poe6A': 'Питание PoE порта 6A', 'Led6B': 'Светодиод порта 6B', 'Poe6B': 'Питание PoE порта 6B', 'Led7A': 'Светодиод порта 7A', 'Poe7A': 'Питание PoE порта 7A', 'Led7B': 'Светодиод порта 7B', 'Poe7B': 'Питание PoE порта 7B', 'Led8A': 'Светодиод порта 8A', 'Poe8A': 'Питание PoE порта 8A', 'Led8B': 'Светодиод порта 8B', 'Poe8B': 'Питание PoE порта 8B'}
def save(filename,g):
 if filename in ("RTL_SW_board.json", "RTL_POE_board.json"):
  apply_original(g, "SW" if filename == "RTL_SW_board.json" else "POE")
 for region in g.get("board",{}).get("Regions",[]): region["Name"] = board_names.get(region["Name"],region["Name"])
 out.mkdir(exist_ok=True); (out/filename).write_text(json.dumps(g,ensure_ascii=False,indent=2)+'\n')
 generated_profiles.append(str(out/filename))
 print(filename, 'nodes',sum(1 for _ in walk(g)))
def walk(g):
 for x in g['nodes']:
  yield x
  for key in ['body','bodyGraph']:
   if key in x: yield from walk(x[key])
# SW board areas are copied from the original 800x585 XAML layout.
root=ET.parse(rtl/'UserInterface/Sw/Views/RtlSwView.xaml').getroot();regions=[];region_groups={}
for e in root.iter():
 if e.tag.split('}')[-1] not in ['Rectangle','Ellipse']:continue
 fill=e.get('Fill','');m=re.search(r'Binding (\w+Status)',fill)
 if not m or not all(k in e.attrib for k in ['Canvas.Left','Canvas.Top','Width','Height']):continue
 group=m[1];ident=group+'_'+str(len(region_groups.get(group,[]))+1);region_groups.setdefault(group,[]).append(ident)
 regions.append(dict(Id=ident,Name=group,X=float(e.get('Canvas.Left'))/800,Y=float(e.get('Canvas.Top'))/585,Width=float(e.get('Width'))/800,Height=float(e.get('Height'))/585))
def swreg(*names):
 for name in names:
  if name not in region_groups: raise ValueError('Unknown SW region group: '+name)
 return ','.join(i for name in names for i in region_groups[name])
swboard=dict(ImageBase64=base64.b64encode((rtl/'Resources/Images/PlateImages/RTL_SW1.png').read_bytes()).decode(),Regions=regions)
# Original raw controller values; calibration coefficients are never changed.
limits={a:sw_source[key] for a,key in SW_WRITES.items() if a not in (2329,2332)}
sw=[]
sw.append(sub('Перед началом',[prompt('Установите прошитую плату в стенд. Проверьте подключение питания и COM-портов.')]))
sw.append(sub('Подготовка стенда', [write(a,0) for a in [2301,2302,2303,2304,2305,2307,2308]]+[write(2329,sw_source["k5_test_delay"]),write(2332,sw_source["vcc_start_delay"])]+[write(a,v) for a,v in limits.items()]+[write(2385,0),delay(500)],description='Пределы и задержки из RTL_SW_v1.json. Коэффициенты калибровки не изменяются.'))
sw.append(sub('CR2032 без питания',[check(2317,sw_source["cr2032_NoPWR_min"],sw_source["cr2032_NoPWR_max"],'CR2032 без питания'),check(2318,sw_source["cr2032_NoPWR_cpu_min"],sw_source["cr2032_NoPWR_cpu_max"],'CR2032 CPU без питания')],swreg('CrStatus')))
sw.append(sub('Чтение MCU ID внешней утилитой',[write(2301,1),write(2307,0),write(2385,1)]+program_uid()+[write(2301,0),write(2385,0)],isEnabled=False,description='Опциональная операция исходника. Укажите существующий read_mcu_id.bat перед включением. Прошивка и эмуляция клавиатуры не выполняются. Основной идентификатор SW читается через DEVICE_ID.'))
k5=[]
for title,start,status in [('VMAIN',2323,2324),('VMAIN+VRES',2325,2326),('VRES',2327,2328),('VMAIN+VRES повтор',2325,2326),('VMAIN повтор',2323,2324)]:
 # RTL_v2 requires acknowledgement (1) before accepting the result (2/3).
 items=[delay(2000),write(start,1),wait_range(status,1,1,1000,60),wait_range(status,2,3),delay(2000)]+[check(a,0,65535,'K5 '+title+' · '+name) for a,name in [(2371,'52 В'),(2372,'55 В'),(2373,'Vout'),(2374,'Vref'),(2375,'12 В')]]+[check(status,2),delay(1000)]
 k5.append(sub('K5 '+title,items))
sw.append(collect('K5 — пять режимов питания',k5,swreg('K5TestStatus')))
sw.append(sub('VCC — две попытки',[repeat([write(2330,1),wait_range(2331,1,3),delay(5000),wait_range(2331,2,3,5000,12)]+[check(a,0,65535,'VCC · '+name) for a,name in [(2376,'3,3 В'),(2377,'1,5 В'),(2378,'1,1 В'),(2379,'CR2032'),(2380,'CR2032 CPU')]]+[check(2331,2)],2)],swreg('V33Status','V15Status','V11Status','CrStatus'),description='Две попытки; статус 2 — успех, 3 — отказ; ожидания ограничены числом попыток; ожидание свежего опроса увеличивает общее время.'))
vcc = sw[-1]['bodyGraph']
original_repeat = vcc['nodes'][1]
vcc_items = [check(2301,0),check(2302,0),write(2302,1),original_repeat]
sw[-1]['bodyGraph'] = graph('VCC',vcc_items)
vcc = sw[-1]['bodyGraph']
for cond in vcc['nodes'][1:3]:
 vcc['connections'].append(dict(sourceNodeId=cond['id'],sourceConnector='False',targetNodeId='4',targetConnector='In'))
sw.append(sub('Запуск DUT после ручной прошивки',[write(2301,0),write(2302,0),delay(10000),write(2301,1),write(2302,1),delay(2000),repeat([console(param('SW_VERS'),timeout=3000),parse('SW_VERS','McuVersion')],30,2000),eq('McuVersion',sw_source['firmware_swd_vers'],'VersionAtLeast')],swreg('ConsoleStatus'),description='Ожидание ответа SW_VERS вместо зависимости от единичной строки загрузочного лога. Версия из RTL_SW_v1.'))
sw.append(sub('DUT self-test',[console('ubus call system board'),parse('release.version','FirmwareVersion'),eq('FirmwareVersion',sw_source['firmware_vers'],'VersionAtLeast')]+[collect('Аппаратные ошибки DUT',[sub(k,paramcheck(k,'0')) for k in ['HW_ERR1','HW_ERR2','HW_ERR3']])],swreg('RtlStatus','FlashStatus'),description='Проверка минимальной версии и трёх HW_ERR. Версия из RTL_SW_v1.'))
for sensor,addr in [('sensor1',2303),('sensor2',2304)]:
 items=[]
 for value in [0,1,0]:items += [write(addr,value),delay(5000)]+paramcheck(sensor,value,2,5000)
 sw.append(sub(sensor.upper()+' — 0 → 1 → 0',items,swreg('Sensor1Status' if addr==2303 else 'Sensor2Status')))
items=[]
for value,ms in [(0,5000),(1,5000),(0,8000)]:items += [console('ubus call tf_hwsys setParam '+"'"+json.dumps(dict(name='relay',value=str(value)),separators=(',',':'))+"'"),delay(ms),check(2306,value)]
sw.append(sub('RELAY — 0 → 1 → 0',items,swreg('RelayStatus')))
items=[]
for value in [0,1,0]:
 items += [write(2305,value),delay(5000)]
 if value==0:items += [check(2319,0,500,'Tamper status'),check(2320,0,500,'Tamper LED')]
 items += paramcheck('tamper',value,2,9000)
sw.append(sub('TAMPER — 0 → 1 → 0',items,swreg('TamperStatus')))
sw.append(sub('RS485',paramcheck('upsModeAvalible','1',2,5000),swreg('Rs485Status')))
sw.append(sub('I²C',paramcheck('sensorConnected','1')+[console(param('sensorTemperature')),parse('sensorTemperature','SensorTemperature',typ='Number')],swreg('I2CStatus')))
sw.append(sub('PoE info',[console('ubus call poe info'),parse('firmware','PoeFirmware'),parse('mcu','PoeMcu'),eq('PoeFirmware',sw_source['firmware']),eq('PoeMcu',sw_source['mcu'])],swreg('PoeStatus'),description='Версии PoE из RTL_SW_v1.'))
sw.append(sub('Идентификатор и серийный номер',[console(param('DEVICE_ID')),parse('DEVICE_ID','CpuId'),serial('RTL-SW')]))
sw.append(sub('Отчёт TestBuilder',report()))
sw.append(sub('Этикетка платы 30×10 мм',[label()]))
cleanup=collect('Снятие питания SW',[sub('Отключить '+str(a),[write(a,0)]) for a in [2301,2302,2307,2385]])
cleanup['runOnFailure']=True
sw.append(cleanup)
g=graph('RTL-SW · проверка платы · параметры RTL_v1',sw);g.update(productKind='Board',deviceModel='RTL-SW',configurationName='Ручная прошивка · параметры RTL_v1',hardware=hardware('RTL-SW'),board=swboard);g['nodes'][0]['type']='Start (board v1)';failure_report(g);save('RTL_SW_board.json',g)
# PoE areas come from disjoint contours of the original SVG, converted offline.
poeboard=json.loads(Path(sys.argv[2]).read_text())
def poereg(*names):return ','.join(r['Id'] for r in poeboard['Regions'] if r['Name'] in names)
poe=[sub('Перед началом',[prompt('Установите прошитую плату в стенд. Проверьте подключение питания и COM-портов.')])]
poe.append(sub('MCU ID',[prompt('Введите или отсканируйте настоящий UID микроконтроллера платы (24 шестнадцатеричных символа). Автоматическое чтение можно включить отдельным подтестом.','CpuId'),parse(r'^[0-9A-Fa-f]{24}$','CpuId','CpuId',mode='Regex')],poereg('Spi_swd_uart93')))
poe.append(sub('MCU ID внешней утилитой',[write(2511,1),write(2492,1)]+program_uid()+[write(2511,0)],isEnabled=False,description='Укажите BAT/EXE чтения UID. При включении отключите предыдущий ручной ввод. Не прошивает и не эмулирует клавиши.'))
poe.append(sub('Серийный номер',[serial('RTL-POE')]))
poe.append(sub('Питание и reset',[write(2511,0),write(2403,1),write(2492,1),write(2489,1),delay(2000),write(2489,0),delay(poe_source["startup_time"]*1000)],description='Пауза загрузки из RTL_POE_v1.'))
poe.append(sub('Напряжение 3,3 В',[check(2404,poe_source["3.3VMin"],poe_source["3.3Vmax"],'3,3 В')],poereg('3V3')))
poe.append(sub('Версия платы',[check(2486,poe_source["board_version"],report_name='Версия платы')],description='Версия из RTL_POE_v1.'))
ports=[]
for port in range(1,9):
 parts=[delay(4000)]+[sub(f'PoE {port}{side}',[check(2405+(port-1)*2+offset,1)],poereg(f'Poe{port}{side}')) for offset,side in enumerate('AB')]
 # Each port continues A/B, the whole stage collects every port before returning failure.
 f=repeat([],1,until=False);f['body']=graph('Порт '+str(port),parts,body=True,continue_errors=True)
 ports.append(sub('Подача PoE порт '+str(port),[f],poereg('PoeTotal1_4' if port<=4 else 'PoeTotal5_8')))
poe.append(collect('Подача PoE — 16 линий',ports))
leds=[]
for ch in range(16):
 port=ch//2+1;side='AB'[ch%2];key=f'Led{port}{side}'
 op=n('Check LED Color',checkLedColor=dict(slaveId=1,useCurrentSlaveId=False,redAddress=2437+ch,greenAddress=2453+ch,blueAddress=2469+ch,whiteAddress=2421+ch,expectedColor=poe_source['led_colour'],colorMargin=20,minIntensity=50,outputPrefix=key))
 leds.append(sub(key,[delay(2000),repeat([op],2,2000)],poereg(key)))
poe.append(collect('Светодиоды — 16 каналов',leds))
poe.append(sub('UART — запуск и статус',[write(2493,1),delay(5000),check(2494,1),delay(2000),check(2494,2)],poereg('Spi_swd_uart93')))
uart=poe[-1]['bodyGraph'];uart['connections'].append(dict(sourceNodeId='3',sourceConnector='False',targetNodeId='5',targetConnector='Вход'))
poe.append(collect('UART — напряжения 16 каналов',[sub('UART канал '+str(ch+1),[check(2495+ch,poe_source[f'uart_ch{ch+1}_voltage_min'],poe_source[f'uart_ch{ch+1}_voltage_max'],'Напряжение UART '+str(ch+1))]) for ch in range(16)],poereg('Spi_swd_uart93')))
poe.append(sub('Отчёт TestBuilder',report()))
poe.append(sub('Этикетка платы 30×10 мм',[label()],isEnabled=False,description='В исходном PoE алгоритме обязательной печати в конце нет; можно включить по регламенту участка.'))
cleanup=collect('Снятие питания PoE',[sub('Отключить '+str(a),[write(a,0)]) for a in [2403,2492,2489,2511]]);cleanup['runOnFailure']=True;poe.append(cleanup)
g=graph('RTL-PoE · проверка платы · параметры RTL_v1',poe);g.update(productKind='Board',deviceModel='RTL-POE',configurationName='Ручная прошивка · параметры RTL_v1',hardware=hardware('RTL-POE'),board=poeboard);g['nodes'][0]['type']='Start (board v1)';failure_report(g);save('RTL_POE_board.json',g)
template=graph('Шаблон проверки цвета LED',[sub('Цвет LED — две попытки',copy.deepcopy(leds[0]['bodyGraph']['nodes'][1:-1]))]);template.update(productKind='Board',deviceModel='RTL-POE',hardware=hardware('RTL-POE'));template['nodes'][0]['type']='Start (board v1)';save('RTL_LED_color_template.json',template)
manual=graph('Ручная печать этикетки платы',[n('Print Label',printerName='TSC TE310',useManualSerialNumber=True,manualSerialNumber='',copies=1,useQtProZplFormat=False,failOnPrinterError=True)]);manual.update(productKind='Board',configurationName='30×10 мм · полный серийный номер');manual['nodes'][0]['type']='Start (board v1)';save('board_manual_label.json',manual)
# Fail generation if any graph cannot be measured or still has spacing conflicts.
layout_command=['dotnet','run','--project','tools/ProfileLayout/ProfileLayout.csproj']
subprocess.run(layout_command+['--','--write',*generated_profiles],check=True)
subprocess.run(layout_command+['--no-build','--','--check',*generated_profiles],check=True)
