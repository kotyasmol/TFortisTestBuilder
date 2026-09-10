#include "mainwindow.h"
#include "functions.h"
#include <QDebug>
#include <QThread>
#include <QProcess>
#include <QNetworkInterface>
#include <QObject>
#include "ui_mainwindow.h"

//проверка параметра, считанного со стенда из переменной mb_addr.
//параметр должен укладываться в диапазон max_value .. min_value
//если параметр в норме, вернуть true
//если ошибка - false

bool TestThread::check_rpsstand_minmax_param(int mb_addr, int max_value,int min_value, int timeout){
    int read_cnt = 0;
    while((dev->modbus_data[mb_addr]>max_value || dev->modbus_data[mb_addr]<min_value)
          && (read_cnt < timeout)){
        Sleep(1000);
        emit rps_read_stand_signal();
        read_cnt++;
    }
    if(read_cnt >= timeout){
        return false;
    }
    if(dev->modbus_data[mb_addr]<=max_value && dev->modbus_data[mb_addr]>=min_value){
        return true;
    }
    else
        return false;
}

//проверка параметра, считанного со стенда из переменной mb_addr.
//если параметр в норме, вернуть true
//если ошибка - false

bool TestThread::check_rpsstand_param(int mb_addr, int param, int timeout){
    int read_cnt = 0;
    while((dev->modbus_data[mb_addr]!=param) && (read_cnt < timeout)){
        Sleep(1000);
        emit rps_read_stand_signal();
        read_cnt++;
        qDebug() << dev->modbus_data[mb_addr];
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

//проверка параметра, считанного из платы RPS-01 из переменной mb_addr.
//параметр должен укладываться в диапазон max_value .. min_value
//если параметр в норме, вернуть true
//если ошибка - false

bool TestThread::check_rps01_minmax_param(int mb_addr, int max_value,int min_value, int timeout){
    int read_cnt = 0;
    while((dev->modbus_data[mb_addr]>max_value || dev->modbus_data[mb_addr]<min_value)
          && (read_cnt < timeout)){
        Sleep(1000);
        emit rps_read_rps_signal();
        read_cnt++;
        qDebug() << dev->modbus_data[mb_addr];
    }
    if(read_cnt >= timeout){
        return false;
    }
    if(dev->modbus_data[mb_addr]<=max_value && dev->modbus_data[mb_addr]>=min_value){
        return true;
    }
    else
        return false;
}

//проверка параметра, считанного из платы RPS-01  из переменной mb_addr.
//если параметр в норме, вернуть true
//если ошибка - false

bool TestThread::check_rps01_param(int mb_addr, int param, int timeout){
    int read_cnt = 0;
    while((dev->modbus_data[mb_addr]!=param) && (read_cnt < timeout)){
        Sleep(1000);
        emit rps_read_rps_signal();
        read_cnt++;
        qDebug() << dev->modbus_data[mb_addr];
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

void TestThread::rps_test_start()
{
    rpsStartFlag = true;
}

void TestThread::rps_test_stop()
{
    rpsStartFlag = true;
}

void TestThread::rps_stand_processing_new()
{
    QString tmp;
    int read_cnt;

    emit rps_read_stand_signal();
    Sleep(100);
    emit set_rps_latr_state(0);

    Sleep(100);
    if(dev->mb_rps_dev_type == DEV_RPS_STAND)
        emit set_rps_380_state(0);
    else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4)
        emit set_rps_380_state(0);
    Sleep(100);
    emit set_rps_rload_state(0);

    Sleep(100);
    emit set_rps_rload_value(100);

    Sleep(100);
    emit set_rps_relay1(0);
    Sleep(100);
    emit set_rps_relay2(0);
    emit syslog("--------------------------------------------------",I);
    emit syslog("Запуск теста RPS-01",I);

    if(test_config_rps.preheating_test){
        emit syslog("Проверка Preheating",C);
        emit rps_stand_painting(RpsStandStage::PreheatingJumper);
        Sleep(100);
        WAIT_OK("Установите джампер «PREHEATING» в положение «YES»");
        Sleep(100);
        WAIT_OK("Установите напряжение на ЛАТР 230В");

        emit syslog("Старт при -30",C);
        emit set_rps_preheating(-30);
        Sleep(100);

        //Подключить ЛАТР
        if(dev->mb_rps_dev_type == DEV_RPS_STAND)
            emit set_rps_latr_state(1);
        else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
            emit set_rps_latr_state(1);//ВКЛ ЛАТР
            Sleep(1000);
            emit set_rps_380_state(1);//ВКЛ AC
        }
        Sleep(1000);
        emit rps_read_stand_signal();
        Sleep(1000);

        //Проконтролировать 230В на входе RPS-01 (IND_RPS-AC_IN)
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_IN_IND]!=1) && (read_cnt < 5)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(dev->modbus_data[MB_RPS_AC_IN_IND]!=1){
            emit set_rps_result(RpsStandPhase::Preheating,RED);
            emit syslog("Ошибка подачи 230В на вход RPS-01",E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        //Проконтролировать 230В на выходе RPS-01 (IND_RPS-AC_OUT),
        //состояние равное 1 должно появиться в течении 8-15 сек., если появится раньше 6 сек. – ошибка.
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < test_config_rps.rkn_startup_time_max)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(read_cnt >= test_config_rps.rkn_startup_time_max || read_cnt < test_config_rps.rkn_startup_time_min){
            emit set_rps_result(RpsStandPhase::Preheating,RED);
            emit syslog(tmp.sprintf("Время старта узла RKN не в допуске: %d",read_cnt),E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Время старта узла RKN в допуске: %d",read_cnt),I);

        Sleep(5000);

        //Проверка PREHEATING:
        //-35 - не должен отключиться
        emit syslog("Проверка перехода на -35",C);
        emit set_rps_preheating(-35);
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < test_config_rps.rkn_disable_time)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(read_cnt >= test_config_rps.rkn_disable_time){
            emit syslog(tmp.sprintf("Ошибка работы на -35"),E);
            emit syslog(tmp.sprintf("Время старта узла RKN не в допуске: %d",read_cnt),E);
            emit set_rps_result(RpsStandPhase::Preheating,RED);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Проверка работы -35: Ok"),I);

        //Проверка PREHEATING:
        //-40 - должен отключиться
        emit syslog("Проверка отключения при -40",C);
        emit set_rps_preheating(-40);
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=0) && (read_cnt < test_config_rps.rkn_disable_time)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(dev->modbus_data[MB_RPS_AC_OUT_IND]!=0 || read_cnt >= test_config_rps.rkn_disable_time){
            emit syslog(tmp.sprintf("Ошибка работы на -40"),E);
            emit syslog(tmp.sprintf("Время выключения узла RKN не в допуске: %d",read_cnt),E);
            emit set_rps_result(RpsStandPhase::Preheating,RED);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Проверка работы -40: Ok"),I);

        //Проверка PREHEATING:
        //-35 - не должно включиться
        emit syslog("Проверка перехода на -35",C);
        emit set_rps_preheating(-35);
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < test_config_rps.rkn_startup_time_max)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(dev->modbus_data[MB_RPS_AC_OUT_IND]!=0 ){
            emit syslog(tmp.sprintf("Ошибка работы на -35: RKN не должен был включиться"),E);
            emit set_rps_result(RpsStandPhase::Preheating,RED);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Проверка работы -35: Ok"),I);
        emit syslog(tmp.sprintf("Проверка узла Preheating: Ok"),I);
        emit set_rps_result(RpsStandPhase::Preheating,GREEN);

        emit set_rps_preheating(-30);
        Sleep(100);
        if(test_config_rps.rkn_test == 0){
            if(dev->mb_rps_dev_type == DEV_RPS_STAND)
                emit set_rps_latr_state(0);
            else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
                emit set_rps_latr_state(0);//ВЫКЛ ЛАТР
                Sleep(1000);
                emit set_rps_380_state(0);//ВЫКЛ AC
            }
        }
    }

    if(test_config_rps.rkn_test){
        Sleep(100);
        if(dev->mb_rps_dev_type == DEV_RPS_STAND)
            emit set_rps_latr_state(1);
        else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
            emit set_rps_latr_state(1);//ВКЛ ЛАТР
            Sleep(1000);
            emit set_rps_380_state(1);//ВКЛ AC
        }

        emit syslog("Проверка старта узла RKN",C);

        //Проконтролировать 230В на выходе RPS-01 (IND_RPS-AC_OUT),
        //состояние равное 1 должно появиться в течении 8-15 сек., если появится раньше 6 сек. – ошибка.

        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < test_config_rps.rkn_startup_time_max)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(read_cnt >= test_config_rps.rkn_startup_time_max || read_cnt < test_config_rps.rkn_startup_time_min){
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit syslog(tmp.sprintf("Время старта узла RKN не в допуске: %d",read_cnt),E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Время старта узла RKN при 230В в допуске: %d",read_cnt),I);

        WAIT_OK("Установите напряжение на ЛАТР 150В");

        //Проверка 150В:
        emit syslog("Проверка RKN на 150В",C);
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=0) && (read_cnt <= test_config_rps.rkn_disable_time)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(dev->modbus_data[MB_RPS_AC_OUT_IND]!=0 || read_cnt > test_config_rps.rkn_disable_time){
            emit syslog(tmp.sprintf("Ошибка проверки 150В"),E);
            emit syslog(tmp.sprintf("Время выключения узла RKN не в допуске: %d",read_cnt),E);
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Проверка 150В: Ok"),I);

        WAIT_OK("Установите напряжение на ЛАТР 190В");
        //Проверка 190В:
        emit syslog("Проверка RKN на 190В",C);
        Sleep(1000);
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < test_config_rps.rkn_startup_time_max)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(read_cnt >= test_config_rps.rkn_startup_time_max){
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit syslog(tmp.sprintf("Время старта узла RKN не в допуске: %d",read_cnt),E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Время старта узла RKN при 190В в допуске: %d",read_cnt),I);

        Sleep(5000);
        emit syslog("Проверка RKN на 250В",C);
        WAIT_OK("Установите напряжение на ЛАТР 250В");
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < test_config_rps.rkn_disable_time)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(dev->modbus_data[MB_RPS_AC_OUT_IND]!=1 || read_cnt >= test_config_rps.rkn_disable_time){
            emit syslog(tmp.sprintf("Ошибка проверки 250В"),E);
            emit syslog(tmp.sprintf("Время включения узла RKN не в допуске: %d",read_cnt),E);
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Проверка 250В: Ok"),I);

        emit syslog("Проверка RKN на 270В",C);
        WAIT_OK("Установите напряжение на ЛАТР 270В");
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=0) && (read_cnt < test_config_rps.rkn_disable_time)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(dev->modbus_data[MB_RPS_AC_OUT_IND]!=0 || read_cnt >= test_config_rps.rkn_disable_time){
            emit syslog(tmp.sprintf("Ошибка проверки 270В"),E);
            emit syslog(tmp.sprintf("Время выключения узла RKN не в допуске: %d",read_cnt),E);
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Проверка 270В: Ok"),I);

        emit syslog("Проверка RKN на 230В",C);
        WAIT_OK("Установите напряжение на ЛАТР 230В");
        //Проверка 230В:
        Sleep(1000);
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < test_config_rps.rkn_startup_time_max)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(read_cnt >= test_config_rps.rkn_startup_time_max ){
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit syslog(tmp.sprintf("Время старта узла RKN не в допуске: %d",read_cnt),E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Время старта узла RKN при 230В в допуске: %d",read_cnt),I);
    }

    //проверка подачей 380В
    if(test_config_rps.rkn_380v_test){
        Sleep(1000);
        emit syslog("Проверка RKN на 380В",C);
        if(dev->mb_rps_dev_type == DEV_RPS_STAND){
            emit set_rps_latr_state(0);
            Sleep(3000);
            emit set_rps_380_state(1);
        }
        else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
            emit set_rps_latr_state(0);//ВКЛ 400V
            Sleep(3000);
            emit set_rps_380_state(1);//ВКЛ AC
        }

        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_IN_IND]!=1) && (read_cnt < 10)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(dev->modbus_data[MB_RPS_AC_IN_IND]!=1){
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit syslog(tmp.sprintf("Ошибка подачи 380В на вход стенда"),E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=0) && (read_cnt < test_config_rps.rkn_startup_time_max)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(read_cnt >= test_config_rps.rkn_disable_time){
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit syslog(tmp.sprintf("Время отключения узла RKN при 380 В не в допуске: %d",read_cnt),E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit syslog(tmp.sprintf("Время отключения узла RKN при 380 В в допуске: %d",read_cnt),I);

        //ждём 10 сек
        Sleep(10000);

        if(dev->mb_rps_dev_type == DEV_RPS_STAND){
            //отключаем 380В
            emit set_rps_380_state(0);
            Sleep(1000);
            //включаем ЛАТР
            emit set_rps_latr_state(1);
        }
        else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
            emit set_rps_latr_state(1);//ВКЛ ЛАТР
            Sleep(3000);
            emit set_rps_380_state(1);//ВКЛ AC
        }

        //проверяем, что включится
        Sleep(1000);
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < test_config_rps.rkn_startup_time_max)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }

        if(read_cnt >= test_config_rps.rkn_startup_time_max){
            emit set_rps_result(RpsStandPhase::RKN,RED);
            emit syslog(tmp.sprintf("Время старта узла RKN после воздействия 380В не в допуске: %d",read_cnt),E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }
        emit syslog(tmp.sprintf("Время старта узла RKN при 230В (после воздействия 380В) в допуске: %d",read_cnt),I);
    }

    if(test_config_rps.rkn_test || test_config_rps.rkn_380v_test){

        if(dev->mb_rps_dev_type == DEV_RPS_STAND){
            emit set_rps_latr_state(0);//выключаем ЛАТР
        }
        else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
            emit set_rps_380_state(0);//ВЫКЛ AC
        }
        Sleep(1000);

        if(test_config_rps.preheating_position == 0){
            WAIT_OK("Установите джампер PREHEATING в положение NO");
        }
        //тест RKN закончен
        emit set_rps_result(0,GREEN);
        emit rps_stand_painting(RpsStandStage::None);
        emit set_rps_result(RpsStandPhase::RKN,GREEN);
    }

    if(test_config_rps.buildin_test){

        emit syslog(tmp.sprintf("Проверка узла АКБ"),C);
        emit set_rps_stand_akb_polarity(INVERSE_POLARITY);
        Sleep(100);
        emit set_rps_stand_akb_state(1);
        emit rps_stand_painting(RpsStandStage::AKB1Polarity);
        emit rps_stand_painting(RpsStandStage::AKB2Polarity);
        WAIT_CONFIRM("Индикатор неправильной полярности АКБ горит?");
        if(confirm_status == 0){
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            emit syslog("Индикатор неисправен",E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        Sleep(100);

        //установка прямой полярности
        emit set_rps_stand_akb_polarity(NORMAL_POLARITY);
        Sleep(1000);
        emit rps_stand_painting(RpsStandStage::None);
        emit rps_stand_painting(RpsStandStage::BtnColdStart);

        WAIT_OK("Нажмите кнопку START");

        //1. Самотестирование
        emit syslog("Самотестирование",C);
        emit syslog("Проверка RS-485",I);
        if(check_rps01_param(MB_RPS1_MANUF_ID,
                             0x11A6,
                             test_config_rps.rps_read_delay))
        {
            emit syslog("Кнопка запуска исправна",I);
            emit syslog("Проверка RS-485 (Чтение идентификатора): Ок",I);
        }
        else
        {
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            emit syslog("Ошибка чтения идентификатора",E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        //версия ПО
        if(check_rps01_minmax_param(MB_RPS1_SW_VERS,
                                    99999,
                                    test_config_rps.fw_version,
                                    test_config_rps.rps_read_delay))
        {
            emit syslog(tmp.sprintf("Версия ПО платы RPS: %d",dev->modbus_data[MB_RPS1_SW_VERS]),I);
        }  else{
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            emit syslog(tmp.sprintf("Версия ПО платы RPS: %d",dev->modbus_data[MB_RPS1_SW_VERS]),E);
            emit syslog(tmp,E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        //температура
        if(check_rps01_minmax_param(MB_RPS1_TEMPER,
                                    test_config_rps.temper_max,
                                    test_config_rps.temper_min,
                                    test_config_rps.rps_read_delay))
        {
            tmp.sprintf("Измерение температуры: Ок (%d)",dev->modbus_data[MB_RPS1_TEMPER]);
            emit syslog(tmp,I);
        }
        else{
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            tmp.sprintf("Измеренная температура не в диапазоне +%d..+%d: фактическая %d",test_config_rps.temper_min,test_config_rps.temper_max,dev->modbus_data[MB_RPS1_TEMPER]);
            emit syslog(tmp,E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        //проверяем работу от АКБ
        emit syslog("Проверка работы от АКБ",C);
        if(check_rps01_param(MB_RPS1_VAC,
                             0,
                             test_config_rps.rps_read_delay))
        {

        }
        else
        {
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            emit syslog("Не видим питание от АКБ",E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        if(check_rps01_minmax_param(MB_RPS1_BAT_VOLTAGE,
                                    test_config_rps.akb_voltage_ac_max,
                                    test_config_rps.akb_voltage_ac_min,
                                    test_config_rps.rps_read_delay))
        {
            emit syslog(tmp.sprintf("Измерение напряжения АКБ: Ок (%d)",dev->modbus_data[MB_RPS1_BAT_VOLTAGE]),I);
            emit syslog("Проверка статуса АКБ:Ok",I);
        }
        else{
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            tmp.sprintf("Измеренное напряжение АКБ не в допуске: %dmV",dev->modbus_data[MB_RPS1_BAT_VOLTAGE]);
            emit syslog(tmp,E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        //проверка реле
        if(test_config_rps.relay1_test){
            emit syslog("Проверка реле RELAY",I);
            emit set_rps_relay1(1);
            Sleep(100);
            emit set_rps_relay1(1);
            Sleep(100);
        }

        if(test_config_rps.relay2_test){
            emit syslog("Проверка реле AC_OK",I);
            emit set_rps_relay2(1);
            Sleep(100);
            emit set_rps_relay2(1);
            Sleep(100);
        }

        if(test_config_rps.relay1_test){
            if(check_rpsstand_param(MB_RPS_REL2_IN,
                                    1,
                                    test_config_rps.rps_read_delay))
            {
                emit syslog("Проверка реле RELAY: Ok",I);
            }
            else{
                emit set_rps_result(RpsStandPhase::Selftest,RED);
                emit syslog("Ошибка проверки реле RELAY",E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        if(test_config_rps.relay2_test){
            if(check_rpsstand_param(MB_RPS_REL1_IN,
                                    1,
                                    test_config_rps.rps_read_delay))
            {
                emit syslog("Проверка реле AC_OK: Ok",I);
            }
            else{
                emit set_rps_result(RpsStandPhase::Selftest,RED);
                emit syslog("Ошибка проверки реле AC_OK",E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        //отключаем реле
        if(test_config_rps.relay1_test){
            emit set_rps_relay1(0);
            Sleep(100);
        }

        if(test_config_rps.relay2_test){
            emit set_rps_relay2(0);
            Sleep(100);
        }

        //проверяем, что отключилось
        if(test_config_rps.relay1_test){
            if(check_rpsstand_param(MB_RPS_REL2_IN,
                                    0,
                                    test_config_rps.rps_read_delay))
            {
                emit syslog("Проверка выключения реле RELAY: Ok",I);
            }
            else{
                emit set_rps_result(RpsStandPhase::Selftest,RED);
                emit syslog("Ошибка проверки реле RELAY",E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }
        if(test_config_rps.relay2_test){
            if(check_rpsstand_param(MB_RPS_REL1_IN,
                                    0,
                                    test_config_rps.rps_read_delay))
            {
                emit syslog("Проверка выключения реле AC_OK: Ok",I);
            }
            else{
                emit set_rps_result(RpsStandPhase::Selftest,RED);
                emit syslog("Ошибка проверки реле AC_OK",E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        emit rps_stand_painting(RpsStandStage::None);

        emit rps_stand_painting(RpsStandStage::LedCPU);
        WAIT_CONFIRM("Индикатор CPU мигает?");
        if(confirm_status == 0){
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            emit syslog("Индикатор CPU неисправен",E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit rps_stand_painting(RpsStandStage::None);
        emit rps_stand_painting(RpsStandStage::LedBat);
        WAIT_CONFIRM("Индикатор BAT мигает?");
        if(confirm_status == 0){
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            emit syslog("Индикатор BAT неисправен",E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit rps_stand_painting(RpsStandStage::None);
        emit rps_stand_painting(RpsStandStage::Led52V);
        WAIT_CONFIRM("Индикатор HL1 (52V) горит?");
        if(confirm_status == 0){
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            emit syslog("Индикатор HL1 (52V) неисправен",E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }

        emit rps_stand_painting(RpsStandStage::None);
        emit rps_stand_painting(RpsStandStage::BtnStop);

        WAIT_OK("Нажмите кнопку STOP до полного отключения RPS-01");
        dev->modbus_data[MB_RPS1_MANUF_ID] = 0;

        if(check_rps01_param(MB_RPS1_MANUF_ID,0x11A6,5)){
            emit set_rps_result(RpsStandPhase::Selftest,RED);
            emit syslog("Неисправность кнопки STOP: плата не отключилась",E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }
        emit rps_stand_painting(RpsStandStage::None);

        emit set_rps_result(RpsStandPhase::Selftest,GREEN);

        Sleep(100);
        emit syslog("Самотестирование пройдено",S);
        Sleep(100);
        emit set_rps_stand_akb_state(0);
        Sleep(100);
    }

    if(test_config_rps.charging_test){
        emit syslog("Запуск тестирования узла заряда",C);


        if(dev->mb_rps_dev_type == DEV_RPS_STAND){
            //включаем ЛАТР
            emit set_rps_latr_state(1);
        }
        else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
            emit set_rps_latr_state(1);//ВКЛ ЛАТР
            Sleep(100);
            emit set_rps_380_state(1);//ВКЛ AC
        }

        //ожидаем включения
        Sleep(1000);
        read_cnt = 0;
        while((dev->modbus_data[MB_RPS_AC_OUT_IND]!=1) && (read_cnt < 30)){
            Sleep(1000);
            emit rps_read_stand_signal();
            read_cnt++;
        }
        if(read_cnt >= 30 ){
            emit set_rps_result(RpsStandPhase::Charging,RED);
            emit syslog(tmp.sprintf("Узел RKN не запустился: %d",read_cnt),E);
            emit rpsTestDone(0);
            rpsStartFlag = false;
            goto StopHard;
        }
        emit syslog(tmp.sprintf("Узел RKN запустился: %d",read_cnt),I);

        //ждём запуск платы
        Sleep(5000);

        //проверка на ХХ
        if(test_config_rps.load_XX_test){
            emit syslog("Запуск тестирования узла заряда на ХХ",C);
            emit set_rps_rload_state(0);
            Sleep(1000);

            if(check_rpsstand_minmax_param(MB_RPS_BAT_CURRENT,
                                           test_config_rps.load_XX_current_max,
                                           test_config_rps.load_XX_current_min,
                                           test_config_rps.rps_read_delay))
            {
                emit syslog(tmp.sprintf("Ток зарядки АКБ на ХХ в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),I);
            }
           else{
                emit set_rps_result(RpsStandPhase::Charging,RED);
                emit syslog(tmp.sprintf("Ток зарядки АКБ на ХХ не в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            if(check_rpsstand_minmax_param(MB_RPS_BAT_VOLTAGE,
                                           test_config_rps.akb_charge_voltage_max,
                                           test_config_rps.akb_charge_voltage_min,
                                           test_config_rps.rps_read_delay))
            {
                tmp.sprintf("Напряжение АКБ на ХХ в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,I);
            }
            else{
                emit set_rps_result(RpsStandPhase::Charging,RED);
                tmp.sprintf("Измеренное напряжение зарядки АКБ на ХХ не в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        //100
        if(test_config_rps.load_100ohm_test){
            emit syslog("Запуск тестирования узла заряда на 100 Ом",C);
            emit set_rps_rload_value(100);
            Sleep(100);
            emit set_rps_rload_state(1);
            Sleep(1000);
            if(check_rpsstand_minmax_param(MB_RPS_BAT_CURRENT,
                                           test_config_rps.load_100ohm_current_max,
                                           test_config_rps.load_100ohm_current_min,
                                           test_config_rps.rps_read_delay))
            {
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 100 Ом в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),I);
            }
           else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 100 Ом не в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            if(check_rpsstand_minmax_param(MB_RPS_BAT_VOLTAGE,
                                           test_config_rps.load_100ohm_voltage_max,
                                           test_config_rps.load_100ohm_voltage_min,
                                           test_config_rps.rps_read_delay))
            {
                tmp.sprintf("Напряжение АКБ на 100 Ом в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,I);            }
            else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                tmp.sprintf("Измеренное напряжение зарядки АКБ на 100 Ом не в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        //70
        if(test_config_rps.load_70ohm_test){
            emit syslog("Запуск тестирования узла заряда на 70 Ом",C);
            emit set_rps_rload_value(70);
            Sleep(100);
            emit set_rps_rload_state(1);
            Sleep(1000);
            if(check_rpsstand_minmax_param(MB_RPS_BAT_CURRENT,
                                           test_config_rps.load_70ohm_current_max,
                                           test_config_rps.load_70ohm_current_min,
                                           test_config_rps.rps_read_delay))
            {
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 70 Ом в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),I);
            }
           else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 70 Ом не в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            if(check_rpsstand_minmax_param(MB_RPS_BAT_VOLTAGE,
                                           test_config_rps.load_70ohm_voltage_max,
                                           test_config_rps.load_70ohm_voltage_min,
                                           test_config_rps.rps_read_delay))
            {
                tmp.sprintf("Напряжение АКБ на 70 Ом в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,I);            }
            else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                tmp.sprintf("Измеренное напряжение зарядки АКБ на 70 Ом не в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        //50
        if(test_config_rps.load_50ohm_test){
            emit syslog("Запуск тестирования узла заряда на 50 Ом",C);
            emit set_rps_rload_value(50);
            Sleep(100);
            emit set_rps_rload_state(1);
            Sleep(1000);
            if(check_rpsstand_minmax_param(MB_RPS_BAT_CURRENT,
                                           test_config_rps.load_50ohm_current_max,
                                           test_config_rps.load_50ohm_current_min,
                                           test_config_rps.rps_read_delay))
            {
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 50 Ом в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),I);
            }
           else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 50 Ом не в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            if(check_rpsstand_minmax_param(MB_RPS_BAT_VOLTAGE,
                                           test_config_rps.load_50ohm_voltage_max,
                                           test_config_rps.load_50ohm_voltage_min,
                                           test_config_rps.rps_read_delay))
            {
                tmp.sprintf("Напряжение АКБ на 50 Ом в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,I);            }
            else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                tmp.sprintf("Измеренное напряжение зарядки АКБ на 50 Ом не в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        //30
        if(test_config_rps.load_30ohm_test){
            emit syslog("Запуск тестирования узла заряда на 30 Ом",C);
            emit set_rps_rload_value(30);
            Sleep(100);
            emit set_rps_rload_state(1);
            Sleep(1000);
            if(check_rpsstand_minmax_param(MB_RPS_BAT_CURRENT,
                                           test_config_rps.load_30ohm_current_max,
                                           test_config_rps.load_30ohm_current_min,
                                           test_config_rps.rps_read_delay))
            {
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 30 Ом в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),I);
            }
           else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 30 Ом не в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            if(check_rpsstand_minmax_param(MB_RPS_BAT_VOLTAGE,
                                           test_config_rps.load_30ohm_voltage_max,
                                           test_config_rps.load_30ohm_voltage_min,
                                           test_config_rps.rps_read_delay))
            {
                tmp.sprintf("Напряжение АКБ на 30 Ом в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,I);
            }
            else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                tmp.sprintf("Измеренное напряжение зарядки АКБ на 30 Ом не в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        //20
        if(test_config_rps.load_20ohm_test){
            emit syslog("Запуск тестирования узла заряда на 20 Ом",C);
            emit set_rps_rload_value(20);
            Sleep(100);
            emit set_rps_rload_state(1);
            Sleep(1000);
            if(check_rpsstand_minmax_param(MB_RPS_BAT_CURRENT,
                                           test_config_rps.load_20ohm_current_max,
                                           test_config_rps.load_20ohm_current_min,
                                           test_config_rps.rps_read_delay))
            {
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 20 Ом в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),I);
            }
           else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 20 Ом не в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            if(check_rpsstand_minmax_param(MB_RPS_BAT_VOLTAGE,
                                           test_config_rps.load_20ohm_voltage_max,
                                           test_config_rps.load_20ohm_voltage_min,
                                           test_config_rps.rps_read_delay))
            {
                tmp.sprintf("Напряжение АКБ на 20 Ом в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,I);
            }
            else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                tmp.sprintf("Измеренное напряжение зарядки АКБ на 20 Ом не в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }
        }

        //15
        if(test_config_rps.load_15ohm_test){
            emit syslog("Запуск тестирования узла заряда на 15 Ом",C);
            emit set_rps_rload_value(15);
            Sleep(100);
            emit set_rps_rload_state(1);
            Sleep(1000);
            if(check_rpsstand_minmax_param(MB_RPS_BAT_CURRENT,
                                           test_config_rps.load_15ohm_current_max,
                                           test_config_rps.load_15ohm_current_min,
                                           test_config_rps.rps_read_delay))
            {
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 15 Ом в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),I);
            }
           else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 15 Ом не в допуске: %d",dev->modbus_data[MB_RPS_BAT_CURRENT]),E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            if(check_rpsstand_minmax_param(MB_RPS_BAT_VOLTAGE,
                                           test_config_rps.load_15ohm_voltage_max,
                                           test_config_rps.load_15ohm_voltage_min,
                                           test_config_rps.rps_read_delay))
            {
                tmp.sprintf("Напряжение АКБ на 15 Ом в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,I);
            }
            else
            {
                emit set_rps_result(RpsStandPhase::Charging,RED);
                tmp.sprintf("Измеренное напряжение зарядки АКБ на 15 Ом не в допуске: %dmV",dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
                emit syslog(tmp,E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            emit syslog("Проверка считывания значений с RPS-01 на 15 Ом",I);
            if(check_rps01_minmax_param(MB_RPS1_BAT_CURRENT,
                                        test_config_rps.load_15ohm_rps01_current_max,
                                        test_config_rps.load_15ohm_rps01_current_min,
                                        test_config_rps.rps_read_delay))
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 15 Ом c RPS-01 в допуске: %d",dev->modbus_data[MB_RPS1_BAT_CURRENT]),I);
            else{
                emit syslog(tmp.sprintf("Ток зарядки АКБ на 15 Ом c RPS-01 не в допуске: %d",dev->modbus_data[MB_RPS1_BAT_CURRENT]),E);
                emit rpsTestDone(0);
                rpsStartFlag = false;
                goto StopHard;
            }

            if(check_rps01_minmax_param(MB_RPS1_CHRG_VOLTAGE,
                                        test_config_rps.load_15ohm_rps01_voltage_max,
                                        test_config_rps.load_15ohm_rps01_voltage_min,
                                        test_config_rps.rps_read_delay))
                emit syslog(tmp.sprintf("Напряжение зарядки АКБ на 15 Ом c RPS-01 в допуске: %d",dev->modbus_data[MB_RPS1_CHRG_VOLTAGE]),I);
            else{
               emit syslog(tmp.sprintf("Напряжение зарядки АКБ на 15 Ом c RPS-01 не в допуске: %d",dev->modbus_data[MB_RPS1_CHRG_VOLTAGE]),E);
                emit rpsTestDone(0);
                emit set_rps_result(RpsStandPhase::Charging,RED);
                rpsStartFlag = false;
                goto StopHard;
            }


        }

        emit set_rps_result(RpsStandPhase::Charging,GREEN);
        emit syslog("Тестирование узла заряда: Успешно",I);
    }

    //тестированеи завершено
    emit rpsTestDone(1);

StopHard:
    Sleep(100);
    if(dev->mb_rps_dev_type == DEV_RPS_STAND){
        emit set_rps_latr_state(0);//включаем ЛАТР
    }
    else if(dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
        emit set_rps_latr_state(1);//ВКЛ ЛАТР
    }
    Sleep(100);
    emit set_rps_380_state(0);
    Sleep(100);
    emit set_rps_stand_akb_polarity(0);
    Sleep(100);
    emit set_rps_stand_akb_state(0);
    Sleep(100);
    emit set_rps_relay1(0);
    Sleep(100);
    emit set_rps_relay2(0);
    Sleep(100);
    emit set_rps_rload_value(100);
    Sleep(100);
    emit set_rps_rload_state(0);
    Sleep(100);
    emit set_rps_latr_state(0);
    rpsStartFlag = false;
}

void MainWindow::rpsStandReadRPSPressed(){
    pTestThread->dev->read_rps_view_result = 1;
    rps_read_rps();
}

void MainWindow::rpsStandReadStandPressed(){
    pTestThread->dev->read_rpsstand_view_result = 1;
    rps_read_stand();
}

void MainWindow::rpsStandReadRPSFinished(){
    QString tmp;
    if(pTestThread->dev->read_rps_view_result){
        pTestThread->dev->read_rps_view_result = 0;
        syslog("----Данные с платы RPS-01------------------------------------------------------",I);
        tmp.sprintf("Идентификатор: %X",pTestThread->dev->modbus_data[MB_RPS1_MANUF_ID]);
        syslog(tmp,I);
        tmp.sprintf("Наличие сетевого напряжения: %d",pTestThread->dev->modbus_data[MB_RPS1_VAC]);
        syslog(tmp,I);
        tmp.sprintf("Напряжение на иммитаторе АКБ: %d",pTestThread->dev->modbus_data[MB_RPS1_BAT_VOLTAGE]);
        syslog(tmp,I);
        tmp.sprintf("Напряжение зарядки АКБ: %d",pTestThread->dev->modbus_data[MB_RPS1_CHRG_VOLTAGE]);
        syslog(tmp,I);
        tmp.sprintf("Ток зарядки АКБ: %d",pTestThread->dev->modbus_data[MB_RPS1_BAT_CURRENT]);
        syslog(tmp,I);
        tmp.sprintf("Температура: %d",pTestThread->dev->modbus_data[MB_RPS1_TEMPER]);
        syslog(tmp,I);
    }
    pTestThread->dev->rps_timer.stop();
}

void MainWindow::rpsStandConnectFinished(){
    if(pTestThread->dev->mb_rps_dev_type == DEV_RPS_STAND_V4){
        ui->rpsLATR->setText("ЛАТР/400");
        ui->rps380->setText("Ключ АС");
    }
}

void MainWindow::rpsStandReadStandFinished(){
    QString tmp;
    if(pTestThread->dev->read_rpsstand_view_result){
        pTestThread->dev->read_rpsstand_view_result = 0;
        syslog("----Данные от Стенда RPS------------------------------------------------------",I);
        tmp.sprintf("Подключение 380В: %X",pTestThread->dev->modbus_data[MB_RPS_AC_STATE]);
        syslog(tmp,I);
        tmp.sprintf("Подключение ЛАТР: %d",pTestThread->dev->modbus_data[MB_RPS_LATR_STATE]);
        syslog(tmp,I);
        tmp.sprintf("Подключение АКБ: %d",pTestThread->dev->modbus_data[MB_RPS_BAT_STATE]);
        syslog(tmp,I);
        tmp.sprintf("Полярность АКБ: %d",pTestThread->dev->modbus_data[MB_RPS_BAT_POLARITY]);
        syslog(tmp,I);
        tmp.sprintf("Эквивалентная температура: %d",(int16_t)(pTestThread->dev->modbus_data[MB_RPS_PREHEATING]));
        syslog(tmp,I);
        tmp.sprintf("Состояние реле AC_OK: %d",pTestThread->dev->modbus_data[MB_RPS_REL1_IN]);
        syslog(tmp,I);
        tmp.sprintf("Состояние реле Relay1: %d",pTestThread->dev->modbus_data[MB_RPS_REL2_IN]);
        syslog(tmp,I);
        tmp.sprintf("Ключ управление нагрузкой: %d",pTestThread->dev->modbus_data[MB_RPS_EL_STATE]);
        syslog(tmp,I);
        tmp.sprintf("Сопротивление нагрузки: %d",pTestThread->dev->modbus_data[MB_RPS_EL_R_SET]);
        syslog(tmp,I);
        tmp.sprintf("Напряжение на АКБ: %d",pTestThread->dev->modbus_data[MB_RPS_BAT_VOLTAGE]);
        syslog(tmp,I);
        tmp.sprintf("Ток через АКБ: %d",pTestThread->dev->modbus_data[MB_RPS_BAT_CURRENT]);
        syslog(tmp,I);
        tmp.sprintf("Напряжение на входе RPS: %d",pTestThread->dev->modbus_data[MB_RPS_AC_IN_IND]);
        syslog(tmp,I);
        tmp.sprintf("Напряжение на выходе RPS: %d",pTestThread->dev->modbus_data[MB_RPS_AC_OUT_IND]);
        syslog(tmp,I);
        tmp.sprintf("Температура 1: %d",pTestThread->dev->modbus_data[MB_RPS_TEMPER1]);
        syslog(tmp,I);
        tmp.sprintf("Температура 2: %d",pTestThread->dev->modbus_data[MB_RPS_TEMPER2]);
        syslog(tmp,I);
        tmp.sprintf("Состояние вентилятора: %d",pTestThread->dev->modbus_data[MB_RPS_FAN_STATE]);
        syslog(tmp,I);
        tmp.sprintf("Температура включения вентилятора: %d",pTestThread->dev->modbus_data[MB_RPS_FAN_T_ON]);
        syslog(tmp,I);
        tmp.sprintf("Температура выключения вентилятора: %d",pTestThread->dev->modbus_data[MB_RPS_FAN_T_OFF]);
        syslog(tmp,I);
        tmp.sprintf("Максимальная температура на радиаторе: %d",pTestThread->dev->modbus_data[MB_RPS_MAX_TEMPER]);
        syslog(tmp,I);
    }

    if(pTestThread->dev->modbus_data[MB_RPS_AC_IN_IND]){
        ui->rpsStandAcIn->setStyleSheet("QPushButton { background-color: grey; }");
    }
    else{
        ui->rpsStandAcIn->setStyleSheet("");
    }

    if(pTestThread->dev->modbus_data[MB_RPS_AC_OUT_IND]){
        ui->rpsStandAcOut->setStyleSheet("QPushButton { background-color: grey; }");
    }
    else{
        ui->rpsStandAcOut->setStyleSheet("");
    }

    if(pTestThread->dev->modbus_data[MB_RPS_REL1_IN]){
        ui->rpsAcOk->setStyleSheet("QPushButton { background-color: grey; }");
    }
    else{
        ui->rpsAcOk->setStyleSheet("");
    }

    if(pTestThread->dev->modbus_data[MB_RPS_REL2_IN]){
        ui->rpsRelay->setStyleSheet("QPushButton { background-color: grey; }");
    }
    else{
        ui->rpsRelay->setStyleSheet("");
    }
    pTestThread->dev->rpsstand_timer.stop();
}

void MainWindow::runRpsTestNew(){
    //rpsStartFlag = true;
}

void MainWindow::stopRpsTestNew(){
    //rpsStartFlag = false;
}

void MainWindow::rpsStandAkbStatePressed(){
    static int state = 0;
    if(state == 0){
        state = 1;
    }
    else{
        state = 0;
    }
    set_rps_stand_akb_state(state);
}

void MainWindow::rpsStandPolarityPressed(){
    static int state = 0;
    if(state == 0){
        state = 1;
    }
    else{
        state = 0;
    }
    set_rps_stand_akb_polarity(state);

}

void MainWindow::rpsStandRloadSetPressed(){
    QString tmp;
    int rload_value;
    bool ok;
    rload_value = ui->rpsRLoadValue->text().toInt(&ok,10);
    if(ok && rload_value >= RPS_RLOAD_MIN && rload_value <= RPS_RLOAD_MAX){
        set_rps_rload_value(rload_value);
    }
    else{
        syslog(tmp.sprintf("Ошибка установки сопротивления нагрузки: от %d до %d Ом",RPS_RLOAD_MIN,RPS_RLOAD_MAX),I);
    }
}

void MainWindow::rpsStandRloadStatePressed(){
    static int state = 0;
    if(state == 0){
        state = 1;
    }
    else{
        state = 0;
    }
    set_rps_rload_state(state);
}

void MainWindow::rpsStandPreHeatPressed(){
    int value = 0;
    if(ui->rpsPreheating30->isChecked())
        value = -30;
    if(ui->rpsPreheating35->isChecked())
        value = -35;
    if(ui->rpsPreheating40->isChecked())
        value = -40;
    set_rps_preheating(value);
}

void MainWindow::rpsStandLatrPressed(){
    static int state = 0;
    if(state == 0){
        state = 1;
    }
    else{
        state = 0;
    }
    set_rps_latr_state(state);
}

void MainWindow::rpsStand380VPressed(){
    static int state = 0;

    if(state == 0){
        state = 1;
    }
    else{
        state = 0;
    }

    set_rps_380_state(state);

}

//управление стендом RPS New

//управление иммитатором АКБ
void MainWindow::set_rps_stand_akb_state(int state){
    if(state){
        syslog("Включение Иммитатора АКБ",I);
        ui->rpsAkbState->setStyleSheet("QPushButton { background-color: grey; }");
    }
    else{
        syslog("Отключение Иммитатора АКБ",I);
        ui->rpsAkbState->setStyleSheet("");
    }
    pTestThread->dev->writeModbus(MB_RPS_STAND_ADDR,MB_RPS_BAT_STATE-1,state);
}

//прямая/обратная полярность
void MainWindow::set_rps_stand_akb_polarity(int state){
    if(state == INVERSE_POLARITY){
        syslog("Обратная полярность АКБ",I);
        ui->rpsAkbPolarityBtn->setStyleSheet("QPushButton { background-color: grey; }");
        pTestThread->dev->writeModbus(MB_RPS_STAND_ADDR,MB_RPS_BAT_POLARITY-1,1);
    }
    else{
        syslog("Прямая полярность АКБ",I);
        ui->rpsAkbPolarityBtn->setStyleSheet("");
        pTestThread->dev->writeModbus(MB_RPS_STAND_ADDR,MB_RPS_BAT_POLARITY-1,0);
    }
}

void MainWindow::set_rps_rload_value(int value){
    QString tmp;
    syslog(tmp.sprintf("Установка сопротивления нагрузки: %d Ом",value),I);
    pTestThread->dev->writeModbus(MB_RPS_STAND_ADDR,MB_RPS_EL_R_SET-1,value);
}

void MainWindow::set_rps_rload_state(int state){
    if(state){
        ui->rpsRloadState->setStyleSheet("QPushButton { background-color: grey; }");
        syslog("Подключение сопротивления нагрузки",I);
    }
    else{
        ui->rpsRloadState->setStyleSheet("");
        syslog("Отключение сопротивления нагрузки",I);
    }
    pTestThread->dev->writeModbus(MB_RPS_STAND_ADDR,MB_RPS_EL_STATE-1,state);
}

void MainWindow::set_rps_preheating(int value){
    QString tmp;
    syslog(tmp.sprintf("Установка эквивалента температуры: %d",value),I);
    pTestThread->dev->writeModbus(MB_RPS_STAND_ADDR,MB_RPS_PREHEATING-1,value);
}

void MainWindow::set_rps_latr_state(int state){
    if(state){
        ui->rpsLATR->setStyleSheet("QPushButton { background-color: grey; }");
        if(pTestThread->dev->mb_rps_dev_type == DEV_RPS_STAND_V4)
            syslog("Подключение ЛАТР",I);
        else
            syslog("Подключение ЛАТР",I);
    }
    else{
        ui->rpsLATR->setStyleSheet("");
        if(pTestThread->dev->mb_rps_dev_type == DEV_RPS_STAND_V4)
            syslog("Подключение 400V",I);
        else
            syslog("Отключение ЛАТР",I);
    }
    pTestThread->dev->writeModbus(MB_RPS_STAND_ADDR,MB_RPS_LATR_STATE-1,state);
}

void MainWindow::set_rps_380_state(int state){
    if(state){
        ui->rps380->setStyleSheet("QPushButton { background-color: grey; }");
        if(pTestThread->dev->mb_rps_dev_type == DEV_RPS_STAND_V4)
            syslog("Подключение AC",I);
        else
            syslog("Подключение 380В",I);
    }
    else{
        ui->rps380->setStyleSheet("");
        if(pTestThread->dev->mb_rps_dev_type == DEV_RPS_STAND_V4)
            syslog("Отключение AC",I);
        else
            syslog("Отключение 380В",I);
    }
    pTestThread->dev->writeModbus(MB_RPS_STAND_ADDR,MB_RPS_AC_STATE-1,state);
}

//управление реле на плате RPS-01
void MainWindow::set_rps_relay1(int state){
    if(state){
        syslog("Включение RELAY",I);
    }
    else{
        syslog("Отключение RELAY",I);
    }
    pTestThread->dev->writeModbus(MB_RPS_ADDR,MB_RPS1_REL_STATE-1,state);
}
void MainWindow::set_rps_relay2(int state){
    if(state){
        syslog("Включение AC_OK",I);
    }
    else{
        syslog("Отключение AC_OK",I);
    }
    pTestThread->dev->writeModbus(MB_RPS_ADDR,MB_RPS1_AC_OK_STATE-1,state);
}

void MainWindow::read_stand(int slot){
    qDebug() << "read_stand slot" << slot;
    pTestThread->dev->set_read_flag(MB_READ_ONE);
    pTestThread->dev->set_current_slot(slot);
}

void MainWindow::rps_read_rps(){
    pTestThread->dev->rps_timer.start(100);
}

void MainWindow::rps_read_stand(){
    pTestThread->dev->rpsstand_timer.start(100);
    qDebug() << "rps_read_stand";
}
