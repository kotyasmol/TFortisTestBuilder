#include <stdlib.h>
#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "stdio.h"
#include "math.h"
#include <QtXml/QtXml>
#include <QtXml/QDomElement>
#include <QFileDialog>
#include <QMessageBox>
#include <QFile>
#include <QProcess>
#include <qdebug.h>
#include <QPrinter>
#include <QPrintDialog>
#include <QPrinterInfo>
#include <winnt.h>
#include <winspool.h>
#include <windef.h>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QNetworkAccessManager>
#include "devicebase.h"
#include <QTimer>
#include "DataTest/DataTestThread.h"
#include "DataTest/BercutThread.h"
#include "modbus_dev.h"
#include "TestThread.h"
#include "functions.h"
#include "debugwindow.h"
#include "dfumultiloadthread.h"


TestThread::TestThread(settingsmodel sett){

    prog_sett = sett;

    QThread* data_thread = new QThread;
    pDataTestThread = new DataTestThread();
    pDataTestThread->moveToThread(data_thread);

    connect(pDataTestThread, SIGNAL(generator_print_msg(QString)), this, SLOT(generator_print_msg_(QString)));
    connect(pDataTestThread, SIGNAL(telnet_config_pair(int,int)), this, SLOT(telnet_config_pair_(int, int)));
    connect(data_thread, SIGNAL(started()), pDataTestThread, SLOT(process()));
    connect(pDataTestThread, SIGNAL(finished()), data_thread, SLOT(quit()));
    connect(pDataTestThread, SIGNAL(finished()), pDataTestThread, SLOT(deleteLater()));
    connect(pDataTestThread, SIGNAL(finished()), data_thread, SLOT(deleteLater()));
    data_thread->start();

    QThread* bercut_thread = new QThread;//Создаем поток
    pBercutThread = new BercutThread();//Создаем обьект по классу
    pBercutThread->moveToThread(bercut_thread);//помешаем класс  в поток

    connect(pBercutThread, SIGNAL(bercutSerialWrite(QString)),this,SLOT(bercutSerialWrite(QString)));
    connect(bercut_thread, SIGNAL(started()), pBercutThread, SLOT(process()));//Переназначения метода run
    connect(pBercutThread, SIGNAL(finished()), bercut_thread, SLOT(quit()));
    connect(pBercutThread, SIGNAL(finished()), pBercutThread, SLOT(deleteLater()));
    connect(pBercutThread, SIGNAL(finished()), bercut_thread, SLOT(deleteLater()));

    bercut_thread->start();

    QThread* dfu_load_thread = new QThread;
    dfuMultiLoad = new dfuMultiLoadThread();
    dfuMultiLoad->moveToThread(dfu_load_thread);

    dfu_load_thread->start();

    socket = new QUdpSocket();
    QHostAddress hostaddr;
    hostaddr.setAddress(DUT_IP_ADDR);
    socket->bind(QHostAddress::Any,/*0xabba*/PSW_PORT);

    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeRPS
            || prog_sett.stand_type == StandType::typeRPSNew){
        dev = new modbus_dev(prog_sett.com_port_name,prog_sett.com_autoconnect,prog_sett.stand_type);
        connect(dev, &QThread::finished, dev, &QObject::deleteLater);
    }

    // Suvorov
    nettest.get_serial = false;
    rpsStartFlag = false;
}



TestThread::~TestThread() {
    qDebug() << "destructor ~TestThread()";
    socket->abort();
    delete socket;
}

void TestThread::stop(){
    qDebug() << "stop";
    stop_test = true;
}

void TestThread::generator_print_msg_(QString str){
    qDebug() << "generator_print_msg_" << str;
    emit syslog(str,I);
}
void TestThread::telnet_config_pair_(){
    qDebug() << "TestThread::telnet_config_pair_";
}

void TestThread::start_test(){
    qDebug() << "start_test";
    start_test_flag = true;
    stop_test = false;
}

void TestThread::syslog_(QString str,int level){
    qDebug() << "syslog_";
    emit syslog(str,level);
}

void TestThread::clear_arp_case(){
    QProcess::startDetached("start  /min arpd.bat");

    QProcess process;
    process.start( "arp", QStringList() << "-d" );
    if( !process.waitForStarted() || !process.waitForFinished() )
    {
        return;
    }

    QString standardError  = Functions::BytesFromCP866toUnicode(process.readAllStandardError());
    QString standardOutput = Functions::BytesFromCP866toUnicode(process.readAllStandardOutput());

    if (standardError.length() > 0)
        emit syslog(standardError, E);
    else
        emit syslog("arp-таблица очищена", I);

    if (standardOutput.length() > 0)
        emit syslog(standardOutput, I);
}

void TestThread::send_com_data(char *data){
    for(int i=0;i<16;i++)
        com_data[i] = data[i];
    com_flag = true;
}

void TestThread::onUploadProgress(qint64 tmp1,qint64 tmp2){
    //aka progres bar
    qDebug() << "onUploadProgress" << tmp1  << tmp2;
}

void TestThread::set_test_shtml(bool value){
    testPageParsed = value;
}
bool TestThread::parse_html_file(QByteArray array){
    int first;
    int last;
    int hack;//удалить потом
    qDebug() << "parse html";
    QDomDocument domDoc;
    QString str;
    QString tmp;

    str.clear();
    str.append(array);

    qDebug() << str;

    if(is_model_type_PRO(test_config.model_name)==false){
        first = str.indexOf("<!DOCTYPE settings>",0, Qt::CaseInsensitive);
        str.remove(0,first);

        //маленький хак из-за бага в прошивке PSW-2G+
        hack = str.indexOf("<adc_2_5>",0,Qt::CaseInsensitive);
        if(hack>=0){
            //ищем второе вхождение строки
            hack = str.indexOf("<adc_2_5>",hack+1,Qt::CaseInsensitive);
            if(hack>=0){
                str.insert(hack+1,QString("/"));
            }
        }
        last = str.indexOf("</settings>",0, Qt::CaseInsensitive);
        str.remove(last+strlen("</settings>"),str.length() - last+strlen("</settings>"));
    }

    nettest.parsed = false;

    if(domDoc.setContent(str)) {
        QDomElement domElement= domDoc.documentElement();
        psw_selftest_result = traverseNodeHP(domElement);
        tmp.sprintf("akb_det=%d",psw_selftest_result.akb_det);
        tmp.sprintf("akb_voltage=%f",psw_selftest_result.akb_voltage);
        tmp.sprintf("akb_voltage_chg=%f",psw_selftest_result.akb_voltage_chg);
        tmp.sprintf("ups_rez=%d",psw_selftest_result.ups_rez);
        tmp.sprintf("ups_det=%d",psw_selftest_result.ups_det);
        qDebug() << "psw_selftest_result.temperature" << psw_selftest_result.temperature;

        nettest.connected = true;
        nettest.parsed = true;
    }
    else{
        nettest.parsed = false;
    }
    return nettest.parsed;
}

void TestThread::onNetworkError(QNetworkReply::NetworkError error){
    qDebug() << "onNetworkError" << error;
}

//установка тестовой конфигурации для стенда RPS-1
void TestThread::set_test_config_rps(struct configmodel_rps  test_config_rps_){
    test_config_rps = test_config_rps_;
}

//установка тестовой конфигурации
void TestThread::set_test_config(struct configmodel  test_config_){
    test_config = test_config_;
}

void TestThread::set_prog_sett(settingsmodel sett){
    prog_sett = sett;
}

int TestThread::get_selftest_result(void){
    int delta = 0;
    QString tmp;
    //по окончанию тестирования POE анализируем результаты test.shtml
    if(test_config.buildin_test == 1){
        if(nettest.parsed){


            //if(is_model_type_PRO()){



            //если не тот тип устройства
            qDebug() << psw_selftest_result.dev_type;
            qDebug() << test_config.model_num;

            if((psw_selftest_result.dev_type != test_config.model_num)
                    && (psw_selftest_result.dev_type != test_config.model_num_mac_print)){
                emit syslog("Неверный тип устройства",E);
                emit set_stage_result(1,RED);
                emit send_report_b("сравнение типа устройства",FAIL,FAIL);
                return SelfTestStatus::IncorrectDevType;
            }
            emit send_report_b("сравнение типа устройства",OK,OK);

            //проверка напряжения для PSW
            if(prog_sett.test_type != TYPE_TELEPORT){
                delta = psw_selftest_result.adc_2_5 - 3100;
                if(psw_selftest_result.adc_2_5){
                    if(CONST_2_5V(psw_selftest_result.adc_2_5)){
                        qDebug() << psw_selftest_result.adc_1_0;
                        emit syslog("Напряжение 2.5 V: вне диапазона",E);
                        emit send_report_f("напряжение 2.5 V",FAIL,psw_selftest_result.adc_2_5*0.0008056640625);
                        emit set_stage_result(1,RED);
                    }
                }

                if(psw_selftest_result.adc_1_0){
                    if(CONST_1V(psw_selftest_result.adc_1_0)){
                        qDebug() << psw_selftest_result.adc_1_0;
                        emit syslog("Напряжение 1.0 V: вне диапазона",E);
                        emit send_report_f("напряжение 1.0 V",FAIL,psw_selftest_result.adc_1_0*0.0008056640625);
                        emit set_stage_result(1,RED);
                        return -1;
                    }
                    emit send_report_f("напряжение 1.0 V",OK,psw_selftest_result.adc_1_0*0.0008056640625);
                }
                if(psw_selftest_result.adc_1_1){
                    if(CONST_1_1V(psw_selftest_result.adc_1_1)){
                        qDebug() << psw_selftest_result.adc_1_1;
                        emit syslog("Напряжение 1.1 V: вне диапазона",E);
                        emit send_report_f("напряжение 1.1 V",FAIL,psw_selftest_result.adc_1_1);
                        emit set_stage_result(1,RED);
                        return -1;
                    }
                    emit send_report_f("напряжение 1.0 V",OK,psw_selftest_result.adc_1_0*0.0008056640625);
                }

                if(psw_selftest_result.adc_1_8){
                    if(CONST_1_8V(psw_selftest_result.adc_1_8)){
                        qDebug() << psw_selftest_result.adc_1_8;
                        emit syslog("Напряжение 1.8 V: вне диапазона",E);
                        emit send_report_f("напряжение 1.8 V",FAIL,psw_selftest_result.adc_1_8*0.0008056640625);
                        emit set_stage_result(1,RED);
                        return -1;
                    }
                    emit send_report_f("напряжение 1.8 V",OK,psw_selftest_result.adc_1_8*0.0008056640625);
                }

                if(psw_selftest_result.adc_1_2){
                    if(CONST_1_2V(psw_selftest_result.adc_1_2 - delta*0.48)){
                        qDebug() << psw_selftest_result.adc_1_2;
                        emit syslog("Напряжение 1.2 V: вне диапазона",E);
                        emit send_report_f("напряжение 1.2 V",FAIL,psw_selftest_result.adc_1_2*0.0008056640625);
                        emit set_stage_result(1,RED);
                        return -1;
                    }
                    emit send_report_f("напряжение 1.2 V",OK,psw_selftest_result.adc_1_2*0.0008056640625);
                }

                if(psw_selftest_result.adc_1_5){
                    if(CONST_1_5V(psw_selftest_result.adc_1_5 - delta*0.6)){
                        qDebug() << psw_selftest_result.adc_1_5;
                        emit syslog("Напряжение 1.5 V: вне диапазона",E);
                        emit send_report_f("напряжение 1.5 V",FAIL,psw_selftest_result.adc_1_5*0.0008056640625);
                        emit set_stage_result(1,RED);
                        return -1;
                    }
                    emit send_report_f("напряжение 1.5 V",OK,psw_selftest_result.adc_1_5*0.0008056640625);
                }
            }

            if(psw_selftest_result.init_ok == 0){
                emit syslog("Самотестирование платы: диагностирована аппаратная ошибка",E);
                emit send_report_b("самотестирование",FAIL,psw_selftest_result.init_ok);
                emit set_stage_result(1,RED);
                return -1;
            }
            emit send_report_b("самотестирование",OK,psw_selftest_result.init_ok);

            if(prog_sett.stand_type == StandType::typeAPK03){
                //датчик вскрытия крышки
                if(test_config.dry_cont_test[0]){
                    if(psw_selftest_result.sensor_0==0){
                        emit syslog("Тестирование сенсора 0(Sensor 0): ошибка",E);
                        emit send_report_d("сухой контакт 0",FAIL,psw_selftest_result.sensor_0);
                        emit set_stage_result(1,RED);
                        return -1;
                    }
                    emit send_report_d("сухой контакт 0",OK,psw_selftest_result.sensor_0);
                }



                if(test_config.test_ups){
                    if(psw_selftest_result.ups_det == 0){
                        emit syslog("Тестирование UPS: ошибка детекции платы IRP",E);
                        emit set_stage_result(1,RED);
                        return -1;
                    }

                    if(psw_selftest_result.akb_det == 0 && psw_selftest_result.akb_voltage == 0 &&
                            psw_selftest_result.akb_voltage_chg == 0){
                        emit syslog("Тестирование UPS: ошибка детекции АКБ",E);
                        emit set_stage_result(1,RED);
                        return -1;
                    }

                    if(psw_selftest_result.ups_rez != 0)
                    {
                        //1 - питаемся от АКБ
                        //0- питаемся от сети
                        emit syslog("Не видим питание от сети, перезапрашиваем.", E);
                        QThread::sleep(test_config.start_delay); // Пауза, чтобы внутри PSW обновились переменные.
                        GetUpsStatus(); // Перезапрашиваем
                    }

                    if(psw_selftest_result.ups_rez != 0)
                    {
                        emit syslog("Тестирование UPS: ошибка, питаемся от АКБ",E);
                        emit set_stage_result(1,RED);
                        return -1;
                    }

                    emit syslog("Питание от сети.", I);

                }

                for(int i=0;i<PORT_NUM;i++){
                    if(i < test_config.port_num){
                        //SFP
                        if(test_config.port_sfp[i]){
                            if(psw_selftest_result.sfp_pres[i]==0){
                                tmp.sprintf("Линия PRESENT SFP%d: Ошибка",i+1);
                                emit syslog(tmp,E);
                                emit set_stage_result(1,RED);
                                return -1;
                            }
                            if(psw_selftest_result.sfp_id[i]!=3){
                                tmp.sprintf("Линия I2C SFP%d: Ошибка чтения",i+1);
                                emit syslog(tmp,E);
                            }
                            if(psw_selftest_result.sfp_sd[i]==0){
                                tmp.sprintf("Линия SignalDetect SFP%d: Ошибка",i+1);
                                emit syslog(tmp,E);
                                emit set_stage_result(1,RED);
                                return -1;
                            }
                        }

                    }
                }
            }

            if(test_config.firmware_load){
                //не требуется обновлять прошивку
                if(compare_FwVersion(psw_selftest_result.firmvare_vers,test_config.firmware_vers)>=0){
                    test_config.firmware_load = 0;
                    emit set_stage_result(2,GREEN);
                    emit syslog("Проверка версии ПО: не требуется обновление ПО",I);
                }
            }

            if(test_config.firmware_check){
                //не требуется обновлять прошивку
                qDebug() << "firmware_chek" << psw_selftest_result.firmvare_vers << test_config.firmware_vers;

                if(compare_FwVersion(psw_selftest_result.firmvare_vers,test_config.firmware_vers)>=0){
                    test_config.firmware_load = 0;
                    emit set_stage_result(2,GREEN);
                }
                else{
                    tmp.sprintf("Проверка версии ПО: требуется обновление ПО %s->%s",psw_selftest_result.firmvare_vers.toLocal8Bit().data(),test_config.firmware_vers.toLocal8Bit().data());
                    emit syslog(tmp,E);
                    emit set_stage_result(1,RED);
                    return -1;
                }

            }

            //детекция PoE
            if(test_config.poe_test){
                for(int i=0;i<test_config.port_num;i++){
                    if(test_config.poe_test && test_config.poe_line_test[i]){
                        //poe A
                        if(i%2==0){
                            if(psw_selftest_result.poe_a_st[i/2] == 0){
                                tmp.sprintf("Ошибка: Отсутствует PoE на порту %d",i/2+1);
                                emit syslog(tmp,E);
                                emit set_stage_result(0,RED);
                                emit set_stage_result(1,RED);
                                return -1;
                            }
                            if((psw_selftest_result.poe_a_v[i/2]*1000 > test_config.poe_line_max[i])&&
                                    (psw_selftest_result.poe_a_v[i/2]*1000 < test_config.poe_line_min[i])){
                                tmp.sprintf("Ошибка: PoE на порту %d вне диапазона (%f mV)",i/2+1,psw_selftest_result.poe_a_v[i/2]*1000); //reset warning %d -> %f
                                emit syslog(tmp,E);
                                emit set_stage_result(0,RED);
                                emit set_stage_result(1,RED);
                                return -1;
                            }

                        }
                    }
                }

                emit syslog("Самотестирование PoE: Успешно", I);
                emit set_stage_result(0,GREEN);
            }
            if(test_config.i2c_test)
            {
                //вынес проверку I2С в отдельный метод

            }
            qDebug() << "selftest result";
        }else{

            emit send_report_d("Загрузка тестовой страницы", FAIL, FAIL);
            emit syslog("Ошибка загрузки тестовой страницы",E);
            emit syslog("Не удалось распарсить страницу: 192.168.0.1/test.shtml",E);
            emit set_stage_result(1,RED);
            return -1;
        }
    }
    return 0;
}

//конвертируем значение АЦПв напряжение
int TestThread::convert_poe_voltage(char *buff){
    unsigned int adc;
    int adc_ret;
    if((buff[0]=='g')&&(buff[1]=='p')){
        if(1){
            adc = strtol((char *)&buff[3],NULL,10);
            if(adc<4096){
                adc_ret = (int)(179 - 1.06*(float)adc);
                qDebug() << "adc_ret"<< adc_ret;
                if((adc_ret >= 0)&&(adc_ret < 100)){
                    return adc_ret;
                }
                else{
                    emit syslog("значение вне диапазона -4",E);
                    return -4;
                }

            }else{
                emit syslog("Значение большое -3",E);
                return -3;
            }
        }
        else{
            emit syslog("Невернывй порт -2",E);
            return -2;
        }
    }
    else{
        emit syslog("Неверный ответ -1",E);
        return -1;
    }
}

void TestThread::print_msg(QString str){
    qDebug() << "print_msg" << str;
}


void TestThread::send_report(){

}

/*******************************************************************************************************/
//формирование отчета
void TestThread::make_report(int status,int type,int serial){
    QString tmp;

    /****************** ОБЯЗАТЕЛЬНЫЕ ПАРАМЕТРЫ ********************/
    emit send_report_d("test_result",OK,status);//0-не пройден / 1 - пройден
    emit send_report_s("stand_id",OK,prog_sett.stand_id);
    emit send_report_d("serial_num",OK,serial);
    if(prog_sett.use_session_id)
        emit send_report_s("session",OK,prog_sett.session_id);

    if(type == TYPE_PRODUCTION || type == TYPE_TELEPORT)
        emit send_report_s("Тип проверки",OK,"production");
    else
        emit send_report_s("Тип проверки",OK,"repair");
    /**************************************************************/

    if(test_config.buildin_test){
        if(nettest.parsed)
        {
            emit send_report_s("cpu_id",OK,psw_selftest_result.cpu_id);

            emit send_report_s("Версия прошивки",OK,psw_selftest_result.firmvare_vers);
            emit send_report_d("Версия бутлоадера",OK,psw_selftest_result.boot_vers);

            emit send_report_s("MAC адрес",OK,psw_selftest_result.default_mac);


            for(int i=0;i<test_config.port_num;i++){
                emit send_report_d(tmp.sprintf("link_%d",i),OK,psw_selftest_result.link[i]);
                if(psw_selftest_result.poe_a_st[i]){
                    emit send_report_d(tmp.sprintf("poe_a_st_%d",i),OK,psw_selftest_result.poe_a_st[i]);
                    emit send_report_f(tmp.sprintf("poe_a_v_%d",i),OK,psw_selftest_result.poe_a_v[i]);
                    emit send_report_d(tmp.sprintf("poe_a_c_%d",i),OK,psw_selftest_result.poe_a_c[i]);
                }
            }

            emit send_report_d("версия платы",OK,psw_selftest_result.board_version);

            if(type != TYPE_TELEPORT){
                emit send_report_d("ID микросхемы PoE контроллера",OK,psw_selftest_result.poe_controller);
                emit send_report_d("ID микросхемы Switch контроллера",OK,psw_selftest_result.marvell_id);
            }
            for(int i=0;i<PORT_NUM;i++){
                if(psw_selftest_result.sfp_pres[i]){
                    tmp.sprintf("присутствие SFP%d",i+1);
                    emit send_report_d(tmp,OK,psw_selftest_result.sfp_pres[i]);
                }
            }

            if(test_config.test_ups){
                emit send_report_f("напряжение АКБ",OK,psw_selftest_result.akb_voltage);
            }
        }else{
            emit send_report_b("Загрузка тестовой страницы", FAIL, nettest.parsed);
        }
    }
}

int TestThread::convert_poe2port(int port){
    if(port<2)
        return 0;
    if(port<4)
        return 1;
    return port-2;
}

int TestThread::bercut_test_compleat(){
    if(com_flag){
        com_flag = 0;
        qDebug() << com_data;
    }
    return 0;
}

void TestThread::set_ports(ports_pair_t *ports_){
    pDataTestThread->set_ports(ports_);
}

void TestThread::set_type(int type){
    pDataTestThread->set_type(type);
}

void TestThread::data_start(){
    pDataTestThread->data_start();
}

void TestThread::get_ports(ports_pair_t *ports_){
    pDataTestThread->get_ports(ports_);
}

bool TestThread::is_running(){
    return pDataTestThread->is_running();
}

void TestThread::data_stop(){
    pDataTestThread->stop();
}

long unsigned  TestThread::get_recieved_pkt(int port){
    return pDataTestThread->get_recieved_pkt(port);
}

long unsigned  TestThread::get_recieved_speed(int port){
    return pDataTestThread->get_recieved_speed(port);
}


long unsigned  TestThread::get_transmitted_pkt(int port){
    return pDataTestThread->get_transmitted_pkt(port);
}

void TestThread::bercut_timer_timeout(){
    qDebug() << "bercut_timer_timeout";

    pBercutThread->timer_timeout();

}

void TestThread::setWebmanagerFinished()
{
    webmanagerFinished = true;
}

bool TestThread::requestTestPage(int timeoutMs)
{
    qDebug() << "Debug. Getting test page";

    webmanagerFinished = false;
    if(is_model_type_PRO(test_config.model_name)){
        requestTestPagePro();
    }
    else{
        emit signal_GetTestPage(timeoutMs);                               // Испускаем сигнал, что нужно запросить тестовую страницу
        syslog("Запрос тестовой страницы",I);
        while (!webmanagerFinished)                                     // Делаем паузу пока не закончит работу webmanager
        {
            QThread::msleep(1000);
        }
        QThread::msleep(1000);
        qDebug() << "requestTestPage Finish";
    }

    return static_cast<bool>(testPageRequestResult);
}

void TestThread::waitingStartDevice(int timeout)
{

    if (test_config.buildin_test) // Если устройство управляемое, то делаем паузу путем ожидания тестовой страницы
    {
        resultRequestTestPage = false;

        for (int i = 0; i < 3; i++) // Повоторяем запрос тестовой страницы, пока устройство не запустится
        {
            if(stop_test==true){ ManualStopTest(); break; }

            QElapsedTimer timer;
            timer.start();

            while (!resultRequestTestPage)     // Периодически запрашиваем тестовую страницу
            {
                if(stop_test==true){ ManualStopTest(); break; }

                resultRequestTestPage = requestTestPage(timeout);           // Запрашиваем тестовую страницу

                if (timer.elapsed() >= timeout)
                    break;

                QThread::msleep(10000);                                      // Пауза 10 сек. Пауза меньше сильно нагрузить проц PSW
            }

            //страница загружена и устройство стало нужным типом
            if(resultRequestTestPage && psw_selftest_result.dev_type == test_config.model_num)
             break;
        }
    }
    else
    {
        QThread::msleep(static_cast<ulong>(timeout));
    }
}

//проверка параметра, считанного со стенда PSW из переменной mb_addr.
//параметр должен укладываться в диапазон max_value .. min_value
//если параметр в норме, вернуть true
//если ошибка - false
bool TestThread::check_stand_minmax_param(int slot,int mb_addr, int max_value,int min_value, int timeout){
    int read_cnt = 0;
    dev->modbus_data[mb_addr] = min_value-1;
    qDebug() << "check_stand_minmax_param" << slot << mb_addr << dev->modbus_data[mb_addr] << max_value << min_value;
    while((dev->modbus_data[mb_addr]>max_value || dev->modbus_data[mb_addr]<min_value)
          && (read_cnt < timeout)){
        Sleep(600);
        emit read_stand_signal(slot);
        //poeCurrent = dev->get_mb_el60v5_current_a(slot);
        read_cnt++;

        qDebug() << "check_stand_minmax_param" << slot << mb_addr << dev->modbus_data[mb_addr] << max_value << min_value;
    }

    if(dev->modbus_data[mb_addr]<=max_value && dev->modbus_data[mb_addr]>=min_value){
        poeVoltage = dev->modbus_data[mb_addr];
        if(slot%2)
            poeCurrent = dev->get_mb_el60v5_current_a(slot);
        else
            poeCurrent = dev->get_mb_el60v5_current_b(slot);

        return true;
    }

    if(read_cnt >= timeout){
        return false;
    }
        return false;
}



//проверка параметра, считанного со стенда из переменной mb_addr.
//если параметр в норме, вернуть true
//если ошибка - false

bool TestThread::check_stand_param(int slot,int mb_addr, int param, int timeout){
    int read_cnt = 0;
    dev->modbus_data[mb_addr] = !param;
    while((dev->modbus_data[mb_addr]!=param) && (read_cnt < timeout)){
        Sleep(1000);
        emit read_stand_signal(slot);
        read_cnt++;
        qDebug() << "check_stand_param" << slot << mb_addr << dev->modbus_data[mb_addr] << param;

    }
    if(read_cnt >= timeout){
        return false;
    }
    if(dev->modbus_data[mb_addr] == param){
        return true;
    }
    else
        return false;
}


void TestThread::set_stand_ac1_state(int state){
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS1 ){

        while(check_stand_param(PS1_ADDR,MB_PS1_AC1_STATE,state,2) == false && cnt){
            if(state)
            {
                emit stand_ac1_on();
                //debuglog("Попытка включения AC1");
            }
            else
            {
                emit stand_ac1_off();
                //debuglog("Попытка отключения AC1");
            }
            cnt--;
        }
    }else if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS2 || dev->get_mb_devtype(PS1_ADDR)==DEV_PS3){
        while(check_stand_param(PS1_ADDR,MB_PS2_AC1_STATE,state,2) == false && cnt){
            if(state)
                emit stand_ac1_on();
            else
                emit stand_ac1_off();
            cnt--;
        }
    }
}

void TestThread::set_stand_ac2_state(int state){
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS1 ){
        while(check_stand_param(PS1_ADDR,MB_PS1_AC2_STATE,state,2) == false && cnt){
            if(state)
                emit stand_ac2_on();
            else
                emit stand_ac2_off();
            cnt--;
        }
    }else if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS2 || dev->get_mb_devtype(PS1_ADDR)==DEV_PS3){
        while(check_stand_param(PS1_ADDR,MB_PS2_AC2_STATE,state,2) == false && cnt){
            if(state)
                emit stand_ac2_on();
            else
                emit stand_ac2_off();
            cnt--;
        }
    }
}

void TestThread::set_stand_sensor1(int state){
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS1){
        while(check_stand_param(PS1_ADDR,MB_PS1_SENSOR1_STATE,state,2) == false && cnt){
            if(state)
                emit stand_dc1_on();
            else
                emit stand_dc1_off();
            cnt--;
        }
    }else if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS2){
        while(check_stand_param(PS1_ADDR,MB_PS2_SENSOR1_STATE,state,2) == false && cnt){
            if(state)
                emit stand_dc1_on();
            else
                emit stand_dc1_off();
            cnt--;
        }
    }else{
        if(dev->io02_is_connected()){
            while(check_stand_param(dev->get_io02_slot(),MB_IO02_OUT1,state,2) == false && cnt){
                if(state)
                    emit stand_dc1_on();
                else
                    emit stand_dc1_off();
                cnt--;
            }
        }
        else{
            emit syslog("Ошибка: плата IO-02 не подключена",E);
        }
    }
}

void TestThread::set_stand_sensor2(int state){
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS1){
        while(check_stand_param(PS1_ADDR,MB_PS1_SENSOR2_STATE,state,2) == false && cnt){
            if(state)
                emit stand_dc2_on();
            else
                emit stand_dc2_off();
            cnt--;
        }
    }
    else if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS2){
        while(check_stand_param(PS1_ADDR,MB_PS2_SENSOR2_STATE,state,2) == false && cnt){
            if(state)
                emit stand_dc2_on();
            else
                emit stand_dc2_off();
            cnt--;
        }
    }else{
        if(dev->io02_is_connected()){
            while(check_stand_param(dev->get_io02_slot(),MB_IO02_OUT2,state,2) == false && cnt){
                if(state)
                    emit stand_dc2_on();
                else
                    emit stand_dc2_off();
                cnt--;
            }
        }
        else{
            emit syslog("Ошибка: плата IO-02 не подключена",E);
        }
    }
}

void TestThread::runMultiLoad(QString devNums[])
{
    QByteArray output[5];
    QString line[5];
    QString text[5];
    QStringList splittedText[5];
    QRegExp rx("\n");
    QString error;
    QProcess processes[5];
    //C:/Repo/psw_stand/dfu-util/win64/dfu-util.exe -a 0 -d 314B:0106 -n 16 -s 0x08000000:leave -t 4096 -D C:/FortTelecom/Launcher/TFortisStand/firmwares/sw407/sw407_0.2.9_15.07.2022_boot1.6.bin

    for(int i = 0; i < 5; i++)
    {
        if(devNums[i] != "")
        {
            processes[i].setProcessChannelMode(QProcess::MergedChannels);

            //multi_dfu_load_command[i]->start("C:/Repo/psw_stand/dfu-util/win64/dfu-util.exe", QStringList() << "-a" << "0" << "-d" << "314B:0101" << "-n" << devNums[i] << "-s" << "0x08000000:leave" << "-t" << "4096" << "-D" << dfuPath);
            processes[i].start(QString("C:/Repo/psw_stand/dfu-util/win64/dfu-util.exe -a 0 -n %1 -s 0x08000000:leave -D \"C:/FortTelecom/Launcher/TFortisStandNew/firmwares/sw407/sw407_0.2.9_31.08.2022_boot1.6.bin\"").arg(devNums[i]));

            if(!processes[i].waitForStarted())
            {
                qDebug() << "proc didnt start";
                return;
            }
        }
    }

    for(int i = 0; i < 5; i++)
    {
        if(devNums[i] != "")
        {
            while(processes[i].waitForFinished())
            {
                Sleep(5000);
            }
            //processes[i].waitForFinished();
        }
    }

    for(int i = 0; i < 5; i++)
    {
        if(devNums[i] != "")
        {
            output[i]= multiLoadProcesses[i]->readAllStandardOutput();
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

                    syslog(error, E);
                }else
                {
                    if(splittedText[i][j].contains("File downloaded successfully"))
                    {
                        error = QString("Port %1: %2").arg(i + 1).arg(splittedText[i][j]);
                        syslog(error, I);

                    }
                }

            }
        }

    }
}
void TestThread::configure_stand_poe()
{
    QString tmp;

    for(int i = 0; i < NUM_POE_LINES; i++)// i - port
    {
        if(test_config.poe_line_test[i])
        {
        int port = i;
        int power = test_config.poe_line_power[i];
        qDebug() << "set_stand_poe_power" << i << test_config.poe_line_power[i];


        if(dev->get_mb_devtype(port - port%2) == DEV_EL60V5){
            emit read_stand_signal(port - port%2);
            Sleep(100);

            emit mb_set_el60_power(port - port%2,power);
            Sleep(100);

            if(power){
                emit mb_set_el60_out(port - port%2,1);
                Sleep(100);
            }
            else{
                emit mb_set_el60_out(port - port%2,0);
                Sleep(100);
            }

        }
        }
    }
}

bool TestThread::check_poe_configuration()
{   
    QString tmp;
    for(int i = 0; i < NUM_POE_LINES; i++)
    {
        if(test_config.poe_line_test[i])
        {
       if(dev->get_mb_devtype(i - i % 2) == DEV_EL60V5){
            if(check_stand_param(i - i % 2,MB_EL60V5_OUT_PWR_A,test_config.poe_line_power[i],3) == false || check_stand_param(i - i % 2,MB_EL60V5_OUT_PWR_B,test_config.poe_line_power[i],3) == false){
                emit syslog(tmp.sprintf("Нагрузка не установлена"),I);
                emit mb_set_el60_power(i - i % 2,test_config.poe_line_power[i]);
                return false;
            }
            else{
                if(i%2){
                    emit syslog(tmp.sprintf("PoE A канал %d нагрузка %dW установлена",i,test_config.poe_line_power[i]/1000),I);
                }
                else{
                    emit syslog(tmp.sprintf("PoE B канал %d нагрузка %dW установлена",i,test_config.poe_line_power[i]/1000),I);
                }
            }

            if(test_config.poe_line_power[i]){                
                if(dev->modbus_data[MB_EL60V5_OUT_EN_A] != 1 || dev->modbus_data[MB_EL60V5_OUT_EN_B] !=1){
                    emit syslog(tmp.sprintf("Нагрузка не подключена"),I);
                    emit mb_set_el60_out(i - i%2,1);
                    return false;
                }
            }
            else{
                if(dev->modbus_data[MB_EL60V5_OUT_EN_A] != 0 || dev->modbus_data[MB_EL60V5_OUT_EN_B] !=0){
                    emit syslog(tmp.sprintf("Нагрузка не подключена"),I);
                    emit mb_set_el60_out(i - i%2,0);
                    return false;
                }
            }
        }
    }
    }
    return true;

}
void TestThread::set_stand_poe_power(int port, int power){

    qDebug() << "set_stand_poe_power" << port << power;
    QString tmp;
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(port) == DEV_EL60){

        //emit mb_set_el60_power(port,power);
        emit mb_set_el60_power_to_all(power);

        //while(check_stand_param(port,MB_EL60_OUT_PWR,power,2) == false && cnt){
            emit mb_set_el60_power_to_all(power);

            //emit mb_set_el60_power(port,power);

            cnt--;
        //}

        if(power){
            emit syslog(tmp.sprintf("Включение PoE нагрузки: канал %d",port),I);
            cnt = 2;//MODBUS_REPEAT_CNT;
            emit mb_set_el60_out(port,1);
            while(check_stand_param(port,MB_EL60_OUT_EN,1,1) == false && cnt){
                emit mb_set_el60_out(port,1);
                cnt--;
            }
        }
        else{
            emit syslog(tmp.sprintf("Отключние PoE нагрузки: канал %d",port+1),I);

            cnt = 2;//MODBUS_REPEAT_CNT;
            emit mb_set_el60_out(port,0);
            while(check_stand_param(port, MB_EL60_OUT_EN,0,1) == false && cnt){
                emit mb_set_el60_out(port,0);
                cnt--;
            }
        }
    }

    else if(dev->get_mb_devtype(port - port%2) == DEV_EL60V5){
        cnt = 2;
        emit mb_set_el60_power(port - port%2,power);
        if(power){
            emit mb_set_el60_out(port - port%2,1);
        }
        else
            emit mb_set_el60_out(port - port%2,0);

        while(check_stand_param(port - port%2,MB_EL60V5_OUT_PWR_A,power,2) == false && cnt){
            emit mb_set_el60_power(port - port%2,power);
            cnt--;
        }
        while(check_stand_param(port - port%2,MB_EL60V5_OUT_PWR_B,power,2) == false && cnt){
            emit mb_set_el60_power(port - port%2,power);
            cnt--;
        }

        if(power){
            emit mb_set_el60_out(port - port%2,1);
            while(check_stand_param(port - port%2,MB_EL60V5_OUT_EN_A,1,2) == false && cnt){
                emit mb_set_el60_out(port - port%2,1);
                cnt--;
            }
            while(check_stand_param(port - port%2,MB_EL60V5_OUT_EN_B,1,2) == false && cnt){
                emit mb_set_el60_out(port - port%2,1);
                cnt--;
            }
        }
        else{
            emit mb_set_el60_out(port - port%2,0);
            while(check_stand_param(port - port%2,MB_EL60V5_OUT_EN_A,0,2) == false && cnt){
                emit mb_set_el60_out(port - port%2,0);
                cnt--;
            }
            while(check_stand_param(port - port%2,MB_EL60V5_OUT_EN_B,0,2) == false && cnt){
                emit mb_set_el60_out(port - port%2,0);
                cnt--;
            }
        }
    }
}

void TestThread::set_stand_poe_passive(int port, int state){
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(port) == DEV_EL60){
        while(check_stand_param(port,MB_EL60_PASSIVE_EN,state,2) == false && cnt){
            emit mb_set_el60_passive(port,state);
            cnt--;
        }
    }
}

void TestThread::set_stand_akb_state(int state){
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS1){
        while(check_stand_param(PS1_ADDR,MB_PS1_AKB_EN,state,2) == false && cnt){
            if(state){
                emit stand_akb_on();
            }
            else{
                emit stand_akb_off();
            }
            cnt--;
        }
    }
}

void TestThread::set_stand_akb_direction(int state){
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS1){
        while(check_stand_param(PS1_ADDR,MB_PS1_AKB_DIRECTION,2,2) == false && cnt){
            if(state)
                emit stand_akb_direction();
            cnt--;
        }
    }
}

void TestThread::set_stand_discharge(int state){
    int cnt = MODBUS_REPEAT_CNT;
    if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS2){
        while(check_stand_param(PS1_ADDR,MB_PS2_DISCHRG_KEY_STATE,state,2) == false && cnt){
            emit set_stand_discharge_key(state);
            cnt--;
        }
    }
}

void TestThread::setTestPageRequestResult(TestPageRequestResult result)
{
    testPageRequestResult = result;
}


void TestThread::requestTestPagePro()
{
    QProcess process;

    QByteArray result;


    syslog("Запрос тестовой страницы PRO",I);
    process.start("get_testpage_pro.bat");
    qDebug() << "process.start";
    process.waitForFinished();


    qDebug() << "process.waitForFinished";

    QFile file("pageloader/selftest.txt");
    if (file.open(QIODevice::ReadOnly | QIODevice::Text)) {
        result = file.readAll();

        qDebug() << result;
        file.close();

        if (result == "invalid testpage") {
            qDebug() << "Invalid test page received";
            setTestPageRequestResult(TestPageRequestResult::FAILURE);
            QString error = QString("Ошибка подключения к %1").arg(DUT_IP_ADDR);
            syslog("Тестовая страница PRO не загружена",E);
        } else {
            qDebug() << "Valid XML content:" << result;
            syslog("Тестовая страница PRO загружена успешно",I);
            setTestPageRequestResult(TestPageRequestResult::SUCCESS);
            setTestPageData(result);
        }
    }
    else{
        syslog("Тестовая страница PRO не загружена",E);
        qDebug() << "File not exist";
    }

    setWebmanagerFinished();
}


void TestThread::setTestPageData(QByteArray data)
{
    testPageData = data;
    //syslog("testPageData = " + data, I);
}

void TestThread::SetUpsStatus(int status)
{
    psw_selftest_result.ups_rez = static_cast<uchar>(status);
}

void TestThread::SetIrpStatus(int status)
{
    psw_selftest_result.ups_det = static_cast<uchar>(status);
}

void TestThread::SetUpsVoltage(double voltage)
{
    psw_selftest_result.akb_voltage = static_cast<float>(voltage);
}

void TestThread::setStatusSendReport(bool status)
{
    sendReportStatus = status;
}

void TestThread::initTestStruct(struct configmodel config)
{
    test_struct.selftest = config.buildin_test;
    qDebug() << "selftest" << test_struct.selftest;
    test_struct.update = config.firmware_load;
    qDebug() << "update" << test_struct.update;

    test_struct.heater = config.test_heating;
    qDebug() << "heater" << test_struct.heater;

    test_struct.poe = config.poe_test;
    qDebug() << "poe" << test_struct.poe;

    test_struct.acBackup = config.ac_backup_test;
    qDebug() << "acBackup" << test_struct.acBackup;

    test_struct.inOut = (config.tlp_rs485 || config.i2c_test || config.dry_cont_test[0] || config.dry_cont_test[1] || config.dry_cont_test[2]);
    qDebug() << "inOut" << test_struct.inOut;

    test_struct.dataTest = config.data_test;
    qDebug() << "dataTest" << test_struct.dataTest;

    test_struct.ups = config.test_ups;
    qDebug() << "ups" << test_struct.ups;

    test_struct.setMac = config.send_mac;
    qDebug() << "setMac" << test_struct.setMac;

    test_struct.printLabel = config.print_label;
    qDebug() << "printLabel" << test_struct.printLabel;
}
