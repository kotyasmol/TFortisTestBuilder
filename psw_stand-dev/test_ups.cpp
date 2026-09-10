#include <stdlib.h>
#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "functions.h"
#include "stdio.h"
#include "math.h"
#include <QtXml/QtXml>
#include <QtXml/QDomElement>
#include <QFileDialog>
#include <QMessageBox>
#include <QFile>
#include <QProcess>
#include <QDebug>
#include <QPrinter>
#include <QPrintDialog>
#include <QPrinterInfo>
#include <winnt.h>
#include <winspool.h>
#include <windef.h>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QNetworkAccessManager>
#include "TestThread.h"
#include "devicebase.h"
#include <QTimer>

/*Проверка узла ИБП*/
/*План проверки:
1) определяем, что устройство с ИБП
2) на БП выставляем напряжение discharge_power_supply_voltage
3) включаем ключ Разряд
4) выставляем сопротивление нагрузки charge_CC_resistance
5) включаем ключ Заряд
6) Измерение
7) выставляем сопротивление нагрузки charge_CV_resistance (для платы RPS-01)
8) Измерение
9) Отключение AC
10)выключаем ключ Заряд
11)Измерение*/

void TestThread::UpsTestPS2(){
    emit syslog("Старт проверки UPS",S);

    // Считываем новые данные со стенда
    if(prog_sett.stand_type == StandType::typeAPK03){
        //dev->mb_update();
        //Pause(2*SEC);
        dev->mb_update();
        Pause(3*SEC);
    }

    if(dev->get_mb_devtype(PS1_ADDR) != DEV_PS2 && !dev->is_simbat24_connected() && !dev->is_simbat48_connected()) //&& !=simbat24 && !=simbat48
    {
        throw "Тест не может быть запущен: не подключена плата PS-2";
    }
    emit clear_mb_ps2_minmax();

    //установка напряжения на внешнем БП, если он управляем,
    //если нет, считаем, что напряжение подано всегда
    if(prog_sett.power_supply_state){

    }

    //включаем ключ Разряд - подключаем БП
    emit set_stand_discharge_key(1);
    emit syslog("Включение ключа разрядки", I);

    Sleep(15000);

    //включаем ключ Заряд - подключаем нагрузку
    //emit set_stand_charge_key(1);
    //Sleep(1000);

    //QThread::sleep(test_config.start_delay);
    emit syslog("Загружаем тестовую страницу", I);
    testPageWasParsed = false;
    LoadTestPageIfNeeded();

    // Обнаруживаем платы RPS или IRP
    // Иногда PSW возвращает мусор. Если тестовая страница вернула мусор, перезапросим через API
    if (psw_selftest_result.ups_det != 0 && psw_selftest_result.ups_det != 1)
    {
        emit syslog("Перезапрашиваем статус ИБП", I);
        QThread::sleep(test_config.start_delay);
        if(is_model_type_PRO(test_config.model_name)){
            emit syslog("Загружаем тестовую страницу", I);
            testPageWasParsed = false;
            LoadTestPageIfNeeded();
        }
        else{
            GetIrpStatus();
        }
    }
    if(psw_selftest_result.ups_det != 1)
    {
        emit send_report_d("детекция ИБП", FAIL, FAIL);
        throw "Не видим ИБП";
    }
    emit send_report_d("детекция ИБП", OK, OK);

    //Обнаруживаем АКБ
    int timeoutWaitingAKBdet = 30 * SEC;
    QElapsedTimer akbTimer;
    akbTimer.start();
    while (psw_selftest_result.akb_det != 1)
    {
        if(is_model_type_PRO(test_config.model_name)){
            emit syslog("Загружаем тестовую страницу", I);
            testPageWasParsed = false;
            LoadTestPageIfNeeded();
        }
        else
            GetUpsVoltage();

        if (akbTimer.elapsed() > timeoutWaitingAKBdet)
            break;
        QThread::msleep(1000);
    }

    if (psw_selftest_result.akb_det != 1 )
    {
        bool voltageIsNormal = psw_selftest_result.akb_voltage*1000 > test_config.charge_CC_voltage_min
                && psw_selftest_result.akb_voltage*1000 < test_config.charge_CC_voltage_max;

        if(!voltageIsNormal)
        {
            emit syslog("Перезапрашиваем напр. АКБ", I);
            QThread::sleep(test_config.start_delay);
            if(is_model_type_PRO(test_config.model_name)){
                emit syslog("Загружаем тестовую страницу", I);
                testPageWasParsed = false;
                LoadTestPageIfNeeded();
            }
            else
                GetUpsVoltage();
        }

        emit syslog(QString("Напряжение на АКБ = %1 V").arg(psw_selftest_result.akb_voltage), I);

        voltageIsNormal = psw_selftest_result.akb_voltage*1000 > test_config.charge_CC_voltage_min
                && psw_selftest_result.akb_voltage*1000 < test_config.charge_CC_voltage_max;

        if(!voltageIsNormal)
        {
            emit send_report_d("детекция АКБ",FAIL,FAIL);
            throw "Не видим АКБ";
        }
    }
    emit send_report_d("детекция АКБ", OK, OK);

    // Смотрим, что питание от сети
    // Иногда PSW возвращает мусор. Если тестовая страница вернула мусор, перезапросим через API
    if (psw_selftest_result.ups_rez != 0)
    {
        emit syslog("Перезапрашиваем статус питания", I);
        QThread::sleep(test_config.start_delay);
        if(is_model_type_PRO(test_config.model_name)){
            emit syslog("Загружаем тестовую страницу", I);
            testPageWasParsed = false;
            LoadTestPageIfNeeded();
        }
        else
            GetUpsStatus();
    }
    if (psw_selftest_result.ups_rez != 0)
    {
        throw "Не видим питание от сети";
    }

    emit syslog("Питание от сети", I);

    //устанавливаем сопротивление нагрузки для СС
    if(test_config.charge_CC_test){
        emit syslog("устанавливаем сопротивление нагрузки для СС", I);
        emit set_stand_charge_rload(test_config.charge_CC_resistance);
        Sleep(2000);
        // Считываем новые данные со стенда
        if(prog_sett.stand_type == StandType::typeAPK03){
            dev->mb_update();
            Pause(3*SEC);
        }
        qDebug()<< test_config.charge_CC_voltage_max << test_config.charge_CC_voltage_min << dev->get_mb_charge_voltage();

        if((dev->get_mb_charge_voltage() > test_config.charge_CC_voltage_max) ||
                (dev->get_mb_charge_voltage() < test_config.charge_CC_voltage_min))
        {
            emit send_report_d("напряжение зарядки", FAIL, dev->get_mb_charge_voltage());
            emit syslog(QString("Напряжение зарядки на нагрузку %1 Ом = %2").arg(test_config.charge_CC_resistance).arg(dev->get_mb_charge_voltage()), E);
            throw "Напряжение АКБ не в допуске";
        }
        else
        {
            emit send_report_d("напряжение зарядки", OK, dev->get_mb_charge_voltage());
            emit syslog(QString("Напряжение зарядки на нагрузку %1 Ом = %2").arg(test_config.charge_CC_resistance).arg(dev->get_mb_charge_voltage()), I);
        }

        if(test_config.charge_CC_current_max < dev->get_mb_charge_current() ||
                test_config.charge_CC_current_min > dev->get_mb_charge_current())
        {
            emit send_report_d("ток зарядки", FAIL, dev->get_mb_charge_current());
            emit syslog(QString("Ток зарядки на нагрузку %1 Ом = %2").arg(test_config.charge_CC_resistance).arg(dev->get_mb_charge_current()), E);
            throw "Ток зарядки АКБ не в допуске";
        }
        else
        {
            emit send_report_d("ток зарядки", OK, dev->get_mb_charge_current());
            emit syslog(QString("Ток зарядки на нагрузку %1 Ом = %2").arg(test_config.charge_CC_resistance).arg(dev->get_mb_charge_current()), I);
        }
    }

    //устанавливаем сопротивление нагрузки для СV
    if(test_config.charge_CV_test){
        emit set_stand_charge_rload(test_config.charge_CV_resistance);
        Sleep(300);
        // Считываем новые данные со стенда
        if(prog_sett.stand_type == StandType::typeAPK03){
            dev->mb_update();
            Pause(3*SEC);
        }
        if((test_config.charge_CV_voltage_max < dev->get_mb_charge_voltage()) ||
                (test_config.charge_CV_voltage_min > dev->get_mb_charge_voltage()))
        {
            emit send_report_d("напряжение зарядки", FAIL, dev->get_mb_charge_voltage());
            emit syslog(QString("Напряжение зарядки на нагрузку %1 Ом = %2").arg(test_config.charge_CV_resistance).arg(dev->get_mb_charge_voltage()), E);
            throw "Напряжение АКБ не в допуске";
        }
        else
        {
            emit send_report_d("напряжение зарядки", OK, dev->get_mb_ps2_charge_voltage());
            emit syslog(QString("Напряжение зарядки на нагрузку %1 Ом = %2").arg(test_config.charge_CV_resistance).arg(dev->get_mb_charge_voltage()), I);
        }

        if((test_config.charge_CV_current_max < dev->get_mb_charge_current()) ||
                (test_config.charge_CV_current_min > dev->get_mb_charge_current()))
        {
            emit send_report_d("ток зарядки", FAIL, dev->get_mb_charge_current());
            emit syslog(QString("Ток зарядки на нагрузку %1 Ом = %2").arg(test_config.charge_CV_resistance).arg(dev->get_mb_charge_current()), E);
            throw "Ток зарядки АКБ не в допуске";
        }
        else
        {
            emit send_report_d("ток зарядки", OK, dev->get_mb_charge_current());
            emit syslog(QString("Ток зарядки на нагрузку %1 Ом = %2").arg(test_config.charge_CV_resistance).arg(dev->get_mb_charge_current()), I);
        }
    }

    //отключаем ключ Заряд
    emit set_stand_charge_key(0);
    Sleep(500);

    //отключаем сетевое напряжение
    Sleep(300);
    TurnOffAC();
    //emit syslog("Отключение AC1", I);
    //отключаем реле проверки тока нагревателей
    if (test_config.test_heating)
    {
        emit set_stand_heater1_relay(0);
        emit set_stand_heater2_relay(0);
    }

    //отключаем ключ зарядки
    emit set_stand_charge_key(0);
    Sleep(500);

    int timeoutWaitingUPS = test_config.start_delay * SEC;
    QElapsedTimer timer;
    timer.start();

    emit syslog("Запрашиваем статус UPS", I);

    while (psw_selftest_result.ups_rez != 1)
    {
        if(is_model_type_PRO(test_config.model_name)){
            emit syslog("Загружаем тестовую страницу", I);
            testPageWasParsed = false;
            LoadTestPageIfNeeded();
        }
        else
            GetUpsStatus();  // Периодически запрашиваем статус UPS
        Sleep(5000);
        emit syslog(QString("Работа от АКБ = %1").arg(psw_selftest_result.ups_rez), I);

        if (timer.elapsed() > timeoutWaitingUPS || psw_selftest_result.ups_rez == 1)
            break;

    }

    qDebug() << QString("Getting time ups_rez: %1").arg(timer.elapsed());
    if (psw_selftest_result.ups_rez != 1)
    {
        emit send_report_d("переключение на АКБ", FAIL, FAIL);
        throw "Не видим переход на АКБ";
    }

    emit send_report_d("переключение на АКБ",OK,OK);

    //emit syslog("Включение ключа зарядки",I);
    //emit set_stand_charge_key(1);
    //emit syslog("Включение ключа разрядки",I);
    //emit set_stand_discharge_key(1);

    Sleep(2000);

    //измеряем напряжения и токи
    int slot;
    if(dev->get_simbat_type() == SimbatType::SIMBAT24)
    {
        slot = dev->get_simbat24_slot();
    }else
    {
        slot = dev->get_simbat48_slot();
    }

    emit syslog("Измеряем напряжение и ток разрядки",I);
    int cnt =3;

    while(check_stand_param(slot,MB_SIMBAT_DISCHARGE_VOLTAGE,1,2) == false && cnt){
        cnt--;
    }
    cnt =3;

    while(check_stand_param(slot,MB_SIMBAT_DISCHARGE_CURRENT,1,2) == false && cnt){
        cnt--;
    }
    emit syslog(QString("Напряжение разрядки = %1 V").arg(static_cast<double>(dev->get_mb_simbat_discharge_voltage(slot)) / 1000), I);
    emit syslog(QString("Ток разрядки = %1 A").arg(static_cast<double>(dev->get_mb_simbat_discharge_current(slot))/ 1000), I);

    if(test_config.use_ac1 == 1)
    {
        emit stand_ac1_on();
        Sleep(3000);
        //waitingStartDevice(test_config.start_delay * SEC);
    }

    timer.restart();

    while(psw_selftest_result.ups_rez != 0)
    {
        if(is_model_type_PRO(test_config.model_name)){
            emit syslog("Загружаем тестовую страницу", I);
            testPageWasParsed = false;
            LoadTestPageIfNeeded();
        }
        else
            GetUpsStatus();  // Периодически запрашиваем статус UPS
        Sleep(5000);
        emit syslog(QString("Работа от АКБ = %1").arg(psw_selftest_result.ups_rez), I);

        if (timer.elapsed() > timeoutWaitingUPS || psw_selftest_result.ups_rez == 0)
            break;
    }

    emit syslog("Отключение ключей зарядки и разрядки",I);

    emit set_stand_discharge_key(0);

    emit set_stand_charge_key(0);
}
