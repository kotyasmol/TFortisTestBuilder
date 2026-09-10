#include "modbus_dev.h"
#include "qdebug.h"
#include <QModbusDevice>
#include <QModbusClient>
#include <QModbusRtuSerialMaster>
#include <QSerialPort>
#include "string.h"
#include "qobject.h"
#include "TestThread.h"
#include "QSignalMapper"
#include <QFile>
#include "constants.h"
#include "debugwindow.h"

modbus_dev::modbus_dev(QString port,int autoconnect_, int type)
{
    qDebug() << "modbus_dev constructor" << port << type;

    emit syslog("modbus_dev constructor",I);
    slave_id = 0;
    for(int i=0;i<STAND_SLOTS_NUM;i++)
        connected_ok[i] = 0;
    rps_connected = 0;
    modbus_port = port;
    autoconnect = autoconnect_;

    modbusDevice = new QModbusRtuSerialMaster();
    if(modbusDevice->state() != QModbusDevice::ConnectedState)
    {
        modbusDevice->setConnectionParameter(QModbusDevice::SerialPortNameParameter,modbus_port);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialParityParameter,  QSerialPort::NoParity);
        if(type == StandType::typeRPS || type == StandType::typeRPSNew)
            modbusDevice->setConnectionParameter(QModbusDevice::SerialBaudRateParameter,QSerialPort::Baud4800);
        if(type == StandType::typeAPK03)
            modbusDevice->setConnectionParameter(QModbusDevice::SerialBaudRateParameter,QSerialPort::Baud9600);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialDataBitsParameter,QSerialPort::Data8);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialStopBitsParameter,QSerialPort::OneStop);
        modbusDevice->setTimeout(0);
        modbusDevice->setNumberOfRetries(1);

        com_openned = modbusDevice->connectDevice();

        if(!com_openned)
        {
            emit syslog("Подключение не удалось: " + modbusDevice->errorString(), E);
            qDebug() << "Подключение не удалось" << modbusDevice->errorString();
        }else{
            emit syslog("Подключение успешно к " + port,I);
            qDebug() << "Подключение  удалось" << port;
        }

    }
    else{
        qDebug() << "Подключение не удалось";
    }

    if(type == StandType::typeAPK03){
        //таймер для опроса
        connect(&stand_timer, SIGNAL(timeout()), this, SLOT(stand_timer_timeout()));
        stand_timer.start(MODBUS_INTERVAL);

        //таймер для автоподключения
        connect(&connect_timer,SIGNAL(timeout()),this, SLOT(standComAutoconnect()));
        connect_timer.start(1000);

        //для первоначального поиска плат
        connect(&search_timer, SIGNAL(timeout()), this, SLOT(search_timer_timeout()));
        search_timer.start(MODBUS_SEARCH_TIME);
        set_read_flag(MB_SEARCH);
        emit syslog("Производится поиск плат стенда",I);
    }

    else  if(type == StandType::typeRPSNew){
        //timer
        connect(&rps_timer, SIGNAL(timeout()), this, SLOT(rpsReadRequestNew()));
        connect(&rpsstand_timer, SIGNAL(timeout()), this, SLOT(rpsStandReadRequest()));


        connect(&connect_timer,SIGNAL(timeout()),this, SLOT(rpsStandComAutoconnect()));
        connect_timer.start(1000);
        rpsstand_timer.start(3000);
    }

}

bool modbus_dev::is_opened(){
    return com_openned;
}

modbus_dev::~modbus_dev() {
    qDebug() << "destructor ~modbus_dev()";

    if (modbusDevice)
    {
        modbusDevice->disconnectDevice();
        delete modbusDevice;
        modbusDevice = nullptr;
    }
}

//вернуть номер слота, который нужно опросить
int modbus_dev::get_current_slot(){
    return slave_id;
}

//вернуть номер слота, который нужно опросить
void modbus_dev::set_current_slot(int slot){
    if(slot < STAND_SLOTS_NUM)
        slave_id = slot;
    else
        slave_id = 0;
}

//флаг нужно ли чтение
int modbus_dev::get_read_flag(){
    return read_flag;
}
void modbus_dev::set_read_flag(int flag){
    read_flag = flag;
}

//чтение значений с конкретного слота
void modbus_dev::read_stand_slot(int slot){

    qDebug() << "read_stand_slot" << slot;

    //if (modbusDevice == nullptr)
        //emit syslog("modbusDevice == nullptr", E);

    if(is_connected(slot)){
        qDebug() << "слот подключен, читаем данные";
        switch(get_mb_devtype(slot)){
        case DEV_EL60:
            reply_one = modbusDevice->sendReadRequest(readRequestEL60(),slot+1);
            break;
        case DEV_PS1:
            reply_one = modbusDevice->sendReadRequest(readRequestPS1(),slot+1);
            break;
        case DEV_PS2:
            reply_one = modbusDevice->sendReadRequest(readRequestPS2(),slot+1);
            break;
        case DEV_PS3:
            reply_one = modbusDevice->sendReadRequest(readRequestPS3(),slot+1);
            break;
        case DEV_EL60V5:
            reply_one = modbusDevice->sendReadRequest(readRequestEL60V5(),slot+1);
            break;
        case DEV_IO02:
            reply_one = modbusDevice->sendReadRequest(readRequestIO02(),slot+1);
            break;
        case DEV_SIMBAT24:
            reply_one = modbusDevice->sendReadRequest(readRequestSIMBAT(),slot+1);
            break;
        case DEV_SIMBAT48:
            reply_one = modbusDevice->sendReadRequest(readRequestSIMBAT(),slot+1);
            break;
        }
    }
    else {
        qDebug() << "запрос на определение типа устройства" << slot;
        reply_one = modbusDevice->sendReadRequest(readRequestDevType(),slot+1);
    }

    if (reply_one != nullptr)
    {
        if (!reply_one->isFinished())
        {
            connect(reply_one, SIGNAL(finished()), this, SLOT(readReadyCommon()));
        }
        else
        {
            connected_ok[slot] = 0;
            reply_one->deleteLater();
            //если слот стал не доступен,отправляем сигнал на перерисовку таблички
            emit repaint_slot_table_signal();
        }
    }
    else
    {
        connected_ok[slot] = 0;
        emit syslog(tr("Ошибка чтения: ") + modbusDevice->errorString(), E);
        //если слот стал не доступен,отправляем сигнал на перерисовку таблички
        emit repaint_slot_table_signal();
    }

}

//ищем платы стенда
void modbus_dev::stand_timer_timeout(void){

    if(get_read_flag() == MB_READ_NONE)
        return;

    qDebug() << "stand_timer_timeout" << get_read_flag() << get_current_slot();

    //поиск плат
    if(get_read_flag() == MB_SEARCH){
        if(!is_connected(get_current_slot()))
            read_stand_slot(get_current_slot());
        set_current_slot(get_current_slot()+1);
    }

    //чтение всех плат
    if(get_read_flag() == MB_READ_ALL){
        if(is_connected(get_current_slot()))
            read_stand_slot(get_current_slot());
        set_current_slot(get_current_slot()+1);
        if(get_current_slot() > STAND_SLOTS_NUM)
            set_read_flag(MB_READ_NONE);
    }

    //чтение только одной платы
    if(get_read_flag() == MB_READ_ONE){
        if(is_connected(get_current_slot()))
            read_stand_slot(get_current_slot());
        //set_read_flag(MB_READ_NONE);
    }
}

void modbus_dev::search_timer_timeout(){
    search_timer.stop();
    set_read_flag(MB_READ_NONE);
    emit syslog("Поиск плат стенда завершен",S);
}

void modbus_dev::rpsReadRequestNew(void){

    //qDebug() << "rpsReadRequest";
    if (modbusDevice == nullptr){
        emit syslog("modbusDevice == nullptr", E);
        return;
    }
    if(modbusDevice->state() != QModbusDevice::ConnectedState){
        qDebug() << modbusDevice->state();
        return;
    }

    reply_one = modbusDevice->sendReadRequest(readRequestRPS(),MB_RPS_ADDR );

    if (reply_one != nullptr)
    {
        if (!reply_one->isFinished())
        {
            connect(reply_one, SIGNAL(finished()), this, SLOT(readReadyRPSNew()));
        }
        else
        {
            reply_one->deleteLater();
        }
    }
    else
    {
        emit syslog(tr("Ошибка чтения: ") + modbusDevice->errorString(), E);
    }
    rps_timer.stop();
}
void modbus_dev::rpsStandReadRequest(){
    //qDebug() << "rpsReadRequest";
    if (modbusDevice == nullptr){
        emit syslog("modbusDevice == nullptr", E);
        return;
    }
    if(modbusDevice->state() != QModbusDevice::ConnectedState){
        qDebug() << modbusDevice->state();
        return;
    }

    if(mb_rps_dev_type == DEV_RPS_STAND_V4 || mb_rps_dev_type == DEV_RPS_STAND){
        reply_one = modbusDevice->sendReadRequest(readRequestStandRPS(),MB_RPS_STAND_ADDR );
    }
    else{
        reply_one = modbusDevice->sendReadRequest(readRequestDevType(),MB_RPS_STAND_ADDR );
    }

    if (reply_one != nullptr)
    {
        if (!reply_one->isFinished())
        {
            connect(reply_one, SIGNAL(finished()), this, SLOT(readReadyRPSNew()));
        }
        else
        {
            reply_one->deleteLater();
        }
    }
    else
    {
        emit syslog(tr("Ошибка чтения: ") + modbusDevice->errorString(), E);
    }
}

void modbus_dev::readReadyRPSNew(){
    QString tmp;
    auto reply = qobject_cast<QModbusReply *>(sender());

    qDebug() << "readReadyRPSNew";

    if (reply == nullptr) {
        qDebug() << "readReadyRPS reply Null";
        return;
    }

    if(reply->error() == QModbusDevice::NoError)
    {
        const QModbusDataUnit unit = reply->result();
        if(unit.valueCount())
        {

            for (uint i = 0; i < unit.valueCount(); i++)
            {
                if((unit.startAddress() + i) < MB_MAX_ADDR){
                    modbus_data[unit.startAddress() + i+1] = unit.value(i);
                    qDebug() << unit.startAddress() + i+1 << modbus_data[unit.startAddress() + i+1];
                }
            }

            if(unit.startAddress() == MB_DEV_TYPE){
                mb_rps_dev_type = modbus_data[MB_DEV_TYPE+1];
                qDebug() << "mb_rps_dev_type" << mb_rps_dev_type;

                rps_connected = 1;
                if(mb_rps_dev_type == DEV_RPS_STAND_V4)
                    tmp.sprintf("Подключен RPS-STAND v4");
                else
                    tmp.sprintf("Подключен RPS-STAND v3");
                tmp.append(reply->errorString());
                emit syslog(tmp,C);
                emit rpsStandConnectFinished();
                rpsstand_timer.stop();
            }
            if(unit.startAddress() == 1299){
                emit rpsStandReadStandFinished();
                rpsstand_timer.stop();
            }
            if(unit.startAddress() == 999){
                emit rpsStandReadRPSFinished();
                rps_timer.stop();
            }

        }
    }
    else if (reply->error() == QModbusDevice::ProtocolError)
    {
        rps_connected = 0;
        qDebug() << "Read response error:ProtocolError "<< reply->errorString() << " Mobus exception:" << reply->rawResult().exceptionCode();
    }
    else
    {
        rps_connected = 0;
        tmp.sprintf("Modbus: ошибка ответа");
        tmp.append(reply->errorString());
        emit syslog(tmp,E);

        rps_timer.stop();
        rpsstand_timer.stop();
    }

    reply->deleteLater();
}

void modbus_dev::readReadyCommon()
{
    QString tmp;
    int slot;

    auto reply = qobject_cast<QModbusReply *>(sender());
    if (reply == nullptr) return;

    slot =  reply->serverAddress() - 1;

    if(reply->error() == QModbusDevice::NoError)
    {
        const QModbusDataUnit unit = reply->result();
        if(unit.valueCount())
        {

            qDebug() << "reply" <<  slot;

            for (uint i = 0; i < unit.valueCount(); i++)
            {
                if((unit.startAddress() + i) < MB_MAX_ADDR){
                    modbus_data[unit.startAddress() + i] = unit.value(i);
                    qDebug() << "modbus_data" << i <<  unit.value(i);
                }
            }
            if(unit.startAddress() == MB_DEV_TYPE){
                qDebug() << "mb_dev_type" << modbus_data[MB_DEV_TYPE] << slot;
                mb_dev_type[slot] = modbus_data[MB_DEV_TYPE];
            }


            //если слот стал доступен,отправляем сигнал на перерисовку таблички
            if(connected_ok[slot] == 0){
                connected_ok[slot] = MODBUS_RETRY_NUM;
            }

            //если было чтение конкретных параметров
            if(unit.startAddress() == MB_EL60_CURRENT)
            {
                mb_el60_current[slot] =  modbus_data[MB_EL60_CURRENT];

                mb_el60_voltage[slot] =  modbus_data[MB_EL60_VOLTAGE];

                mb_el60_current_max[slot] =  modbus_data[MB_EL60_MAXCURR];
                mb_el60_voltage_max[slot] =  modbus_data[MB_EL60_MAXVOLT];
                mb_el60_current_min[slot] =  modbus_data[MB_EL60_MINCURR];
                mb_el60_voltage_min[slot] =  modbus_data[MB_EL60_MINVOLT];
                mb_el60_out_pwr[slot] =  modbus_data[MB_EL60_OUT_PWR];
                mb_el60_out_en[slot] =  modbus_data[MB_EL60_OUT_EN];
                mb_el60_temper[slot] = modbus_data[MB_EL60_TEMPER];
                mb_el60_fan_en[slot] =  modbus_data[MB_EL60_FAN_EN];
                mb_el60_fan_err[slot] =  modbus_data[MB_EL60_FAN_ERR];
                mb_el60_fan_pwm[slot] =  modbus_data[MB_EL60_FAN_PWM];
                mb_el60_offline_mode[slot] =  modbus_data[MB_EL60_OFFLINE_EN];
                mb_el60_offline_pwr[slot] =  modbus_data[MB_EL60_OFFLINE_PWR];
                mb_el60_passive_en[slot] =  modbus_data[MB_EL60_PASSIVE_EN];
            }

            if(unit.startAddress() == MB_PS1_AC1_STATE)
            {
                mb_ps1_ac1_state = modbus_data[MB_PS1_AC1_STATE];
                mb_ps1_ac2_state = modbus_data[MB_PS1_AC2_STATE];
                mb_ps1_dc1_state = modbus_data[MB_PS1_SENSOR1_STATE];
                mb_ps1_dc2_state = modbus_data[MB_PS1_SENSOR2_STATE];
                mb_ps1_r_bat = modbus_data[MB_PS1_R_BAT];
                mb_ps1_i_bat = modbus_data[MB_PS1_I_BAT];
                mb_ps1_i_heater = modbus_data[MB_PS1_I_HETAER];
                mb_ps1_voltage = modbus_data[MB_PS1_V_AKB];
                mb_ps1_set_voltage = modbus_data[MB_PS1_V_BAT];
                mb_ps1_current = modbus_data[MB_PS1_I_BAT];
                mb_ps1_current_min = modbus_data[MB_PS1_I_BAT_MIN];
                mb_ps1_current_max = modbus_data[MB_PS1_I_BAT_MAX];
            }

            if(unit.startAddress() == MB_PS2_AC1_STATE)
            {
                mb_ps2_ac1_state = modbus_data[MB_PS2_AC1_STATE];
                mb_ps2_ac2_state = modbus_data[MB_PS2_AC2_STATE];
                mb_ps2_dc1_state = modbus_data[MB_PS2_SENSOR1_STATE];
                mb_ps2_dc2_state = modbus_data[MB_PS2_SENSOR2_STATE];
                mb_ps2_charge_key_state = modbus_data[MB_PS2_CHRG_KEY_STATE];
                mb_ps2_charge_voltage = modbus_data[MB_PS2_CHRG_VOLTAGE];
                mb_ps2_charge_current = modbus_data[MB_PS2_CHRG_CURRENT];
                mb_ps2_charge_voltage_max = modbus_data[MB_PS2_CHRG_VOLTGAGE_MAX];
                mb_ps2_charge_voltage_min = modbus_data[MB_PS2_CHRG_VOLTGAGE_MIN];
                mb_ps2_charge_rload = modbus_data[MB_PS2_CHRG_RLOAD];
                mb_ps2_discharge_key_state = modbus_data[MB_PS2_DISCHRG_KEY_STATE];
                mb_ps2_discharge_voltage = modbus_data[MB_PS2_DISCHRG_VOLTAGE];
                mb_ps2_discharge_current = modbus_data[MB_PS2_DISCHRG_CURRENT];
                mb_ps2_discharge_voltage_max = modbus_data[MB_PS2_DISCHRG_VOLTGAGE_MAX];
                mb_ps2_discharge_voltage_min = modbus_data[MB_PS2_DISCHRG_VOLTGAGE_MIN];
                mb_ps2_heater_relay_state = modbus_data[MB_PS2_HEATER_RELAY_STATE];
                mb_ps2_heater_current = modbus_data[MB_PS2_HEATER_CURRENT];
                mb_ps2_temper = modbus_data[MB_PS2_CURR_TEMPERATURE];
                mb_ps2_max_temper = modbus_data[MB_PS2_MAX_TEMPERATURE];
                mb_ps3_heater2_relay = modbus_data[MB_PS3_HEATER_RELAY2_STATE];
                mb_ps3_heater2_current = modbus_data[MB_PS3_HEATER_RELAY2_CURRENT];

            }
            //для платы EL-60v5
            if(unit.startAddress() == MB_EL60V5_CURRENT_A)
            {
                mb_el60v5_current_a[slot] = modbus_data[MB_EL60V5_CURRENT_A];
                mb_el60v5_current_b[slot] = modbus_data[MB_EL60V5_CURRENT_B];
                mb_el60v5_voltage_a[slot] = modbus_data[MB_EL60V5_VOLTAGE_A];
                mb_el60v5_voltage_b[slot] = modbus_data[MB_EL60V5_VOLTAGE_B];
                mb_el60v5_temper_a[slot] =  modbus_data[MB_EL60V5_TEMPER_A];
                mb_el60v5_temper_b[slot] =  modbus_data[MB_EL60V5_TEMPER_B];
                mb_el60v5_out_en_a[slot] =  modbus_data[MB_EL60V5_OUT_EN_A];
                mb_el60v5_out_en_b[slot] =  modbus_data[MB_EL60V5_OUT_EN_B];
                mb_el60v5_pwr_a[slot] =     modbus_data[MB_EL60V5_OUT_PWR_A];
                mb_el60v5_pwr_b[slot] =     modbus_data[MB_EL60V5_OUT_PWR_B];
            }
            //для платы IO-02
            if(unit.startAddress() == MB_IO02_OUT1)
            {
                mb_io02_input[0] = modbus_data[MB_IO02_IN1];
                mb_io02_input[1] = modbus_data[MB_IO02_IN2];
                mb_io02_input[2] = modbus_data[MB_IO02_IN3];
                mb_io02_input[3] = modbus_data[MB_IO02_IN4];
                mb_io02_input[4] = modbus_data[MB_IO02_IN5];
                mb_io02_input[5] = modbus_data[MB_IO02_IN6];
                mb_io02_input[6] = modbus_data[MB_IO02_IN7];
                mb_io02_input[7] = modbus_data[MB_IO02_IN8];
                mb_io02_input[8] = modbus_data[MB_IO02_IN9];
                mb_io02_input[9] = modbus_data[MB_IO02_IN10];
                mb_io02_input[10] = modbus_data[MB_IO02_IN11];

                mb_io02_output[0] = modbus_data[MB_IO02_OUT1];
                mb_io02_output[1] = modbus_data[MB_IO02_OUT2];
                mb_io02_output[2] = modbus_data[MB_IO02_OUT3];
                mb_io02_output[3] = modbus_data[MB_IO02_OUT4];
                mb_io02_output[4] = modbus_data[MB_IO02_OUT5];
                mb_io02_output[5] = modbus_data[MB_IO02_OUT6];
                mb_io02_output[6] = modbus_data[MB_IO02_OUT7];
            }

            //для плат SIMBAT
            if(unit.startAddress() == MB_SIMBAT_CHARGE_STATE)
            {
                mb_simbat_charge_state = modbus_data[MB_SIMBAT_CHARGE_STATE];
                mb_simbat_charge_voltage = modbus_data[MB_SIMBAT_CHARGE_VOLTAGE];
                mb_simbat_charge_current = modbus_data[MB_SIMBAT_CHARGE_CURRENT];
                mb_simbat_charge_voltage_max = modbus_data[MB_SIMBAT_CHARGE_VOLTAGE_MAX];
                mb_simbat_charge_voltage_min = modbus_data[MB_SIMBAT_CHARGE_VOLTAGE_MIN];
                mb_simbat_charge_rload = modbus_data[MB_SIMBAT_CHARGE_LOAD];
                mb_simbat_discharge_state = modbus_data[MB_SIMBAT_DISCHARGE_STATE];
                mb_simbat_discharge_voltage = modbus_data[MB_SIMBAT_DISCHARGE_VOLTAGE];
                mb_simbat_discharge_current = modbus_data[MB_SIMBAT_DISCHARGE_CURRENT];
                mb_simbat_discharge_voltage_max = modbus_data[MB_SIMBAT_DISCHARGE_VOLTAGE_MAX];
                mb_simbat_discharge_voltage_min = modbus_data[MB_SIMBAT_DISCHARGE_VOLTAGE_MIN];
                mb_simbat_temper1 = modbus_data[MB_SIMBAT_TEMPER1];
                mb_simbat_temper2 = modbus_data[MB_SIMBAT_TEMPER2];
                mb_simbat_max_temper = modbus_data[MB_SIMBAT_MAX_TEMPER];
                mb_simbat_int = modbus_data[MB_SIMBAT_INT];
                mb_simbat_fan = modbus_data[MB_SIMBAT_FAN_STATE];
            }

            if(get_read_flag() == MB_READ_ONE){
                set_read_flag(MB_READ_NONE);
            }
            Sleep(100);
            emit repaint_slot_table_signal();
        }

    }
    else if (reply->error() == QModbusDevice::ProtocolError)
    {
        qDebug() << "Read response error:ProtocolError "<< slot << reply->errorString() << " Mobus exception:" << reply->rawResult().exceptionCode();

        if(connected_ok[slot]){
            tmp.sprintf("Modbus: ошибка формата (%d) ",slot+1);
            tmp.append(reply->errorString());
            emit syslog(tmp,E);
        }
    }
    else
    {
        if(connected_ok[slot]){
            tmp.sprintf("Modbus: ошибка ответа (%d) ",slot+1);
            tmp.append(reply->errorString());
            emit syslog(tmp,E);
        }
    }

    reply->deleteLater();
}


void modbus_dev::mb_update(){
    qDebug() << "start mb_update";
    set_read_flag(MB_READ_ALL);
}

//запись значений в Modbus
void modbus_dev::writeModbus(int id,int addr, int val)
{
    if (modbusDevice==nullptr)
    {
        return;
    }
    if(modbusDevice->state() != QModbusDevice::ConnectedState){
        qDebug() << modbusDevice->state();
        return;
    }

    qDebug() << "writeModbus" << id << addr << val;

    QModbusDataUnit writeUnit = writeRequest(addr);
    writeUnit.setValue(0, val);

    /*reply_write*/
    auto *lastRequest = modbusDevice->sendWriteRequest(writeUnit, id);

    if (lastRequest != nullptr)
    {
        if (!lastRequest->isFinished())
        {
            qDebug() << connect(lastRequest, &QModbusReply::finished, this, &modbus_dev::writeFinished);
        }
        else
        {
            lastRequest->deleteLater();
        }
    }
    else
    {
        emit syslog("Ошибка записи: " + modbusDevice->errorString(), E);
    }
}

void modbus_dev::writeFinished()
{
    qDebug() << "writeFinished";

    auto reply = qobject_cast<QModbusReply *>(sender());

    if (modbusDevice == nullptr){
        qDebug() << "modbusDevice == nullptr";
        return;
    }
    if(modbusDevice->state() != QModbusDevice::ConnectedState){
        qDebug() << modbusDevice->state();
        return;
    }

    if(modbusDevice->error()!=QModbusDevice::NoError){
        qDebug() << "Ошибка modbusDevice: " << modbusDevice->errorString() << modbusDevice->error();
        qDebug() << "Ошибка reply: " << reply->errorString();
    }

    reply->deleteLater();
}

//определение подключенных плат
QModbusDataUnit modbus_dev::readRequestDevType()
{
    qDebug() << "readRequestDevType";

    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = MB_DEV_TYPE;
    int numberOfEntries = 1;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}
//считывание показаний EL-60
QModbusDataUnit modbus_dev::readRequestEL60()
{
    qDebug() << "readRequestEL60";
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = MB_EL60_CURRENT;
    int numberOfEntries = 17;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}
//считывание показаний RPS-01
QModbusDataUnit modbus_dev::readRequestRPS()
{
    //qDebug() << "readRequestRPS";
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = 999;
    int numberOfEntries = 17;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

//считывание показаний автономный стенд RPS-01
QModbusDataUnit modbus_dev::readRequestStandRPS()
{
    //qDebug() << "readRequestStandRPS";
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = 1299;
    int numberOfEntries = 20;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

QModbusDataUnit modbus_dev::writeRequest(int addr){
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = addr;
    int numberOfEntries = 1;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

//считывание показаний PS-1
QModbusDataUnit modbus_dev::readRequestPS1()
{
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = MB_PS1_AC1_STATE;
    int numberOfEntries = 14;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

//считывание показаний PS-2
QModbusDataUnit modbus_dev::readRequestPS2()
{
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = MB_PS2_AC1_STATE;
    int numberOfEntries = 19;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

//считывание показаний PS-3
QModbusDataUnit modbus_dev::readRequestPS3()
{
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = MB_PS2_AC1_STATE;
    int numberOfEntries = 22;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

//считывание показаний EL-60v5
QModbusDataUnit modbus_dev::readRequestEL60V5()
{
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = MB_EL60V5_CURRENT_A;
    int numberOfEntries = 31;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

//считывание показаний IO-02
QModbusDataUnit modbus_dev::readRequestIO02()
{
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = MB_IO02_OUT1;
    int numberOfEntries = 30;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

//считывание показаний SIMBAT24/SIMBAT48
QModbusDataUnit modbus_dev::readRequestSIMBAT()
{
    const auto table =  static_cast<QModbusDataUnit::RegisterType> (QModbusDataUnit::HoldingRegisters);
    int startAddress = MB_SIMBAT_CHARGE_STATE;
    int numberOfEntries = 17;
    return QModbusDataUnit(table, startAddress, numberOfEntries);
}

bool modbus_dev::is_connected(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(connected_ok[slot])
            return 1;
        else
            return 0;
    }
    else
        return 0;
}

int modbus_dev::is_rps_connected(){
    return rps_connected;
}

int modbus_dev::get_mb_devtype(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(slot < 22){
            return mb_dev_type[slot];
        }
        else{
            if(mb_dev_type[slot] == DEV_PS2)
                return DEV_PS2;
            else if(mb_dev_type[slot] == DEV_PS3)
                return DEV_PS3;
            else if(connected_ok[slot])//если хоть что-то подключено
                return DEV_PS1;
        }
    }
    return 0;
}

int modbus_dev::get_mb_rs485rx(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_rs485rx[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_rs485tx(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_rs485tx[slot];
    }
    else
        return 0;
    return 0;
}

//для EL-60
int modbus_dev::get_mb_el60_current(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_current[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_voltage(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot)){
            //qDebug() << "get_mb_el60_voltage" << mb_el60_voltage[slot];
            return mb_el60_voltage[slot];
        }
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_current_max(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_current_max[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_voltage_max(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot)){
            //qDebug() << "get_mb_el60_voltage_max" << mb_el60_voltage_max[slot];
            return mb_el60_voltage_max[slot];
        }
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_current_min(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_current_min[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_voltage_min(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot)){
            //qDebug() << "get_mb_el60_voltage_min" << mb_el60_voltage_min[slot];
            return mb_el60_voltage_min[slot];
        }
    }
    else
        return 0;
    return 0;
}

//очищаем мин/макс значения
void modbus_dev::clear_mb_el60_minmax(int slot){
    qDebug() << "clear_mb_el60_minmax" << slot;
    mb_el60_voltage_min[slot] = 100000;
    mb_el60_voltage_max[slot] = 0;
    writeModbus(slot+1,MB_EL60_CLEAR,1);
}

int modbus_dev::get_mb_el60_out_pwr(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_out_pwr[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_out_en(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_out_en[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_temper(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_temper[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_offline_mode(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_offline_mode[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_offline_current(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_offline_pwr[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60_passive(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60_passive_en[slot];
    }
    else
        return 0;
    return 0;
}

void modbus_dev::set_mb_el60_out_pwr(int slot,int power){
    mb_el60_out_pwr[slot]=power;

    if(power){
        if(get_mb_devtype(slot) == DEV_EL60){
            writeModbus(slot+1,MB_EL60_OUT_PWR,power);
        }
        else if(get_mb_devtype(slot) == DEV_EL60V5){
            writeModbus(slot+1,MB_EL60V5_OUT_PWR_A,power);
            writeModbus(slot+1,MB_EL60V5_OUT_PWR_B,power);
        }
    }
}

void modbus_dev::set_mb_el60_out_en(int slot,int state){
    QString tmp;

    mb_el60_out_en[slot]=state;

    if(get_mb_devtype(slot) == DEV_EL60){
        writeModbus(slot+1,MB_EL60_OUT_EN,state);
    }
    else if(get_mb_devtype(slot) == DEV_EL60V5){
        writeModbus(slot+1,MB_EL60V5_OUT_EN_A,state);
        writeModbus(slot+1,MB_EL60V5_OUT_EN_B,state);
    }
}

void modbus_dev::set_mb_el60_passive(int slot,int state){
    QString tmp;
    emit syslog(tmp.sprintf("Реле байпаса для пассивного PoE: канал %d, состояние %d",slot+1,state),I);
    if(get_mb_devtype(slot) == DEV_EL60){
        writeModbus(slot+1,MB_EL60_PASSIVE_EN,state);
    }
    else if(get_mb_devtype(slot) == DEV_EL60V5){
        writeModbus(slot+1,MB_EL60V5_PASSIVE_EN_A,state);
        writeModbus(slot+1,MB_EL60V5_PASSIVE_EN_B,state);
    }
}

//для EL-60v5
int modbus_dev::get_mb_el60v5_current_a(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_current_a[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_current_b(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_current_b[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_voltage_a(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_voltage_a[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_voltage_b(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_voltage_b[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_temper_a(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_temper_a[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_temper_b(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_temper_b[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_out_en_a(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_out_en_a[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_out_en_b(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_out_en_b[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_out_pwr_a(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_pwr_a[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::get_mb_el60v5_out_pwr_b(int slot){
    if(slot < STAND_SLOTS_NUM){
        if(is_connected(slot))
            return mb_el60v5_pwr_b[slot];
    }
    else
        return 0;
    return 0;
}

int modbus_dev::is_simbat24_connected()
{
    for(int i = 0; i < STAND_SLOTS_NUM; i++)
    {
        if(is_connected(i) && mb_dev_type[i] == DEV_SIMBAT24)
            return 1;

    }
    return 0;
}

int modbus_dev::is_simbat48_connected()
{
    for(int i = 0; i < STAND_SLOTS_NUM; i++)
    {
        if(is_connected(i) && mb_dev_type[i] == DEV_SIMBAT48)
            return 1;

    }
    return 0;
}

int modbus_dev::get_simbat24_slot(){
    for(int i=0;i<STAND_SLOTS_NUM;i++){
        if(mb_dev_type[i] == DEV_SIMBAT24)
            return i;
    }
    return 0;
}

int modbus_dev::get_simbat48_slot(){
    for(int i=0;i<STAND_SLOTS_NUM;i++){
        if(mb_dev_type[i] == DEV_SIMBAT48)
            return i;
    }
    return 0;
}

//io-02
int modbus_dev::io02_is_connected(){
    for(int i=0;i<STAND_SLOTS_NUM;i++){
        if(mb_dev_type[i] == DEV_IO02)
            return 1;
    }
    return 0;
}

//возвращаем номер слота
int modbus_dev::get_io02_slot(){
    for(int i=0;i<STAND_SLOTS_NUM;i++){
        if(mb_dev_type[i] == DEV_IO02)
            return i;
    }
    return 0;
}

int modbus_dev::get_mb_io02_input(int input){
    return mb_io02_input[input];
}

void modbus_dev::set_mb_io02_out(int output,int state){
    int slot;
    if(io02_is_connected()){
        slot = get_io02_slot();
        writeModbus(slot+1,MB_IO02_OUT1+output,state);
    }
}

void modbus_dev::set_mb_io02_in(int input,int state){
    int slot;
    if(io02_is_connected()){
        slot = get_io02_slot();
        writeModbus(slot+1,MB_IO02_IN1+input,state);
    }
}

int modbus_dev::get_mb_io02_output(int output){
    return mb_io02_output[output];
}

//очищаем мин/макс значения
void modbus_dev::clear_mb_ps1_minmax(){
    mb_ps1_current = 0;
    mb_ps1_voltage = 0;
    mb_ps1_current_min = 100000;
    mb_ps1_current_max = 0;
    writeModbus(PS1_ADDR+1,MB_PS1_CLEAR,1);
}

int modbus_dev::get_mb_ps1_voltage(void){
    if(is_connected(PS1_ADDR) && get_mb_devtype(PS1_ADDR) == DEV_PS1 ){
        qDebug() << "get_mb_ps1_voltage" << mb_ps1_voltage;
        return mb_ps1_voltage;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps1_current(void){
    if(is_connected(PS1_ADDR) && get_mb_devtype(PS1_ADDR) == DEV_PS1){
        qDebug() << "get_mb_ps1_current" << mb_ps1_current;
        return mb_ps1_current;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps1_current_min(void){
    if(is_connected(PS1_ADDR)){
        qDebug() << "get_mb_ps1_current_min" << mb_ps1_current_min;
        return mb_ps1_current_min;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps1_current_max(void){
    if(is_connected(PS1_ADDR) && get_mb_devtype(PS1_ADDR) == DEV_PS1){
        qDebug() << "get_mb_ps1_current_max" << mb_ps1_current_max;
        return mb_ps1_current_max;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps1_set_voltage(void){

    if(is_connected(PS1_ADDR) && get_mb_devtype(PS1_ADDR) == DEV_PS1){
        qDebug() << "get_mb_ps1_setvoltage" << mb_ps1_set_voltage;
        return mb_ps1_set_voltage;
    }
    else
        return 0;
}

int modbus_dev::get_ps1_heating_current(void){
    if(is_connected(PS1_ADDR) && get_mb_devtype(PS1_ADDR) == DEV_PS1){
        qDebug() << "get_ps1_heating_current" << mb_ps1_i_heater;
        return mb_ps1_i_heater;
    }
    else
        return 0;
}

//плата PS-2
int modbus_dev::get_mb_ps2_ac1_state(void){
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2){
        qDebug() << "get_mb_ps2_ac1_satate" << mb_ps2_ac1_state;
        return mb_ps2_ac1_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_ac2_state(void){
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2){
        qDebug() << "get_mb_ps2_a21_satate" << mb_ps2_ac2_state;
        return mb_ps2_ac2_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_sensor1_state(void){
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2){
        qDebug() << "mb_ps2_dc1_state" << mb_ps2_dc1_state;
        return mb_ps2_dc1_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_sensor2_state(void){
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2){
        qDebug() << "mb_ps2_dc2_state" << mb_ps2_dc2_state;
        return mb_ps2_dc2_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_charge_key_state(void){
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2){
        qDebug() << "mb_ps2_charge_key_state" << mb_ps2_charge_key_state;
        return mb_ps2_charge_key_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_charge_voltage(void){
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2){
        qDebug() << "mb_ps2_charge_voltage" << mb_ps2_charge_voltage;
        return mb_ps2_charge_voltage;
    }
    else
        return 0;
}

int modbus_dev::get_mb_charge_voltage(void)
{
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2 && SimbatType::NotConnected){
        qDebug() << "mb_ps2_charge_voltage" << mb_ps2_charge_voltage;
        return mb_ps2_charge_voltage;
    }
    else
        if(is_simbat24_connected() && get_simbat_type() == SimbatType::SIMBAT24){
            qDebug() << "mb_simbat24_charge_voltage" << mb_simbat_charge_voltage;
            return mb_simbat_charge_voltage;
        } else
            if(is_simbat48_connected() && get_simbat_type() == SimbatType::SIMBAT48){
                qDebug() << "mb_simbat48_charge_voltage" << mb_simbat_charge_voltage;

                return mb_simbat_charge_voltage;
            }else
                return 0;
}

int modbus_dev::get_mb_ps2_charge_current(void){
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2){
        qDebug() << "mb_ps2_charge_current" << mb_ps2_charge_current;
        return mb_ps2_charge_current;
    }
    else
        return 0;
}

int modbus_dev::get_mb_charge_current(void)
{
    if(is_connected(PS2_ADDR) && get_mb_devtype(PS2_ADDR) == DEV_PS2){
        qDebug() << "mb_ps2_charge_current" << mb_ps2_charge_current;
        return mb_ps2_charge_current;
    }else
        if(is_simbat24_connected() && get_simbat_type() == SimbatType::SIMBAT24){
            qDebug() << "mb_simbat24_charge_current" << mb_simbat_charge_current;
            return mb_simbat_charge_current;
        }else
            if(is_simbat48_connected() && get_simbat_type() == SimbatType::SIMBAT48){
                qDebug() << "mb_simbat48_charge_current" << mb_simbat_charge_current;
                return mb_simbat_charge_current;
            }
            else
                return 0;
}

int modbus_dev::get_mb_ps2_charge_voltage_min(void){
    if(is_connected(PS2_ADDR) ){
        qDebug() << "mb_ps2_charge_voltage_min" << mb_ps2_charge_voltage_min;
        return mb_ps2_charge_voltage_min;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_charge_voltage_max(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_charge_voltage_max" << mb_ps2_charge_voltage_max;
        return mb_ps2_charge_voltage_min;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_charge_rload(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_charge_rload" << mb_ps2_charge_rload;
        return mb_ps2_charge_rload;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_discharge_key_state(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_discharge_key_state" << mb_ps2_discharge_key_state;
        return mb_ps2_discharge_key_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_discharge_voltage(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_discharge_voltage" << mb_ps2_discharge_voltage;
        return mb_ps2_discharge_voltage;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_discharge_current(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_discharge_current" << mb_ps2_discharge_current;
        return mb_ps2_discharge_current;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_discharge_voltage_min(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_discharge_voltage_min" << mb_ps2_discharge_voltage_min;
        return mb_ps2_discharge_voltage_min;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_discharge_voltage_max(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_discharge_voltage_max" << mb_ps2_discharge_voltage_max;
        return mb_ps2_discharge_voltage_max;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_heater_relay_state(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_heater_relay_state" << mb_ps2_heater_relay_state;
        return mb_ps2_heater_relay_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_heater_current(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_heater_current" << mb_ps2_heater_current;
        return mb_ps2_heater_current;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_heater_current2(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps3_heater_current" << mb_ps3_heater2_current;
        return mb_ps3_heater2_current;
    }
    else
        return 0;
}


int modbus_dev::get_mb_ps2_temper(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_temper" << mb_ps2_temper;
        return mb_ps2_temper;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps2_max_temper(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "mb_ps2_max_temper" << mb_ps2_max_temper;
        return mb_ps2_max_temper;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps3_ac1_state(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "get_mb_ps3_ac1_state" << mb_ps1_ac1_state;
        return mb_ps1_ac1_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps3_ac2_state(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "get_mb_ps3_ac2_state" << mb_ps1_ac2_state;
        return mb_ps1_ac2_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps3_heater1_relay(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "get_mb_ps3_heater1_relay" << mb_ps2_heater_relay_state;
        return mb_ps2_heater_relay_state;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps3_heater2_relay(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "get_mb_ps3_heater2_relay" << mb_ps3_heater2_relay;
        return mb_ps3_heater2_relay;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps3_heater1_current(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "get_mb_ps3_heater1_current" << mb_ps2_heater_current;
        return mb_ps2_heater_current;
    }
    else
        return 0;
}

int modbus_dev::get_mb_ps3_heater2_current(void){
    if(is_connected(PS2_ADDR)){
        qDebug() << "get_mb_ps3_heater2_current" << mb_ps3_heater2_current;
        return mb_ps3_heater2_current;
    }
    else
        return 0;
}

//SIMBAT
int modbus_dev::get_mb_simbat_charge_state(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_charge_state" << mb_simbat_charge_state;
            return mb_simbat_charge_state;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_charge_voltage(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_charge_voltage" << mb_simbat_charge_voltage;
            return mb_simbat_charge_voltage;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_charge_current(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_charge_current" << mb_simbat_charge_current;
            return mb_simbat_charge_current;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_charge_voltage_min(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_charge_voltage_min" << mb_simbat_charge_voltage_min;
            return mb_simbat_charge_voltage_min;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_charge_voltage_max(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_charge_voltage_max" << mb_simbat_charge_voltage_max;
            return mb_simbat_charge_voltage_max;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_discharge_state(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_discharge_state" << mb_simbat_discharge_state;
            return mb_simbat_discharge_state;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_discharge_voltage(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_discharge_voltage" << mb_simbat_discharge_voltage;
            return mb_simbat_discharge_voltage;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_discharge_current(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_discharge_current" << mb_simbat_discharge_current;
            return mb_simbat_discharge_current;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_discharge_voltage_min(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_discharge_voltage_min" << mb_simbat_discharge_voltage_min;
            return mb_simbat_discharge_voltage_min;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_discharge_voltage_max(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_discharge_voltage_max" << mb_simbat_discharge_voltage_max;
            return mb_simbat_discharge_voltage_max;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_charge_rload(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_charge_rload" << mb_simbat_charge_rload;
            return mb_simbat_charge_rload;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_temper1(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_temper1" << mb_simbat_temper1;
            return mb_simbat_temper1;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_temper2(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_temper2" << mb_simbat_temper2;
            return mb_simbat_temper2;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_max_temper(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_max_temper" << mb_simbat_max_temper;
            return mb_simbat_max_temper;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_int(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_int" << mb_simbat_int;
            return mb_simbat_int;
        }
    else
        return 0;
}

int modbus_dev::get_mb_simbat_fan(int slot){
        if(is_connected(slot) && slot < STAND_SLOTS_NUM){
            qDebug() << "mb_simbat_fan" << mb_simbat_fan;
            return mb_simbat_fan;
        }
    else
        return 0;
}

void modbus_dev::com_connect(){
    modbusDevice->connectDevice();
}

void modbus_dev::com_disconnect(){
    modbusDevice->disconnectDevice();
}

//переподключение к стенду PSW в случае ошибок связи
void modbus_dev::standComAutoconnect(){


    if (autoconnect && (modbusDevice->state() == QModbusDevice::UnconnectedState
                        || modbusDevice->error() == QModbusDevice::ConnectionError)){


        emit syslog("Ошибка открытия COM порта стенда, переподключение...",E);

        modbusDevice->disconnectDevice();
        modbusDevice->deleteLater();


        modbusDevice = new QModbusRtuSerialMaster();
        modbusDevice->setConnectionParameter(QModbusDevice::SerialPortNameParameter,modbus_port);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialParityParameter,  QSerialPort::NoParity);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialBaudRateParameter,QSerialPort::Baud9600);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialDataBitsParameter,QSerialPort::Data8);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialStopBitsParameter,QSerialPort::OneStop);
        modbusDevice->setTimeout(30);
        modbusDevice->setNumberOfRetries(0);

        com_openned = modbusDevice->connectDevice();
        if(com_openned)
            emit syslog("COM порта стенда открыт",I);
    }
}

//переподключение к стенду RPS в случае ошибок связи
void modbus_dev::rpsStandComAutoconnect(){

    if (autoconnect && (modbusDevice->state() == QModbusDevice::UnconnectedState
                        || modbusDevice->error() == QModbusDevice::ConnectionError)){

        qDebug() << "rpsStandComAutoconnect" << modbusDevice->state() <<modbusDevice->error() << modbusDevice->errorString().length() << modbusDevice->errorString();

        emit syslog("Ошибка открытия COM порта стенда, переподключение...",E);

        modbusDevice->disconnectDevice();
        modbusDevice->deleteLater();


        modbusDevice = new QModbusRtuSerialMaster();
        modbusDevice->setConnectionParameter(QModbusDevice::SerialPortNameParameter,modbus_port);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialParityParameter,  QSerialPort::NoParity);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialBaudRateParameter,QSerialPort::Baud4800);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialDataBitsParameter,QSerialPort::Data8);
        modbusDevice->setConnectionParameter(QModbusDevice::SerialStopBitsParameter,QSerialPort::OneStop);
        modbusDevice->setTimeout(30);
        modbusDevice->setNumberOfRetries(0);

        com_openned = modbusDevice->connectDevice();
        if(com_openned){
            emit syslog("COM порт стенда открыт",I);
            rps_timer.start(3000);
        }
    }
}
