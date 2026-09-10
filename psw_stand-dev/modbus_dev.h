#ifndef MODBUS_H
#define MODBUS_H

#include <QThread>
#include <QObject>
#include <QModbusDevice>
#include <QModbusClient>
#include <QModbusRtuSerialMaster>
#include <QTimer>
#include <QSignalMapper>

#define STAND_SLOTS_NUM 23//число слотов в на кросс-плате

#define PS1_ADDR            22//адрес платы PS-1 (месторасположение фиксировано в слоте)
#define PS2_ADDR            22//адрес платы PS-2 (месторасположение фиксировано в слоте)
#define PS3_ADDR            22

#define MODBUS_RETRY_NUM    10//число попыок подключения, после которого считаем плату недоступной
#define MODBUS_INTERVAL     1000//интервал опроса между слотами

#define DEV_EL60			1//плата для проверки PoE (сплиттер+электронная нагрузка)
#define DEV_PS1				2//плата для коммутации 220В и проверки IRP
#define DEV_PS2				3//модернизированная плата для коммутации 220В и проверки IRP (с внешним БП)
#define DEV_EL60V5			4//модернизированная плата для проверки PoE
#define DEV_IO02			5//плата для проверки входов/выходов
#define DEV_RPS_STAND 		6//автономный стенд RPS-01
#define DEV_PWR180_STAND 	7//автономный стенд PWR-180/PWR-50
#define DEV_PS3				8//Плата PS-03
#define DEV_SIMBAT24		9//плата SimBat-24
#define DEV_SIMBAT48		10//плата SimBat-48
#define DEV_RPS_STAND_V4    11//плата RPS Stand V4

//common
#define MB_DEV_TYPE			0
#define MB_DEV_TYPE_S		1
#define MB_CPUID			11
#define MB_SW_VERS			43
#define MB_RS485_RX			44
#define MB_RS485_TX			45
#define MB_ERR_CODE1		46
#define MB_ERR_CODE2		47
#define MB_ERR_CODE3		48
#define MB_ERR_CODE4		49

#define MB_UPDATE_BUFF      101

//el-60
#define MB_EL60_CURRENT		1000
#define MB_EL60_VOLTAGE		1001
#define MB_EL60_MAXCURR		1002
#define MB_EL60_MAXVOLT		1003
#define MB_EL60_MINCURR		1004
#define MB_EL60_MINVOLT		1005
#define MB_EL60_CLEAR		1006
#define MB_EL60_OUT_PWR		1007
#define MB_EL60_OUT_EN		1008
#define MB_EL60_TEMPER		1009
#define MB_EL60_FAN_EN		1010
#define MB_EL60_FAN_ERR		1011
#define MB_EL60_FAN_PWM		1012
#define MB_EL60_LED_EN		1013
#define MB_EL60_OFFLINE_EN	1014
#define MB_EL60_OFFLINE_PWR	1015
#define MB_EL60_PASSIVE_EN	1016

//ps-1
#define MB_PS1_AC1_STATE		1100
#define MB_PS1_AC2_STATE		1101
#define MB_PS1_SENSOR1_STATE	1102
#define MB_PS1_SENSOR2_STATE	1103
#define MB_PS1_R_BAT			1104
#define MB_PS1_V_BAT			1105//уставка напряжения АКБ
#define MB_PS1_I_BAT			1106
#define MB_PS1_I_HETAER			1207
#define MB_PS1_V_AKB			1108//фактическое напряжение на имитаторе акб
#define MB_PS1_AKB_EN			1109//подключение реле АКБ
#define MB_PS1_AKB_DIRECTION	1110
#define MB_PS1_I_BAT_MIN		1111//минимальный ток заряда
#define MB_PS1_I_BAT_MAX		1112//максимальный ток заряда
#define MB_PS1_CLEAR			1113//очистка статистики

//PS-2
#define MB_PS2_AC1_STATE            1200
#define MB_PS2_AC2_STATE            1201
#define MB_PS2_SENSOR1_STATE        1202
#define MB_PS2_SENSOR2_STATE        1203
#define MB_PS2_CHRG_KEY_STATE       1204
#define MB_PS2_CHRG_VOLTAGE         1205
#define MB_PS2_CHRG_CURRENT         1206
#define MB_PS2_CHRG_VOLTGAGE_MAX    1207
#define MB_PS2_CHRG_VOLTGAGE_MIN    1208
#define MB_PS2_CHRG_RLOAD           1209
#define MB_PS2_DISCHRG_KEY_STATE    1210
#define MB_PS2_DISCHRG_VOLTAGE      1211
#define MB_PS2_DISCHRG_CURRENT      1212
#define MB_PS2_DISCHRG_VOLTGAGE_MAX 1213
#define MB_PS2_DISCHRG_VOLTGAGE_MIN 1214
#define MB_PS2_HEATER_RELAY_STATE   1215
#define MB_PS2_HEATER_CURRENT       1216
#define MB_PS2_CURR_TEMPERATURE     1217
#define MB_PS2_MAX_TEMPERATURE      1218
#define MB_PS2_CLEAR                1219//очистка статистики
#define MB_PS3_HEATER_RELAY2_STATE  1220
#define MB_PS3_HEATER_RELAY2_CURRENT  1221

//EL60v5
#define MB_EL60V5_CURRENT_A		1400//текущее значение тока А
#define MB_EL60V5_CURRENT_B		1401//текущее значение тока В
#define MB_EL60V5_VOLTAGE_A		1402//текущее значение напряжения А
#define MB_EL60V5_VOLTAGE_B		1403//текущее значение напряжения B
#define MB_EL60V5_MAXCURR_A		1404//максимальное значение тока А
#define MB_EL60V5_MAXCURR_B		1405//максимальное значение тока B
#define MB_EL60V5_MAXVOLT_A		1406//максимальное значение напряжения A
#define MB_EL60V5_MAXVOLT_B		1407//максимальное значение напряжения B
#define MB_EL60V5_MINCURR_A		1408//минимальное значение тока A
#define MB_EL60V5_MINCURR_B		1409//минимальное значение тока B
#define MB_EL60V5_MINVOLT_A		1410//минимальное значение напряжения A
#define MB_EL60V5_MINVOLT_B		1411//минимальное значение напряжения B
#define MB_EL60V5_OUT_PWR_A		1412//установка нагрузки канал А
#define MB_EL60V5_OUT_PWR_B		1413//установка нагрузки канал B
#define MB_EL60V5_OUT_EN_A		1414//включение нагрузки А
#define MB_EL60V5_OUT_EN_B		1415//включение нагрузки А
#define MB_EL60V5_TEMPER_A		1416//температура А
#define MB_EL60V5_TEMPER_B		1417//температура B
#define MB_EL60V5_MAX_TEMPER_A	1418//максимальняа температура, при превышении отключаем ключ нагрузки
#define MB_EL60V5_MAX_TEMPER_B	1419
#define MB_EL60V5_OFFLINE_EN_A	1420//включение нагрузки при старте
#define MB_EL60V5_OFFLINE_EN_B	1421
#define MB_EL60V5_OFFLINE_PWR_A	1422//включение нагрузки при старте
#define MB_EL60V5_OFFLINE_PWR_B	1423//установка нагрузки при старте
#define MB_EL60V5_PASSIVE_EN_A	1424//включение пассивного PoE
#define MB_EL60V5_PASSIVE_EN_B	1425
#define MB_EL60V5_T2P_A			1426
#define MB_EL60V5_T2P_B			1427
#define MB_EL60V5_ALERT_A		1428
#define MB_EL60V5_ALERT_B		1429
#define MB_EL60V5_CLEAR			1430//очистка статистики

//IO-02
#define MB_IO02_OUT1			1500
#define MB_IO02_OUT2			1501
#define MB_IO02_OUT3			1502
#define MB_IO02_OUT4			1503
#define MB_IO02_OUT5			1504
#define MB_IO02_OUT6			1505
#define MB_IO02_OUT7			1506
#define MB_IO02_IN1				1507
#define MB_IO02_IN2				1508
#define MB_IO02_IN3				1509
#define MB_IO02_IN4				1510
#define MB_IO02_IN5				1511
#define MB_IO02_IN6				1512
#define MB_IO02_IN7				1513
#define MB_IO02_IN8				1514
#define MB_IO02_IN9				1515
#define MB_IO02_IN10			1516
#define MB_IO02_IN11			1517
#define MB_IO02_I2C_DEVADDR		1518
#define MB_IO02_RS485_TEST_START    1519
#define MB_IO02_RS485_TEST_OK       1520
#define PS2_RLOAD_MAX                    267
#define PS2_RLOAD_MIN                      4

//SIMBAT24 / SIMBAT48
#define MB_SIMBAT_CHARGE_STATE			1700//подключение цепи заряда
#define MB_SIMBAT_CHARGE_VOLTAGE		1701
#define MB_SIMBAT_CHARGE_CURRENT		1702
#define MB_SIMBAT_CHARGE_VOLTAGE_MAX	1703
#define MB_SIMBAT_CHARGE_VOLTAGE_MIN	1704
#define MB_SIMBAT_CHARGE_LOAD			1705//установка сопротивления нагрузки
#define MB_SIMBAT_DISCHARGE_STATE		1706//подключение цепи разрядки
#define MB_SIMBAT_DISCHARGE_VOLTAGE		1707
#define MB_SIMBAT_DISCHARGE_CURRENT		1708
#define MB_SIMBAT_DISCHARGE_VOLTAGE_MAX	1709
#define MB_SIMBAT_DISCHARGE_VOLTAGE_MIN	1710
#define MB_SIMBAT_TEMPER1				1711//температура на радиаторе нагрузки
#define MB_SIMBAT_TEMPER2				1712//температура на радиаторе нагрузки
#define MB_SIMBAT_MAX_TEMPER			1713//установка максимальной температуры на радиаторе нагрузки.
#define MB_SIMBAT_INT					1714//линия на внешнее прерывание
#define MB_SIMBAT_FAN_STATE				1715//включение усиленного обдува
#define MB_SIMBAT_CLEAR					1716//очистка статистики
#define SIMBAT24_RLOAD_MAX               250
#define SIMBAT24_RLOAD_MIN                13
#define SIMBAT48_RLOAD_MAX               500
#define SIMBAT48_RLOAD_MIN                51

#define MB_MAX_ADDR              MB_SIMBAT_CLEAR

#define MB_RPS_STAND_ADDR       2//адрес стенда RPS-1
#define MB_RPS_ADDR             1//адрес платы RPS-1 при проверке на стенде

//переменные платы RPS-1
#define MB_RPS1_MANUF_ID        1000
#define MB_RPS1_SW_VERS         1003
#define MB_RPS1_VAC             1004
#define MB_RPS1_BAT_VOLTAGE     1005
#define MB_RPS1_CHRG_VOLTAGE    1006
#define MB_RPS1_BAT_CURRENT     1007
#define MB_RPS1_TEMPER          1008
#define MB_RPS1_REL_STATE       1012
#define MB_RPS1_AC_OK_STATE     1013

//переменные для Stand Rps-01
#define MB_RPS_AC_STATE					(1300+1)//подключение АС
#define MB_RPS_LATR_STATE				(1301+1)//подключение ЛАТР
#define MB_RPS_BAT_STATE				(1302+1)//подключение АКБ
#define MB_RPS_BAT_POLARITY				(1303+1)//полярность АКБ
#define MB_RPS_PREHEATING				(1304+1)//установка эквивалента значения на термодатчике
#define MB_RPS_REL1_IN					(1305+1)//состояние входа реле 1
#define MB_RPS_REL2_IN					(1306+1)//состояние входа реле 2
#define MB_RPS_EL_STATE					(1307+1)//включение нагрузки
#define MB_RPS_EL_R_SET					(1308+1)//установка сопротивления нагрузки
#define MB_RPS_BAT_VOLTAGE				(1309+1)//напряжение на иммитаторе АКБ
#define MB_RPS_BAT_CURRENT				(1310+1)//ток через иммитатор АКБ
#define MB_RPS_AC_IN_IND				(1311+1)//индикация напряжения на входе RPS
#define MB_RPS_AC_OUT_IND				(1312+1)//индикация напряжения на выходе RPS
#define MB_RPS_TEMPER1					(1313+1)//температура на датчике 1
#define MB_RPS_TEMPER2					(1314+1)//температура на датчике 2
#define MB_RPS_FAN_STATE				(1315+1)//состояние вентилятора
#define MB_RPS_FAN_T_ON					(1316+1)//температура включения вентилятора
#define MB_RPS_FAN_T_OFF				(1317+1)//температура выключения вентилятора
#define MB_RPS_MAX_TEMPER				(1318+1)//максимальная температура на радиаторе
#define MB_RPS_CLEAR_STAT				(1319+1)//очистка статистики

#define MB_READ_NONE    0//не читать
#define MB_READ_ONE     1//читать один адрес
#define MB_READ_ALL     2//читать все адреса (только подключенные)
#define MB_SEARCH       3//читать статус подключения плат

class modbus_dev:public QThread {
    Q_OBJECT
public:

    modbus_dev(QString port,int autoconnect,int type);
    virtual ~modbus_dev();

    QTimer rps_timer;
    QTimer rpsstand_timer;
    QTimer stand_timer;
    QTimer search_timer;
    QTimer connect_timer;
    int slave_id;
    int read_rps_view_result;
    int read_rpsstand_view_result;

    //rps stand
    int mb_rps_dev_type;

    void read_stand_slot(int slot);
    int get_read_flag();
    void set_read_flag(int flag);
    int get_current_slot();
    void set_current_slot(int slot);

    bool is_connected(int slot);
    int get_mb_devtype(int slot);
    int get_mb_rs485rx(int slot);
    int get_mb_rs485tx(int slot);
    int get_mb_charge_voltage(void);
    int get_mb_charge_current(void);
    //для EL-60
    int get_mb_el60_current(int slot);
    int get_mb_el60_voltage(int slot);
    int get_mb_el60_current_max(int slot);
    int get_mb_el60_voltage_max(int slot);
    int get_mb_el60_current_min(int slot);
    int get_mb_el60_voltage_min(int slot);
    void clear_mb_el60_minmax(int slot);
    int get_mb_el60_out_pwr(int slot);
    int get_mb_el60_out_en(int slot);
    int get_mb_el60_offline_mode(int slot);
    int get_mb_el60_offline_current(int slot);
    int get_mb_el60_passive(int slot);
    void set_mb_el60_out_pwr(int slot,int power);
    void set_mb_el60_out_en(int slot,int state);
    void set_mb_el60_passive(int slot,int state);
    int get_mb_el60_temper(int slot);
    //для EL-60v5
    int get_mb_el60v5_current_a(int slot);
    int get_mb_el60v5_voltage_a(int slot);
    int get_mb_el60v5_current_b(int slot);
    int get_mb_el60v5_voltage_b(int slot);
    int get_mb_el60v5_temper_a(int slot);
    int get_mb_el60v5_temper_b(int slot);
    int get_mb_el60v5_out_en_a(int slot);
    int get_mb_el60v5_out_en_b(int slot);
    int get_mb_el60v5_out_pwr_a(int slot);
    int get_mb_el60v5_out_pwr_b(int slot);

    //io-02
    int io02_is_connected();
    int get_io02_slot();
    int get_mb_io02_input(int input);
    int get_mb_io02_output(int output);
    void set_mb_io02_out(int output,int state);
    void set_mb_io02_in(int input,int state);

    void writeModbus(int id,int addr, int val);

    void set_stand_akb(int state);
    void set_stand_dc1(int state);
    void set_stand_dc2(int state);
    void set_stand_ac2(int state);
    void set_stand_ac1(int state);
    void set_akb_voltage(int voltage);
    void set_ps1_rload(int state);
    void set_ps1_measurement_direction(int dir);

    void set_stand_all_off(void);

    void clear_mb_ps1_minmax();
    int get_mb_ps1_voltage();
    int get_mb_ps1_set_voltage();
    int get_mb_ps1_current();
    int get_mb_ps1_current_min();
    int get_mb_ps1_current_max();
    int get_ps1_charge_current(void);
    int get_ps1_heating_current(void);

    int get_mb_ps2_ac1_state();
    int get_mb_ps2_ac2_state();
    int get_mb_ps2_sensor1_state(void);
    int get_mb_ps2_sensor2_state(void);
    int get_mb_ps2_charge_key_state(void);
    int get_mb_ps2_charge_voltage(void);
    int get_mb_ps2_charge_current();
    int get_mb_ps2_charge_voltage_min();
    int get_mb_ps2_charge_voltage_max();
    int get_mb_ps2_charge_rload();
    int get_mb_ps2_discharge_key_state();
    int get_mb_ps2_discharge_voltage();
    int get_mb_ps2_discharge_current();
    int get_mb_ps2_discharge_voltage_min();
    int get_mb_ps2_discharge_voltage_max();
    int get_mb_ps2_heater_relay_state();
    int get_mb_ps2_heater_current();
    int get_mb_ps2_heater_current2();
    int get_mb_ps2_temper();
    int get_mb_ps2_max_temper();
    int get_mb_ps3_ac1_state();
    int get_mb_ps3_ac2_state();
    int get_mb_ps3_heater1_relay();
    int get_mb_ps3_heater1_current();
    int get_mb_ps3_heater2_relay();
    int get_mb_ps3_heater2_current();

    int is_simbat24_connected();
    int is_simbat48_connected();
    int get_simbat24_slot();
    int get_simbat48_slot();
    int get_mb_simbat_charge_state(int slot);
    int get_mb_simbat_charge_voltage(int slot);
    int get_mb_simbat_charge_current(int slot);
    int get_mb_simbat_charge_voltage_max(int slot);
    int get_mb_simbat_charge_voltage_min(int slot);
    int get_mb_simbat_discharge_state(int slot);
    int get_mb_simbat_discharge_voltage(int slot);
    int get_mb_simbat_discharge_current(int slot);
    int get_mb_simbat_discharge_voltage_max(int slot);
    int get_mb_simbat_discharge_voltage_min(int slot);
    int get_mb_simbat_charge_rload(int slot);
    int get_mb_simbat_temper1(int slot);
    int get_mb_simbat_temper2(int slot);
    int get_mb_simbat_max_temper(int slot);
    int get_mb_simbat_int(int slot);
    int get_mb_simbat_fan(int slot);

    int get_simbat_type();
    void set_simbat_type(int type);

    void set_stand_charge_key(int state);
    void set_stand_discharge_key(int state);
    void set_stand_charge_rload(int rload);
    void set_stand_heater1_relay(int state);
    void set_stand_heater2_relay(int state);
    void set_stand_max_temper(int temper);
    void clear_mb_ps2_minmax();

    void set_rps_relay_polarity(int num,int inverse);
    void set_rps_relay_state(int state);
    void set_rps_r_load(int state);
    int is_rps_connected();
    void mb_update();

    unsigned short modbus_data[MB_MAX_ADDR];
    bool com_openned;//флаг открытия порта
    int read_flag;
    bool is_opened();

public slots:
    void readReadyCommon();
    void stand_timer_timeout(void);
    void search_timer_timeout();
    void writeFinished();
    void rpsReadRequestNew();
    void rpsStandReadRequest();
    void rpsStandComAutoconnect();
    void standComAutoconnect();
    void readReadyRPSNew();

    void com_connect();
    void com_disconnect();

signals:
    void syslog(QString str,int level);
    void rpsStandReadRPSFinished();
    void rpsStandReadStandFinished();
    void rpsStandConnectFinished();
    void repaint_slot_table_signal();

private:
    QModbusClient *modbusDevice;

    int autoconnect;
    QString modbus_port;
    QSignalMapper *signalMapper;
    QModbusReply *reply_write;
    QModbusReply *reply_one;

    int modbus_refresh_flag;
    int rps_connected;
    int simbat_test_type;

    QModbusDataUnit readRequestDevType();
    QModbusDataUnit readRequestEL60();
    QModbusDataUnit readRequestPS1();
    QModbusDataUnit readRequestPS2();
    QModbusDataUnit readRequestPS3();
    QModbusDataUnit readRequestRPS();
    QModbusDataUnit readRequestEL60V5();
    QModbusDataUnit readRequestIO02();
    QModbusDataUnit readRequestSIMBAT();
    QModbusDataUnit readRequestStandRPS();
    QModbusDataUnit writeRequest(int addr);

    //переменные для статистики из модулей
    int  connected_ok[STAND_SLOTS_NUM];
    int mb_dev_type[STAND_SLOTS_NUM];
    QString mb_cpuid[STAND_SLOTS_NUM];
    int mb_sw_vers[STAND_SLOTS_NUM];
    int mb_rs485rx[STAND_SLOTS_NUM];
    int mb_rs485tx[STAND_SLOTS_NUM];
    //для EL-60
    long mb_el60_current[STAND_SLOTS_NUM];
    int mb_el60_voltage[STAND_SLOTS_NUM];
    int mb_el60_current_max[STAND_SLOTS_NUM];
    int mb_el60_voltage_max[STAND_SLOTS_NUM];
    int mb_el60_current_min[STAND_SLOTS_NUM];
    int mb_el60_voltage_min[STAND_SLOTS_NUM];
    int mb_el60_out_pwr[STAND_SLOTS_NUM];//выходная мощность нагрузки
    int mb_el60_out_en[STAND_SLOTS_NUM];//включения нагрузки
    int mb_el60_temper[STAND_SLOTS_NUM];//температура радиатора
    int mb_el60_fan_en[STAND_SLOTS_NUM];
    int mb_el60_fan_err[STAND_SLOTS_NUM];
    int mb_el60_fan_pwm[STAND_SLOTS_NUM];
    int mb_el60_offline_mode[STAND_SLOTS_NUM];
    int mb_el60_offline_pwr[STAND_SLOTS_NUM];
    int mb_el60_passive_en[STAND_SLOTS_NUM];

    int mb_ps1_ac1_state;
    int mb_ps1_ac2_state;
    int mb_ps1_dc1_state;
    int mb_ps1_dc2_state;
    int mb_ps1_r_bat;
    int mb_ps1_i_bat;
    int mb_ps1_i_heater;
    int mb_ps1_voltage;//фактическое
    int mb_ps1_set_voltage;//уставка
    int mb_ps1_current;
    int mb_ps1_current_min;
    int mb_ps1_current_max;

    //плата PS-2
    int mb_ps2_ac1_state;
    int mb_ps2_ac2_state;
    int mb_ps2_dc1_state;
    int mb_ps2_dc2_state;
    int mb_ps2_charge_key_state;
    int mb_ps2_charge_voltage;
    int mb_ps2_charge_current;
    int mb_ps2_charge_voltage_min;
    int mb_ps2_charge_voltage_max;
    int mb_ps2_charge_rload;//установка номинала резистора нагрузки для измерения тока заряда
    int mb_ps2_discharge_key_state;
    int mb_ps2_discharge_voltage;
    int mb_ps2_discharge_current;
    int mb_ps2_discharge_voltage_min;
    int mb_ps2_discharge_voltage_max;
    int mb_ps2_heater_relay_state;
    int mb_ps2_heater_current;
    int mb_ps2_temper;
    int mb_ps2_max_temper;
    int mb_ps3_heater2_relay;
    int mb_ps3_heater2_current;

    //EL-60v5
    int mb_el60v5_current_a[STAND_SLOTS_NUM];
    int mb_el60v5_current_b[STAND_SLOTS_NUM];
    int mb_el60v5_voltage_a[STAND_SLOTS_NUM];
    int mb_el60v5_voltage_b[STAND_SLOTS_NUM];
    int mb_el60v5_temper_a[STAND_SLOTS_NUM];
    int mb_el60v5_temper_b[STAND_SLOTS_NUM];
    int mb_el60v5_out_en_a[STAND_SLOTS_NUM];
    int mb_el60v5_out_en_b[STAND_SLOTS_NUM];
    int mb_el60v5_pwr_a[STAND_SLOTS_NUM];
    int mb_el60v5_pwr_b[STAND_SLOTS_NUM];

    //IO-02
#define IO02_INPUT_NUM  11//число входов
    int mb_io02_input[IO02_INPUT_NUM];
#define IO02_OUT_NUM  7
    int mb_io02_output[IO02_OUT_NUM];

    //SIMBAT
    int mb_simbat_charge_state;
    int mb_simbat_charge_voltage;
    int mb_simbat_charge_current;
    int mb_simbat_charge_voltage_max;
    int mb_simbat_charge_voltage_min;
    int mb_simbat_discharge_state;
    int mb_simbat_discharge_voltage;
    int mb_simbat_discharge_current;
    int mb_simbat_discharge_voltage_max;
    int mb_simbat_discharge_voltage_min;
    int mb_simbat_charge_rload;
    int mb_simbat_temper1;
    int mb_simbat_temper2;
    int mb_simbat_max_temper;
    int mb_simbat_int;
    int mb_simbat_fan;
};

#endif // MODBUS_H
