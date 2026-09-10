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
#include "debugwindow.h"

extern int is_poe_load_configured;
#define GET_HELP_HTML         parse_test_shtml_flag = false;\
    parse_cnt = 0;\
    get_help_html();\
    while((parse_test_shtml_flag == false)&&(parse_cnt < MAX_CNT )){\
    Sleep(10);\
    parse_cnt++;\
    }

#define CHECK_SERIAL_NUM      nettest.get_serial = false; \
    parse_cnt = 0;\
    check_serial_num(psw_selftest_result.cpu_id);\
    Sleep(1000);\
    while((nettest.get_serial==false)&&(parse_cnt < 15 )){\
    Sleep(1000);\
    parse_cnt++;\
    }
//получение идентификатора
#define GET_SERIAL_ID   nettest.get_id = false; \
    nettest.id = 0;\
    parse_cnt = 0;\
    qDebug() << "GET_SERIAL_ID"; \
    get_serial_id();\
    Sleep(1000);\
    while((nettest.get_id==false)&&(parse_cnt < 15 )){\
    Sleep(1000);\
    parse_cnt++;\
    }

#define IF_STOP if(stop_test==true){qDebug()<<"HARD STOP TEST"; ManualStopTest(); continue;}

#define TELEPORT_RS485_HELLO tlp_send_rs485_hello();\
    Sleep(5*SEC);

void TestThread::process(){

    int  parse_cnt;

    QString tmp,str;
    ports_pair_t ports_pair[PORT_NUM];
    int i,j;

    psw_selftest_result.cpu_id.clear();
    nettest.serial_num = 0;

    stop_test = 0;
    start_test_flag = 0;
    psw_selftest_result.cpu_id = "";

    if(prog_sett.stand_type == StandType::typeAPK03)
        dev->set_stand_ac1(0);

    while(1)
    {
        Sleep(2000);
        //отдельный тест RPS-1
        if(rpsStartFlag && !start_test_flag){
            rps_stand_processing_new();
        }

        if(start_test_flag == true)
        {

            testPageWasParsed = false;

            //получаем номер модели (из настроек)
            qDebug() << "test_config.model_num" << test_config.model_num;

            if(prog_sett.stand_type == StandType::typeAPK03){
                for(int i=0;i<MB_MAX_ADDR;i++)
                    dev->modbus_data[i] = 0;
            }

            if(prog_sett.test_type == TYPE_PRODUCTION)
                emit syslog("Тип теста: Выпуск",I);
            else if(prog_sett.test_type == TYPE_REPAIR)
                emit syslog("Тип теста: Ремонт",I);

            if(test_config.pre_comment.length())
                emit syslog(test_config.pre_comment,C);

            if(test_struct.selftest)
            {
                clear_arp_case();
            }

            //подключние нагрузки PoE
//            if(test_config.poe_test){
                //включение нагрузки и установка мощности
                if(!is_poe_load_configured){

                    configure_stand_poe();
                    int cnt = 0;
                    while(!check_poe_configuration() && cnt < 15)
                    {
                        configure_stand_poe();//todo у Паши было закомментировано
                        Sleep(400);

                        cnt++;
                    }

                    is_poe_load_configured = 1;
                }
//            }

            if(test_struct.ups)
            {
                if(test_config.charge_CV_voltage_min < SIMBAT_GET_TYPE_VOLTAGE)
                {
                    dev->set_simbat_type(SimbatType::SIMBAT24);
                    emit syslog("Используется SIMBAT24",I);
//                    emit syslog("Размыкание ключей SIMBAT24",I);
//                    dev->set_stand_discharge_key(0);
//                    dev->set_stand_charge_key(0);

                }
                else
                {
                    dev->set_simbat_type(SimbatType::SIMBAT48);
                    emit syslog("Используется SIMBAT48",I);
//                    emit syslog("Размыкание ключей SIMBAT48",I);
//                    set_stand_discharge(0);
//                    set_stand_charge_key(0);
                }
            }else
            {
                dev->set_simbat_type(SimbatType::NotConnected);
            }

            //настройка оборудования для АПК-Стенд-3
            if(prog_sett.stand_type == StandType::typeAPK03){

                //настройка промежуточного комутатора, если используется
                if(prog_sett.switch_state){
                    emit syslog("Настройка промежуточного комутатора", I);
                    emit pDataTestThread->telnet_config_chain(test_config.switch_config_normal);
                    Sleep(100);
                }

                Pause(200);

                //подключаем акб
                if(test_struct.ups){
                    if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS1){
                        emit syslog("Подключение АКБ",I);
                        set_stand_akb_state(1);
                        set_stand_akb_direction(1);
                    }
                    else{
                        //включаем ключ Разряд - подключаем БП
                        emit set_stand_discharge_key(1);
                        Sleep(300);
                    }
                }

                IF_STOP

                        Pause(500);



                TurnOnAC();

                //подаём питание
//                if(test_config.use_ac1){
//                    set_stand_ac1_state(1);
//                }
//                Pause(100);

//                if(test_config.use_ac2){
//                    set_stand_ac2_state(1);
//                }
//                Pause(100);

                    emit syslog("Ожидание запуска тестируемого оборудования", I);
                    // Ждем включение устойства
                    waitingStartDevice(test_config.start_delay * SEC);
                    //waitingStartDevice(test_config.start_delay * SEC);
                    Sleep(2000);

                    if(resultRequestTestPage)
                    {
                        emit syslog("Тестируемое оборудование запущено", I);
                    }
                    else
                    {
                        emit syslog("Тестируемое оборудование не отвечает", I);
                        StandOff(); // Отключаем все питание
                        emit test_finished(ERROR_STOP, nettest.serial_num);
                        Sleep(100);
                        emit syslog("Тест завершен", I);
                        start_test_flag = false;
                        stop_test = true;
                    }

                    IF_STOP

/*************************Самотестирование**************************/

                if(test_struct.selftest)
                {
                    try {
                        SelfTestStage();
                    }  catch (const char* ex) {
                        QString message = QString::fromUtf8(ex);
                        emit syslog(message, E);
                        StopTest(ERROR_EMBEDTEST);
                        continue;
                    }
                }
                IF_STOP

/********************************************************************/

 //получение предварительного серийного номера
                        if((prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld)&&
                                (test_struct.printLabel || (test_struct.selftest && test_struct.setMac))){

                            try
                            {
                                // TODO разбить метод на новый тест и тест для ремонта
                                GetSerialIdOrNumber();     // Получаем серийный номер
                            }
                            catch (const char* ex)
                            {
                                QString message = QString::fromUtf8(ex);
                                emit syslog(message, E);
                                StopTest(ERROR_REPORT);
                                continue;
                            }

                            //получение серийного номера для ремонта
                            if(prog_sett.test_type == TYPE_REPAIR){
                                //если устройство неуправляемое, получаем серийник по ID
                                if(!test_struct.selftest){
                                    if(test_config.serial_num == 0){
                                        //получение серийного номера
                                        getSerialNumber();
                                        if(nettest.get_serial == false){
                                            emit syslog("Запрос серийного номера: не получен ответ от сервера, повторный запрос",E);
                                            //получение серийного номера
                                            getSerialNumber();
                                            if(nettest.get_serial == false){
                                                emit syslog("Запрос серийного номера: не получен ответ от сервера",E);

                                                StopTest(ERROR_REPORT);
                                                continue;
                                            }
                                        }
                                        psw_selftest_result.cpu_id = test_config.id;
                                    }
                                    else{
                                        //проверяем существование серийного номера
                                        //если serial указали явно
                                        nettest.serial_num = test_config.serial_num;
                                    }
                                }

                            }

                            IF_STOP
                        }


/************************Проверка нагревателей*************************************/

                if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){
                    if(test_struct.heater)
                    {

                        try {
                            HeaterTestStage();
                        }  catch (const char* ex) {
                            QString message = QString::fromUtf8(ex);
                            emit syslog(message, E);
                            StopTest(HEATING_ERROR);
                            continue;
                        }
                    }
                }
                    IF_STOP

/********************************************************************************/



/**************************Тест PoE********************************************/

                    if(test_struct.poe)
                    {
                        // Тестируем PoE
                        int resultTestPoE = PoeTest();

                        // Заканчиваем тест, если не пройден
                        if (resultTestPoE != POE_TEST_SUCCESS)
                        {
                            emit set_stage_result(0, RED);

                            StopTest(ERROR_POE1 + resultTestPoE);
                            continue;
                        }

                        // Если тест пройден
                        Sleep(100);
                        emit set_stage_result(0, GREEN);
                    }

                    IF_STOP

/********************************************************************************/



/*************Тестируем переход блоков питания на резерв. Тестируем PoE*********/

                    if (test_struct.acBackup)
                    {
                        /*
                         * 1. Подаем питание на левый блок питания
                         * 2. Ждем включение устойства
                         * 3. Измеряем PoE
                         * 4. Включаем правый блок питания. Отключаем левый.
                         * 5. Измеряем PoE
                         * 6. Если все ок. То считаем что тест пройден. И тест PoE тоже пройден
                         */

                        // Подаем питание на левый блок питания


                        // Ждем включение устойства
                        //waitingStartDevice(test_config.start_delay * SEC);

                        //emit syslog("Тестируемое оборудование запущено", I);

                        // Тестируем PoE
                        //int resultTestPoE = PoeTest();

                        // Заканчиваем тест, если не пройден
//                        if (resultTestPoE != 0)
//                        {
//                            emit syslog("Тест PoE не пройден", E);
//                            emit set_stage_result(0, RED);

//                            StopTest(ERROR_POE1 + resultTestPoE);
//                            continue;
//                        }

                        // Окрашиваем лейбл тестирования PoE
//                        emit set_stage_result(0, GREEN);

                        // Включаем правый блок питания. Отключаем левый.
                        set_stand_ac2_state(1);

                        //emit syslog("Подключение AC2", I);
                        Pause(200);
                        set_stand_ac1_state(0);
                        //debug_window->debug_set_ac1_state(0);

                        // Тестируем PoE
                        int resultTestPoE = PoeTest();

                        // Заканчиваем тест, если не пройден
                        if (resultTestPoE != POE_TEST_SUCCESS)
                        {
                            emit syslog("Тест PoE не пройден", E);
                            emit set_stage_result(0, RED);
                            emit syslog("Тест резервирования БП не пройден", E);
                            emit set_stage_result(9, RED);

                            StopTest(ERROR_POE1 + resultTestPoE);
                            continue;
                        }

                        set_stand_ac1_state(1);

                        Pause(300);

                        set_stand_ac2_state(0);

                        // Если тест пройден
                        Sleep(100);
                        // Окрашиваем лейбл тестирования PoE
                        emit set_stage_result(0, GREEN);
                        // Окрашиваем лейбл тестирования Блоков питания
                        emit set_stage_result(9, GREEN);
                        emit syslog("Тест резервирования БП пройден", S);

                    }



/************************************************************************/

//                //обновление ПО в первую очередь
//                if(test_config.firmware_load_first){
//                    LoadTestPageIfNeeded();
//                    UpdatePSW();
//                }
//                //ПО обновлено

//                IF_STOP

/*********************Проверка входов/выходов***************************/

                    //Проверка входов/выходов платы IO-02
                    if (test_struct.inOut)
                    {
                        if(dev->io02_is_connected()){
                            emit syslog("Проверка In/Out на плате IO-02", S);
                            try{
                                InOutTestStage();
                            }
                            catch (const char* ex) {
                                QString message = QString::fromUtf8(ex);
                                emit syslog(message, E);
                                emit set_stage_result(8,RED);
                                StopTest(ERROR_INPUT1);
                                continue;
                            }
                        }


                        IF_STOP

                                //Тестирование I2C / датчик температуры и влажности

                                if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){
                            if (test_config.i2c_test == 1){
                                emit syslog("Тест I2C", S);
                                try
                                {
                                    I2CTest();
                                    emit syslog("I2C проверен",S);
                                    emit send_report_d("I2C", OK, OK);
                                }
                                catch (const char* ex)
                                {
                                    QString message = QString::fromUtf8(ex);
                                    emit syslog(message, E);
                                    emit set_stage_result(8, RED);
                                    emit send_report_d("I2C", FAIL, FAIL);
                                    StopTest(ERROR_I2C);
                                    continue;
                                }
                            }
                            IF_STOP
                        }

                        //Тестирование Teleport

                        int errorInput;
                        if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){

                            if(prog_sett.test_type == TYPE_TELEPORT){
                                //перевести в тестовый режим
                                emit set_tlp_test_mode(1);
                                //входы
                                bool breakLoop = false;
                                for(i=0;i<TLP_INPUTS_NUM;i++){
                                    if(test_config.tlp_inputs[i]){
                                        if(psw_selftest_result.tlp_input[i]!=0){
                                            str.sprintf("Ошибка входа %d: замкнут",i+1);
                                            emit syslog(str,E);
                                            emit set_stage_result(8,RED);
                                            emit set_tlp_input_result(i,RED);
                                            str.sprintf("вход %d",i+1);
                                            emit send_report_d(str,FAIL,FAIL);
                                            errorInput = i;
                                            breakLoop = true;
                                            break;
                                        }
                                    }
                                }

                                if (breakLoop)
                                {
                                    StopTest(ERROR_INPUT1 + errorInput);
                                    continue;
                                }
                                //todo
                                //замкнуть входы
                                emit syslog("Замкнуть входы",C);
                                Sleep(5*SEC);

                                // GET_TEST_HTML;

                                emit syslog("Запрашиваем тестовую страницу", I);
                                bool resultRequestTestPage = requestTestPage();                  // Запрашиваем тестовую страницу
                                if (!resultRequestTestPage)                                      // Если неудалось получить или распарсить тестовую страницу, заканчиваем тест
                                {
                                    //errorcode = ERROR_GET_TEST_PAGE;
                                    emit syslog(QString("Тестовая страница не была загружена"), E);
                                    //goto stop;
                                    nettest.parsed = false;
                                    emit send_report_d("Загрузка тестовой страницы", FAIL, FAIL);

                                    StopTest(ERROR_GET_TEST_PAGE);
                                    continue;
                                }
                                emit syslog(QString("Тестовая страница была загружена"), I);

                                emit syslog("Парсим тестовую страницу", I);
                                bool parseTestPageResult = parse_html_file(testPageData);         // Парсим тестовую страницу

                                if (!parseTestPageResult)                                         // Если неудалось распарсить страницу, заканчиваем тест
                                {
                                    emit syslog(QString("Тестовая страница не была распарсена"), E);
                                    emit send_report_d("Загрузка тестовой страницы", FAIL, FAIL);

                                    nettest.parsed = false;
                                    StopTest(ERROR_PARSE_TEST_PAGE);
                                    continue;
                                }

                                emit syslog(QString("Тестовая страница была распарсена"), I);

                                breakLoop = false;
                                for(i=0;i<TLP_INPUTS_NUM;i++){
                                    if(test_config.tlp_inputs[i]){
                                        if(psw_selftest_result.tlp_input[i]!=1){
                                            str.sprintf("Ошибка входа %d: разомкнут",i+1);
                                            emit syslog(str,E);
                                            emit set_stage_result(8,RED);
                                            emit set_tlp_input_result(i,RED);
                                            str.sprintf("вход %d",i+1);
                                            errorInput = i;
                                            emit send_report_d(str,FAIL,FAIL);
                                            breakLoop = true;
                                            break;
                                        }
                                        else
                                            emit set_tlp_input_result(i,GREEN);
                                    }
                                }
                                if (breakLoop)
                                {
                                    StopTest(ERROR_INPUT1 + errorInput);
                                    continue;
                                }

                                //выходы
                                emit syslog("Проконтролируйте выходы",C);
                                for(i=0;i<TLP_OUTPUTS_NUM;i++){
                                    if(test_config.tlp_outputs[i]){
                                        emit set_tlp_output_state(i,1);
                                        Sleep(1*SEC);
                                    }
                                }

                                //ожидание подтверждения
                                WAIT_CONFIRM("Все выходы исправны?");
                                emit syslog("Все выходы исправны",C);
                                int errorOutput;
                                //todo
                                //потом автоматизировать
                                breakLoop = false;
                                for(i=0;i<TLP_OUTPUTS_NUM;i++){
                                    if(test_config.tlp_outputs[i] ){
                                        if(confirm_status == 0){
                                            str.sprintf("Ошибка выхода %d",i+1);
                                            emit syslog(str,E);
                                            emit set_stage_result(8,RED);
                                            emit set_tlp_output_result(i,RED);
                                            str.sprintf("выход %d",i+1);
                                            emit send_report_d(str,FAIL,FAIL);

                                            errorOutput = i;
                                            breakLoop = true;
                                            break;
                                        }
                                        else{
                                            emit set_tlp_output_result(i,GREEN);
                                            str.sprintf("выход %d",i+1);
                                            emit send_report_d(str,OK,OK);
                                        }
                                    }
                                }
                                if (breakLoop)
                                {

                                    StopTest(ERROR_OUTPUT1 + errorOutput);
                                    continue;
                                }

                                //rs-485
                                if(test_config.tlp_rs485 == 1)
                                {
                                    emit syslog("Тест RS485", I);

                                    try
                                    {
                                        RS485Test();
                                        emit syslog("RS-485 проверен",I);
                                        emit send_report_d("RS-485", OK, OK);
                                    }
                                    catch (const char* ex)
                                    {
                                        QString message = QString::fromUtf8(ex);
                                        emit syslog(message, E);
                                        emit set_stage_result(8, RED);
                                        emit send_report_d("RS-485", FAIL, FAIL);

                                        StopTest(ERROR_RS485);
                                        continue;
                                    }
                                }
                            }
                        }

                        //Тестирование RS-485 для PSW

                        if (test_config.tlp_rs485 == 1 && prog_sett.test_type != TYPE_TELEPORT)
                        {
                            try
                            {
                                RS485Test();
                                emit syslog("RS-485 проверен",S);
                                emit set_stage_result(8, GREEN);
                                emit send_report_d("RS-485", OK, OK);
                            }
                            catch (const char* ex)
                            {
                                QString message = QString::fromUtf8(ex);
                                emit syslog(message, E);
                                emit set_stage_result(8, RED);
                                emit send_report_d("RS-485", FAIL, FAIL);

                                StopTest(ERROR_RS485);
                                continue;
                            }
                        }

                        IF_STOP


                                emit set_stage_result(8,GREEN);
                    }

/**************************************************************************************/


/**************************Data-тест***************************************************/

                //тестирование с использованием шлейфа коммутатора SWU-16
                if(test_struct.dataTest && (test_config.model_num==200 || test_config.model_num==203) &&
                        test_config.data_test_chain && prog_sett.switch_state)
                {
                    emit syslog("Старт тестирования с использованием шлейфа",I);
                    if(test_config.buildin_test){
                        emit syslog("Перевод коммутатора в тестовый режим",I);
                        emit set_sw_test_mode(1);

                        Sleep(100);
                    }
                    else{
                        emit syslog("Неверная конфигурация: неуправляемый коммутатор "
                                    "невозможно перевести в режим шлейфа",E);
                        StopTest(ERROR_DATATEST_CONFIGURATION);
                        continue;
                    }
                    pDataTestThread->set_type(TYPE_HARD_CHAIN);//тип генератора
                    //настраиваем порты промежуточного коммутатора
                    if(prog_sett.switch_state){
                        emit pDataTestThread->telnet_config_chain(test_config.switch_config_chain);
                    }
                    //если telnet выключен, втыкаем перемычки ручками
                    else{
                        //ожидание подтверждения
                        WAIT_CONFIRM("Установите перемычки шлейфа");
                    }
                    emit syslog("Тестовый шлейф настроен",C);
                    Sleep(3000);
                    pDataTestThread->data_start();
                    Sleep(1000);
                    //устанавливаем таймер.
                    qDebug() << "datatest  timer start";
                    timer_cnt = 0;
                    while(pDataTestThread->is_finished()==0){
                        Sleep(1000);
                        qDebug() << timer_cnt;
                        if(timer_cnt > 60){
                            qDebug() << "datatest  timeout";
                            break;
                        }
                        timer_cnt++;
                    }
                    qDebug() << "datatest  is_finished";
                    emit syslog("Получение результатов",1);
                    Sleep(100);

                    //set rezult
                    pDataTestThread->stop();
                    Sleep(100);

                    if(pDataTestThread->get_ports_status(0) == TEST_OK){
                        emit set_stage_result(3,GREEN);
                        for(i=0;i<PORT_NUM;i++){
                            if(test_config.data_test_ports[i] == 1){
                                emit set_dataport_result(ports_pair[i].in,GREEN);
                                emit set_dataport_result(ports_pair[i].out,GREEN);
                                tmp.sprintf("Передача данных порт %d",i);
                                emit send_report_d(tmp,OK,OK);
                                tmp.sprintf("Передача данных порт %d: успешно",i);
                                emit syslog(tmp,I);
                            }
                        }
                         emit syslog("Тест передачей данных: успешно",1);
                    }
                    else{
                        for(i=0;i<PORT_NUM;i++){
                            if(test_config.data_test_ports[i] == 1){
                                emit set_dataport_result(ports_pair[i].in,RED);
                                emit set_dataport_result(ports_pair[i].out,RED);

                                emit set_stage_result(3,RED);
                                emit send_report_d(tmp.sprintf("Передача данных порт %d",i),FAIL,FAIL);
                            }
                        }
                        StopTest(ERROR_DATATEST1 + i);
                        continue;
                    }

                    //отключение шлейфа
                    if(prog_sett.switch_state){
                        emit pDataTestThread->telnet_config_sw(test_config.data_test_ports,prog_sett.port_dut);
                    }
                    //если telnet выключен, убираем перемычки ручками
                    else{
                        //ожидание подтверждения
                        WAIT_CONFIRM("Уберите перемычки шлейфа, загрузите рабочую версию ПО, нажмите ОК для продолжения");
                    }
                    emit syslog("Тестовый шлейф отключен",C);
                }

            }

            //контроль передачи данных
            Sleep(1000);

            qDebug() << "test_config.data_test" << test_config.data_test;
            qDebug() << "test_config.data_test_chain" << test_config.data_test_chain;
            qDebug() << "prog_sett.switch_state" << prog_sett.switch_state;

            if(test_struct.dataTest){

                //если промежуточный коммутатор не подключен, проверяем сетевыми картами
                if(prog_sett.switch_state == 0 && test_config.port_num < PORT_NUM){
                    //clear all flags
                    for(int i=0;i<PORT_NUM;i++)
                        ports_pair[i].in_valid = ports_pair[i].out_valid = 0;

                    emit syslog("Запуск теста передачей данных",S);

                    emit syslog("Настройка проверки передачи данных",I);

                    //составляем пары теста
                    j = 0;
                    for(int i=0;i<PORT_NUM;i++){
                        if(test_config.data_test_ports[i]){
                            if(prog_sett.switch_state == 1){
                                if(ports_pair[j].in_valid==0){
                                    ports_pair[j].in = i;
                                    ports_pair[j].in_valid = 1;
                                }
                                else{
                                    if(ports_pair[j].out_valid==0){
                                        ports_pair[j].out = i;
                                        ports_pair[j].out_valid = 1;
                                        j++;
                                    }
                                }
                            }
                            else {
                                if(ports_pair[j].in_valid==0){
                                    ports_pair[j].in_ip = prog_sett.card_ip[i];
                                    ports_pair[j].in_valid = 1;
                                }
                                else
                                    if(ports_pair[j].out_valid==0){
                                        ports_pair[j].out_ip = prog_sett.card_ip[i];
                                        ports_pair[j].out_valid = 1;
                                        j++;
                                    }
                            }
                        }
                    }

                    //если нечетное количество
                    if((ports_pair[j].in_valid == 1)&&(ports_pair[j].out_valid == 0)){
                        if(ports_pair[j].in_ip != ports_pair[j-1].in_ip)
                            ports_pair[j].out_ip = ports_pair[j-1].in_ip;
                        else
                            ports_pair[j].out_ip = ports_pair[j-1].out_ip;
                        ports_pair[j].out_valid = 1;
                        ports_pair[j].out = j-1;
                    }

                    Sleep(100);
                    pDataTestThread->set_ports(ports_pair);
                    if(prog_sett.switch_state == 1)
                        pDataTestThread->set_type(TYPE_HARD_GEN);//тип генератора
                    else
                        pDataTestThread->set_type(TYPE_SOFT_GEN);

                    pDataTestThread->data_start();

                    qDebug() << "pDataTestThread->is_finished? " << pDataTestThread->is_finished();
                    qDebug() << "Wait finish dataTest";

                    QElapsedTimer timerDataTest;
                    timerDataTest.start();
                    while(!pDataTestThread->is_finished())
                    {
                        Pause(1000);
                        if (timerDataTest.elapsed() > 360000) // 6 мин
                        {
                            qDebug() << "datatest  timeout";
                            break;
                        }
                    }

                    qDebug() << "time left " << timerDataTest.elapsed();

                    emit syslog("Тест передачей данных окончен, получение результатов",I);
                    Sleep(100);
                    pDataTestThread->stop();
                    pDataTestThread->get_ports(ports_pair);

                    if(prog_sett.switch_state){
                        emit pDataTestThread->telnet_config_chain(test_config.switch_config_normal);

                        emit syslog("Отключение режима шлейфа",I);
                        emit pDataTestThread->telnet_config_sw(test_config.data_test_ports,prog_sett.port_dut);
                    }

                    int errorDatatest;
                    bool breakLoop = false;
                    for(int i=0;i<DATA_TEST_LINES;i++)
                    {
                        if(test_config.data_test_ports[i] != -1)
                        {
                            if(ports_pair[i].in_valid && ports_pair[i].out_valid)
                            {
                                if(pDataTestThread->get_ports_status(i) == TEST_OK)
                                {
                                    emit set_dataport_result(i,GREEN);
                                    emit send_report_d(tmp.sprintf("Передача данных порт %d",i),OK,OK);
                                    emit syslog(tmp.sprintf("Передача данных %s->%s: успешно",
                                                            ports_pair[i].in_ip.toLocal8Bit().data(),
                                                            ports_pair[i].out_ip.toLocal8Bit().data()),I);
                                }
                                else
                                {
                                    emit set_dataport_result(i,RED);

                                    emit set_stage_result(3,RED);
                                    emit send_report_d(tmp.sprintf("Передача данных порт %d",i),FAIL,FAIL);
                                    emit syslog(tmp.sprintf("Передача данных %s->%s: ошибка",
                                                            ports_pair[i].in_ip.toLocal8Bit().data(),
                                                            ports_pair[i].out_ip.toLocal8Bit().data()),E);

                                    errorDatatest = i;
                                    breakLoop = true;
                                    break;
                                }
                            }

                            emit set_stage_result(3,GREEN);
                        }

                    }
                    emit syslog("Тест передачей данных: успешно",S);


                    if (breakLoop)
                    {
                        StopTest(ERROR_DATATEST1 + errorDatatest);
                        continue;
                    }
                }

                //тестирование с использованием шлейфа и промежуточного коммутатора
                else if(test_config.data_test_chain && prog_sett.switch_state)
                {
                    if(test_config.buildin_test){
                        emit syslog("Перевод DUT коммутатора в тестовый режим",I);
                        emit set_sw_test_mode(1);
                        Sleep(100);
                    }
                    else{
                        emit syslog("Неверная конфигурация: неуправляемый коммутатор "
                                    "невозможно перевести в режим шлейфа",E);

                        StopTest(ERROR_DATATEST_CONFIGURATION);
                        continue;
                    }
                    pDataTestThread->set_type(TYPE_HARD_CHAIN);//тип генератора
                    //настраиваем порты промежуточного коммутатора
                    if(prog_sett.switch_state){
                        emit syslog("Настройка портов промежуточного коммутатора в режим шлейфа",I);
                        emit pDataTestThread->telnet_config_chain(test_config.switch_config_chain);
                    }
                    //если telnet выключен, втыкаем перемычки ручками
                    else{
                        //ожидание подтверждения
                        WAIT_CONFIRM("Установите перемычки шлейфа");
                    }
                    emit syslog("Тестовый шлейф настроен",C);
                    Sleep(3000);
                    pDataTestThread->data_start();
                    Sleep(1000);
                    //устанавливаем таймер.
                    qDebug() << "datatest  timer start";
                    timer_cnt = 0;
                    while(pDataTestThread->is_finished()==0){
                        Sleep(1000);
                        qDebug() << timer_cnt;
                        if(timer_cnt > 60){
                            qDebug() << "datatest  timeout";
                            break;
                        }
                        timer_cnt++;
                    }
                    qDebug() << "datatest  is_finished";
                    emit syslog("Тест передачей данных окончен, получение результатов",S);
                    Sleep(100);
                    //set rezult
                    pDataTestThread->stop();
                    Sleep(100);

                    //отключение шлейфа
                    if(prog_sett.switch_state){
                        emit pDataTestThread->telnet_config_chain(test_config.switch_config_normal);
                        emit syslog("Отключение режима шлейфа",I);
                        emit pDataTestThread->telnet_config_sw(test_config.data_test_ports,prog_sett.port_dut);
                    }
                    //если telnet выключен, убираем перемычки ручками
                    else{
                        //ожидание подтверждения
                        WAIT_CONFIRM("Уберите перемычки шлейфа");
                    }

                    emit syslog("Тестовый шлейф отключен",C);
                    int errorDatatest;
                    if(pDataTestThread->get_ports_status(0) == TEST_OK){
                        emit set_stage_result(3,GREEN);
                        for(i=0;i<PORT_NUM;i++){
                            if(test_config.data_test_ports[i] == 1){
                                emit set_dataport_result(i,GREEN);
                                tmp.sprintf("Передача данных порт %d",i);
                                tmp.sprintf("Передача данных порт %d: успешно",i);
                                emit send_report_d(tmp,OK,OK);
                                emit syslog(tmp,I);
                            }
                        }
                    }
                    else{
                        for(i=0;i<PORT_NUM;i++){
                            if(test_config.data_test_ports[i] == 1){
                                emit set_dataport_result(i,RED);
                                emit set_stage_result(3,RED);
                                tmp.sprintf("Передача данных порт %d: ошибка",i);
                                errorDatatest = i;
                                emit send_report_d(tmp,FAIL,FAIL);
                                emit syslog(tmp,E);
                            }
                        }
                        StopTest(ERROR_DATATEST1 + errorDatatest);
                        continue;
                    }
                }
            }

            IF_STOP

/*****************************************************************/



/*********************Тестирование UPS***************************/

                    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld)
            {
                if(test_struct.ups)
                {
                    //отключение нагрузки PoE (если плата RPS-01)

                    //disablePoeLoad();

                    if(test_config.ups_rload){
                        emit stand_akb_rload_on();
                        Sleep(700);
                    }

                    try
                    {
                        UpsTestPS2();
                        emit syslog("UPS проверен",S);
                        emit set_stage_result(4,GREEN);
                    }
                    catch (const char* ex)
                    {
                        QString message = QString::fromUtf8(ex);
                        emit syslog(message, E);
                        emit set_stage_result(4, RED);

                        StopTest(ERROR_UPS);
                        continue;
                    }
                }
                IF_STOP
            }

/***************************Загрузка файлов справки***********************************/

            if(test_config.load_help){
                emit upload(test_config.firmware_path);
                emit syslog("Загрузка файлов справки...",I);
                emit get_help_html();
                parse_cnt = 0;
                while(!nettest.help_loaded && parse_cnt<MAX_CNT){
                    parse_cnt++;
                    Sleep(100);
                }

                if(nettest.help_loaded != true){
                    emit syslog("Файлы справки не были загружены",E);
                    emit set_stage_result(1,RED);
                    //errorcode = ERROR_LOADHELP;
                    //goto stop;

                    StopTest(ERROR_LOADHELP);
                    continue;
                }
                emit syslog("Файлы справки загружены",I);
            }
            IF_STOP

/*****************************************************************************************/

            Pause(100);

            //установка MAC адреса
                        if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){
                            if(test_struct.setMac)
                            {
                                //emit syslog("Ждем, пока устройство не станет доступным...", I);
                                int timeout = test_config.start_delay * SEC;
                                waitingStartDevice(timeout);

                                emit syslog("Установка MAC адреса...",I);

                                if(!test_config.model_name.contains("Pro"))
                                {
                                tmp.clear();
                                tmp.append(psw_selftest_result.default_mac.at(9));
                                tmp.append(psw_selftest_result.default_mac.at(10));
                                tmp.append(psw_selftest_result.default_mac.at(12));
                                tmp.append(psw_selftest_result.default_mac.at(13));
                                tmp.append(psw_selftest_result.default_mac.at(15));
                                tmp.append(psw_selftest_result.default_mac.at(16));

                                if(prog_sett.test_type == TYPE_PRODUCTION || prog_sett.test_type == TYPE_TELEPORT){
                                    if(nettest.get_serial == false){
                                        emit syslog("Ошибка:  не удалось получить ответ от сервера",E);
                                        emit set_stage_result(5,RED);
                                        emit send_report_d("Установка MAC адреса", FAIL, FAIL);
                                        StopTest(ERROR_MAC);
                                        continue;
                                    }
                                }
                                else if(prog_sett.test_type == TYPE_REPAIR){
                                    nettest.serial_num = test_config.serial_num;
                                }
                                IF_STOP

                                        uint32_t model_num = 0;
                                if (test_config.model_num_mac_print > 0)
                                {
                                    model_num = test_config.model_num_mac_print;
                                }
                                else
                                {
                                    model_num = test_config.model_num;
                                }

                                device_info.mac[0] = 0xC0;
                                device_info.mac[1] = 0x11;
                                device_info.mac[2] = 0xA6;
                                device_info.mac[3] = (quint8)model_num;
                                if(prog_sett.test_type == TYPE_PRODUCTION ||prog_sett.test_type == TYPE_TELEPORT){
                                    device_info.mac[4] = (quint8)((nettest.serial_num-100000 * model_num) >> 8);
                                    device_info.mac[5] = (quint8)(nettest.serial_num-100000 * model_num);
                                }
                                else{
                                    device_info.mac[4] = (quint8)(nettest.serial_num >> 8);
                                    device_info.mac[5] = (quint8)(nettest.serial_num);
                                }
                                qDebug() <<  "set serial num = " << nettest.serial_num;


                                send_set_mac(socket, &device_info);
                                Sleep(7000); // Пауза чтобы мак успел примениться

                                emit syslog(str.sprintf("Установили MAC %X:%X:%X:%X:%X:%X",device_info.mac[0],device_info.mac[1],device_info.mac[2],
                                        device_info.mac[3],device_info.mac[4],device_info.mac[5]),I);

                                }
                                else
                                {
                                //для Pro линейки

                                 QString mac_address =str.sprintf("%X:%X:%X:%X:%X:%X",device_info.mac[0],device_info.mac[1],device_info.mac[2],
                                         device_info.mac[3],device_info.mac[4],device_info.mac[5]);

                                 if (setProMacAddress(mac_address, test_config.model_name)) {
                                     qDebug() << "MAC-адрес установлен";
                                 } else {
                                     qDebug() << "Ошибка установки MAC";
                                 }
                                }
                                //
                                IF_STOP

                                //снимаем питание
                                //emit syslog("Отключение Питания",I);
                                StandOff();
                                if(prog_sett.stand_type == StandType::typeAPK03)
                                    Pause(10000); // Важно! Не уменьшать! Сгорит реле
                                else
                                    Pause(5000);

                                //подаём питание
                                if(test_config.use_ac1){
                                    set_stand_ac1_state(1);
                                }
                                Pause(100);

                                if(test_config.use_ac2){
                                    set_stand_ac2_state(1);
                                }
                                Pause(100);

                                if(test_config.buildin_test == 1)
                                    clear_arp_case();


                                //wait start
                                emit syslog("Ожидаем включение устройства", I);
                                if((test_config.use_ac1)||(test_config.use_ac2)){
                                    int timeout = test_config.start_delay*SEC;
                                    waitingStartDevice(timeout);
                                    Pause(2000);
                                }

                                IF_STOP

/******************************Проверка установки MАС-адреса****************************/

                                try
                                {
                                    CheckMac();
                                    emit syslog(QString("MAC адрес установлен %1").arg(psw_selftest_result.default_mac),I);
                                    emit send_report_d("установка MAC адреса", OK, OK);
                                    emit set_stage_result(5, GREEN);
                                }
                                catch (const char* ex)
                                {
                                    QString message = QString::fromUtf8(ex);
                                    emit syslog(message, E);
                                    emit set_stage_result(5, RED);
                                    emit send_report_d("установка MAC адреса", FAIL, FAIL);
                                    StopTest(ERROR_MAC);
                                    continue;
                                }
                            }
                            IF_STOP
                        }

/*****************************************************************************************/


/****************************Печать этикеток*********************************************/

                        if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){
                            if(test_struct.printLabel)
                            {
                                PrintLabelStage();
                            }

/*****************************************************************************************/

                            if(test_struct.setMac){
                                //получение статистики

                                emit syslog("Запрашиваем тестовую страницу", I);
                                bool resultRequestTestPage = requestTestPage();                  // Запрашиваем тестовую страницу
                                if (!resultRequestTestPage)                                      // Если неудалось получить или распарсить тестовую страницу, заканчиваем тест
                                {
                                    emit syslog(QString("Тестовая страница не была загружена"), E);
                                    StopTest(ERROR_GET_TEST_PAGE);
                                    continue;
                                }
                                emit syslog(QString("Тестовая страница была загружена"), I);

                                emit syslog("Парсим тестовую страницу", I);
                                bool parseTestPageResult = parse_html_file(testPageData);         // Парсим тестовую страницу
                                if (!parseTestPageResult)                                         // Если неудалось распарсить страницу, заканчиваем тест
                                {
                                    emit syslog(QString("Тестовая страница не была распарсена"), E);
                                    StopTest(ERROR_GET_TEST_PAGE);
                                    continue;
                                }
                                emit syslog(QString("Тестовая страница была распарсена"), I);

                            }
                        }

                        StopTest();
        }
    }

    Pause(1000);

    emit finished();
}

/*****************************************************************************************/

void TestThread::SelfTestStage()
{

    emit syslog("Старт самотестирования", S);// Проводим самотестирование

    testPageWasParsed = false;



    LoadTestPageIfNeeded();
    int selftest_result = get_selftest_result();

    QElapsedTimer timer;
    timer.start();
    while(selftest_result == SelfTestStatus::IncorrectDevType && timer.elapsed() < test_config.start_delay*1000)
    {
        emit syslog("Перезапрашиваем страницу", I);
        testPageWasParsed = false;
        QThread::msleep(3000);
        qApp->processEvents();
        LoadTestPageIfNeeded();
        selftest_result = get_selftest_result();
    }

    if(test_config.firmware_load)
    UpdatePSW();

    if(test_config.hw_check)
    {
        if(psw_selftest_result.hw_vers == test_config.hw_vers)
        {
            qDebug()<<"Версии устройства совпадают" << psw_selftest_result.hw_vers;
        }else
        {
            qDebug()<<"Версии устройства не совпадают: " << psw_selftest_result.hw_vers << " | " << test_config.hw_vers;

            emit syslog(QString("Ошибка аппаратной версии, получено значение %1, ожидалось %2").arg(psw_selftest_result.hw_vers).arg(test_config.hw_vers), E);

            emit syslog("Самотестирование не пройдено", E);
            emit set_stage_result(1, RED);
            throw "Самотестирование не пройдено";
        }
    }

    if(selftest_result == SelfTestStatus::Ok)
    {
        emit syslog("Самотестирование пройдено",S);
        emit set_stage_result(1, GREEN);
    }else
    {
        emit syslog("Самотестирование не пройдено", E);
        emit set_stage_result(1, RED);
        throw "Самотестирование не пройдено";
    }
}

void TestThread::HeaterTestStage()
{
    QString str;
    bool ok = false;
    emit syslog("Тест тока нагревателей", S);

    if ((dev->get_mb_devtype(PS2_ADDR) == DEV_PS2 || dev->get_mb_devtype(PS2_ADDR) == DEV_PS3)) //if ps2 || ps3
    {
        emit syslog("Проверка тока первого нагревательного элемента", I);
        int cnt =5;
        if(check_stand_minmax_param(PS2_ADDR,MB_PS2_HEATER_CURRENT,50,0, 3))
        {
            ok = true;//реле исправно

        }else
        {
            emit syslog(str.sprintf("Реле нагревателя 1 в стенде неисправно, ток=%d",dev->get_mb_ps3_heater1_current()),E);
            ok = false;
        }

        cnt =5;
        emit set_stand_heater1_relay(0);
        while(check_stand_param(PS2_ADDR,MB_PS2_HEATER_RELAY_STATE,0,3) == false && cnt){
            emit set_stand_heater1_relay(0);
            cnt--;
        }
        emit set_stand_heater1_relay(1);
        while(check_stand_param(PS2_ADDR,MB_PS2_HEATER_RELAY_STATE,1,3) == false && cnt){
            emit set_stand_heater1_relay(1);

            cnt--;
        }
    }
    if (test_config.test_heating && (dev->get_mb_devtype(PS2_ADDR) == DEV_PS2 || dev->get_mb_devtype(PS2_ADDR) == DEV_PS3)){ //if ps2 || ps3
        qDebug()<<"Ток первого нагревательного элемента(1)"<<dev->get_mb_ps3_heater1_current();
        if(check_stand_minmax_param(PS2_ADDR,MB_PS2_HEATER_CURRENT,test_config.heating_curr_max,test_config.heating_curr_min, 3))
        {
            emit send_report_d("Ток первого нагревательного элемента", OK, dev->get_mb_ps3_heater1_current());
            emit syslog(str.sprintf("Ток первого нагревательного элемента: %f A", static_cast<double>(dev->get_mb_ps3_heater1_current()) / 1000), I);

            ok = true;
            emit syslog("Проверка тока первого нагревательного элемента: Успешно", I);

        }else
        {
            qDebug()<<"Ток первого нагревательного элемента(2)"<<dev->get_mb_ps3_heater1_current();
            emit syslog(str.sprintf("Неисправность первого нагревательного элемента, ток=%d",dev->get_mb_ps3_heater1_current()),E);
            emit send_report_d("Ток первого нагревательного элемента", FAIL, dev->get_mb_ps3_heater1_current());
            emit set_stand_heater1_relay(0);
            ok = false;
            emit syslog("Проверка первого нагревателя не пройдена", E);
        }

        int cnt =5;
        emit set_stand_heater1_relay(0);
        while(check_stand_param(PS2_ADDR,MB_PS2_HEATER_RELAY_STATE,0,3) == false && cnt){
            emit set_stand_heater1_relay(0);
            cnt--;
        }
    }

    if (test_config.test_heating2 && (dev->get_mb_devtype(PS2_ADDR) == DEV_PS2 || dev->get_mb_devtype(PS2_ADDR) == DEV_PS3)) //if ps2 || ps3
    {
        emit syslog("Проверка тока второго нагревательного элемента", I);

        int cnt =5;
        if(check_stand_minmax_param(PS2_ADDR,MB_PS3_HEATER_RELAY2_CURRENT,50,0, 3))
        {
            ok = true;//реле исправно

        }else
        {
            emit syslog(str.sprintf("Реле нагревателя 2 в стенде неисправно, ток=%d",dev->get_mb_ps3_heater1_current()),E);
            ok = false;
        }
        cnt = 5;
        emit set_stand_heater2_relay(1);
        while(check_stand_param(PS2_ADDR,MB_PS3_HEATER_RELAY2_STATE,1,3) == false && cnt){
            emit set_stand_heater2_relay(1);
            cnt--;
        }
    }
    if (test_config.test_heating2 && (dev->get_mb_devtype(PS2_ADDR) == DEV_PS2 || dev->get_mb_devtype(PS2_ADDR) == DEV_PS3)){ //if ps2 || ps3

        if(check_stand_minmax_param(PS2_ADDR,MB_PS3_HEATER_RELAY2_CURRENT,test_config.heating2_curr_max,test_config.heating2_curr_min, 3))
        {
            if ((dev->get_mb_ps3_heater2_current() < test_config.heating2_curr_min)
                    || (dev->get_mb_ps3_heater2_current() > test_config.heating2_curr_max))
            {

            }

            emit send_report_d("Ток второго нагревательного элемента", OK, dev->get_mb_ps3_heater2_current());
            emit syslog(str.sprintf("Ток второго нагревательного элемента: %f A", static_cast<double>(dev->get_mb_ps3_heater2_current()) / 1000), I);
            ok *= true;
            emit syslog("Проверка тока второго нагревательного элемента: Успешно", I);

        }else
        {
            emit syslog(str.sprintf("Неисправность второго нагревательного элемента, ток=%d",dev->get_mb_ps3_heater2_current()),E);
            emit send_report_d("Ток второго нагревательного элемента", FAIL, dev->get_mb_ps3_heater2_current());
            emit set_stand_heater2_relay(0);
            ok *= false;
            emit syslog("Проверка второго нагревателя не пройдена", E);
        }
        int cnt =5;
        emit set_stand_heater2_relay(0);
        while(check_stand_param(PS2_ADDR,MB_PS3_HEATER_RELAY2_STATE,0,3) == false && cnt){
            emit set_stand_heater2_relay(0);
            cnt--;
        }
    }
    if(ok)
    {
        emit syslog("Ток нагревателей проверен",S);
        emit set_stage_result(10, GREEN);
        emit send_report_d("Ток нагревателей", OK, OK);
    }else
    {
         emit set_stage_result(10, RED);
        throw "Проверка тока нагревателей не пройдена";
    }
}

int TestThread::PoeTest()
{
    emit syslog("Тест PoE",S);

    // Проверяем напряжения на линиях
    for(int i = 0; i < NUM_POE_LINES; i++)
    {
        // Если тест линии не требуется, то переходим к следующей линии
        if (test_config.poe_line_test[i] == 0)
            continue;

        // Тестируем линию
        bool testPoeResult = PoeLineTest(i);
        // Если тест PoE линии не пройден
        if (!testPoeResult)
        {
            return i;
        }

        Pause(200);
    }

    emit syslog("Тест PoE завершен ", S);
    return POE_TEST_SUCCESS;
}

bool TestThread::PoeLineTest(int indexLine)
{
    double lineVoltage = 0;
    //double lineCurrent = 0;
    int addr;
    QString message;
    qDebug()<<"PoeLineTest" << indexLine;
    if(dev->is_connected(indexLine) && dev->get_mb_devtype(indexLine) == DEV_EL60){
        if(check_stand_minmax_param(indexLine,MB_EL60_VOLTAGE,
                                    test_config.poe_line_max[indexLine]*1000,
                                    test_config.poe_line_min[indexLine]*1000,
                                    3)){
            lineVoltage = dev->get_mb_el60_voltage(indexLine);
            //lineCurrent = dev->get_mb_el60v5_current_a(indexLine);
            emit set_poeport_result(indexLine, GREEN);
            QString message = QString("Тест PoE на линии %1: пройдено, %2V").arg(indexLine).arg(static_cast<double>(lineVoltage) /1000);
            emit syslog(message,I);
            emit send_report_f(message, OK, lineVoltage);
            return true;
        }
        else{
            lineVoltage = dev->get_mb_el60_voltage(indexLine);
            //lineCurrent = dev->get_mb_el60v5_current_a(indexLine);

            message = QString("Тест PoE на линии %1: не пройдено, %2V").arg(indexLine).arg(static_cast<double>(lineVoltage) /1000);
            emit syslog(message, E);
            message = QString("напряжение PoE на линии %1").arg(indexLine);
            emit send_report_f(message, FAIL, lineVoltage);
            return false;
        }
    }
    else if(dev->get_mb_devtype(indexLine-indexLine%2) == DEV_EL60V5){
        //PoE B - нечётные
        if(indexLine%2)
            addr = MB_EL60V5_VOLTAGE_A;
        else
            addr = MB_EL60V5_VOLTAGE_B;

        if(check_stand_minmax_param(indexLine-indexLine%2,addr,
                                    test_config.poe_line_max[indexLine]*1000,
                                    test_config.poe_line_min[indexLine]*1000,
                                    3)){
            emit set_poeport_result(indexLine, GREEN);
            if(indexLine%2){
                lineVoltage =poeVoltage;
                //lineCurrent = poeCurrent;

                message = QString("Тест PoE A на линии %1: пройдено, %2V").arg(indexLine).arg(static_cast<double>(lineVoltage) /1000)/*.arg(static_cast<double>(lineCurrent) / 1000)*/;
            }
            else{
                lineVoltage = poeVoltage;
                //lineCurrent = poeCurrent;

                message = QString("Тест PoE B на линии %1: пройдено, %2V").arg(indexLine).arg(static_cast<double>(lineVoltage)/1000)/*.arg(static_cast<double>(lineCurrent) / 1000)*/;
            }
            emit syslog(message,I);
            emit send_report_f(message, OK, lineVoltage);
            return true;
        }
        else{
            if(indexLine%2){
                lineVoltage = dev->get_mb_el60v5_voltage_a(indexLine-indexLine%2);
                //lineCurrent = dev->get_mb_el60v5_current_a(indexLine-indexLine%2);

                message = QString("Тест PoE A на линии %1: не пройдено, %2V").arg(indexLine).arg(static_cast<double>(lineVoltage) / 1000)/*.arg(static_cast<double>(lineCurrent) / 1000)*/;
            }
            else{
                lineVoltage = dev->get_mb_el60v5_voltage_b(indexLine-indexLine%2);
                //lineCurrent = dev->get_mb_el60v5_current_b(indexLine-indexLine%2);

                message = QString("Тест PoE B на линии %1: не пройдено, %2V").arg(indexLine).arg(static_cast<double>(lineVoltage) / 1000)/*.arg(static_cast<double>(lineCurrent) / 1000)*/;
            }
            emit syslog(message, E);
            emit set_poeport_result(indexLine, RED);
            emit send_report_f(message, FAIL, lineVoltage);
            return false;
        }
    }
    return 0;
}

void TestThread::InOutTestStage()
{
    //bool in_out_test_result = true;
    if(test_config.dry_cont_test[0] || test_config.dry_cont_test[1])
        disable_io02_outputs();

    //Для UPS+ включаем выходное реле в тестовый режим
    if(prog_sett.stand_type == StandType::typeAPK03)
    {
        if(test_config.tlp_outputs[0] && test_config.test_ups){
            if(!check_io02_input(0))
                throw "Проверка релейного выхода не пройдена";
        }
    }
    if(test_config.dry_cont_test[0])
    {
        if(!check_sensor1())
            throw "Проверка Sensor1 не пройдена";
    }
    if(test_config.dry_cont_test[1])
    {
        if(!check_sensor2())
            throw "Проверка Sensor2 не пройдена";
    }
        emit syslog("Проверка In/Out на плате IO-02 прошла успешно", S);
}

void TestThread::PrintLabelStage()
{
    label_info.num = test_config.label_num;

    if(test_config.printable_name.isEmpty())
        label_info.dev_name = test_config.model_name;
    else
        label_info.dev_name = test_config.printable_name;

    if (test_config.model_num_mac_print > 0)
    {
        label_info.dev_type = test_config.model_num_mac_print;
    }
    else
    {
        label_info.dev_type = test_config.model_num;
    }

    label_info.serial_num = (nettest.serial_num-100000 * label_info.dev_type);

    for(int i=0;i<6;i++)
        label_info.mac[i] = device_info.mac[i];
    label_info.print_status = false;
    label_info.retry = 0;
    if(test_config.equipment_field_use == 1){
        label_info.equipment_field_use = test_config.equipment_field_use;
        label_info.equipment_type = test_config.equipment_type;
        label_info.equipment_str = test_config.equipment_str;

        emit send_report_s("Комплектация",1,test_config.equipment_str);
        emit send_report_d("Комплектация",1,test_config.equipment_type);
    }
    else
        label_info.equipment_field_use = 0;
    emit print_label(&label_info);
    int parse_cnt = 0;
    while((label_info.print_status==false)&&(parse_cnt < MAX_CNT )){
        Sleep(10);
        parse_cnt++;
    }
    if(label_info.print_status  == false){
        emit syslog("Ошибка принтера",E);
        emit set_stage_result(6,RED);
    }else{
        emit syslog("Этикетка напечатана",I);
        emit set_stage_result(6,GREEN);
    }
}

void TestThread::UpdatePSW()
{
    //обновление ПО
    int parse_cnt = 0;
    if(psw_selftest_result.firmvare_vers < test_config.firmware_vers){
        emit syslog("Обновление ПО",I);

        emit update_clear();//очистка флешки
        Sleep(10000);
        emit upload(test_config.firmware_path);
        emit syslog("Загрузка ПО...",I);
        Sleep(5000);
        qDebug() << "wait";
        parse_cnt = 0;
        //ждем загрузки
        while((uploading) && (parse_cnt<80)){
            parse_cnt++;
            Sleep(1000);
            qDebug() << "wait";
        }
        //подтверждаем
        if(uploading == 0){
            Sleep(10*SEC);
            emit confirm();
            emit syslog("ПО загружено, начинается обновление...",I);
        }else{
            emit syslog("Ошибка загрузки ПО",I);
            emit set_stage_result(2,RED);

            StopTest(ERROR_DOWNLOAD_UPDATE);
            return;
        }
        Sleep(40*SEC);
        waitingStartDevice(test_config.start_delay * SEC);

        // GET_TEST_HTML

        emit syslog("Запрашиваем тестовую страницу", I);
        bool resultRequestTestPage = requestTestPage();                  // Запрашиваем тестовую страницу
        if (!resultRequestTestPage)                                      // Если неудалось получить или распарсить тестовую страницу, заканчиваем тест
        {
            emit syslog(QString("Тестовая страница не была загружена"), E);
            nettest.parsed = false;
            emit send_report_d("Загрузка тестовой страницы", FAIL, FAIL);

            StopTest(ERROR_GET_TEST_PAGE);
            return;
        }
        emit syslog(QString("Тестовая страница была загружена"), I);

        emit syslog("Парсим тестовую страницу", I);
        bool parseTestPageResult = parse_html_file(testPageData);         // Парсим тестовую страницу
        if (!parseTestPageResult)                                         // Если неудалось распарсить страницу, заканчиваем тест
        {
            emit syslog(QString("Тестовая страница не была распарсена"), E);
            nettest.parsed = false;
            emit send_report_d("Загрузка тестовой страницы", FAIL, FAIL);

            StopTest(ERROR_PARSE_TEST_PAGE);
            return;
        }
        emit syslog(QString("Тестовая страница была распарсена"), I);

        if(nettest.parsed){
            if(psw_selftest_result.firmvare_vers >= test_config.firmware_vers){
                emit syslog("ПО обновлено успешно",I);
                emit set_stage_result(2,GREEN);
                return;
            }else{
                emit syslog("ПО не было обновлено: не совпадает версия ПО",E);
                emit set_stage_result(2,RED);
                qDebug()<<psw_selftest_result.firmvare_vers;
                qDebug()<<test_config.firmware_vers;
                StopTest(ERROR_BAD_VERSIONS_UPDATING);
                return;
            }
        }
        else{
            emit syslog("Не удалось загрузить: 192.168.0.1/test.shtml",E);
            emit set_stage_result(2,RED);

            StopTest(ERROR_DOWNLOAD_TEST_UPDATING);
            return;
        }
    }
    else{
        emit syslog("Обновление ПО не требуется",I);
        emit set_stage_result(2,GREEN);
        return;
    }
    //ПО обновлено
}

void TestThread::sendReport()
{
    qDebug() << "send_report";
    int parse_cnt = 0;

    emit set_test_result(); // Отправка отчета
    Sleep(100);

    while(!nettest.send_report && parse_cnt < 10 )
    {
        Sleep(1000);
        parse_cnt++;
    }

    if(!nettest.send_report)
    {
        emit syslog("Отчет не передан, повторная отправка",I);
        emit set_test_result();

        parse_cnt = 0;
        Sleep(1000);

        while(!nettest.send_report && parse_cnt < 10 )
        {
            Sleep(1000);
            parse_cnt++;
        }
    }
}

void TestThread::CheckMac()
{
    psw_selftest_result.default_mac = "";

    testPageWasParsed = false;
    LoadTestPageIfNeeded(); // Загружаем тестовую страницу, чтобы считать mac

    emit syslog(QString("Проверяем MAC-адрес"), I);

    int serial = 0;
    bool ok = false;

    QString mac = "";
    mac.append(psw_selftest_result.default_mac.at(12));
    mac.append(psw_selftest_result.default_mac.at(13));
    mac.append(psw_selftest_result.default_mac.at(15));
    mac.append(psw_selftest_result.default_mac.at(16));
    qDebug() << mac;

    serial = mac.toInt(&ok,16);

    qDebug() << "serial" << serial << nettest.serial_num;

    QString str = "";
    str.sprintf("serial %d %d",serial,nettest.serial_num);
    emit syslog(str,I);

    int type;
    if (test_config.model_num_mac_print > 0)
    {
        type = test_config.model_num_mac_print;
    }
    else
    {
        type = test_config.model_num;
    }

    if(prog_sett.test_type == TYPE_REPAIR){
        nettest.serial_num += 100000 * type;
    }

    //смотрим сохранился ли MAC
    if((nettest.serial_num - 100000 * type) == (serial&0xFFFF))
    {
        return;
    }
    else
    {
        throw "Ошибка установки MAC адреса: MAC не сохранился";
    }
}

void TestThread::LoadTestPageIfNeeded()
{
    if (!testPageWasParsed)
    {
        emit syslog("Запрашиваем тестовую страницу", I);
        int timeout = test_config.start_delay * SEC;

        bool resultRequestTestPage = requestTestPage(timeout);            // Запрашиваем тестовую страницу

        if (!resultRequestTestPage)                                       // Если неудалось получить страницу, заканчиваем тест
            resultRequestTestPage = requestTestPage(timeout);             // Повторяем попытку

        if (!resultRequestTestPage)                                       // Если неудалось получить страницу, заканчиваем тест
            throw "Тестовая страница не была загружена";

        emit syslog(QString("Тестовая страница была загружена"), I);

        emit syslog("Парсим тестовую страницу", I);
        bool parseTestPageResult = parse_html_file(testPageData);         // Парсим тестовую страницу
        if (!parseTestPageResult)                                         // Если неудалось распарсить страницу, заканчиваем тест
            throw "Тестовая страница не была распарсена";

        emit syslog(QString("Тестовая страница была распарсена"), I);
        testPageWasParsed = true;
    }
}

void TestThread::RS485Test()
{
    emit syslog("Тест RS485", S);

    if(dev->io02_is_connected() && prog_sett.test_type != TYPE_TELEPORT){
        int slot = dev->get_io02_slot();
        int cnt = 3;
        emit syslog("Переход в тестовый режим", I);
        emit set_tlp_test_mode(1);
        Sleep(1000);
        emit syslog("Тестирование RS-485",I);
        emit io02_rs485_test_start();
        Sleep(3000);
        while(check_stand_param(slot,MB_IO02_RS485_TEST_OK,1,2) == false && cnt){
            emit io02_rs485_test_start();
            Sleep(1000);
            cnt--;
        }
        emit syslog("Выход из тестового режима", I);
        emit set_tlp_test_mode(0);
        Sleep(1000);
        if(check_stand_param(slot,MB_IO02_RS485_TEST_OK,1,3)== false){
            throw "Ошибка RS-485";
        }

    }else
    {
        teleport_com_data = "";

        emit set_tlp_test_mode(1); //Вкл тестовый режим

        emit tlp_send_rs485_hello();

        bool resultCompare = teleport_com_data.contains("Hello Stand\r\n", Qt::CaseInsensitive);
        QElapsedTimer timer;
        timer.start();
        while (!resultCompare)
        {
            resultCompare = teleport_com_data.contains("Hello Stand\r\n", Qt::CaseInsensitive);
            if (timer.elapsed() > 5000)
                break;
        }
        emit set_tlp_test_mode(0); // Выкл тестовый режим
        teleport_com_data = "";

        if (!resultCompare)
            throw "Ошибка RS-485";
    }
}

void TestThread::I2CTest(){
    testPageWasParsed = false;
    psw_selftest_result.temperature = 0;
    psw_selftest_result.humidity = 0;
    Sleep(5000);
    LoadTestPageIfNeeded();
    if(psw_selftest_result.temperature < 15 || psw_selftest_result.temperature > 50)
    {
        throw "Ошибка I2C (Температура вне диапазона)";
    }

    if(psw_selftest_result.humidity < 5 || psw_selftest_result.humidity > 90)
    {
        throw "Ошибка I2C (Влажность вне диапазона)";
    }
}

bool TestThread::setProMacAddress(QString& macAddress, QString& model)
{
    QString timestamp = QString::number(QDateTime::currentSecsSinceEpoch());

    QStringList commands;
    commands << QString("fw_setenv ethaddr %1").arg(macAddress);
    commands << QString("fw_setenv boardversion %1").arg(model);
    commands << "./usr/bin/macset.sh";
    commands << QString("ubus call tf_hwsys setParam '{\"name\":\"RTC_TIMESTAMP\",\"value\":\"%1\"}'").arg(timestamp);
    commands << "echo root:VR2MSISdpcb5 | chpasswd";

    QString fullCommand = commands.join(" && sleep 0.1 && ");

    // Создание процесса для выполнения plink
    QProcess process;
    process.setProcessChannelMode(QProcess::MergedChannels); // Объединяем stdout и stderr

#ifdef Q_OS_WIN
    process.setCreateProcessArgumentsModifier([](QProcess::CreateProcessArguments *args) {
        args->flags |= CREATE_NO_WINDOW;
    });
#endif

    // Запуск plink
    QStringList plinkArgs;
    plinkArgs << "-ssh" << "-batch" << "-pw" << "root" << "root@192.168.0.1" << fullCommand;

    process.start("plink", plinkArgs);

    if (!process.waitForStarted()) {
        qDebug() << "Failed to start plink process";
        return false;
    }

    if (!process.waitForFinished(20000)) {
        qDebug() << "plink process timed out";
        process.terminate();
        return false;
    }

    // проверка
    if (process.exitCode() == 0) {
        qDebug() << "MAC address set successfully";
        qDebug() << "Output:" << process.readAll();
        return true;
    } else {
        qDebug() << "Failed to set MAC address. Error:" << process.readAll();
        return false;
    }
}

void TestThread::getSerialNumber()
{
    //если нет CPU ID то ошибка
    if(psw_selftest_result.cpu_id.isEmpty()){
        emit syslog("Нет CPU ID. Получение серийного номера невозможно",E);
        return;
    }

    emit get_serial_num(test_config.model_name, psw_selftest_result.cpu_id);

    // Ожидаем получение серийного номера
    QElapsedTimer timer;
    timer.start();
    while(!nettest.get_serial && timer.elapsed() < 30000 )
    {
        QThread::msleep(100);
        qApp->processEvents();
    }
}

void TestThread::GetSerialIdOrNumber()
{
    if(prog_sett.test_type == TYPE_PRODUCTION || prog_sett.test_type == TYPE_TELEPORT)
    {

        if (test_config.buildin_test == 0 && prog_sett.product_test_type == TYPE_FIRST)      //для неуправляемых устройств проверяемых впервые
        {
            emit syslog("Оборудование проверяется впервые", I);
            emit syslog("Получение ID", I);

            nettest.get_id = false;
            nettest.id = 0;

            // Отправляем сигнал на запрос ID
            emit get_serial_id();

            // Ожидаем получение ID
            QElapsedTimer timer;
            timer.start();
            while(!nettest.get_id && timer.elapsed() < 30000 )
            {
                QThread::msleep(100);
                qApp->processEvents();
            }

            // Проверяем результат получения ID
            if(nettest.get_id)
            {
                psw_selftest_result.cpu_id = QString("%1").arg(nettest.id);

                QString str = QString("Назначен ID: %1").arg(nettest.id);
                emit syslog(str,I);
            }
            else
            {
                throw "ID не получен";
            }

        }
        else if (test_config.buildin_test == 0 && prog_sett.product_test_type == TYPE_SECOND) //для неуправляемых устройств проверяемых повторно
        {
            emit syslog("Оборудование проверяется повторно", I);

            QString str = QString("ID: %1").arg(test_config.serial_num);
            emit syslog(str, I);

            psw_selftest_result.cpu_id = QString("%1").arg(test_config.serial_num);
        }

        emit syslog("Получение серийного номера", I);

        nettest.get_serial = false;
        nettest.serial_num = 0;

        // Запрос серийного номера
        getSerialNumber();

        // Проверяем результат получения серийного номера
        if(!nettest.get_serial)
        {
            emit syslog("Запрос серийного номера: не получен ответ от сервера, повторный запрос", E);
            getSerialNumber();
        }

        if(!nettest.get_serial || nettest.serial_num == 0)
        {
            throw "Запрос серийного номера: не получен ответ от сервера";
        }

        emit syslog(QString("Предварительный серийный номер: %1").arg(nettest.serial_num), I);
    }

}

void TestThread::waitingWebmanagerFinished()
{
    while (!webmanagerFinished)
    {
        QThread::msleep(100);
    }
}

void TestThread::GetIrpStatus()
{
    webmanagerFinished = false;
    emit signal_GetIrpStatus();

    waitingWebmanagerFinished(); // Делаем паузу пока не закончит работу webmanager

    qDebug("ups_det = %d", psw_selftest_result.ups_det);

    /* Переменная psw_selftest_result.ups_det установилась
     * в методе void SetIrpStatus(int status), вызванного из основного потока.
     */
}

void TestThread::GetUpsStatus()
{
    webmanagerFinished = false;
    emit signal_GetUpsStatus();

    waitingWebmanagerFinished(); // Делаем паузу пока не закончит работу webmanager

    qDebug("ups_rez = %d", psw_selftest_result.ups_rez);

    /* Переменная psw_selftest_result.ups_rez установилась
     * в методе void SetUpsStatus(int status), вызванного из основного потока.
     */
}

void TestThread::GetUpsVoltage()
{
    webmanagerFinished = false;
    emit signal_GetUpsVoltage();

    waitingWebmanagerFinished(); // Делаем паузу пока не закончит работу webmanager

    qDebug("akb_voltage = %f", psw_selftest_result.akb_voltage);

    /* Переменная psw_selftest_result.akb_voltage установилась
     * в методе void SetUpsVoltage(double voltage), вызванного из основного потока.
     */

}

void TestThread::StandOff()
{
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){
        if(test_config.use_ac1)
            set_stand_ac1_state(0);
        if(test_config.use_ac2)
            set_stand_ac2_state(0);
        if(dev->get_mb_devtype(PS1_ADDR) == DEV_PS1){
            if(test_config.test_ups){
                emit stand_akb_rload_off();
                set_stand_akb_state(0);
            }
        }
        else
        {
            if(test_config.test_ups){
                emit set_stand_discharge_key(0);
            }
        }
    }
}

void TestThread::Pause(int msec)
{
    QElapsedTimer timer;
    timer.start();
    while (timer.elapsed() < msec)
    {
        QThread::msleep(5);
        qApp->processEvents();
    }
}

void TestThread::ManualStopTest()
{
    emit syslog("Ручная остановка теста",S);
    StandOff(); // Отключаем все питание
    emit test_finished(ERROR_STOP, nettest.serial_num);
    Sleep(100);
    emit syslog("Тест завершен", I);
    start_test_flag = false;
    stop_test = true;
}

void TestThread::StopTest(int errorCode)
{
    emit syslog("Остановка теста",S);
    StandOff(); // Отключаем все питание

    //формирование отчета о тестировании и занесение в БД SQL
    nettest.send_report = false;

    if(errorCode != 0 )
    {

        if(test_config.print_label)
        {
            if(test_config.serial_num == 0){
                GetSerialIdOrNumber();
                label_info.serial_num = nettest.serial_num;
                emit syslog(QString("Серийный номер: %1").arg(label_info.serial_num), E);

            }
            else{
                label_info.serial_num = test_config.serial_num;
                emit syslog(QString("Серийный номер: %1").arg(label_info.serial_num), E);

            }

            if(test_config.printable_name.isEmpty())
                label_info.dev_name = test_config.model_name;
            else
                label_info.dev_name = test_config.printable_name;
            label_info.dev_type = test_config.model_num;

            make_report(0, prog_sett.test_type,label_info.serial_num); //неуспешный тест
        }
        emit syslog("Неуспешный тест", E);
        emit syslog(QString("Код ошибки: %1").arg(errorCode), E);
    }
    else
    {
        make_report(1, prog_sett.test_type,nettest.serial_num); //успешный тест
        emit syslog("Успешный тест", S);

        emit ClearSerial();  //очищаем поле ввода Id
    }

    qDebug() << QString("Статус получения s/n: %1").arg(nettest.get_serial);

    //если серийный номер был получен, то отправляем отчет
    if ((nettest.get_serial && prog_sett.product_test_type == TYPE_FIRST && (test_config.send_mac || test_config.print_label))
            || (prog_sett.product_test_type == TYPE_SECOND && test_config.serial_num != 0))
    {
        //формирование файла отчёта и отправка его на сервер
        if (sendReportStatus)
        {
            nettest.send_report = false;

            sendReport(); //Отправляем отчет

            if(nettest.send_report)
            {
                emit syslog("Отчет передан на сервер", S);
                emit set_stage_result(7,GREEN);
            }
            else
            {
                emit syslog("Ошибка передачи отчета на сервер", E);
                emit set_stage_result(7,RED);
                errorCode = ERROR_REPORT;
            }
        }
        else
        {
            emit syslog("Отправка отчета на сервер не требуется", I);
        }
    }
    else
    {
        nettest.serial_num = 0;
        emit syslog("Отчет не передан. Отсутствует S/N", I);
    }

    //комментарий после тестирования
    if(test_config.post_comment.length() > 0 && errorCode == 0)
        emit syslog(test_config.post_comment,C);

    emit test_finished(errorCode, nettest.serial_num);
    start_test_flag = false;
    stop_test = false;

    qDebug() << "Тест завершен";
}

bool TestThread::TestIsRunning()
{
    return start_test_flag;
}

void TestThread::disablePoeLoad()
{
    for(int i = 0; i < PORT_NUM; i++)
    {
        if(test_config.poe_line_test[i] && test_config.poe_line_testCanOff[i])
        {
            emit mb_set_el60_out(i, 0);
            Pause(1000);
        }
    }
}

//размыкание выходов OUT1 и OUT2 платы IO-02

void TestThread::disable_io02_outputs()
{
    emit syslog("Размыкание выходов IO-02",I);
    int cnt = 5;
    if(test_config.dry_cont_test[1])
    {
        emit syslog("Размыкаем OUT1 на плате IO-02",I);

        while(check_stand_param(dev->get_io02_slot(),MB_IO02_OUT1,0,2) == false && cnt){
            // dev->set_mb_io02_out(0,0);
            //emit set_mb_io02_out1();
            emit stand_dc1_off();

            cnt--;
        }
    }
    if(test_config.dry_cont_test[1])
    {
        emit syslog("Размыкаем OUT2 на плате IO-02",I);

        cnt = 5;
        while(check_stand_param(dev->get_io02_slot(),MB_IO02_OUT2,0,2) == false && cnt){
            //emit set_mb_io02_out2();
            //dev->set_mb_io02_out(1,0);
            emit stand_dc2_off();

            cnt--;
        }
    }
    if(dev->get_mb_io02_output(0)==0 && dev->get_mb_io02_output(1)==0)
    {
        emit syslog("Выходы IO-02 разомкнуты",I);
        //debug_window->debug_set_io02_out1_state(0);
        //debug_window->debug_set_io02_out2_state(0);

    }else
    {
        emit syslog("Выходы IO-02 не были разомкнуты",I);
    }
}

//Проверка релейного выхода. Проверяем вход IO-02 и замыкаем реле.

bool TestThread::check_io02_input(int input)
{
    emit syslog("Проверка релейного выхода",I);
    int cnt = 5;
    if(dev->get_mb_io02_input(input) == 0)
    {
        emit syslog("Переводим плату RPS-01 в тестовый режим.",I);

        while(check_stand_param(dev->get_io02_slot(),MB_IO02_IN1 + input,1,3) == false && cnt){
            emit set_ups_plus_out(1);

            cnt--;
        }

        if(dev->get_mb_io02_input(input) == 0)
        {
            emit syslog(QString("IN%1 разомкнут").arg(input + 1),I);
        }else
        {
            emit syslog(QString("IN%1 замкнут").arg(input + 1),I);
        }

    }else{
        emit syslog(QString("Размыкаем IN%1 на плате IO02").arg(input + 1),I);
        cnt = 5;
        while(check_stand_param(dev->get_io02_slot(),MB_IO02_IN1 + input,0,5) == false && cnt){
            dev->set_mb_io02_in(input, 0);
            cnt--;
        }
        if(dev->get_mb_io02_input(input) == 0)
        {
            emit syslog(QString("IN%1 разомкнут").arg(input + 1),I);
        }else
        {
            emit syslog(QString("IN%1 замкнут").arg(input + 1),I);
        }
         emit syslog("Переводим плату RPS-01 в тестовый режим.",I);
        cnt = 5;
        while(check_stand_param(dev->get_io02_slot(),MB_IO02_IN1 + input,1,5) == false && cnt){
            emit set_ups_plus_out(1);

            cnt--;
        }
    }

    if(dev->get_mb_io02_input(input) == 1)
    {
        emit syslog(QString("Проверка релейного выхода пройдена. IN%1 замкнут.").arg(input + 1),I);

        emit syslog(QString("Вывод платы RPS-01 из тестового режима").arg(input + 1),I);
        cnt = 5;
        while(check_stand_param(dev->get_io02_slot(),MB_IO02_IN1 + input,1,5) == false && cnt){
            emit set_ups_plus_out(0);

            cnt--;
        }
        if(dev->get_mb_io02_input(input) == 0)
        {
            emit syslog(QString("Тестовый режим выключен").arg(input + 1),I);
        }

        return 1;
    }else{
        emit syslog(QString("Проверка релейного выхода не пройдена. IN%1 разомкнут.").arg(input + 1),I);

        return 0;
    }


}

//Проверка Sensor1

bool TestThread::check_sensor1()
{
    emit syslog("Производится проверка Sensor 1",I);
    testPageWasParsed = false;
    LoadTestPageIfNeeded();
    if(/*!dev->get_mb_io02_output(0)*/!psw_selftest_result.sensor_1)
    {
        emit syslog("Sensor1 разомкнут",I);
        //debug_set_sensor1_state(0);

    }else
    {
        emit syslog("Sensor1 замкнут",I);
        //debug_set_sensor1_state(1);

    }

    int cnt = 5;
    emit syslog("Замыкаем Sensor1",I);

    while(check_stand_param(dev->get_io02_slot(),MB_IO02_OUT1,1,2) == false && cnt){
        //emit set_mb_io02_out1();
        emit stand_dc1_on();
        cnt--;
    }

    testPageWasParsed = false;

    LoadTestPageIfNeeded();

    Sleep(1000);
    if(psw_selftest_result.sensor_1 == 1)
    {
        emit syslog("Sensor1 замкнут",I);
        //debug_set_sensor1_state(1);
        //emit set_mb_io02_out1();
        emit stand_dc1_off();

        emit syslog("Проверка Sensor1 пройдена.",I);
        return 1;
    }
    else
    {
        emit syslog("Sensor1 разомкнут",I);
        //debug_set_sensor1_state(0);
        emit syslog("Проверка Sensor1 не пройдена.",I);
        return 0;
    }
}
//
//Проверка Sensor2

bool TestThread::check_sensor2()
{
    emit syslog("Производится проверка Sensor 2",I);
    testPageWasParsed = false;

    LoadTestPageIfNeeded();
    if(/*!dev->get_mb_io02_output(1)*/!psw_selftest_result.sensor_2)
    {
        emit syslog("Sensor2 разомкнут",I);
        //debug_set_sensor2_state(0);
    }else
    {
        emit syslog("Sensor2 замкнут",I);
        //debug_set_sensor2_state(1);

    }

    int cnt = 5;
    emit syslog("Замыкаем Sensor2",I);

    while(check_stand_param(dev->get_io02_slot(),MB_IO02_OUT2,1,2) == false && cnt){
        //emit set_mb_io02_out2();
        emit stand_dc2_on();

        cnt--;
    }

    testPageWasParsed = false;

    LoadTestPageIfNeeded();
    Sleep(1000);

    if(psw_selftest_result.sensor_2 == 1)
    {
        emit syslog("Sensor2 замкнут",I);
        //debug_set_sensor1_state(1);
        //emit set_mb_io02_out1();
        emit stand_dc2_off();

        emit syslog("Проверка Sensor2 пройдена.",I);
        return 1;
    }
    else
    {
        emit syslog("Sensor2 разомкнут",I);
        //debug_set_sensor1_state(0);
        emit syslog("Проверка Sensor2 не пройдена.",I);
        return 0;
    }
}

void TestThread::TurnOnAC()
{
    if(test_config.use_ac1){
        set_stand_ac1_state(1);
    }

    Pause(200);

    if(test_config.use_ac2){
        set_stand_ac2_state(1);
    }

    Pause(200);

}
void TestThread::TurnOffAC()
{
    if(test_config.use_ac1){
        set_stand_ac1_state(0);
        //debug_window->debug_set_ac1_state(0);

    }
    Pause(500);

    if(test_config.use_ac2){
        set_stand_ac2_state(0);
        //debug_window->debug_set_ac2_state(0);

    }
    Pause(200);
}

