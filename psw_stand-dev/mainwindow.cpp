#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "functions.h"
#include <cstdio>
#include <cstring>
#include <cmath>
#include <QFileDialog>
#include <QMessageBox>
#include <QFile>
#include <QDoubleSpinBox>
#include <QGraphicsScene>
#include <QTimer>
#include <QDebug>
#include <QUdpSocket>
#include <QAbstractItemView>
#include <QAbstractItemDelegate>
#include "QtXml/QtXml"
#include "QtXml/QDomDocument"
#include "QtXml/QDomElement"
#include "QtXml/QDomNode"
#include <QtXml/qxml.h>
#include <QProcess>
#include <QMenuBar>

#include <iostream>
#include "ui.h"
#include <QNetworkAccessManager>
#include <QNetworkCookieJar>

#include <QNetworkRequest>
#include <pcap.h>
#include <QNetworkInterface>
#include <QTextCodec>

#include <QSerialPort>
#include <QSerialPortInfo>
#include <QPrinterInfo>

#include <QModbusDevice>
#include <QModbusClient>
#include <QModbusRtuSerialMaster>

#include "modbus_dev.h"

#include "telnet.h"
#include "rps_test.h"
#include "dfumultiloadthread.h"

#include "debugwindow.h"

pcap_if_t *get_card_if_gen(pcap_if_t *alldev,int index);
int is_poe_load_configured;

void MainWindow::startPing()
{
    term_command = new QProcess(this);
    term_command->setProcessChannelMode(QProcess::MergedChannels);
    connect(term_command, &QProcess::readyReadStandardOutput, this, &MainWindow::commandPrint);
    term_command->start("ping", QStringList() << DUT_IP_ADDR << "-t");

}

void MainWindow::commandPrint()
{
    QByteArray output = term_command->readAllStandardOutput();
    QString line =  Functions::BytesFromCP866toUnicode(output);
    QDateTime now = QDateTime::currentDateTime();
    QString text = QString("%1 %2")
            .arg(now.toString("hh:mm:ss"))
            .arg(line);
    ui->pingOutputBox->append(text);
}

MainWindow::MainWindow(QWidget *parent, int argc, char *argv[]) :
    QMainWindow(parent),
    ui(new Ui::MainWindow)
{
    // Сашин код
    {
        QMessageBox msgBox;
        ui->setupUi(this);
        debug_window = new DebugWindow();
        debug_window->show();
        debug_window->setVisible(false);
        //load last config

        open_last_config();
        syslog("Загрузка настроек",I);

        set_theme();

        ui->device_list->setCurrentRow(prog_sett.last_config_num);  //Выделяем строку с девайсом из предыдущего сеанса

        webmanager = new QNetworkAccessManager(this);
        webmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);
        cookieJar = new QNetworkCookieJar(this);
        webmanager->setCookieJar(cookieJar);

        reportmanager = new QNetworkAccessManager(this);
        reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        //поток для тестирования
        auto test_thread = new QThread;
        pTestThread = new TestThread(prog_sett);
        pTestThread->moveToThread(test_thread);

        auto dfu_load_thread = new QThread;
        dfuMultiLoad = new dfuMultiLoadThread();
        dfuMultiLoad->moveToThread(dfu_load_thread);

        dfu_load_thread->start();

//        QThread* dfu_load_thread = new QThread;
//        dfuMultiLoad = new dfuMultiLoadThread();
//        dfuMultiLoad->moveToThread(dfu_load_thread);

//        dfu_load_thread->start();

        //connect(dfuMultiLoad, SIGNAL(start_dfu_loading(QProcess[])), this, SLOT(runMultiLoad(QProcess[])));

        connect(pTestThread, SIGNAL(error(QString)), this, SLOT(errorString(QString)));
        connect(test_thread, SIGNAL(started()), pTestThread, SLOT(process()));
        connect(pTestThread, SIGNAL(finished()), test_thread, SLOT(quit()));
        connect(pTestThread, SIGNAL(finished()), pTestThread, SLOT(deleteLater()));
        connect(test_thread, SIGNAL(finished()), test_thread, SLOT(deleteLater()));
        connect(pTestThread, SIGNAL(syslog(QString,int)),this,SLOT(syslog(QString,int)));
        connect(pTestThread, SIGNAL(stateChanged(int)),this,SLOT(on_LoadFirmvare_stateChanged(int)));

        if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeRPS
                || prog_sett.stand_type == StandType::typeRPSNew){
            connect(pTestThread->dev,SIGNAL(syslog(QString,int)),this,SLOT(syslog(QString,int)));
            connect(pTestThread->dev,SIGNAL(rpsStandReadRPSFinished()),this,SLOT(rpsStandReadRPSFinished()));
            connect(pTestThread->dev,SIGNAL(rpsStandReadStandFinished()),this,SLOT(rpsStandReadStandFinished()));
            connect(pTestThread->dev,SIGNAL(rpsStandConnectFinished()),this,SLOT(rpsStandConnectFinished()));
            connect(pTestThread->dev,SIGNAL(repaint_slot_table_signal()),this,SLOT(repaint_stand_slots()));
        }

        connect(pTestThread,SIGNAL(set_stage_result(int,int) ),this,SLOT(set_stage_result(int,int)));
        connect(pTestThread,SIGNAL(set_poeport_result(int,int)),this,SLOT(set_poeport_result(int,int)));
        connect(pTestThread,SIGNAL(set_dataport_result(int,int)),this,SLOT(set_dataport_result(int,int)));
        connect(pTestThread,SIGNAL(set_tlp_input_result(int,int)),this,SLOT(set_tlp_input_result(int,int)));
        connect(pTestThread,SIGNAL(set_tlp_output_result(int,int)),this,SLOT(set_tlp_output_result(int,int)));
        connect(pTestThread,SIGNAL(set_rps_result(int,int)),this,SLOT(set_rps_result(int,int)));
        connect(pTestThread,SIGNAL(test_finished(int,int)),this,SLOT(test_finished(int,int)));

        connect(pTestThread,SIGNAL(get_test_shtml()),this,SLOT(get_test_shtml()));
        connect(pTestThread, &TestThread::signal_GetTestPage, this, &MainWindow::GetTestPage); //NOTE added Suvorov
        qDebug() << connect(pTestThread, &TestThread::signal_GetUpsStatus, this, &MainWindow::GetUpsStatus);
        qDebug() << connect(pTestThread, &TestThread::signal_GetIrpStatus, this, &MainWindow::GetIrpStatus);
        qDebug() << connect(pTestThread, &TestThread::signal_GetUpsVoltage, this, &MainWindow::GetUpsVoltage);

        connect(pTestThread,SIGNAL(get_help_html()),this,SLOT(get_help_html()));
        connect(pTestThread,SIGNAL(upload(QString)),this,SLOT(upload(QString)));
        connect(pTestThread,SIGNAL(confirm()),this,SLOT(confirm()));
        connect(pTestThread,SIGNAL(update_clear()),this,SLOT(update_clear()));

        connect(pTestThread,SIGNAL(send_report_d(QString,int,int)),this,SLOT(send_report_d(QString,int,int)));
        connect(pTestThread,SIGNAL(send_report_f(QString,int,float)),this,SLOT(send_report_f(QString,int,float)));
        connect(pTestThread,SIGNAL(send_report_s(QString,int,QString)),this,SLOT(send_report_s(QString,int,QString)));
        connect(pTestThread,SIGNAL(send_report_b(QString,int,int)),this,SLOT(send_report_b(QString,int,int)));
        connect(pTestThread,SIGNAL(send_session(QString)),this,SLOT(send_session(QString)));

        connect(pTestThread,SIGNAL(teport_clear()),this,SLOT(teport_clear()));

        //получение серийного номера/мас от сервера
        connect(pTestThread,SIGNAL(get_serial_num(QString,QString)),this,SLOT(get_serial_num(QString,QString)));
        connect(pTestThread,SIGNAL(check_serial_num(QString)),this,SLOT(check_serial_num(QString)));
        connect(pTestThread,SIGNAL(set_test_result()),this,SLOT(set_test_result()));
        connect(pTestThread,SIGNAL(get_serial_id()),this,SLOT(get_next_ident()));
        connect(pTestThread,SIGNAL(set_serial_num(int,int,int,QString)),this,SLOT(set_serial_num(int,int,int,QString)));

        //печать  этикеток
        connect(pTestThread,SIGNAL(print_label(struct label_info_t *)),this,SLOT(print_label(struct label_info_t *)));
        connect(pTestThread, SIGNAL(bercutSerialWrite(QString)),this,SLOT(bercutSerialWrite(QString)));
        connect(pTestThread,SIGNAL(print_label_id(int)),this,SLOT(print_id_label(int)));

        connect(ui->printLabel_btn,  &QPushButton::clicked, this, &MainWindow::PrintLabel);

        connect(ui->serialPrintFromFilePathBtn,  &QPushButton::clicked, this, &MainWindow::PrintLabelFromFilePath);
        connect(ui->serialPrintFromFilePrintBtn,  &QPushButton::clicked, this, &MainWindow::PrintLabelFromFile);

        //printLabel_btn

        //start thread
        test_thread->start();

        //4 bercut thread
        connect(pTestThread->pBercutThread,SIGNAL(syslog(QString,int)),this,SLOT(syslog(QString,int)));
        connect(pTestThread->pBercutThread,SIGNAL(bercutSerialWrite(QString)),this,SLOT(bercutSerialWrite(QString)));
        connect(pTestThread->pBercutThread,SIGNAL(bercutSerialClear()),this,SLOT(bercutSerialClear()));
        connect(pTestThread->pBercutThread,SIGNAL(bercutTestCompleat(int)),this,SLOT(bercut_test_stop(int)));

        ui->bercut_start->setDisabled(false);

        connect(pTestThread->pDataTestThread,SIGNAL(bercut_start()),this,SLOT(bercut_test_start()));
        connect(&bercut_timer, SIGNAL(timeout()), this, SLOT(bercut_timer_timeout()));
        bercut_timer.start(300000);

        //telnet
        connect(pTestThread->pDataTestThread,SIGNAL(telnet_config_pair(int,int)),this,SLOT(telnet_config_pair(int,int)));
        connect(pTestThread->pDataTestThread,SIGNAL(telnet_config_sw(int *,int)),this,SLOT(telnet_config_sw(int *,int)));
        connect(pTestThread->pDataTestThread,SIGNAL(telnet_config_chain(QString)),this,SLOT(telnet_config_chain(QString)));

        connect(pTestThread,SIGNAL(set_sw_test_mode(int)),this,SLOT(set_sw_test_mode(int)));
        connect(pTestThread,SIGNAL(set_tlp_test_mode(int)),this,SLOT(set_tlp_test_mode(int)));
        connect(pTestThread,SIGNAL(set_ups_plus_out(int)),this,SLOT(set_ups_plus_out(int)));
        connect(pTestThread,SIGNAL(set_tlp_output_state(int,int)),this,SLOT(set_tlp_output_state(int,int)));
        connect(pTestThread,SIGNAL(send_confirm(QString,int)),this,SLOT(send_confirm(QString,int)));
        connect(pTestThread,SIGNAL(tlp_send_rs485_hello()),this,SLOT(tlp_send_rs485_hello()));

        //поток для печати этикеток-идентификаторов
        pLabelThread = new LabelThread();
        connect(pLabelThread,SIGNAL(get_next_ident()),this,SLOT(get_next_ident()));
        connect(pLabelThread,SIGNAL(print_id_label(int)),this,SLOT(print_id_label(int)));
        connect(pLabelThread,SIGNAL(show_label_print_rezult(QString)),this,SLOT(show_label_print_rezult(QString)));

        pTestThread->db = QSqlDatabase::addDatabase("QMYSQL");

        //udp сокет для установки MAC адреса
        create_UDPsocket(PSW_PORT);

        //Меню
        create_ui_menu();

        //Соединения кнопок со слотами
        connect(ui->btn_test,SIGNAL(clicked()),SLOT(start_test())); // Соединяем нажатие кнопки Начать тест со слотом make()

        //slots init
        //on Make pressed
        connect(ui->btn_stop,SIGNAL(clicked()),SLOT(stop()));
        //repair tab
        connect(ui->btn_test_rp,SIGNAL(clicked()),SLOT(start_test()));
        connect(ui->btn_stop_rp,SIGNAL(clicked()),SLOT(stop()));
        //teleport tab
        connect(ui->tlpStart,SIGNAL(clicked()),SLOT(teleport_start()));
        //контестное меню списка утройств
        ui->device_list->setContextMenuPolicy(Qt::ActionsContextMenu);

        openConfigAction = new QAction(tr("Открыть профиль"), this);
        showConnectAction = new QAction(tr("Показать схему подключения"), this);

        ui->device_list->addAction(openConfigAction);
        ui->device_list->addAction(showConnectAction);

        ui->test_result->addAction(openConfigAction);

        connect(openConfigAction,SIGNAL(triggered()),this,SLOT(deviceMenuHandler()));

        connect(showConnectAction,SIGNAL(triggered()),this,SLOT(showConnectMenuHandler()));

        //set MAC
        connect(ui->set_mac,SIGNAL(clicked()),SLOT(mac_sender_tools()));
        connect(ui->print_label_pb,SIGNAL(clicked()),SLOT(print_label_tools()));

        //start Generation
        connect(ui->gen_start,SIGNAL(clicked()),SLOT(start_generation_tools()));
        //stop Generation
        connect(ui->gen_stop,SIGNAL(clicked()),SLOT(stop_generation_tools()));
        //установка пресета
        connect(ui->gen_config,SIGNAL(clicked()),SLOT(set_generation_preset()));
        //bercut generation
        connect(ui->bercut_start,SIGNAL(clicked()),SLOT(bercut_test_start()));
        connect(ui->bercut_stop,SIGNAL(clicked()),SLOT(bercut_test_stop_btn()));
        //кнопка Test mode
        connect(ui->test_mode_btn,SIGNAL(clicked()),SLOT(test_mode_pressed()));

        connect(ui->LastSerialBtn,SIGNAL(clicked()),SLOT(get_last_serial_pressed()));

        //stand
        connect(ui->modbus_set_curr,SIGNAL(clicked()),this,SLOT(modbus_set_current()));
        connect(ui->modbus_set_curr_all,SIGNAL(clicked()),this,SLOT(modbus_set_current_all()));
        connect(ui->modbus_set_ac1,SIGNAL(clicked()),SLOT(stand_ac1()));
        connect(ui->modbus_set_ac2,SIGNAL(clicked()),SLOT(stand_ac2()));
        connect(ui->modbus_set_dc1,SIGNAL(clicked()),SLOT(stand_dc1()));
        connect(ui->modbus_set_dc2,SIGNAL(clicked()),SLOT(stand_dc2()));
        connect(ui->modbus_read_all,SIGNAL(clicked()),SLOT(read_stand_all_slots()));
        connect(ui->modbus_read_one,SIGNAL(clicked()),SLOT(read_stand_one_slot()));
        connect(ui->prog_set_ac1,SIGNAL(clicked()),SLOT(stand_ac1()));

        connect(ui->modbus_simbat24_charge_btn,SIGNAL(clicked()),SLOT(simbat24_charge_relay()));
        connect(ui->modbus_simbat48_charge_btn,SIGNAL(clicked()),SLOT(simbat48_charge_relay()));

        connect(ui->modbus_simbat24_discharge_btn,SIGNAL(clicked()),SLOT(simbat24_discharge_relay()));
        connect(ui->modbus_simbat48_discharge_btn,SIGNAL(clicked()),SLOT(simbat48_discharge_relay()));

        connect(ui->modbus_poe_load_on_btn,SIGNAL(clicked()),SLOT(stand_poe_load_on()));
        connect(ui->modbus_poe_load_off_btn,SIGNAL(clicked()),SLOT(stand_poe_load_off()));
        connect(ui->modbus_ps2_heater1_relay_pb,SIGNAL(clicked()),SLOT(stand_heater1_relay()));
        connect(ui->modbus_ps2_heater_relay2_pb,SIGNAL(clicked()),SLOT(stand_heater2_relay()));

        connect(ui->modbus_simbat24_set_rload_btn,SIGNAL(clicked()),SLOT(stand_simbat24_rload()));
        connect(ui->modbus_simbat48_set_rload_btn,SIGNAL(clicked()),SLOT(stand_simbat48_rload()));

        connect(ui->modbus_io02_out1,SIGNAL(clicked()),SLOT(stand_io02_out1_clicked()));
        connect(ui->modbus_io02_out2,SIGNAL(clicked()),SLOT(stand_io02_out2_clicked()));
        connect(ui->modbus_io02_out3,SIGNAL(clicked()),SLOT(stand_io02_out3_clicked()));
        connect(ui->modbus_io02_out4,SIGNAL(clicked()),SLOT(stand_io02_out4_clicked()));
        connect(ui->modbus_io02_out5,SIGNAL(clicked()),SLOT(stand_io02_out5_clicked()));
        connect(ui->modbus_io02_out6,SIGNAL(clicked()),SLOT(stand_io02_out6_clicked()));
        connect(ui->modbus_io02_out7,SIGNAL(clicked()),SLOT(stand_io02_out7_clicked()));


        connect(pTestThread,SIGNAL(set_mb_io02_out1()),SLOT(stand_io02_out1_clicked()));
        connect(pTestThread,SIGNAL(set_mb_io02_out2()),SLOT(stand_io02_out2_clicked()));

        connect(this, SIGNAL(multi_laod_started(QString[], Qstring)), pTestThread, SLOT(runMultiLoad(QString[], QString)));


        connect(pTestThread,SIGNAL(io02_rs485_test_start()),SLOT(stand_io02_rs485_test_start()));


        connect(pTestThread,SIGNAL(stand_all_off()),SLOT(stand_all_off()));
        connect(pTestThread,SIGNAL(stand_ac1_on()),SLOT(stand_ac1_on()));
        connect(pTestThread,SIGNAL(stand_ac1_off()),SLOT(stand_ac1_off()));
        connect(pTestThread,SIGNAL(stand_ac2_on()),SLOT(stand_ac2_on()));
        connect(pTestThread,SIGNAL(stand_ac2_off()),SLOT(stand_ac2_off()));
        connect(pTestThread,SIGNAL(stand_dc1_on()),SLOT(stand_dc1_on()));
        connect(pTestThread,SIGNAL(stand_dc1_off()),SLOT(stand_dc1_off()));
        connect(pTestThread,SIGNAL(stand_dc2_on()),SLOT(stand_dc2_on()));
        connect(pTestThread,SIGNAL(stand_dc2_off()),SLOT(stand_dc2_off()));
        connect(pTestThread,SIGNAL(stand_akb_on()),SLOT(stand_akb_on()));
        connect(pTestThread,SIGNAL(stand_akb_off()),SLOT(stand_akb_off()));

        connect(pTestThread,SIGNAL(read_stand_signal(int)),SLOT(read_stand(int)));
        connect(pTestThread,SIGNAL(rps_stand_painting(int)),SLOT(rps_stand_painting(int)));

        //rps stand new
        connect(pTestThread,SIGNAL(set_rps_stand_akb_state(int)),SLOT(set_rps_stand_akb_state(int)));
        connect(pTestThread,SIGNAL(set_rps_stand_akb_polarity(int)),SLOT(set_rps_stand_akb_polarity(int)));
        connect(pTestThread,SIGNAL(set_rps_rload_value(int)),SLOT(set_rps_rload_value(int)));
        connect(pTestThread,SIGNAL(set_rps_rload_state(int)),SLOT(set_rps_rload_state(int)));
        connect(pTestThread,SIGNAL(set_rps_preheating(int)),SLOT(set_rps_preheating(int)));
        connect(pTestThread,SIGNAL(set_rps_latr_state(int)),SLOT(set_rps_latr_state(int)));
        connect(pTestThread,SIGNAL(set_rps_380_state(int)),SLOT(set_rps_380_state(int)));
        connect(pTestThread,SIGNAL(rps_read_stand_signal()),SLOT(rps_read_stand()));
        connect(pTestThread,SIGNAL(rps_read_rps_signal()),SLOT(rps_read_rps()));
        //управление платой RPS-01 - реле
        connect(pTestThread,SIGNAL(set_rps_relay1(int)),SLOT(set_rps_relay1(int)));
        connect(pTestThread,SIGNAL(set_rps_relay2(int)),SLOT(set_rps_relay2(int)));

        connect(pTestThread, &TestThread::disablePoeLine, this, &MainWindow::PoeDisable); // Отключение PoE

        connect(pTestThread,SIGNAL(stand_akb_rload_on()),SLOT(stand_akb_rload_on()));
        connect(pTestThread,SIGNAL(stand_akb_rload_off()),SLOT(stand_akb_rload_off()));
        connect(pTestThread,SIGNAL(stand_akb_direction()),SLOT(stand_akb_direction()));

        connect(pTestThread,SIGNAL(mb_clear_minmax(int)),SLOT(mb_clear_minmax(int)));
        connect(pTestThread,SIGNAL(mb_set_el60_out(int,int)),SLOT(mb_set_el60_out(int,int)));
        connect(pTestThread,SIGNAL(mb_set_el60_power(int,int)),SLOT(mb_set_el60_power(int,int)));
        connect(pTestThread,SIGNAL(mb_set_el60_power_to_all(int)),SLOT(mb_set_el60_power_to_all(int)));

        connect(pTestThread,SIGNAL(mb_set_el60_passive(int,int)),SLOT(mb_set_el60_passive(int,int)));

        connect(pTestThread,SIGNAL(set_stand_charge_key(int)),SLOT(set_stand_charge_key(int)));
        connect(pTestThread,SIGNAL(set_stand_discharge_key(int)),SLOT(set_stand_discharge_key(int)));
        connect(pTestThread,SIGNAL(set_stand_charge_rload(int)),SLOT(set_stand_charge_rload(int)));
        connect(pTestThread,SIGNAL(set_stand_heater1_relay(int)),this, SLOT(set_stand_heater1_relay(int)));
        connect(pTestThread,SIGNAL(set_stand_heater2_relay(int)),this,SLOT(set_stand_heater2_relay(int)));
        connect(pTestThread,SIGNAL(set_stand_max_temper(int)),SLOT(set_stand_max_temper(int)));
        connect(pTestThread,SIGNAL(clear_mb_ps2_minmax()),SLOT(clear_mb_ps2_minmax()));

        //generator timout timer
        connect(&gen_timeout,SIGNAL(timeout()),this,SLOT(stop_generation_tools()));
        ui->gen_stop->setDisabled(true);

        //programers tab
        connect(ui->pbFwStm32Start,SIGNAL(clicked()),SLOT(programmers_dfu_start()));
        connect(ui->pbFwAvrStart,SIGNAL(clicked()),SLOT(programmers_avr_start()));

        //печать этикеток-идентификаторов
        connect(ui->id_label_print,SIGNAL(clicked()),this,SLOT(id_label_print()));
        //печать этикеток-маркеров//123
        connect(ui->marker_label_print,SIGNAL(clicked()),this,SLOT(marker_label_print()));

        connect(ui->pbPrintLabelRetry,SIGNAL(clicked()),SLOT(print_label_retry()));

        ui->device_list->setCurrentRow(prog_sett.last_config_num);
        ui->device_list->itemDelegateForRow(prog_sett.last_config_num);

        //result label
        ui->test_result->setText(tr(" "));
        ui->gen_result->setText(" ");
        ui->test_result_rp->setText(" ");

        ui->btn_stop->setDisabled(true);
        ui->btn_stop->setChecked(true);
        ui->btn_stop->setStyleSheet("QPushButton { background-color: grey; }");

        ui->btn_stop_rp->setDisabled(true);
        ui->btn_stop_rp->setChecked(true);
        ui->btn_stop_rp->setStyleSheet("QPushButton { background-color: grey; }");


        ui->selftestLabel->setVisible(false);
        ui->updateLabel->setVisible(false);
        ui->heaterLabel->setVisible(false);
        ui->poeLabel->setVisible(false);
        ui->backeupLabel->setVisible(false);
        ui->inOutLabel->setVisible(false);
        ui->dataLabel->setVisible(false);
        ui->upsLabel->setVisible(false);
        ui->macLabel->setVisible(false);
        ui->printLabel->setVisible(false);
        ui->pbPrintLabelRetry->setVisible(false);
        ui->reportLabel->setVisible(false);

        ui->selftestOk->setVisible(false);
        ui->updateOk->setVisible(false);
        ui->heaterOk->setVisible(false);
        ui->poeOk->setVisible(false);
        ui->acBackupOk->setVisible(false);
        ui->inOutOk->setVisible(false);
        ui->dataOk->setVisible(false);
        ui->upsOk->setVisible(false);
        ui->sendMacOk->setVisible(false);
        ui->printOk->setVisible(false);
        ui->reportOk->setVisible(false);

        test_config.config_loaded = 0;
        if(argc>1){
            prog_sett.use_session_id = 1;
            prog_sett.session_id.append(argv[1]);
            syslog(prog_sett.session_id,I);
        }
        else{
            prog_sett.use_session_id = 0;
            syslog("Сессия не получена, отправка результатов на сервер невозможна",E);
        }

        pLabelThread->set_prog_sett(prog_sett);
        pTestThread->set_prog_sett(prog_sett);

        //подключение к БД
        if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld)
            connect_database();

        //telnet с технологическим коммутатором
        if(prog_sett.switch_state){
            qDebug() << "start telnet";
            telnet_dev = new telnet(prog_sett.telnet_host,prog_sett.telnet_login,prog_sett.telnet_pass);
            if(telnet_dev){
                connect(telnet_dev,SIGNAL(syslog(QString,int)),this,SLOT(syslog(QString,int)));
                telnet_dev->set_telnet_master_ports(prog_sett.port_a,prog_sett.port_b,prog_sett.port_dut,
                                                    prog_sett.port_sfp1,prog_sett.port_sfp2);
            }
            else
                syslog("Коммутатор по Telnet не  подключен",E);
        }

        //Подключение Беркут
        if(prog_sett.bercut_state){
            bercut_com_connect();
        }
        else{
            ui->bercut_start->setDisabled(true);
            ui->bercut_stop->setDisabled(true);
        }

        //если проверяем Teleport, то открываем порт
        if(prog_sett.teleport_state){
            teleport_com_connect();
        }

        for(int i=0;i<9;i++){
            prog_sett.test_type=TYPE_REPAIR;
            set_stage_result(i,NO_ACTIVE);
            prog_sett.test_type=TYPE_PRODUCTION;
            set_stage_result(i,NO_ACTIVE);
            prog_sett.test_type=TYPE_TELEPORT;
            set_stage_result(i,NO_ACTIVE);
            set_rps_result(i,NO_ACTIVE);
        }
        ui->type_first_rb->setChecked(true);

        //настройка вкладки генератора трафика
        syslog("Производится инициализация сетевых карт",I);
        generator_tab_config();

        //Проверка доступности принтеров в системе
        QPrinterInfo PrinterInfo;
        QStringList pinfo;
        pinfo= PrinterInfo.availablePrinterNames();
        if(pinfo.count()==0)
            syslog("В системе не найдено ни одного принтера!",E);

        //первоначально всё отключено
        num = 0;
        last_num = 0;

        //заголовок окна
        this->setWindowTitle(WINDOW_TITLE);

        if(prog_sett.stand_type == StandType::typeAPK03)
        {
            ui->btn_test->setDisabled(true);
            ui->btn_test->setChecked(true);
            ui->btn_test->setStyleSheet("QPushButton { background-color: grey; }");


            //отключаем вкладку со старым стендом
            ui->main_tab->removeTab(7);

            //управление по Modbus платами стенда
            //табличка со статусом подключения

            syslog("Производится поиск плат стенда",I);

            scene = new QGraphicsScene(ui->graphicsView);
            ui->graphicsView->setScene(scene);

            repaint_stand_slots();
            connect(&repaint_timer,SIGNAL(timeout()),this,SLOT(repaint_stand_slots()));
            connect(&repaint_timer, &QTimer::timeout, this, &MainWindow::EnableBtnStartTest);

            repaint_timer.setSingleShot(true);
            repaint_timer.start(MODBUS_SEARCH_TIME);

            //табличка с переменными
            create_modbus_table();
        }
        else if(prog_sett.stand_type == StandType::typeRPSNew){
            ui->main_tab->removeTab(0);
            ui->main_tab->removeTab(0);
            ui->main_tab->removeTab(0);
            ui->main_tab->removeTab(0);
            ui->main_tab->removeTab(0);
            ui->main_tab->removeTab(0);
            ui->main_tab->removeTab(0);
            ui->main_tab->removeTab(6);
        }
        else if(prog_sett.stand_type == StandType::typeAPK02){
            ui->main_tab->removeTab(1);
            ui->main_tab->removeTab(1);
            ui->main_tab->removeTab(1);
            ui->main_tab->removeTab(1);
            ui->main_tab->removeTab(1);
            ui->main_tab->removeTab(1);
            ui->main_tab->removeTab(1);
        }
    }
    dev_buttons[0] = ui->dev_button1;
    dev_buttons[1] = ui->dev_button3;
    dev_buttons[2] = ui->dev_button5;
    dev_buttons[3] = ui->dev_button7;
    dev_buttons[4] = ui->dev_button9;
    dev_buttons[5] = ui->dev_button11;
    dev_buttons[6] = ui->dev_button13;
    dev_buttons[7] = ui->dev_button15;
    dev_buttons[8] = ui->dev_button17;
    dev_buttons[9] = ui->dev_button19;
    dev_buttons[10] = ui->dev_button21;
    dev_buttons[11] = ui->dev_button23;

    dfuLoadStatus[0] = ui->plate_1_state;
    dfuLoadStatus[1] = ui->plate_2_state;
    dfuLoadStatus[2] = ui->plate_3_state;
    dfuLoadStatus[3] = ui->plate_4_state;
    dfuLoadStatus[4] = ui->plate_5_state;

    ui->restart_from_selftest_btn->setVisible(false);
    ui->restart_from_update_btn->setVisible(false);
    ui->restart_from_heater_btn->setVisible(false);
    ui->restart_from_poe_btn->setVisible(false);
    ui->restart_from_ps_btn->setVisible(false);
    ui->restart_from_inout_btn->setVisible(false);
    ui->restart_from_data_btn->setVisible(false);
    ui->restart_from_ups_btn->setVisible(false);
    ui->restart_from_mac_btn->setVisible(false);
    ui->restart_from_label_btn->setVisible(false);
    ui->restart_from_report_btn->setVisible(false);


    ui->dfu_load_btn->setDisabled(true);
    ui->dfu_load_btn->setStyleSheet("QPushButton { background-color: grey; }");

    for(int i = 0; i < 12; i++)
        dev_buttons[i]->setHidden(true);

    ui->graphicsView->setHidden(true);

    ui->modbus_read_one->setHidden(true);
    ui->modbus_read_all->setHidden(true);
    ui->modbus_id->setHidden(true);
    ui->label_40->setHidden(true);

    testRunning = false; // Флаг, показывающий был ли запущен тест
    stopTheTest = false;

    connect(ui->device_list, SIGNAL(itemSelectionChanged()), this, SLOT(ShowHideBoxIdWhenSelectionChanged())); // При изменении выбора в таблице устройств, будет вызван ShowHideBoxIdWhenSelectionChanged
    connect(ui->serial, SIGNAL(textEdited(QString)), this, SLOT(ChangeCheckedRadioButtonTypeTest(QString)));   // Переключение состояния радикнопок при вводе текста в serial

    connect(pTestThread, &TestThread::ClearSerial, this, &MainWindow::ClearSerialNumberField);

    //для автономного теста RPS-1 новым стендом
    connect(ui->rpsStartBtnNew, &QPushButton::clicked, this, &MainWindow::runRpsTest); // Запуск теста по нажатию кнопки
    connect(ui->rpsStopBtnNew, &QPushButton::clicked, this, &MainWindow::stopRpsTest); // Остановка теста по нажатию кнопки
    connect(pTestThread,SIGNAL(rpsTestDone(int)),this,SLOT(testRpsDone(int)));
    connect(ui->rpsAkbState, &QPushButton::clicked, this, &MainWindow::rpsStandAkbStatePressed);
    connect(ui->rpsAkbPolarityBtn, &QPushButton::clicked, this, &MainWindow::rpsStandPolarityPressed);
    connect(ui->rpsRLoadSet, &QPushButton::clicked, this, &MainWindow::rpsStandRloadSetPressed);
    connect(ui->rpsRloadState, &QPushButton::clicked, this, &MainWindow::rpsStandRloadStatePressed);
    connect(ui->rpsPreheatingSet, &QPushButton::clicked, this, &MainWindow::rpsStandPreHeatPressed);
    connect(ui->rpsLATR, &QPushButton::clicked, this, &MainWindow::rpsStandLatrPressed);
    connect(ui->rps380, &QPushButton::clicked, this, &MainWindow::rpsStand380VPressed);

    connect(ui->rpsReadStand, &QPushButton::clicked, this, &MainWindow::rpsStandReadStandPressed);
    connect(ui->rpsReadRPS, &QPushButton::clicked, this, &MainWindow::rpsStandReadRPSPressed);

    ShowHideBoxIdWhenSelectionChanged(); // Скрываем или отображает поле ввода Id для неуправляемых устройств

    startPing();

    if(prog_sett.stand_type == StandType::typeRPS || prog_sett.stand_type == StandType::typeRPSNew){
        rps_stand_painting(RpsStandStage::None);
    }

    //процесс прошивки по USB плат PSW
    dfu_command = new QProcess(this);

    poe_count = 0;

    for(int i = 0; i < 16; i++)
    {
        if(test_config.poe_line_test[i] == 1)
        {
            poe_count++;
        }
    }

    //подсчет количества тестируемых дата-портов
    data_count = 0;
    for(int i = 0; i < 16; i++)
    {
        if(test_config.data_test_ports[i] == 1)
        {
            data_count++;
        }
    }

    pTestThread->initTestStruct(test_config);

    data_count = data_count / 2; //тестирование передачи данных происходит на паре портов

    poe_progress = 0;
    data_progress = 0;

    QString def_text = "PoE";
    QString data_placeholder = "Data";
    ui->rpDataProgressBar->setFormat(data_placeholder);
    ui->rpPoeProgressBar->setFormat(def_text);

    for(int i = 0; i < 5; i++)
    {
        multi_dfu_load_command[i] = new QProcess(this);
        multi_dfu_load_command[i]->setProcessChannelMode(QProcess::MergedChannels);
    }

    dfuLoadSignalMapper = new QSignalMapper(this);

//    for(int i = 0; i < 5; i++)
//    {
//        connect(multi_dfu_load_command[i], &QProcess::started, dfuLoadSignalMapper, SLOT(map()));

//        dfuLoadSignalMapper->setMapping(multi_dfu_load_command[i], i);
//    }

    connect(multi_dfu_load_command[0], &QProcess::readyReadStandardOutput, this, &MainWindow::ParseDfuLoadResult1);
    connect(multi_dfu_load_command[1], &QProcess::readyReadStandardOutput, this, &MainWindow::ParseDfuLoadResult2);
    connect(multi_dfu_load_command[2], &QProcess::readyReadStandardOutput, this, &MainWindow::ParseDfuLoadResult3);
    connect(multi_dfu_load_command[3], &QProcess::readyReadStandardOutput, this, &MainWindow::ParseDfuLoadResult4);
    connect(multi_dfu_load_command[4], &QProcess::readyReadStandardOutput, this, &MainWindow::ParseDfuLoadResult5);

}

MainWindow::~MainWindow()
{
    qDebug() << "~MainWindow()";
    stand_all_off();
    Sleep(3000);
    deinitSerial();
    socket->abort();

    delete pTestThread;
    pTestThread = nullptr;

    delete pLabelThread;
    pLabelThread = nullptr;

    term_command->close();
    connect(term_command, SIGNAL(finished(int)), term_command, SLOT(deleteLater()));
    term_command->waitForFinished();

    dfu_command->close();
    connect(dfu_command, SIGNAL(finished(int)), dfu_command, SLOT(deleteLater()));
    dfu_command->waitForFinished();

    delete debug_window;
    debug_window = nullptr;
    delete ui;
    ui = nullptr;
}

//Код, который добавил Suvorov
//! Переключение состояния радикнопок. Связан с сигналом serial::textEdited()
void MainWindow::ChangeCheckedRadioButtonTypeTest(QString)
{
    ui->type_second_rb->setChecked(true);
}

//Код, который добавил Suvorov
//! Считывание конфигурации устройства при смене выделенного устройства
void MainWindow::ShowHideBoxIdWhenSelectionChanged()
{
//    if(ui->main_tab->currentIndex()!=0)
//        return;
    const auto index = ui->device_list->currentIndex().row();

    qDebug()<< "------------" << index << prog_sett.config_dir[index];

    int result = open_config_name(&test_config,prog_sett.config_dir[index]); //Открываем конфиг для устройства
    save_current_item();
    pTestThread->initTestStruct(test_config);

    //если файл не прочитан
    if (result == -1)
    {
        ui->groupBoxUnmanagment->hide(); // то скрываем виджет для ввода ID неуправляемых устройств
    }
    else
    {
        //если устройство управляемое,
        if(!test_config.print_label)
        {
            ui->groupBoxUnmanagment->hide(); // то скрываем виджет для ввода ID неуправляемых устройств
        }
        else
        {
            ui->groupBoxUnmanagment->show();
        }
    }

    clearOldResult(); //очищаем окно от старых результатов теста

    ui->serial->clear();                 //очищаем поле ввода Id
    ui->type_first_rb->setChecked(true); //устанавливаем радиокнопку Первичная проверка
}


//! Запускается при нажатии кнопки ТЕСТ
void MainWindow::start_test(){

    open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]); //Открываем конфиг для устройства
    save_current_item(); //Сохраняем выделение устройства (чтобы при следующем запуске усройство осталось выделенным)
    pTestThread->initTestStruct(test_config);
    hide_restart_buttons();
    make();
}

void MainWindow::make(){
    //int poe_ports,i;
    bool ok;
    int tab_num;
    tab_num = ui->main_tab->currentIndex();
    test_config.serial_num = 0;
    psw_selftest_result.serial_num = 0;

    if(ui->type_second_rb->isChecked())
    {
        long long serial = ui->serial->text().toLongLong();
        //SerialValidation(serial);
        ui->serial->setPlaceholderText("Введите ID");

        if(!SerialValidation(serial))
            return;
    }

    ui->device_list->setSelectionMode(QAbstractItemView::NoSelection); //блокируем выбор устройства в списке

    //вкладка ПРОВЕРКА
    if(tab_num == 0){
        pTestThread->setStatusSendReport(true); // Передаем статус, что отправка отчета требуется.
        //if(!test_config.config_loaded){
            open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]); //Открываем конфиг для устройства
            save_current_item(); //Сохраняем выделение устройства (чтобы при следующем запуске усройство осталось выделенным)
        //}

        prog_sett.test_type=TYPE_PRODUCTION;
        prog_sett.product_test_type = TYPE_FIRST;
        //первичная проверка
        if(ui->type_first_rb->isChecked()){
            prog_sett.product_test_type = TYPE_FIRST;
        }
        //повторная проверка
        if(ui->type_second_rb->isChecked()){
            prog_sett.product_test_type = TYPE_SECOND;
            test_config.serial_num = ui->serial->text().toULong(&ok,10);
            test_config.serial_num -= SERVICE_PREFIX;
            if(!ok)
                test_config.serial_num = 0;
            if(test_config.buildin_test == 0 && test_config.serial_num == 0){
                syslog("Необходимо указать идентификатор",E);
                return;
            }
            qDebug()<<"Присвоение test_config.serial_num" << test_config.serial_num;
        }

    }


    //вкладка РЕМОНТ
    else if(tab_num == 1)
    {
        pTestThread->setStatusSendReport(ui->save_rp->isChecked()); // Передаем статус установки галочки передавать отчет или нет

        open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]);

        prog_sett.test_type=TYPE_REPAIR;

        //ID для неуправляемых устройств
        test_config.id = ui->id_rp->text();

        //устанавливать ли серийный номер/МАС заново
        if(ui->serial_form_rp->isChecked()){
            test_config.send_mac = 1;
            test_config.serial_num = ui->serial_rp->text().toInt(&ok,10);
            if(!ok){
                syslog("Введите корректный серийный номер",E);
                return;
            }
            qDebug() << test_config.serial_num;
        }
        else{
            test_config.send_mac = 0;
        }

        //если неуправляемое, то должен быть введен либо серийник либо ID
        if(test_config.buildin_test == 0 && ui->save_rp->isChecked()){
            if(ui->id_rp->text().isEmpty() && ui->serial_rp->text().isEmpty()){
                syslog("Нужно ввести серийный номер или ID",E);
                stop();
                return;
            }
        }

        //печать этикеток
        if(ui->label_print_rp->isChecked())
            test_config.print_label = 1;
        else
            test_config.print_label = 0;
        //колличество этикеток
        test_config.label_num = ui->label_num_rp->value();
    }

    //если проверка с использованием беркута
    if(test_config.data_test && prog_sett.bercut_state){
        if(prog_sett.bercut_connected == 0){
            syslog("Bercut не подключен",E);
            return;
        }
        if(prog_sett.switch_state){
            if(!telnet_dev->is_connected()){
                syslog("Нет подключения к промежуточному коммутатору",E);
                return;
            }
        }
    }
    if(test_config.config_loaded == 1){

        if((prog_sett.connected || (prog_sett.stand_type == StandType::typeAPK03))||
                prog_sett.stand_type == StandType::typeAPK02){

            //Меняем состояние кнопок Старт/Стоп
            ui->btn_test->setDisabled(true);
            ui->btn_test->setChecked(true);
            ui->btn_test->setStyleSheet("QPushButton { background-color: grey; }");

            ui->btn_stop->setDisabled(false);
            ui->btn_stop->setChecked(false);
            ui->btn_stop->setStyleSheet("");

            ui->btn_test_rp->setDisabled(true);
            ui->btn_test_rp->setChecked(true);
            ui->btn_test_rp->setStyleSheet("QPushButton { background-color: grey; }");

            ui->btn_stop_rp->setDisabled(false);
            ui->btn_test_rp->setChecked(false);
            ui->btn_test_rp->setStyleSheet("");


            syslog("---------------------------------------------",I);
            syslog("Запуск теста",I);

            pTestThread->nettest.connected = false;
            pTestThread->nettest.macset = false;
            pTestThread->nettest.testrezult = 0;
            pTestThread->nettest.parsed = false;
            pTestThread->nettest.serial_num = 0;
            pTestThread->nettest.get_serial = false;
            pTestThread->uploading = 0;
            pTestThread->downloading = 0;

            num=0;
            error_code = 0;

            //очистка окна от старых результатов
            clearOldResult();

            test_report.clear();//очищаем отчет
            pTestThread->set_prog_sett(prog_sett);//передаём настройки программы
            pTestThread->set_test_config(test_config);//передаём профиль тестирования

            //pTestThread->disable_io02_outputs();

            pTestThread->start_test();
        }
        else{
            syslog(QString("Стенд не подключен"),E);
        }
    }
    else
        syslog(QString("Конфигурация не загружена"),E);

    //подсчет количества тестируемых poe-портов


    if(test_config.poe_test == 0 || poe_count == 0)
    {
        ui->rpPoeProgressBar->setEnabled(false);
    }else{
        ui->rpPoeProgressBar->setMinimum(0);
        ui->rpPoeProgressBar->setMaximum(poe_count);
        if(poe_count > 0 && poe_count <= 16)
        {
            QString def_text = QString("PoE: 0 из %1").arg(poe_count);
            ui->rpPoeProgressBar->setFormat(def_text);
        }
    }
    QString def_text = QString("PoE: 0 из %1").arg(poe_count);
    ui->rpPoeProgressBar->setFormat(def_text);
    ui->rpPoeProgressBar->setStyleSheet("");



    if(test_config.data_test == 0 || data_count == 0)
    {
        ui->rpDataProgressBar->setEnabled(false);

    }else{
        ui->rpDataProgressBar->setMinimum(0);
        ui->rpDataProgressBar->setMaximum(data_count);
        if(data_count > 0 && data_count <=16)
        {
            QString data_placeholder = QString("Data: 0 из %1").arg(data_count);
            ui->rpDataProgressBar->setFormat(data_placeholder);
        }
    }

    QString data_placeholder = QString("Data: 0 из %1").arg(data_count);
    ui->rpDataProgressBar->setFormat(data_placeholder);
    ui->rpDataProgressBar->setStyleSheet("");
}


//Код, который добавил Suvorov
//! Очистка окна от старых результатов теста
void MainWindow::clearOldResult()
{
    int poe_ports = 0;

    poe_count = 0;

    for(int i = 0; i < 16; i++)
    {
        if(test_config.poe_line_test[i] == 1)
        {
            poe_count++;
        }
    }

    //подсчет количества тестируемых дата-портов
    data_count = 0;
    for(int i = 0; i < 16; i++)
    {
        if(test_config.data_test_ports[i] == 1)
        {
            data_count++;
        }
    }

    data_count = data_count / 2; //тестирование передачи данных происходит на паре портов

    poe_progress = 0;
    data_progress = 0;

    int tab_num = ui->main_tab->currentIndex(); //Номер вкладки, на которой находимся

    // Это нужно, чтобы методы set_poeport_result, set_dataport_result правильно работали. Переделай эти методы!
    if(tab_num == 0)
        prog_sett.test_type = TYPE_PRODUCTION;
    else if(tab_num == 1)
        prog_sett.test_type = TYPE_REPAIR;

    // Делаем неактивными поля-заголовки стадий тестирования
    for(int i = 0; i < 10; i++)
    {
        set_stage_result(i, NO_ACTIVE);
    }

    //Очищаем список подтестов, сбрасываем состояние виджетов о прохождении теста
    SetStagesToDefault();

    for(int i = 0; i < PORT_NUM; i++)
    {
        set_poeport_result(i, NO_ACTIVE); // Делаем неактивными поля-PoE-порты
        set_dataport_result(i, NO_ACTIVE); // Делаем неактивными поля-порты

        if(test_config.poe_line_test[i] == 1 && test_config.poe_test == 1)
        {
            set_poeport_result(i, GREY); // Делаем серыми поля-PoE-порты, которые будут тестироваться
            poe_ports++;                 // Считаем, сколько таких портов
        }

        set_dataport_result(i, NO_ACTIVE); // Делаем неактивными поля-порты

        if(test_config.data_test_ports[i] == 1 && test_config.data_test == 1)
            set_dataport_result(i, GREY); // Делаем серыми поля-порты, которые будут тестироваться
    }

    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){

        if(test_config.ac_backup_test)
        {
            set_stage_result(StageTypes::acBackup, GREY); // Делаем серым поле-заголовок стадии тестирования резервирования блоков питания
            ui->backeupLabel->setVisible(true);
            ui->acBackupOk->setVisible(true);
            ui->restart_from_ps_wiget->setVisible(true);
        }else
        {
            ui->restart_from_ps_wiget->setVisible(false);
        }

        if(test_config.test_ups)
        {
            set_stage_result(StageTypes::ups, GREY); // Делаем серым поле-заголовок стадии UPS
            ui->upsLabel->setVisible(true);
            ui->upsOk->setVisible(true);
            ui->restart_from_ups_wiget->setVisible(true);
        }else
        {
            ui->restart_from_ups_wiget->setVisible(false);
        }

        if(test_config.send_mac)
        {
            set_stage_result(StageTypes::setMac, GREY); // Делаем серым поле-заголовок стадии отправки MACa
            ui->macLabel->setVisible(true);
            ui->sendMacOk->setVisible(true);
        }else
        {
            ui->restart_from_mac_wiget->setVisible(false);
        }

        if(test_config.print_label)
        {
            set_stage_result(StageTypes::printLabel, GREY); // Делаем серым поле-заголовок стадии печати этикетки
            ui->printLabel->setVisible(true);
            ui->printOk->setVisible(true);
            ui->pbPrintLabelRetry->setVisible(true);
            ui->restart_from_label_wiget->setVisible(true);
        }
        else
            {
            ui->restart_from_label_wiget->setVisible(false);
            }

        set_stage_result(StageTypes::sendReport, GREY);     // Делаем серым поле-заголовок стадии отправки на сервер
        ui->reportLabel->setVisible(true);
        ui->reportOk->setVisible(true);

        if (test_config.tlp_rs485 || test_config.i2c_test || test_config.dry_cont_test[0] || test_config.dry_cont_test[1] || test_config.dry_cont_test[2])
        {
            set_stage_result(StageTypes::inOut, GREY);     // Делаем серым поле-заголовок стадии RS485
            ui->inOutLabel->setVisible(true);
            ui->inOutOk->setVisible(true);
            ui->restart_from_inout_wiget->setVisible(true);
        }else
        {
            ui->restart_from_inout_wiget->setVisible(false);
        }
    }

    if(poe_ports > 0)
    {
        set_stage_result(StageTypes::poe, GREY); // Делаем серым поле-заголовок стадии тестирования PoE
        ui->poeLabel->setVisible(true);
        ui->poeLabel->setText(QString("PoE: %1 из %2").arg(poe_progress).arg(poe_count));
        ui->poeOk->setVisible(true);
        ui->restart_from_poe_wiget->setVisible(true);
    }else
    {
        ui->restart_from_poe_wiget->setVisible(false);
    }

    if(test_config.buildin_test)
    {
        set_stage_result(StageTypes::selfTest, GREY); // Делаем серым поле-заголовок стадии самотестирования
        ui->selftestLabel->setVisible(true);
        ui->selftestOk->setVisible(true);
        ui->restart_from_selftest_wiget->setVisible(true);
    }else
    {
        ui->restart_from_selftest_wiget->setVisible(false);
    }

    if((test_config.firmware_load) || (test_config.firmware_load_first) || (test_config.firmware_check))
    {
        set_stage_result(StageTypes::updateFirmware, GREY); // Делаем серым поле-заголовок стадии обновления прошивки
        ui->updateLabel->setVisible(true);
        ui->updateOk->setVisible(true);
        ui->restart_from_update_wiget->setVisible(true);
    }else
    {
        ui->restart_from_update_wiget->setVisible(false);
    }

    if(test_config.data_test)
    {
        set_stage_result(StageTypes::dataTest, GREY); // Делаем серым поле-заголовок стадии дата-тест
        ui->dataLabel->setVisible(true);
        ui->dataLabel->setText(QString("Data: %1 из %2").arg(data_progress).arg(data_count));
        ui->dataOk->setVisible(true);
        ui->restart_from_data_wiget->setVisible(true);
    }else
    {
        ui->restart_from_data_wiget->setVisible(false);
    }

    if(test_config.test_heating)
    {
        set_stage_result(StageTypes::heater, GREY); // Делаем серым поле-заголовок стадии тест нагревателей
        ui->heaterLabel->setVisible(true);
        ui->heaterOk->setVisible(true);
        ui->restart_from_heater_wiget->setVisible(true);
    }else
    {
        ui->restart_from_heater_wiget->setVisible(false);
    }

    ui->test_result->clear();
    ui->test_result_widget->setStyleSheet("");
    ui->test_result_rp->clear();

}

//Код, который добавил Suvorov
void MainWindow::ClearSerialNumberField()
{
    ui->serial->clear();
}

void MainWindow::EnableBtnStartTest()
{
    ui->btn_test->setEnabled(true);
    ui->btn_test->setChecked(false);
    ui->btn_test->setStyleSheet("");
}

bool MainWindow::SerialValidation(long long serial)
{

    if(serial < 900000000 || serial > 999999999){

        QMessageBox wrongSerialBox;
        wrongSerialBox.setWindowTitle("TFortisStand");
        wrongSerialBox.setText("Введите корректный ID");
        wrongSerialBox.standardIcon(QMessageBox::Warning);
        wrongSerialBox.exec();
        return false;
    }
    return true;
}

void MainWindow::test_finished(int errorcode, int serial){
    QString tmp,tmp2;
    unsigned int serial_tmp=0;
    unsigned int type_tmp=0;

    if(serial>100000){
        serial_tmp = serial - (serial/100000)*100000;
        type_tmp = (serial/100000);
    }else
        serial_tmp = serial;
    if(errorcode)
        tmp.sprintf("Тест не пройден");
    else{
        tmp.sprintf("Тест пройден ");
        if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld ){
            tmp.append(tmp2.sprintf("(%03d %d)",type_tmp,serial_tmp));
        }
    }

    if(ui->main_tab->currentIndex()==0){
        ui->test_result->setText(tmp);
        if(errorcode)
        ui->test_result_widget->setStyleSheet("image: url(:/images/fail.png);");
        else
            ui->test_result_widget->setStyleSheet("image: url(:/images/sucess.png);");

    }else if(ui->main_tab->currentIndex()==1){
        ui->test_result_rp->setText(tmp);
    }else if(ui->main_tab->currentIndex()==3){
        ui->test_result_tlp->setText(tmp);
    }
    stop();
}

void MainWindow::stop(){
    ui->btn_test->setDisabled(false);
    ui->btn_test->setChecked(false);
    ui->btn_test->setStyleSheet("");

    ui->btn_test_rp->setDisabled(false);
    ui->btn_test_rp->setChecked(false);
    ui->btn_test_rp->setStyleSheet("");

    ui->btn_stop->setDisabled(true);
    ui->btn_stop->setChecked(true);
    ui->btn_stop->setStyleSheet("QPushButton { background-color: grey; }");

    ui->btn_stop_rp->setDisabled(true);
    ui->btn_stop_rp->setChecked(true);
    ui->btn_stop_rp->setStyleSheet("QPushButton { background-color: grey; }");

    ui->tlpStart->setDisabled(false);
    ui->tlpStart->setChecked(false);
    ui->tlpStart->setStyleSheet("");

    ui->device_list->setSelectionMode(QAbstractItemView::SingleSelection); //разблокируем выбор устройства в списке
    pTestThread->stop();
}

//Тест автономным стендом RPS-1
void MainWindow::runRpsTest()
{
    const auto index = ui->device_list->currentIndex().row();
    int result = open_config_rps(prog_sett.config_dir[index]);

    if(result){
        syslog("Конфигурация теста не загружена",C);
        return;
    }
    if(test_config_rps.config_loaded){
        pTestThread->set_test_config_rps(test_config_rps);
        ui->rpsStopBtnNew->setDisabled(false);
        ui->rpsStartBtnNew->setDisabled(true);
        ui->test_result_rps_new->clear();


        set_rps_result(0,NO_ACTIVE);
        set_rps_result(1,NO_ACTIVE);
        set_rps_result(2,NO_ACTIVE);
        set_rps_result(3,NO_ACTIVE);

        if(pTestThread->dev->is_opened()){
            syslog("Тест запущен",C);
            pTestThread->rps_test_start();
        }
    }
    else{
        syslog("Ошибка в файле конфигурации",C);
    }
}

void MainWindow::stopRpsTest()
{
    pTestThread->rps_test_stop();
    ui->rpsStopBtnNew->setDisabled(true);
    ui->rpsStartBtnNew->setDisabled(false);
}

//Остановка теста проверки RPS
void MainWindow::testRpsDone(int status)
{
    syslog("Тест окончен", C);
    ui->rpsStopBtnNew->setDisabled(true);
    ui->rpsStartBtnNew->setDisabled(false);

    if(status){
        ui->test_result_rps_new->setText("<font color = \"green\">Тест пройден</font>");
    }
    else{
        ui->test_result_rps_new->setText("<font color = \"red\">Тест не пройден</font>");
    }
}

void MainWindow::grey_serial(){
    ui->serial->setEnabled(false);
}

void MainWindow::no_grey_serial(){
    ui->serial->setEnabled(true);
}

//!Сохраняет выделение устройства (чтобы при следующем запуске усройство осталось выделенным)
void MainWindow::save_current_item(){
    prog_sett.last_config_num = ui->device_list->currentIndex().row();
    save_last_config();
}

void MainWindow::send_report_f(QString str,int ok,float val){
    QString tmp;
    qDebug() << str << val;
    char rezult[16];
    if(ok)
        strcpy(rezult,"true");
    else
        strcpy(rezult,"false");
    test_report.append(str);
    tmp.sprintf("=%s=%03f\r\n",rezult,val);
    test_report.append(tmp);
}

void MainWindow::send_report_d(QString str,int ok,int val){
    QString tmp;
    //qDebug() << str << val;
    char rezult[16];
    if(ok)
        strcpy(rezult,"true");
    else
        strcpy(rezult,"false");
    test_report.append(str);
    tmp.sprintf("=%s=%d\r\n",rezult,val);
    test_report.append(tmp);
}

void MainWindow::send_report_s(QString str,int ok, QString val){
    QString tmp;
    char rezult[16];
    if(ok)
        strcpy(rezult,"true");
    else
        strcpy(rezult,"false");
    test_report.append(str);
    tmp.sprintf("=%s=%s\r\n",rezult,val.toLocal8Bit().data());
    test_report.append(tmp);
}

void MainWindow::send_report_b(QString str,int ok,int val){
    QString tmp;
    char rezult[16];
    char val_str[16];
    if(ok)
        strcpy(rezult,"true");
    else
        strcpy(rezult,"false");

    if(val)
        strcpy(val_str,"true");
    else
        strcpy(val_str,"false");

    test_report.append(str);
    tmp.sprintf("=%s=%s\r\n",rezult,val_str);
    test_report.append(tmp);
}

void MainWindow::send_session(QString val){
    QString tmp;
    tmp.sprintf("session=%s\r\n",val.toLocal8Bit().data());
    test_report.append(tmp);
}

void MainWindow::teport_clear(){
    test_report.clear();
}

void MainWindow::generator_tab_config(void){
    //1
    if(prog_sett.card_ip[0].length()){
        ui->p1_gen_ip->setText(prog_sett.card_ip[0]);
    }else{
        ui->p1_gen_cb->setDisabled(true);
        ui->p1_gen_ip->setText("");
    }
    //2
    if(prog_sett.card_ip[1].length()){
        ui->p2_gen_ip->setText(prog_sett.card_ip[1]);
    }else{
        ui->p2_gen_cb->setDisabled(true);
        ui->p2_gen_ip->setText("");
    }
    //3
    if(prog_sett.card_ip[2].length()){
        ui->p3_gen_ip->setText(prog_sett.card_ip[2]);
    }else{
        ui->p3_gen_cb->setDisabled(true);
        ui->p3_gen_ip->setText("");
    }
    //4
    if(prog_sett.card_ip[3].length()){
        ui->p4_gen_ip->setText(prog_sett.card_ip[3]);
    }else{
        ui->p4_gen_cb->setDisabled(true);
        ui->p4_gen_ip->setText("");
    }
    //5
    if(prog_sett.card_ip[4].length()){
        ui->p5_gen_ip->setText(prog_sett.card_ip[4]);
    }else{
        ui->p5_gen_cb->setDisabled(true);
        ui->p5_gen_ip->setText("");
    }
    //6
    if(prog_sett.card_ip[5].length()){
        ui->p6_gen_ip->setText(prog_sett.card_ip[5]);
    }else{
        ui->p6_gen_cb->setDisabled(true);
        ui->p6_gen_ip->setText("");
    }
    //7
    if(prog_sett.card_ip[6].length()){
        ui->p7_gen_ip->setText(prog_sett.card_ip[6]);
    }else{
        ui->p7_gen_cb->setDisabled(true);
        ui->p7_gen_ip->setText("");
    }
    //8
    if(prog_sett.card_ip[7].length()){
        ui->p8_gen_ip->setText(prog_sett.card_ip[7]);
    }else{
        ui->p8_gen_cb->setDisabled(true);
        ui->p8_gen_ip->setText("");
    }
    //9
    if(prog_sett.card_ip[8].length()){
        ui->p9_gen_ip->setText(prog_sett.card_ip[8]);
    }else{
        ui->p9_gen_cb->setDisabled(true);
        ui->p9_gen_ip->setText("");
    }
    //10
    if(prog_sett.card_ip[9].length()){
        ui->p10_gen_ip->setText(prog_sett.card_ip[9]);
    }else{
        ui->p10_gen_cb->setDisabled(true);
        ui->p10_gen_ip->setText("");
    }
    syslog("Инициализация сетевых карт завершена", I);
    pcap_freealldevs(alldev);
}

int get_dev_name(settingsmodel program_sett,int i,QString *tmp){
    tmp->clear();
    if(i<MAX_DEVICES){
        if(!program_sett.config_name[i].isEmpty()){
            tmp->append(program_sett.config_name[i]);
            return 1;
        }
    }
    return 0;
}

int get_dev_type(settingsmodel prog_sett,int i){
    return prog_sett.device_id[i];
}

int get_dev_type_by_name(settingsmodel prog_sett,QString name){
    qDebug() << "get_dev_type_by_name" << name;

    for(int i=0;i<MAX_DEVICES;i++){
        if(prog_sett.config_name[i].compare(name,Qt::CaseInsensitive)==0){
            return prog_sett.device_id[i];
        }
    }
    return 0;
}

void MainWindow::on_device_list_currentRowChanged(int currentRow)
{
    prog_sett.last_config_num = currentRow;
    save_last_config();
}

void MainWindow::on_dev_button1_clicked()
{
    ui->modbus_id->setCurrentIndex(0);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button1->setChecked(true);

    read_stand_one_slot();
}


void MainWindow::on_dev_button3_clicked()
{
    ui->modbus_id->setCurrentIndex(2);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button3->setChecked(true);


    read_stand_one_slot();
}



void MainWindow::on_dev_button5_clicked()
{
    ui->modbus_id->setCurrentIndex(4);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button5->setChecked(true);

    read_stand_one_slot();
}



void MainWindow::on_dev_button7_clicked()
{
    ui->modbus_id->setCurrentIndex(6);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button7->setChecked(true);


    read_stand_one_slot();
}



void MainWindow::on_dev_button9_clicked()
{
    ui->modbus_id->setCurrentIndex(8);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }

    ui->dev_button9->setChecked(true);

    read_stand_one_slot();
}



void MainWindow::on_dev_button11_clicked()
{
    ui->modbus_id->setCurrentIndex(10);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button11->setChecked(true);

    read_stand_one_slot();
}



void MainWindow::on_dev_button13_clicked()
{
    ui->modbus_id->setCurrentIndex(12);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button13->setChecked(true);

    read_stand_one_slot();
}



void MainWindow::on_dev_button15_clicked()
{
    ui->modbus_id->setCurrentIndex(14);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button15->setChecked(true);

    read_stand_one_slot();
}



void MainWindow::on_dev_button17_clicked()
{
    ui->modbus_id->setCurrentIndex(16);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button17->setChecked(true);

    read_stand_one_slot();
}


void MainWindow::on_dev_button19_clicked()
{
    ui->modbus_id->setCurrentIndex(18);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button19->setChecked(true);

    read_stand_one_slot();
}


void MainWindow::on_dev_button21_clicked()
{
    ui->modbus_id->setCurrentIndex(20);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button21->setChecked(true);

    read_stand_one_slot();
}


void MainWindow::on_dev_button23_clicked()
{
    ui->modbus_id->setCurrentIndex(22);

    for(int i = 0; i< 12; i++)
    {
            dev_buttons[i]->setChecked(false);
    }
    ui->dev_button23->setChecked(true);

    read_stand_one_slot();
}

void MainWindow::on_dfu_search_btn_clicked()
{
    QString appPath = QCoreApplication::applicationDirPath();

    for(int i = 0; i < 5; i++)
    {
        dfuLoadStatus[i]->setStyleSheet("");
    }

    ui->port_1_percentage->setText("");
    ui->port_2_percentage->setText("");
    ui->port_3_percentage->setText("");
    ui->port_4_percentage->setText("");
    ui->port_5_percentage->setText("");

//    ui->dfu_load_btn->setStyleSheet("");

//    ui->dfu_load_btn->setEnabled(true);

    multi_dfu_scan_command = new QProcess(this);

    multi_dfu_scan_command->setProcessChannelMode(QProcess::MergedChannels);

    multi_dfu_scan_command->start(QString("%1/dfu-util/win64/dfu-util.exe").arg(appPath), QStringList() << "-l");

    if(!multi_dfu_scan_command -> waitForStarted())
    {
        qDebug() << "proc didnt start";
        return;
    }

    multi_dfu_scan_command->waitForFinished();

    QByteArray output = multi_dfu_scan_command->readAllStandardOutput();
    QString line =  Functions::BytesFromCP866toUnicode(output);
    QString text = QString(line);


    //qDebug() << text;

    parseDfuList(text);
}

void MainWindow::commandMultiLoadPrint()
{
    QByteArray output = multi_dfu_scan_command->readAllStandardOutput();
    QString line =  Functions::BytesFromCP866toUnicode(output);
    QString text = QString(line);

    qDebug() << text;

    parseDfuList(text);
}

void MainWindow::parseDfuList(QString found_devices)
{
    QString dev_name[5];
    QRegExp rx("\n");
    QStringList query = found_devices.split(rx);
    QString str;

    int startIndex;
    int finishIndex;
    int dev_num;

    dev_num = 0;

    for(int i = 0; i < query.count(); i++)
    {
        if(query[i].contains("devnum") && query[i].contains("Found DFU"))
        {
            str = query[i];
            qDebug() << "\nSTRING:    " <<str << "\n";
            startIndex = str.indexOf("devnum=");
            str.remove(0, startIndex + 7);
            finishIndex = str.indexOf(", cfg=");
            str.remove(finishIndex, str.length()-finishIndex);

            if(dev_name[0] != str && dev_name[1] != str && dev_name[2] != str && dev_name[3] != str && dev_name[4] != str)
            {
                dev_name[dev_num] = str;
                dev_num++;
            }
        }
    }



    if(dev_name[0] != "")
    {
        ui->port_1_name->setText("devnum = ");
        ui->port_1_devnum->setText(dev_name[0]);
        ui->dfu_load_btn->setEnabled(true);
        ui->dfu_load_btn->setStyleSheet("");
    }
        else
    {
        ui->port_1_name->setText("Не подключена");
        ui->port_1_devnum->setText("");

}
    if(dev_name[1] != "")
    {
        ui->port_2_name->setText("devnum = ");
        ui->port_2_devnum->setText(dev_name[1]);
        ui->dfu_load_btn->setEnabled(true);
        ui->dfu_load_btn->setStyleSheet("");
    }
    else
    {
        ui->port_2_name->setText("Не подключена");
        ui->port_2_devnum->setText("");
    }

    if(dev_name[2] != "")
    {
        ui->port_3_name->setText("devnum = ");
        ui->port_3_devnum->setText(dev_name[2]);
        ui->dfu_load_btn->setEnabled(true);
        ui->dfu_load_btn->setStyleSheet("");
    }
    else
    {
        ui->port_3_name->setText("Не подключена");
        ui->port_3_devnum->setText("");
    }

    if(dev_name[3] != "")
    {
        ui->port_4_name->setText("devnum = ");
        ui->port_4_devnum->setText(dev_name[3]);
        ui->dfu_load_btn->setEnabled(true);
        ui->dfu_load_btn->setStyleSheet("");
    }
    else
    {
        ui->port_4_name->setText("Не подключена");
        ui->port_4_devnum->setText("");
    }

    if(dev_name[4] != "")
    {
        ui->port_5_name->setText("devnum = ");
        ui->port_5_devnum->setText(dev_name[4]);
        ui->dfu_load_btn->setEnabled(true);
        ui->dfu_load_btn->setStyleSheet("");
    }
    else
    {
        ui->port_5_name->setText("Не подключена");
        ui->port_5_devnum->setText("");
    }
}


void MainWindow::on_dfu_load_btn_clicked()
{
    QByteArray output[5];
    QString line[5];
    QString text[5];
    QStringList splittedText[5];
    QRegExp rx("\n");
    QString error;
    QString dfuPath =  test_config.dfu_path;
    QString devNums[5];

    dfuLoaded = 0;
    dfuCount = 0;

    if(ui->port_1_devnum->text() != "")
    {
        devNums[0] = ui->port_1_devnum->text();
        ui->port_1_percentage->setText("  0%");
        dfuCount++;

    }
    if(ui->port_2_devnum->text() != "")
    {
        devNums[1] = ui->port_2_devnum->text();
        ui->port_2_percentage->setText("  0%");
        dfuCount++;

    }
    if(ui->port_3_devnum->text() != "")
    {
        devNums[2] = ui->port_3_devnum->text();
        ui->port_3_percentage->setText("  0%");
        dfuCount++;

    }
    if(ui->port_4_devnum->text() != "")
    {
        devNums[3] = ui->port_4_devnum->text();
        ui->port_4_percentage->setText("  0%");
        dfuCount++;

    }
    if(ui->port_5_devnum->text() != "")
    {
        devNums[4] = ui->port_5_devnum->text();
        ui->port_5_percentage->setText("  0%");
        dfuCount++;

    }
    for(int i = 0; i < 5; i++)
    {
        dfuLoadStatus[i]->setStyleSheet("");
    }

//    ui->port_1_percentage->setText("  0%");
//    ui->port_2_percentage->setText("  0%");
//    ui->port_3_percentage->setText("  0%");
//    ui->port_4_percentage->setText("  0%");
//    ui->port_5_percentage->setText("  0%");


    ui->dfu_load_btn->setStyleSheet("QPushButton { background-color: grey; }");
    ui->dfu_search_btn->setStyleSheet("QPushButton { background-color: grey; }");

    ui->dfu_load_btn->setDisabled(true);
    ui->dfu_search_btn->setDisabled(true);

    //C:/Repo/psw_stand/dfu-util/win64/dfu-util.exe -a 0 -d 314B:0106 -n 16 -s 0x08000000:leave -t 4096 -D C:/FortTelecom/Launcher/TFortisStand/firmwares/sw407/sw407_0.2.9_15.07.2022_boot1.6.bin
    QString appPath = QCoreApplication::applicationDirPath();

    qDebug() << appPath;

    qDebug() << test_config.dfu_path;
    for(int i = 0; i < 5; i++)
    {
        if(devNums[i] != "")
        {
            //multi_dfu_load_command[i] = new QProcess(this);
            //multi_dfu_load_command[i]->setProcessChannelMode(QProcess::MergedChannels);
            //multi_dfu_load_command[i]->start("C:/Repo/psw_stand/dfu-util/win64/dfu-util.exe", QStringList() << "-a" << "0" << "-d" << "314B:0101" << "-n" << devNums[i] << "-s" << "0x08000000:leave" << "-t" << "4096" << "-D" << dfuPath);
            multi_dfu_load_command[i]->start(QString("%1/dfu-util/win64/dfu-util.exe -a 0 -n %2 -s 0x08000000 -D %3").arg(appPath).arg(devNums[i]).arg(test_config.dfu_path)/*\"C:/FortTelecom/Launcher/TFortisStandNew/firmwares/sw407/sw407_0.2.9_31.08.2022_boot1.6.bin\"")*/);

            if(!multi_dfu_load_command[0] -> waitForStarted())
            {
                qDebug() << "proc didnt start";
                return;
            }
        }
    }

//    QMovie *movie = new QMovie(":/images/loading.gif");

//    if(ui->port_1_devnum->text() != "")
//        ui->load_process_1->setMovie(movie);

//    if(ui->port_2_devnum->text() != "")
//        ui->load_process_2->setMovie(movie);

//    if(ui->port_3_devnum->text() != "")
//        ui->load_process_3->setMovie(movie);

//    if(ui->port_4_devnum->text() != "")
//        ui->load_process_4->setMovie(movie);

//    if(ui->port_5_devnum->text() != "")
//        ui->load_process_5->setMovie(movie);

//    movie->start();
/*
    for(int i = 0; i < 5; i++)
    {
        if(devNums[i] != "")
        {
            multi_dfu_load_command[i]->waitForFinished(-1);
        }
    }

    //pTestThread->runMultiLoad(devNums, dfuPath);

    //emit start_dfu_loading(multi_dfu_load_command);

    if(ui->port_1_devnum->text() != "")
        ui->load_process_1->setMovie(nullptr);

    if(ui->port_2_devnum->text() != "")
        ui->load_process_2->setMovie(nullptr);

    if(ui->port_3_devnum->text() != "")
        ui->load_process_3->setMovie(nullptr);

    if(ui->port_4_devnum->text() != "")
        ui->load_process_4->setMovie(nullptr);

    if(ui->port_5_devnum->text() != "")
        ui->load_process_5->setMovie(nullptr);

    for(int i = 0; i < 5; i++)
    {
        if(devNums[i] != "")
        {
            output[i]= multi_dfu_load_command[i]->readAllStandardOutput();
            line[i]=  Functions::BytesFromCP866toUnicode(output[i]);
            text[i]= QString(line[i]);
            splittedText[i] = text[i].split(rx);
        }
    }

    for(int i = 0; i < 5; i++)
    {
        error = "";

        if(devNums[i] != "")
        {
            for(int j = 0; j < splittedText[i].count(); j++)
            {
                qDebug() << splittedText[i][j] << "\n";
                if(splittedText[i][j].contains("Error"))
                {
                    error = QString("Port %1: %2").arg(i + 1).arg(splittedText[i][j]);
                    dfuLoadStatus[i]->setStyleSheet("image: url(:/images/fail.png);");

                    syslog(error, E);
                }else
                {
                    if(splittedText[i][j].contains("File downloaded successfully"))
                    {
                        error = QString("Port %1: %2").arg(i + 1).arg(splittedText[i][j]);
                        syslog(error, I);

                        dfuLoadStatus[i]->setStyleSheet("image: url(:/images/sucess.png);");
                    }
                }

            }
        }

    }*/

}

void MainWindow::ParseDfuLoadResult1()
{
    QByteArray output;
    QString line;
    QString text;
    QStringList splittedText;
    QRegExp rx("\n");

    output = multi_dfu_load_command[0]->readAllStandardOutput();
    line =  Functions::BytesFromCP866toUnicode(output);
    text = QString(line);
    splittedText = text.split(rx);

    QString error = "";


        for(int i = 0; i < splittedText.count(); i++)
        {
            qDebug() << splittedText[i] << "\n";

            if(splittedText[i].contains("Download") && splittedText[i].contains("%"))
            {
                QString str;
                int startIndex = splittedText[i].indexOf("%");
                str.append(splittedText[i][startIndex-3]);
                str.append(splittedText[i][startIndex-2]);
                str.append(splittedText[i][startIndex-1]);
                str.append(splittedText[i][startIndex]);

                ui->port_1_percentage->setText(str);
            }

            if(splittedText[i].contains("Error"))
            {
                error = QString("Port 1: %1").arg(splittedText[i]);
                dfuLoadStatus[0]->setStyleSheet("image: url(:/images/fail.png);");
                ui->load_process_1->setMovie(nullptr);
                dfuLoaded++;

                syslog(error, E);

            }else
            {
                if(splittedText[i].contains("File downloaded successfully"))
                {
                    error = QString("Port 1: %1").arg(splittedText[i]);
                    syslog(error, I);
                    ui->load_process_1->setMovie(nullptr);

                    dfuLoadStatus[0]->setStyleSheet("image: url(:/images/sucess.png);");
                    dfuLoaded++;


                }
            }

            if(dfuLoaded == dfuCount)
            {
                ui->dfu_search_btn->setStyleSheet("");

                ui->dfu_search_btn->setEnabled(true);
            }

//            if(multi_dfu_scan_command[0].state() == QProcess::NotRunning)
//            {
//                error = QString("Port 1: %1").arg("неизвестная ошибка");
//                dfuLoadStatus[0]->setStyleSheet("image: url(:/images/fail.png);");
//                ui->load_process_1->setMovie(nullptr);

//                syslog(error, E);
//            }

        }
}

void MainWindow::ParseDfuLoadResult2()
{
    QByteArray output;
    QString line;
    QString text;
    QStringList splittedText;
    QRegExp rx("\n");

    output = multi_dfu_load_command[1]->readAllStandardOutput();
    line =  Functions::BytesFromCP866toUnicode(output);
    text = QString(line);
    splittedText = text.split(rx);

    QString error = "";


        for(int i = 0; i < splittedText.count(); i++)
        {
            qDebug() << splittedText[i] << "\n";

            if(splittedText[i].contains("Download") && splittedText[i].contains("%"))
            {
                QString str;
                int startIndex = splittedText[i].indexOf("%");
                str.append(splittedText[i][startIndex-3]);
                str.append(splittedText[i][startIndex-2]);
                str.append(splittedText[i][startIndex-1]);
                str.append(splittedText[i][startIndex]);

                ui->port_2_percentage->setText(str);

            }

            if(splittedText[i].contains("Error"))
            {
                error = QString("Port 2: %1").arg(splittedText[i]);
                dfuLoadStatus[1]->setStyleSheet("image: url(:/images/fail.png);");
                ui->load_process_2->setMovie(nullptr);

                syslog(error, E);
                dfuLoaded++;

            }else
            {
                if(splittedText[i].contains("File downloaded successfully"))
                {
                    error = QString("Port 2: %1").arg(splittedText[i]);
                    syslog(error, I);
                    ui->load_process_2->setMovie(nullptr);

                    dfuLoadStatus[1]->setStyleSheet("image: url(:/images/sucess.png);");

                    dfuLoaded++;


                }
            }

        }

        if(dfuLoaded == dfuCount)
        {
            ui->dfu_search_btn->setStyleSheet("");

            ui->dfu_search_btn->setEnabled(true);
        }

//        if(multi_dfu_scan_command[1].state() == QProcess::NotRunning)
//        {
//            error = QString("Port 2: %1").arg("неизвестная ошибка");
//            dfuLoadStatus[1]->setStyleSheet("image: url(:/images/fail.png);");
//            ui->load_process_2->setMovie(nullptr);

//            syslog(error, E);
//        }

}

void MainWindow::ParseDfuLoadResult3()
{
    QByteArray output;
    QString line;
    QString text;
    QStringList splittedText;
    QRegExp rx("\n");

    output = multi_dfu_load_command[2]->readAllStandardOutput();
    line =  Functions::BytesFromCP866toUnicode(output);
    text = QString(line);
    splittedText = text.split(rx);

    QString error = "";


        for(int i = 0; i < splittedText.count(); i++)
        {
            qDebug() << splittedText[i] << "\n";

            if(splittedText[i].contains("Download") && splittedText[i].contains("%"))
            {
                QString str;
                int startIndex = splittedText[i].indexOf("%");
                str.append(splittedText[i][startIndex-3]);
                str.append(splittedText[i][startIndex-2]);
                str.append(splittedText[i][startIndex-1]);
                str.append(splittedText[i][startIndex]);

                ui->port_3_percentage->setText(str);

            }

            if(splittedText[i].contains("Error"))
            {
                error = QString("Port 3: %1").arg(splittedText[i]);
                dfuLoadStatus[2]->setStyleSheet("image: url(:/images/fail.png);");
                ui->load_process_3->setMovie(nullptr);

                syslog(error, E);

                dfuLoaded++;

            }else
            {
                if(splittedText[i].contains("File downloaded successfully"))
                {
                    error = QString("Port 3: %1").arg(splittedText[i]);
                    syslog(error, I);
                    ui->load_process_3->setMovie(nullptr);

                    dfuLoadStatus[2]->setStyleSheet("image: url(:/images/sucess.png);");

                   dfuLoaded++;
                }
            }

        }

        if(dfuLoaded == dfuCount)
        {
            ui->dfu_search_btn->setStyleSheet("");

            ui->dfu_search_btn->setEnabled(true);
        }

//        if(multi_dfu_scan_command[2].state() == QProcess::NotRunning)
//        {
//            error = QString("Port 3: %1").arg("неизвестная ошибка");
//            dfuLoadStatus[2]->setStyleSheet("image: url(:/images/fail.png);");
//            ui->load_process_3->setMovie(nullptr);

//            syslog(error, E);
//        }

}

void MainWindow::ParseDfuLoadResult4()
{
    QByteArray output;
    QString line;
    QString text;
    QStringList splittedText;
    QRegExp rx("\n");

    output = multi_dfu_load_command[3]->readAllStandardOutput();
    line =  Functions::BytesFromCP866toUnicode(output);
    text = QString(line);
    splittedText = text.split(rx);

    QString error = "";


        for(int i = 0; i < splittedText.count(); i++)
        {
            qDebug() << splittedText[i] << "\n";

            if(splittedText[i].contains("Download") && splittedText[i].contains("%"))
            {
                QString str;
                int startIndex = splittedText[i].indexOf("%");
                str.append(splittedText[i][startIndex-3]);
                str.append(splittedText[i][startIndex-2]);
                str.append(splittedText[i][startIndex-1]);
                str.append(splittedText[i][startIndex]);

                ui->port_4_percentage->setText(str);

            }

            if(splittedText[i].contains("Error"))
            {
                error = QString("Port 4: %1").arg(splittedText[i]);
                dfuLoadStatus[3]->setStyleSheet("image: url(:/images/fail.png);");
                ui->load_process_4->setMovie(nullptr);

                syslog(error, E);
                dfuLoaded++;

            }else
            {
                if(splittedText[i].contains("File downloaded successfully"))
                {
                    error = QString("Port 4: %1").arg(splittedText[i]);
                    syslog(error, I);
                    ui->load_process_4->setMovie(nullptr);

                    dfuLoadStatus[3]->setStyleSheet("image: url(:/images/sucess.png);");

                    dfuLoaded++;

                }
            }

        }

        if(dfuLoaded == dfuCount)
             {
                 ui->dfu_search_btn->setStyleSheet("");

                 ui->dfu_search_btn->setEnabled(true);
             }

//        if(multi_dfu_scan_command[3].state() == QProcess::NotRunning)
//        {
//            error = QString("Port 4: %1").arg("неизвестная ошибка");
//            dfuLoadStatus[3]->setStyleSheet("image: url(:/images/fail.png);");
//            ui->load_process_4->setMovie(nullptr);

//            syslog(error, E);
//        }

}

void MainWindow::ParseDfuLoadResult5()
{
    QByteArray output;
    QString line;
    QString text;
    QStringList splittedText;
    QRegExp rx("\n");

    output = multi_dfu_load_command[4]->readAllStandardOutput();
    line =  Functions::BytesFromCP866toUnicode(output);
    text = QString(line);
    splittedText = text.split(rx);

    QString error = "";


        for(int i = 0; i < splittedText.count(); i++)
        {
            qDebug() << splittedText[i] << "\n";

            if(splittedText[i].contains("Download") && splittedText[i].contains("%"))
            {
                QString str;
                int startIndex = splittedText[i].indexOf("%");
                str.append(splittedText[i][startIndex-3]);
                str.append(splittedText[i][startIndex-2]);
                str.append(splittedText[i][startIndex-1]);
                str.append(splittedText[i][startIndex]);

                ui->port_5_percentage->setText(str);

            }

            if(splittedText[i].contains("Error"))
            {
                error = QString("Port 5: %1").arg(splittedText[i]);
                dfuLoadStatus[4]->setStyleSheet("image: url(:/images/fail.png);");
                ui->load_process_5->setMovie(nullptr);

                syslog(error, E);

                dfuLoaded++;

            }else
            {
                if(splittedText[i].contains("File downloaded successfully"))
                {
                    error = QString("Port 5: %1").arg(splittedText[i]);
                    syslog(error, I);
                    ui->load_process_5->setMovie(nullptr);

                    dfuLoadStatus[4]->setStyleSheet("image: url(:/images/sucess.png);");

                    dfuLoaded++;

                }
            }

        }

        if(dfuLoaded == dfuCount)
             {
                 ui->dfu_search_btn->setStyleSheet("");

                 ui->dfu_search_btn->setEnabled(true);

                 ui->dfu_load_btn->setStyleSheet("QPushButton { background-color: grey; }");

                 ui->dfu_load_btn->setDisabled(true);
             }

//        if(multi_dfu_scan_command[4].state() == QProcess::NotRunning)
//        {
//            error = QString("Port 5: %1").arg("неизвестная ошибка");
//            dfuLoadStatus[4]->setStyleSheet("image: url(:/images/fail.png);");
//            ui->load_process_5->setMovie(nullptr);

//            syslog(error, E);
//        }


}


void MainWindow::on_reset_dfu_load_btn_clicked()
{
    for(int i = 0; i < 5; i++)
    {
        try {
            multi_dfu_load_command[i]->kill();
        }  catch (const char* ex) {

        }

        try {
            //multi_dfu_scan_command[i].kill();
        }  catch (const char* ex) {

        }
//        multi_dfu_load_command[i]->kill();
//        multi_dfu_scan_command[i].kill();
        dfuLoadStatus[i]->setStyleSheet("");


    }

    ui->port_1_percentage->setText("");
    ui->port_2_percentage->setText("");
    ui->port_3_percentage->setText("");
    ui->port_4_percentage->setText("");
    ui->port_5_percentage->setText("");

    ui->dfu_load_btn->setStyleSheet("");
    ui->dfu_load_btn->setEnabled(true);
    ui->dfu_search_btn->setStyleSheet("");
    ui->dfu_search_btn->setEnabled(true);

    ui->port_1_name->setText("Не подключена");
    ui->port_2_name->setText("Не подключена");
    ui->port_3_name->setText("Не подключена");
    ui->port_4_name->setText("Не подключена");
    ui->port_5_name->setText("Не подключена");

    ui->port_1_devnum->setText("");
    ui->port_2_devnum->setText("");
    ui->port_3_devnum->setText("");
    ui->port_4_devnum->setText("");
    ui->port_5_devnum->setText("");

    ui->dfu_load_btn->setStyleSheet("QPushButton { background-color: grey; }");
    ui->dfu_load_btn->setDisabled(true);
}


void MainWindow::on_restart_from_selftest_btn_clicked()
{
    pTestThread->test_struct.selftest = test_config.buildin_test;
    pTestThread->test_struct.update = test_config.firmware_load;
    pTestThread->test_struct.heater = test_config.test_heating;
    pTestThread->test_struct.poe = test_config.poe_test;
    pTestThread->test_struct.acBackup = test_config.ac_backup_test;
    pTestThread->test_struct.inOut = (test_config.tlp_rs485 || test_config.i2c_test || test_config.dry_cont_test[0] || test_config.dry_cont_test[1] || test_config.dry_cont_test[2]);
    pTestThread->test_struct.dataTest = test_config.data_test;
    pTestThread->test_struct.ups = test_config.test_ups;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_update_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = test_config.firmware_load;
    pTestThread->test_struct.heater = test_config.test_heating;
    pTestThread->test_struct.poe = test_config.poe_test;
    pTestThread->test_struct.acBackup = test_config.ac_backup_test;
    pTestThread->test_struct.inOut = (test_config.tlp_rs485 || test_config.i2c_test || test_config.dry_cont_test[0] || test_config.dry_cont_test[1] || test_config.dry_cont_test[2]);
    pTestThread->test_struct.dataTest = test_config.data_test;
    pTestThread->test_struct.ups = test_config.test_ups;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_heater_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = false;
    pTestThread->test_struct.heater = test_config.test_heating;
    pTestThread->test_struct.poe = test_config.poe_test;
    pTestThread->test_struct.acBackup = test_config.ac_backup_test;
    pTestThread->test_struct.inOut = (test_config.tlp_rs485 || test_config.i2c_test || test_config.dry_cont_test[0] || test_config.dry_cont_test[1] || test_config.dry_cont_test[2]);
    pTestThread->test_struct.dataTest = test_config.data_test;
    pTestThread->test_struct.ups = test_config.test_ups;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_poe_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = false;
    pTestThread->test_struct.heater = false;
    pTestThread->test_struct.poe = test_config.poe_test;
    pTestThread->test_struct.acBackup = test_config.ac_backup_test;
    pTestThread->test_struct.inOut = (test_config.tlp_rs485 || test_config.i2c_test || test_config.dry_cont_test[0] || test_config.dry_cont_test[1] || test_config.dry_cont_test[2]);
    pTestThread->test_struct.dataTest = test_config.data_test;
    pTestThread->test_struct.ups = test_config.test_ups;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_ps_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = false;
    pTestThread->test_struct.heater = false;
    pTestThread->test_struct.poe = false;
    pTestThread->test_struct.acBackup = test_config.ac_backup_test;
    pTestThread->test_struct.inOut = (test_config.tlp_rs485 || test_config.i2c_test || test_config.dry_cont_test[0] || test_config.dry_cont_test[1] || test_config.dry_cont_test[2]);
    pTestThread->test_struct.dataTest = test_config.data_test;
    pTestThread->test_struct.ups = test_config.test_ups;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_inout_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = false;
    pTestThread->test_struct.heater = false;
    pTestThread->test_struct.poe = false;
    pTestThread->test_struct.acBackup = false;
    pTestThread->test_struct.inOut = (test_config.tlp_rs485 || test_config.i2c_test || test_config.dry_cont_test[0] || test_config.dry_cont_test[1] || test_config.dry_cont_test[2]);
    pTestThread->test_struct.dataTest = test_config.data_test;
    pTestThread->test_struct.ups = test_config.test_ups;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_data_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = false;
    pTestThread->test_struct.heater = false;
    pTestThread->test_struct.poe = false;
    pTestThread->test_struct.acBackup = false;
    pTestThread->test_struct.inOut = false;
    pTestThread->test_struct.dataTest = test_config.data_test;
    pTestThread->test_struct.ups = test_config.test_ups;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_ups_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = false;
    pTestThread->test_struct.heater = false;
    pTestThread->test_struct.poe = false;
    pTestThread->test_struct.acBackup = false;
    pTestThread->test_struct.inOut = false;
    pTestThread->test_struct.dataTest = false;
    pTestThread->test_struct.ups = test_config.test_ups;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_mac_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = false;
    pTestThread->test_struct.heater = false;
    pTestThread->test_struct.poe = false;
    pTestThread->test_struct.acBackup = false;
    pTestThread->test_struct.inOut = false;
    pTestThread->test_struct.dataTest = false;
    pTestThread->test_struct.ups = false;
    pTestThread->test_struct.setMac = test_config.send_mac;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();
}


void MainWindow::on_restart_from_label_btn_clicked()
{
    pTestThread->test_struct.selftest = false;
    pTestThread->test_struct.update = false;
    pTestThread->test_struct.heater = false;
    pTestThread->test_struct.poe = false;
    pTestThread->test_struct.acBackup = false;
    pTestThread->test_struct.inOut = false;
    pTestThread->test_struct.dataTest = false;
    pTestThread->test_struct.ups = false;
    pTestThread->test_struct.setMac = false;
    pTestThread->test_struct.printLabel = test_config.print_label;
    hide_restart_buttons();
    make();

}


void MainWindow::on_restart_from_report_btn_clicked()
{
//    pTestThread->test_struct.selftest = test_config.buildin_test;
//    pTestThread->test_struct.update = test_config.firmware_load;
//    pTestThread->test_struct.heater = test_config.test_heating;
//    pTestThread->test_struct.poe = test_config.poe_test;
//    pTestThread->test_struct.acBackup = test_config.ac_backup_test;
//    pTestThread->test_struct.inOut = (test_config.tlp_rs485 || test_config.i2c_test || test_config.dry_cont_test[0] || test_config.dry_cont_test[1] || test_config.dry_cont_test[2]);
//    pTestThread->test_struct.dataTest = test_config.data_test;
//    pTestThread->test_struct.ups = test_config.test_ups;
//    pTestThread->test_struct.setMac = test_config.send_mac;
//    pTestThread->test_struct.printLabel = test_config.print_label;

}

void MainWindow::hide_restart_buttons()
{
    ui->restart_from_selftest_btn->setVisible(false);
    ui->restart_from_update_btn->setVisible(false);
    ui->restart_from_heater_btn->setVisible(false);
    ui->restart_from_poe_btn->setVisible(false);
    ui->restart_from_ps_btn->setVisible(false);
    ui->restart_from_inout_btn->setVisible(false);
    ui->restart_from_data_btn->setVisible(false);
    ui->restart_from_ups_btn->setVisible(false);
    ui->restart_from_mac_btn->setVisible(false);
    ui->restart_from_label_btn->setVisible(false);
    ui->restart_from_report_btn->setVisible(false);
}

void MainWindow::on_LoadFirmvare_stateChanged(int state)
{
    pTestThread->loadFirmvare = state;
}

