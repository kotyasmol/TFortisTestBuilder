#ifndef MAINWINDOW_H
#define MAINWINDOW_H

#include "TestThread.h"
#include <QMainWindow>
#include <QPushButton>
#include <QLineEdit>
#include <QVBoxLayout>
#include <QTableWidget>
#include <QtWidgets>
#include <QSqlDatabase>
#include <QSqlError>
#include <QSqlQuery>
#include <QNetworkReply>
#include <pcap.h>
#include <QDomNode>
#include "DataTest/DataTestThread.h"
#include "Printer/LabelThread.h"
#include <QtTelnet>
#include <QModbusReply>
#include "Dialogs/devicelistdialog.h"
#include <QNetworkConfigurationManager>
#include "rps_test.h"
#include "debugwindow.h"
#include "dfumultiloadthread.h"

namespace Ui {
class MainWindow;
}

pcap_if_t *get_card_if_gen(pcap_if_t *alldev,int index);
port_if_t get_card_if_ip(QString addr);

int get_dev_name(settingsmodel program_sett,int i,QString *tmp);
int get_dev_type(settingsmodel prog_sett,int i);
int get_dev_type_by_name(settingsmodel prog_sett,QString name);

void clear_old_parse_struct();

void packet_handler(u_char *param, const struct pcap_pkthdr *header, const u_char *pkt_data);
html_parse_struct traverseNodeHP(const QDomNode& node);

class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    explicit MainWindow(QWidget *parent, int argc,char *argv[]);
    ~MainWindow();


    struct configmodel test_config;
    struct configmodel_rps test_config_rps;
    struct settingsmodel prog_sett;
    struct html_parse_struct psw_selftest_result;//результаты самотестирование psw (test.shtml)
    QSerialPort *serial; // Здесь хранится наш порт
    QSerialPort *bercut_serial; // порт для беркута 100
    QSerialPort *bercut100_serial; // порт для беркута 100
    QSerialPort *switch_serial; // Здесь хранится наш порт
    QSerialPort *teleport_serial;
    struct device_info_t device_info;

    struct label_info_t label_info;
    char session_id[128];
    QPushButton *dev_buttons[12];
    QWidget *dfuLoadStatus[5];


    QNetworkAccessManager *webmanager;//для подключения к web интерфейсу
    QNetworkReply *webreply;

    QNetworkAccessManager  *reportmanager;//для отправки отчетов
    QNetworkReply *reportreply;

    QUdpSocket *socket;

    void open_last_config();
    void save_last_config();
    int open_config_name(struct configmodel *config,QString name);
    int open_config_rps(QString name);
    bool SerialValidation(long long serial);
    QString get_rps_config_path();
    QGraphicsItem *dev_rect[STAND_SLOTS_NUM];
    //QPushButton *dev_buttons[23];

    DebugWindow *debug_window;

public slots:
    void start_test();
    void make();
    void stop(void);
    //! Логгирование событий в файл и на экран
    void syslog(QString text, int level);
    void read(void);
    void exit_app(void);
    void com_sett(void);
    void com_connect(void);
    void db_sett();
    void connect_database();
    void show_database_form();
    void print_reports_list();
    void deviceMenuHandler(void);
    void showConnectMenuHandler(void);
    //! Срабатывает при окончании webmanager
    void replyFinished();
    void GetUpsStatusReplyFinished();
    void GetIrpStatusReplyFinished();
    void GetUpsVoltageReplyFinished();
    void waitHelpFinished();
    void onUploadProgress(qint64 tmp1,qint64 tmp2);
    void onFinished();
    void onNetworkError(QNetworkReply::NetworkError error);
    void serialRecieve();
    void bercutSerialRecieve();
    void bercutSerialClear();
    void bercutSerialWrite(QString data);
    void test_mode_pressed(void);
    void set_sw_test_mode(int state);
    void set_tlp_test_mode(int state);
    void set_ups_plus_out(int state);
    void set_tlp_output_state(int port,int state);
    void send_confirm(QString text,int conf);
    void get_last_serial_pressed();

    void net_card_sett();
    void profiles_sett();

    void dev_list_sett_slot();// открывает окно со списком устройств
    void grey_serial();
    void no_grey_serial();
    void save_current_item();
    void mac_sender_tools();
    void print_label_tools();
    void start_generation_tools();
    void stop_generation_tools();
    void set_generation_preset();

    //stand buttons
    void stand_ac1();
    void stand_ac1_on();
    void stand_ac1_off();
    void stand_ac2();
    void stand_ac2_on();
    void stand_ac2_off();
    void stand_dc1();
    void stand_dc1_on();
    void stand_dc1_off();
    void stand_dc2();
    void stand_dc2_on();
    void stand_dc2_off();
    void stand_akb();
    void stand_akb_on();
    void stand_akb_rload_on();
    void stand_akb_rload_off();
    void stand_akb_direction();
    void stand_akb_off();
    void stand_all_off();
    void stand_io02_out1_clicked();
    void stand_io02_out2_clicked();
    void stand_io02_out3_clicked();
    void stand_io02_out4_clicked();
    void stand_io02_out5_clicked();
    void stand_io02_out6_clicked();
    void stand_io02_out7_clicked();
    void stand_io02_rs485_test_start();

    //new prs stand
    void set_rps_stand_akb_state(int state);
    void set_rps_stand_akb_polarity(int state);
    void set_rps_rload_value(int value);
    void set_rps_rload_state(int state);
    void set_rps_preheating(int value);
    void set_rps_latr_state(int state);
    void set_rps_380_state(int state);
    void rps_read_rps();
    void rps_read_stand();
    void read_stand(int slot);
    void set_rps_relay1(int state);
    void set_rps_relay2(int state);

    void stand_ps2_rload(void);
    void stand_simbat24_rload(void);
    void stand_simbat48_rload(void);
    void simbat24_charge_relay();
    void simbat48_charge_relay();
    void simbat24_discharge_relay();
    void simbat48_discharge_relay();
    void stand_poe_load_on();
    void stand_poe_load_off();
    void stand_heater1_relay();
    void stand_heater2_relay();

    void rps_stand_painting(int stage);
    void mb_clear_minmax(int slot);
    void mb_set_el60_out(int slot,int state);
    void mb_set_el60_power(int slot,int power);
    void mb_set_el60_power_to_all(int power);
    void mb_set_el60_passive(int slot,int state);
    void set_stand_charge_key(int state);
    void set_stand_discharge_key(int state);
    void set_stand_charge_rload(int rload);
    void set_stand_heater1_relay(int state);
    void set_stand_heater2_relay(int state);
    void set_stand_max_temper(int temper);
    void clear_mb_ps2_minmax();

    void printer_sett();
    void bercut_sett();
    void switch_sett();
    void teleport_sett();
    void rps_stand_sett();
    void power_supply_sett();
    void programmers_sett();
    void programmers_fw_view();
    void programmers_dfu_start();
    void programmers_avr_start();
    void manual_view();

    void id_label_print(void);
    void marker_label_print(void);
    void print_id_label(int id);

    //! Печать этикеток без теста
    void PrintLabel();
    void PrintLabelFromFile();
    void PrintLabelFromFilePath();
    void get_next_ident(void);
    void set_serial_num(int serial,int type,int id,QString date);
    void show_label_print_rezult(QString str);

    void set_stage_result(int num,int state);
    void set_poeport_result(int num,int state);
    void set_dataport_result(int num, int state);
    void set_tlp_input_result(int num,int state);
    void set_tlp_output_result(int num,int state);
    void set_rps_result(int num,int state);

    void SetStagesToDefault();

    void get_test_shtml();
    //! Получение тестовой страницы
    void GetTestPage(int); // NOTE added SUVOROV
    void GetUpsStatus();
    void GetIrpStatus();
    void GetUpsVoltage();

    void get_help_html();
    void upload(QString ad);//upload firmware file to device

    void confirm();//emul: press confirm button on webinterface
    void update_clear();

    void get_serial_num(QString dev_name ,QString cpu_id);
    void getSerialFinished();

    void check_serial_num(QString cpu_id);
    void checkSerialFinished();

    void getLastSerialNum(QString devType);
    void getLastSerialFinished();

    void setTestResultFinished();
    void getNextIdentFinished();
    void setSerialFinished();

    void set_test_result(void);

    void generator_print_msg(QString str);

    void test_finished(int errorcode,int serial);

    void print_label(struct label_info_t *label_info);
    void print_label_retry(void);

    void send_report_f(QString str,int ok,float val);
    void send_report_d(QString str,int ok,int val);
    void send_report_s(QString str,int ok,QString val);
    void send_report_b(QString str,int ok,int val);
    void send_session(QString val);
    void teport_clear();

    void reportReplyFinished();
    void reportConnectFinished();
    void reportConnectError(QNetworkReply::NetworkError error);
    void reportSslConnectError(const QList<QSslError> &errors);
    void reportError(QNetworkReply::NetworkError error);
    //bercut
    void bercut_test_start(void);
    void bercut_test_stop(int status);
    void bercut_test_stop_btn(void);
    void bercut_timer_timeout();

    void teleport_start();
    void errorString(QString text);

    void telnet_config_pair(int rx,int tx);
    void telnet_config_sw(int *ports,int port_sw);
    void telnet_config_chain(QString str);

    void teleportSerialRecieve(void);
    void tlp_send_rs485_hello();
    void repaint_stand_slots(void);
    void repaint_stand_one_slot();
    void read_stand_all_slots(void);
    void read_stand_one_slot();
    void show_modbus_table(int slot);
    void create_modbus_table();
    void modbus_set_current();
    void modbus_set_current_all();
    void set_theme();

    //Код, который добавил Suvorov
    void ShowHideBoxIdWhenSelectionChanged(); // Считывание конфигурации устроства при смене выделенного устройства
    void ChangeCheckedRadioButtonTypeTest(QString);  // Переключение состояния радикнопок. Связан с сигналом serial::textEdited()
    void setTestModeFinished();

    bool changeTheme(); //смена темы

private:
    Ui::MainWindow *ui;

    QNetworkCookieJar* cookieJar;
    QTimer timer;
    QTimer *timerForWebManager;
    QTimer *timerForLoading;
    QTimer tmp_timer;
    QTimer repaint_timer;
    QTimer bercut_timer;
    QDialog *com_sett_dlg;
    QGraphicsScene *scene,*scene_new;

    QThread *dfuLoadThread;

    pcap_if_t *alldev;

    DataTestThread *pDataTestThread;// генерация трафика
    TestThread *pTestThread;

    dfuMultiLoadThread *dfuMultiLoad;

    LabelThread *pLabelThread;

    QProcess *term_command;
    QProcess *dfu_command;
    QProcess *multi_dfu_scan_command;
    QProcess *multi_dfu_load_command[5];


    double array[100];
    int cur_row;
    int _port;
    struct in_psw_msg in_msg;
    char com_data[16];
    char bercut_com_data[64000];
    char switch_com_data[64000];
    char teleport_com_data[64000];

    QAction             *loadAction;
    QAction             *saveAction;
    QAction             *exitAction;
    QAction             *ComSetAction;
    QAction             *connectAction;
    QAction             *DatabaseSetAction;
    QAction             *DBconnectAction;
    QAction             *UserConnectAction;
    QAction             *DeviceBrowser;
    QAction             *PrintReports;
    QAction             *NetCardSettAction;
    QAction             *UserSettAction;
    QAction             *PrinterSetAction;
    QAction             *ProfileSetAction;
    QAction             *BercutSetAction;
    QAction             *PowerSupplySetAction;
    QAction             *SwitchSetAction;
    QAction             *DevListSetAction;
    QAction             *TeleportSetAction;
    QAction             *ProgrammersSetAction;
    QAction             *FirmwareViewAction;
    QAction             *ManualViewAction;
    QAction             *ChangeThemeAction;
    QMenu               *fileMenu;
    QAction             *openConfigAction;
    QAction             *showConnectAction;
    QTableWidget        *tableInfoWidget;
    QTableWidgetItem    *item0[MODBUS_TABLE_SIZE];
    QTableWidgetItem    *item1[MODBUS_TABLE_SIZE];
    QHBoxLayout         *table_layout;

    pcap_t              *pcapd_gen;
    pcap_t              *pcapd_capt;
    QTimer               gen_timeout;

    QSignalMapper *dfuLoadSignalMapper;

    int dfuLoaded;
    int dfuCount;

    int num,error_code,last_num;/*для задачи тестирования*/

    struct capture_result_t capture_result;

    QString test_report;//содержимое отчета для передачи на сервер
    QString current_file_name; //имя файла, получаемое из строки

    telnet *telnet_dev;

    void create_UDPsocket(int port);
    void initSerial(); // Инициализации посл. порта
    void deinitSerial(); // И деинициализация (закрытие) порта

    void bercut_com_connect(); // Инициализации посл. порта
    void bercut100_com_connect();
    void teleport_com_connect();
    void switchInitSerial(); // Инициализации посл. порта
    void switchDeinitSerial(); // И деинициализация (закрытие) порта

    void PoeDisable(int lineIndex);

    void create_ui_menu(void);

    void get_checkbox_state(int *ports, int type);
    void set_checkbox_state(int *ports, int type);

    void generator_tab_config(void);
    void refresh_device_list_ui();

    //testing

    configmodel parse_config_json(QJsonObject jsonObject);
    configmodel_rps parse_config_rps_json(QJsonObject jsonObject);

    //modbus parts
    QModbusDataUnit readRequest();
    void set_el60_current(int addr, int current);
    void set_el60_passive(int addr, int state);
    void set_ps1_voltage(int addr, int voltage);

    //Код, который добавил Suvorov
    bool testRunning; // показывает, был ли запущен тест
    bool stopTheTest;    // остановка теста

    bool themeDark; //false - светлая, true - темная
    int poe_count;
    int data_count;
    int poe_progress;
    int data_progress;


    //! Типы стадий тестирования
    enum StageTypes
    {
        poe,
        selfTest,
        updateFirmware,
        dataTest,
        ups,
        setMac,
        printLabel,
        sendReport,
        inOut,
        acBackup,
        heater
    };

    void selectRowInDeviceList(int row); // Выделение строки в device_list из предыдущего сеанса
    void clearOldResult(); // Очистка формы от старых результатов

    //! Запись строки в лог-файл
    void writeLineToLogFile(QString);
    //! Запись строки в окно вывода лога
    void writeLineToWindow(QString);
    
    void startPing();
    void commandPrint();
    void ParseDfuLoadResult1();
    void ParseDfuLoadResult2();
    void ParseDfuLoadResult3();
    void ParseDfuLoadResult4();
    void ParseDfuLoadResult5();

    void hide_restart_buttons();

    void commandMultiLoadPrint();
    void parseDfuList(QString found_devices);

    void debuglog(QString text);

signals:

    void start_dfu_loading(QProcess *loading_processes[]);
    void multi_laod_started(QString[], QString);

private slots:
    //! Очистка поля ввода серийного номера
    void ClearSerialNumberField();

    //!Сделать активной кнопку Тест
    void EnableBtnStartTest();

    //стенд RPS-1
    //! Запуск теста
    void runRpsTest();
    //! Остановка теста
    void stopRpsTest();
    //! Окончание теста
    void testRpsDone(int status);
    //new
    void runRpsTestNew();
    void stopRpsTestNew();
    void rpsStandAkbStatePressed();
    void rpsStandPolarityPressed();
    void rpsStandRloadSetPressed();
    void rpsStandRloadStatePressed();
    void rpsStandPreHeatPressed();
    void rpsStandLatrPressed();
    void rpsStand380VPressed();
    void rpsStandReadStandPressed();
    void rpsStandReadRPSPressed();
    void rpsStandConnectFinished();
    void rpsStandReadStandFinished();
    void rpsStandReadRPSFinished();

    void on_device_list_currentRowChanged(int currentRow);

    void on_dev_button1_clicked();
    void on_dev_button3_clicked();
    void on_dev_button5_clicked();
    void on_dev_button7_clicked();
    void on_dev_button9_clicked();
    void on_dev_button11_clicked();
    void on_dev_button13_clicked();
    void on_dev_button15_clicked();
    void on_dev_button17_clicked();
    void on_dev_button19_clicked();
    void on_dev_button21_clicked();
    void on_dev_button23_clicked();
    void on_dfu_search_btn_clicked();
    void on_dfu_load_btn_clicked();
    void on_reset_dfu_load_btn_clicked();
    void on_restart_from_selftest_btn_clicked();
    void on_restart_from_update_btn_clicked();
    void on_restart_from_heater_btn_clicked();
    void on_restart_from_poe_btn_clicked();
    void on_restart_from_ps_btn_clicked();
    void on_restart_from_inout_btn_clicked();
    void on_restart_from_data_btn_clicked();
    void on_restart_from_ups_btn_clicked();
    void on_restart_from_mac_btn_clicked();
    void on_restart_from_label_btn_clicked();
    void on_restart_from_report_btn_clicked();
    void on_GetTestPageButton_clicked();
    void on_LoadFirmvare_stateChanged(int state);
};

#endif // MAINWINDOW_H
