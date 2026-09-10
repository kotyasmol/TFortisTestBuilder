#ifndef TESTTHREAD_H
#define TESTTHREAD_H

#include <QThread>
#include <QObject>
#include <QProcess>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QNetworkAccessManager>
#include <QUdpSocket>
#include <QSqlDatabase>
#include "DataTest/DataTestThread.h"
#include "DataTest/BercutThread.h"
#include <QTimer>
#include "configmodel.h"
#include "modbus_dev.h"
#include "settingsmodel.h"
#include "telnet.h"
#include "constants.h"
#include "debugwindow.h"
#include "dfumultiloadthread.h"


#define WINDOW_TITLE    "TFortis Stand New v6.6.7"

#define DUT_IP_ADDR "192.168.0.1"

#define SERIAL_OLD(x) (x<100000)

#define SERVICE_PREFIX 900000000

#define SERIAL_2OLD(type,serial) serial - type*100000

#define SERIAL_2NEW(type,serial) type*100000 + serial

#define myDebug() qDebug()

#define ENDTEST 99999

#define WAIT_CONFIRM(text) \
    confirmed = 0;\
    emit send_confirm(text,1);\
    while(confirmed == 0){\
    Sleep(1*SEC);\
    qDebug() << confirmed;\
    }

#define WAIT_OK(text) \
    confirmed = 0;\
    emit send_confirm(text,0);\
    while(confirmed == 0){\
    Sleep(1*SEC);\
    qDebug() << confirmed;\
    }

#define PROG_DEBUG      1//1 - отладка/0 - выпуск

#define TYPE_PRODUCTION 0
#define TYPE_REPAIR     1
#define TYPE_TELEPORT   2

#define TYPE_FIRST      0
#define TYPE_SECOND     1

#define TYPE_SOFT_GEN   0
#define TYPE_HARD_GEN   1
#define TYPE_HARD_CHAIN 2

#define ROOT_POSITION   USERS_NUM-1//root будет всегда последний

#define DATA_TEST_LINES 10//число сетевых карт на ПК

#define STAND_PORT_NUM  8//число  в стенде

#define PSW_PORT        6123

#define CAPTURE_LEN             10000//число пакетов на тест передачи данных
#define CAPTURE_MAXTIMOUT_LEN   100  //завершение теста при таком числе непринятых пакетов

//msg type
#define PSW_REQUEST     0xE0
#define PSW_RESPONSE    0xE1

//udp ports for trafic generator
#define SRC_PORT        0xABBA
#define DST_PORT        0xABBA

#define MAX_CNT 1000

#define DB_HTTP   0
#define DB_HTTPS  1

#define SEC 1000
#define KOSTYL  50//дополнительное расширение диапазона

//пороги выбраны исходя из рук. по настройке +-50мВ -- исходя из даташита на marvell
#define CONST_1V(x)    ((x > (1240+62+KOSTYL)) || (x < (1240-62-KOSTYL)))
#define CONST_1_1V(x)    ((x > (1000)) || (x < (1200)))
#define CONST_1_2V(x)  ((x > (1488+/*62*/89+KOSTYL)) || (x < (1488-/*62*/74-KOSTYL)))
#define CONST_1_5V(x)  ((x > (1860+/*62*/111+KOSTYL)) || (x < (1860-/*62*/93-KOSTYL)))
#define CONST_1_8V(x)  ((x > (2232+/*62*/111+KOSTYL)) || (x < (2232-/*62*/111-KOSTYL)))
#define CONST_2_5V(x)  ((x > (3100+/*62*/155+KOSTYL)) || (x < (3100-/*62*/155-KOSTYL)))

#define POE_VOLTAGE_NORM(x) (x>20)&&(x<65)
#define POE_TEST_SUCCESS 100


#define NORMAL_POLARITY     0
#define INVERSE_POLARITY    1


enum SelfTestStatus
{
    Ok=0,
    IncorrectDevType,
    Other
};

struct in_psw_msg{
    unsigned char type;
    unsigned char dev_type;
    unsigned char ip[4];
    unsigned char mac[6];
    char dev_descr[128];
    char dev_loc[128];
    unsigned long uptime;
    unsigned long firmware;
};

struct html_parse_struct{
    unsigned char dev_type;
    unsigned char init_ok;
    QString  firmvare_vers;
    int hw_vers = 0;
    unsigned int  boot_vers;
    unsigned int  serial_num;
    QString       default_mac;//text
    QString       cpu_id;//text
    unsigned char link[PORT_NUM];
    unsigned char poe_a_st[PORT_NUM];
    unsigned char poe_b_st[PORT_NUM];
    float         poe_a_v[PORT_NUM];
    float         poe_b_v[PORT_NUM];
    unsigned int  poe_a_c[PORT_NUM];
    unsigned int  poe_b_c[PORT_NUM];
    unsigned char board_version;
    unsigned char poe_controller;
    unsigned char sensor_0;
    unsigned char sensor_1;
    unsigned char sensor_2;
    unsigned char sfp_pres[PORT_NUM];
    unsigned char sfp_sd[PORT_NUM];
    unsigned char sfp_id[PORT_NUM];
    unsigned int  marvell_id;
    unsigned int  adc_1_0;
    unsigned int  adc_1_1;
    unsigned int  adc_1_2;
    unsigned int  adc_1_8;
    unsigned int  adc_1_5;
    unsigned int  adc_2_5;
    unsigned char ups_det;
    unsigned char akb_det;
    unsigned char ups_rez;
    float         akb_voltage;
    float         akb_voltage_chg;
    unsigned char tlp_input[TLP_INPUTS_NUM];

    unsigned int temperature;
    unsigned int humidity;
};

struct nettest_t{
    bool connected; // true if connected
    int  testrezult;//rezult of test
    bool macset;//true if mac setted
    bool parsed;//test shtmp parsed ok
    bool help_loaded;//true if help pages is loaded

    bool get_serial;//запрос на выдачу серийного номера
    quint32 serial_num;

    bool get_id;//запрос на выдачу ID
    quint32 id;

    bool set_serial;

    bool send_report;
    bool send_report_rezult;
    bool set_serial_db;//серийный номер сохранён в БД
};

struct device_info_t{
    quint32 serial_num;
    int dev_type;
    QString cpu_id;
    QString date_work;
    QString date_sale;
    QString date_repair;
    QString comment;
    QString user;
    quint8 mac[6];
};

struct actual_test_struct{
   bool selftest;
   bool update;
   bool heater;
   bool poe;
   bool acBackup;
   bool inOut;
   bool dataTest;
   bool ups;
   bool setMac;
   bool printLabel;
   bool sendReport;
//   bool tlpRS485;
//   bool i2c;
//   bool dryCont1;
//   bool dryCont2;
//   bool dryCont3;

};


struct label_info_t{
    quint32 serial_num;//серийник
    int dev_type;//тип
    quint8 mac[6];//МАС
    QString equipment_str;//текстовое описание исполнения
    int equipment_type;//номер исполнения
    bool equipment_field_use;//использовать дополниельное поле комплектация в штрихкоде
    QString dev_name;//имя устройства
    int num;//число этикеток
    bool print_status;//статус печати
    bool retry;

};

struct test_preconfig_t{
    bool serial_db;
    bool serial_form;
    int serial_num;
};

struct capture_result_t{
    long transmitted_pkts;
    long recieved_bytes;//число принятых байт
    long recieved_pkts;//число принятых пакетов
    long tiemout_pkts;//число непринятых пакетов
    QDateTime start_time;//начало теста
    QDateTime stop_time;//завершение теста
    long speed;
};

class TestThread: public QThread
{
    Q_OBJECT
public:
    //prototypes
    TestThread(settingsmodel sett);
    virtual ~TestThread();

    void stop();
    void start_test();
    void clear_arp_case();
    void set_test_config(struct configmodel test_config);
    void set_test_config_rps(struct configmodel_rps  test_config_rps_);
    void set_prog_sett(settingsmodel sett);
    bool parse_html_file(QByteArray array);
    int  get_selftest_result(void);
    void send_com_data(char *data);
    int convert_poe_voltage(char *data);
    void set_ports(ports_pair_t *ports_);
    void get_ports(ports_pair_t *ports_);
    bool is_running();
    void set_type(int type);
    void data_start();
    void data_stop();
    long unsigned  get_recieved_pkt(int port);
    long unsigned  get_transmitted_pkt(int port);
    long unsigned  get_recieved_speed(int port);

    QProcess *multiLoadProcesses[5];

    void print_service_label();

    void set_test_shtml(bool value);
    void send_set_mac(QUdpSocket *socket,struct device_info_t *device_info);
    bool TestIsRunning();

    void bercut_start(int test_type);

    bool check_rpsstand_minmax_param(int mb_addr, int max_value,int min_value, int timeout);
    bool check_rpsstand_param(int mb_addr, int param, int timeout);
    bool check_rps01_minmax_param(int mb_addr, int max_value,int min_value, int timeout);
    bool check_rps01_param(int mb_addr, int param, int timeout);
    bool check_stand_minmax_param(int slot,int mb_addr, int max_value,int min_value, int timeout);

    bool check_stand_param(int slot,int mb_addr, int param, int timeout);   

    //variables
    QUdpSocket *socket;
    struct nettest_t nettest;
    int uploading;
    int downloading;
    int loadFirmvare = 0; //временные изменения для загрузки ПО перед самотестированием
    struct html_parse_struct psw_selftest_result;//результаты самотестирование psw (test.shtml)
    struct test_preconfig_t test_preconfig;
    struct device_info_t device_info;
    struct actual_test_struct test_struct;
    struct label_info_t label_info;
    QSqlDatabase db;
    int db_connected;
    BercutThread *pBercutThread;

    int confirm_status;
    int confirmed;
    QString teleport_com_data;
    modbus_dev *dev;
    DataTestThread *pDataTestThread;

    dfuMultiLoadThread *dfuMultiLoad;

    void setWebmanagerFinished();
    void setTestPageRequestResult(TestPageRequestResult);
    void requestTestPagePro();
    void setTestPageData(QByteArray data);
    void SetUpsStatus(int status);
    void SetIrpStatus(int status);
    void SetUpsVoltage(double voltage);

    void setStatusSendReport(bool);
    void initTestStruct(struct configmodel config);

    void LoadTestPageIfNeeded();
    //! Отключаем питание стенда
    void StandOff();

    void rps_test_start();
    void rps_test_stop();

    void set_stand_ac1_state(int state);
    void set_stand_ac2_state(int state);
    void set_stand_sensor1(int state);
    void set_stand_sensor2(int state);
    void set_stand_poe_power(int port, int power);
    void configure_stand_poe();
    bool check_poe_configuration();


    void set_stand_poe_passive(int port, int state);
    void set_stand_akb_state(int state);
    void set_stand_akb_direction(int state);
    void set_stand_discharge(int state);

    void disable_io02_outputs();
    bool check_io02_input(int input);
    bool check_sensor1();
    bool check_sensor2();

    DebugWindow *debug_window;

    void debuglog(QString text);
    void debug_set_ac1_state(bool state);
    void debug_set_ac2_state(bool state);
    void debug_set_sensor1_state(bool state);
    void debug_set_sensor2_state(bool state);
    void debug_set_simbat24_charge_state(bool state);
    void debug_set_simbat24_discharge_state(bool state);
    void debug_set_simbat48_charge_state(bool state);
    void debug_set_simbat48_discharge_state(bool state);
    void debug_set_heater1_state(bool state);
    void debug_set_heater2_state(bool state);
    void debug_set_io02_out1_state(bool state);
    void debug_set_io02_out2_state(bool state);
    void debug_set_io02_in1_state(bool state);
    void debug_set_rs485_state(bool state);
    void debug_set_i2c_state(bool state);

    int compare_FwVersion(QString selftest_vers,QString config_vers);
    bool is_model_type_PRO(QString model_name);
    bool setProMacAddress(QString& macAddress, QString& model);

public  slots:
    void process();

    void onUploadProgress(qint64 tmp1,qint64 tmp2);

    void runMultiLoad(QString devNums[]);

    void onNetworkError(QNetworkReply::NetworkError error);
    void print_msg(QString str);
    void send_report();
    void syslog_(QString str,int level);
    void bercutSerialRecieve();
    void generator_print_msg_(QString str);
    void telnet_config_pair_();
    void bercut_timer_timeout();

signals:

    void set_mb_io02_out1();
    void set_mb_io02_out2();


    void finished();

    void signal_debug_ac1();

    void test_finished(int errorcode,int serial);
    void syslog(QString str,int level);
    void set_stage_result(int num,int state);
    void set_poeport_result(int num,int state);
    void set_dataport_result(int num,int state);
    void set_tlp_input_result(int num,int state);
    void set_tlp_output_result(int num,int state);
    void set_rps_result(int num,int state);
    void stand_ac1_on();
    void stand_ac1_off();
    void stand_ac2_on();
    void stand_ac2_off();
    void stand_dc1_on();
    void stand_dc1_off();
    void stand_dc2_on();
    void stand_dc2_off();
    void stand_akb_on();
    void stand_akb_rload_on();
    void stand_akb_rload_off();
    void stand_akb_direction();
    void stand_akb_off();
    void stand_all_off();
    void read_stand_signal(int slot);

    //rps stand new
    void set_rps_stand_akb_state(int state);
    void set_rps_stand_akb_polarity(int state);
    void set_rps_rload_value(int value);
    void set_rps_rload_state(int state);
    void set_rps_preheating(int value);
    void set_rps_latr_state(int state);
    void set_rps_380_state(int state);
    void rps_read_stand_signal();
    void rps_read_rps_signal();
    //rps-01
    void set_rps_relay1(int state);
    void set_rps_relay2(int state);

    void mb_set_el60_out(int slot, int state);
    void mb_set_el60_power(int slot, int power);
    void mb_set_el60_power_to_all(int power);

    void mb_set_el60_passive(int slot, int state);

    //io-02 signals
    void io02_rs485_test_start();


    void get_test_shtml();
    void get_help_html();
    void signal_GetTestPage(int);
    void signal_GetUpsStatus();
    void signal_GetIrpStatus();
    void signal_GetUpsVoltage();
    void disablePoeLine(int line);

    //ps-2 signals
    void set_stand_charge_key(int state);
    void set_stand_discharge_key(int state);
    void set_stand_charge_rload(int rload);
    void set_stand_heater1_relay(int state);
    void set_stand_heater2_relay(int state);
    void set_stand_max_temper(int temper);
    void clear_mb_ps2_minmax();

    void get_serial_num(QString dev_name,QString cpu_id);
    void check_serial_num(QString cpu_id);
    void set_test_result();
    void get_serial_id();
    void set_serial_num(int serial,int type,int id,QString date);

    void upload(QString path);
    void confirm();
    void update_clear();

    //to database
    void send_report_f(QString var,int ok,float value);
    void send_report_d(QString var,int ok,int value);
    void send_report_s(QString var,int ok,QString value);
    void send_report_b(QString var,int ok,int value);
    void send_session(QString value);
    void teport_clear();

    void print_label(struct label_info_t *label_info);

    //bercut test
    void bercutSerialWrite(QString data);
    void set_sw_test_mode(int state);//включение тестового режима на коммутаторах

    //teleport
    void set_tlp_test_mode(int state);
    void set_ups_plus_out(int state);
    void set_tlp_output_state(int port,int state);

    void send_confirm(QString text,int confirm);
    void tlp_send_rs485_hello();

    //Suvorov
    void ClearSerial();

    void rps_stand_painting(int);
    void rpsTestDone(int flag);



private:
    bool rpsStartFlag;

    QProcess prog;
    bool testPageParsed;
    char com_data[64];
    char bercut_com_data[64];

    bool com_flag;
    struct configmodel test_config;
    struct configmodel_rps test_config_rps;
    struct settingsmodel prog_sett;
    bool  stop_test;
    bool  start_test_flag;
    bool testPageWasParsed = false;
    double poeVoltage = 0;
    double poeCurrent = 0;

    int timer_cnt;
    bool webmanagerFinished;

    //! Результат запроса тестовой страницы
    TestPageRequestResult testPageRequestResult = TestPageRequestResult::FAILURE;
    //! Хранилище для тестовой страницы
    QByteArray testPageData;
    //! Храним состояние галочки, отправлять ли отчет на сервер при ремонте
    bool sendReportStatus = false;

    void make_report(int status,int type, int serial);
    int convert_poe2port(int port);

    int add_device_db(struct device_info_t *device_info);
    int bercut_test_compleat();

    //! Запрос test.shtml
    //! Возвращает true/false - результат запроса страницы
    bool requestTestPage(int timeoutMs = 10000);
    bool resultRequestTestPage;
    //! Ожидание запуска устройства путем запроса тестовой страницы
    void waitingStartDevice(int timeout);
    //! запрос статуса IRP
    void GetIrpStatus();
    //! Запрос статуса работы от ИБП
    void GetUpsStatus();
    //! Запрос напряжения на АКБ
    void GetUpsVoltage();

    void sendReport();

    //!Стадии тестирования

    void PrimarySetting();

    void SelfTestStage();

    void HeaterTestStage();

    void PowerSupplyBackupStage();

    void PrintLabelStage();

    void InOutTestStage();

    //!Обновление коммутатора
    void UpdatePSW();

    //! Тест PoE
    int PoeTest();
    //! Тест PoE линии
    bool PoeLineTest(int indexLine/*, double linesVoltageBefore[]*/);
    //! Тест PoE когда PoE выключено
    //bool PoeLineTestWhenPoeDisable(int indexLine, double linesVoltageBefore[]);
    //! Самотестирование
    //void SelfTest();
    //! Тест ИБП
    //void UpsTestPS1();
    //тест ИБП платой PS2
    void UpsTestPS2();

    //! Тест RS-485
    void RS485Test();
    //! Тест I2C
    void I2CTest();

    //! Получение серийного номера
    void GetSerialIdOrNumber();
    //! Тест MAC
    void CheckMac();
    //! Остановка теста
    void StopTest(int errorCode = 0);
    //! Ручная остановка теста
    void ManualStopTest();

    //! Пауза без блокировки
    void Pause(int msec);
    void disablePoeLoad();
    void waitingWebmanagerFinished();
    void getSerialNumber();


    void TurnOnAC();
    void TurnOffAC();


    //!формирование строки ошибки для печати сервисной этикетки
    void rps_stand_processing_new();
    void rps_stand_painting();
};


#endif // TESTTHREAD_H
