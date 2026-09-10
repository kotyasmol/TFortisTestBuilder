#include "ui_mainwindow.h"
#include "mainwindow.h"


//команды управления стендом по ModbusRTU

void MainWindow::modbus_set_current(){
    set_el60_current(ui->modbus_id->currentIndex(),ui->modbus_curr->value()*1000);
}

void MainWindow::modbus_set_current_all(){
    for(int i=0;i<STAND_SLOTS_NUM;i++){
        if(pTestThread->dev->get_mb_devtype(i)==DEV_EL60 ||
                pTestThread->dev->get_mb_devtype(i)==DEV_EL60V5)
            set_el60_current(i,ui->modbus_curr->value()*1000);
    }
}



void MainWindow::stand_ac1(){
    static bool state = 1;
    if(state){
        stand_ac1_on();
        state = 0;
    }
    else
    {
        stand_ac1_off();
        state = 1;
    }
}

void MainWindow::stand_ac2(){
    static bool state = 1;
    if(state){
        stand_ac2_on();
        state = 0;
    }
    else
    {
        stand_ac2_off();
        state = 1;
    }
}

void MainWindow::stand_dc1(){
    static bool state = 1;
    if(state){
        stand_dc1_on();
        state = 0;
    }
    else
    {
        stand_dc1_off();
        state = 1;
    }
}

void MainWindow::stand_dc2(){
    static bool state = 1;
    if(state){
        stand_dc2_on();
        state = 0;
    }
    else
    {
        stand_dc2_off();
        state = 1;
    }
}

//по нажатию кнопки
void MainWindow::stand_akb(){
    static bool state = 1;
    if(state){
        stand_akb_on();
        state = 0;
    }
    else
    {
        stand_akb_off();
        state = 1;
    }
}

void MainWindow::stand_all_off(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){
        ui->modbus_set_ac1->setText("Включить AC1");
        ui->modbus_set_ac2->setText("Включить AC2");

        if (pTestThread == nullptr)
            return;
        pTestThread->StandOff();
    }
}

void MainWindow::stand_ac1_on(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){


        if(prog_sett.stand_type == StandType::typeAPK03){
            if(pTestThread->dev->is_connected(PS1_ADDR)){
                //на вкладке Stand
                ui->modbus_set_ac1->setText("Выключить AC1");
                ui->modbus_set_ac1->setStyleSheet("QPushButton { background-color: grey; }");
                //на вкладке Программатор
                ui->prog_set_ac1->setText("Выключить AC1");
                ui->prog_set_ac1->setStyleSheet("QPushButton { background-color: grey; }");
                pTestThread->dev->set_stand_ac1(1);
                syslog("Включение AC1", I);
            }
            else
                syslog("Плата PS не подключена",E);
        }
        else
        {
            syslog("Стенд не подключен",E);

        }
    }
}

void MainWindow::stand_ac1_off(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){


        if(prog_sett.stand_type == StandType::typeAPK03){
            if(pTestThread->dev->is_connected(PS1_ADDR)){
                ui->modbus_set_ac1->setText("Включить AC1");
                ui->modbus_set_ac1->setStyleSheet("");
                ui->prog_set_ac1->setText("Включить AC1");
                ui->prog_set_ac1->setStyleSheet("");
                pTestThread->dev->set_stand_ac1(0);
                syslog("Отключение AC1", I);
            }
            else
                syslog("Плата PS не подключена",E);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);

    }
}

void MainWindow::stand_ac2_on(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){

        if(prog_sett.stand_type == StandType::typeAPK03){
            if(pTestThread->dev->is_connected(PS1_ADDR)){
                ui->modbus_set_ac2->setText("Выключить AC2");
                ui->modbus_set_ac2->setStyleSheet("QPushButton { background-color: grey; }");
                pTestThread->dev->set_stand_ac2(1);
                syslog("Включение AC2", I);
            }
            else
                syslog("Плата PS не подключена",E);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);

    }
}

void MainWindow::stand_ac2_off(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){

        if(prog_sett.stand_type == StandType::typeAPK03){
            if(pTestThread->dev->is_connected(PS1_ADDR)){
                ui->modbus_set_ac2->setText("Включить AC2");
                ui->modbus_set_ac2->setStyleSheet("");
                pTestThread->dev->set_stand_ac2(0);
                syslog("Отключение AC2", I);
            }
            else
                syslog("Плата PS не подключена",E);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);

    }
}

void MainWindow::stand_dc1_on(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){

        if(prog_sett.stand_type == StandType::typeAPK03){
            ui->modbus_set_dc1->setText("Выключить Sensor1");
            ui->modbus_set_dc1->setStyleSheet("QPushButton { background-color: grey; }");
            syslog("Включение Sensor1", I);
            pTestThread->dev->set_stand_dc1(1);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);

    }
}

void MainWindow::stand_dc1_off(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){

        if(prog_sett.stand_type == StandType::typeAPK03){
            ui->modbus_set_dc1->setText("Включить Sensor1");
            ui->modbus_set_dc1->setStyleSheet("");
            syslog("Выключение Sensor1", I);
            pTestThread->dev->set_stand_dc1(0);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);

    }
}

void MainWindow::stand_dc2_on(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){


        if(prog_sett.stand_type == StandType::typeAPK03){
            pTestThread->dev->set_stand_dc2(1);
            ui->modbus_set_dc2->setText("Выключить Sensor2");
            ui->modbus_set_dc2->setStyleSheet("QPushButton { background-color: grey; }");
            syslog("Включение Sensor2", I);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);
    }
}

void MainWindow::stand_dc2_off(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){

        if(prog_sett.stand_type == StandType::typeAPK03){
            pTestThread->dev->set_stand_dc2(0);
            ui->modbus_set_dc2->setText("Включить Sensor2");
            ui->modbus_set_dc2->setStyleSheet("");
            syslog("Выключение Sensor2", I);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);
    }
}

void MainWindow::stand_akb_on(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){
        if(prog_sett.stand_type == StandType::typeAPK03){
            if(pTestThread->dev->is_connected(PS1_ADDR)){
                pTestThread->dev->set_stand_akb(1);
                syslog("Включение АКБ", I);
            }
            else
                syslog("Плата PS-1 не подключена",E);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);
    }
}

void MainWindow::stand_akb_off(){
    if(prog_sett.stand_type == StandType::typeAPK03 || prog_sett.stand_type == StandType::typeOld){
        if(prog_sett.stand_type == StandType::typeAPK03){
            if(pTestThread->dev->is_connected(PS1_ADDR)){
                pTestThread->dev->set_stand_akb(0);
                syslog("Выключение АКБ", I);
            }
            else
                syslog("Плата PS-1 не подключена",E);
        }
    }
    else
    {
        syslog("Стенд не подключен",E);

    }
}

void MainWindow::stand_akb_rload_on(){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->get_mb_devtype(PS1_ADDR)==DEV_PS1){
            pTestThread->dev->set_ps1_rload(1);
            syslog("PS-1: Подключение нагрузочного резистора",I);
        }
    }
}

void MainWindow::stand_akb_rload_off(){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->get_mb_devtype(PS1_ADDR)==DEV_PS1){
            pTestThread->dev->set_ps1_rload(0);
            syslog("PS-1: Отключение нагрузочного резистора",I);
        }
    }
}

void MainWindow::stand_akb_direction(){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->get_mb_devtype(PS1_ADDR)==DEV_PS1){
            pTestThread->dev->set_ps1_measurement_direction(2);
        }
    }
}

void MainWindow::mb_clear_minmax(int slot){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->get_mb_devtype(PS1_ADDR)==DEV_EL60){
            pTestThread->dev->clear_mb_el60_minmax(slot);
        }
    }
}

void MainWindow::mb_set_el60_out(int slot,int state){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->get_mb_devtype(slot)==DEV_EL60){
            pTestThread->dev->set_mb_el60_out_en(slot,state);
        }
        else{
            pTestThread->dev->set_mb_el60_out_en(slot,state);
        }
    }
}

void MainWindow::mb_set_el60_power(int slot,int power){
    if(prog_sett.stand_type == StandType::typeAPK03)
        pTestThread->dev->set_mb_el60_out_pwr(slot,power);
}

//установка нагрузки в цикле
void MainWindow::mb_set_el60_power_to_all(int power)
{
    if(prog_sett.stand_type == StandType::typeAPK03)
        for(int i = 0; i<NUM_POE_LINES; i++)
        {
            pTestThread->dev->set_mb_el60_out_pwr(i,power);
        }

}

//установка режима пассивного PoE
void MainWindow::mb_set_el60_passive(int slot,int state){
    if(prog_sett.stand_type == StandType::typeAPK03)
        pTestThread->dev->set_mb_el60_passive(slot,state);
}

void MainWindow::set_stand_charge_key(int state)
{
    int type = 0;
    if(prog_sett.stand_type == StandType::typeAPK03)
    {
        if(SimbatType::SIMBAT24){type = 1;}else{type = 2;}
        pTestThread->dev->set_stand_charge_key(state);
        if(state){
            if(type == 1)
              {
                ui->modbus_simbat24_charge_btn->setStyleSheet("QPushButton { background-color: grey; }");
            }
            else
            {
            ui->modbus_simbat48_charge_btn->setStyleSheet("QPushButton { background-color: grey; }");
             }
        }
        else{
            if(type == 1)
            {
                ui->modbus_simbat24_charge_btn->setStyleSheet("");
            }
            else
            {
            ui->modbus_simbat48_charge_btn->setStyleSheet("");
            }
        }
    }
}

void MainWindow::set_stand_discharge_key(int state)
{
    int type = 0;
    if(prog_sett.stand_type == StandType::typeAPK03)
    {
        if(SimbatType::SIMBAT24){type = 1;}else{type = 2;}
        pTestThread->dev->set_stand_discharge_key(state);
        if(state){
            if(type == 1)
            {
                ui->modbus_simbat24_discharge_btn->setStyleSheet("QPushButton { background-color: grey; }");
            }
            else
            {
                ui->modbus_simbat48_discharge_btn->setStyleSheet("QPushButton { background-color: grey; }");
            }
        }
        else{
            if(type == 1)
            {
                ui->modbus_simbat24_discharge_btn->setStyleSheet("");
            }
            else
            {
                ui->modbus_simbat48_discharge_btn->setStyleSheet("");
             }

        }
    }
}

void MainWindow::set_stand_charge_rload(int rload)
{
    QString tmp;
    tmp.sprintf("PS-2: установка нагрузки %d",rload);
    syslog(tmp, I);
    if(prog_sett.stand_type == StandType::typeAPK03)
        pTestThread->dev->set_stand_charge_rload(rload);
}

void MainWindow::set_stand_heater1_relay(int state)
{
    if(prog_sett.stand_type == StandType::typeAPK03)
    {
        pTestThread->dev->set_stand_heater1_relay(state);
    }
}

void MainWindow::set_stand_heater2_relay(int state)
{
    if(prog_sett.stand_type == StandType::typeAPK03)
    {
        pTestThread->dev->set_stand_heater2_relay(state);
    }
}

void MainWindow::set_stand_max_temper(int temper)
{
    if(prog_sett.stand_type == StandType::typeAPK03)
        pTestThread->dev->set_stand_max_temper(temper);
}
void MainWindow::clear_mb_ps2_minmax(){
    if(prog_sett.stand_type == StandType::typeAPK03)
        pTestThread->dev->clear_mb_ps2_minmax();
}

void MainWindow::simbat24_charge_relay(){

    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_simbat24_connected()){
            pTestThread->dev->set_simbat_type(SimbatType::SIMBAT24);
            if(state){
                pTestThread->dev->set_stand_charge_key(1);
                ui->modbus_simbat24_charge_btn->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("Simbat-24: включение ключа Зарядки", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_stand_charge_key(0);
                ui->modbus_simbat24_charge_btn->setStyleSheet("");
                syslog("Simbat-24: выключение ключа Зарядки", I);
                state = 1;
            }
        }
        else
            syslog("Плата Simbat-24 не подключена",E);
    }
    else
        syslog("Плата Simbat-24 не подключена",E);
}

void MainWindow::simbat48_charge_relay(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_simbat48_connected()){
            pTestThread->dev->set_simbat_type(SimbatType::SIMBAT48);
            if(state){
                pTestThread->dev->set_stand_charge_key(1);
                ui->modbus_simbat48_charge_btn->setStyleSheet("QPushButton { background-color: grey; }");
                 syslog("Simbat-48: включение ключа Зарядки", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_stand_charge_key(0);
                ui->modbus_simbat48_charge_btn->setStyleSheet("");
                 syslog("Simbat-48: выключение ключа Зарядки", I);
                state = 1;
            }
        }
        else
             syslog("Плата Simbat-48 не подключена",E);
    }
    else
         syslog("Плата Simbat-48 не подключена",E);
}

void MainWindow::simbat24_discharge_relay(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_simbat24_connected()){
            pTestThread->dev->set_simbat_type(SimbatType::SIMBAT24);
            if(state){
                pTestThread->dev->set_stand_discharge_key(1);
                ui->modbus_simbat24_discharge_btn->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("Simbat-24: включение ключа Разрядки", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_stand_discharge_key(0);
                ui->modbus_simbat24_discharge_btn->setStyleSheet("");
                syslog("Simbat-24: выключение ключа Разрядки", I);
                state = 1;
            }
        }
        else
            syslog("Плата Simbat-24 не подключена",E);
    }
    else
        syslog("Плата Simbat-24 не подключена",E);
}

void MainWindow::simbat48_discharge_relay(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_simbat48_connected()){
            pTestThread->dev->set_simbat_type(SimbatType::SIMBAT48);
            if(state){
                pTestThread->dev->set_stand_discharge_key(1);
                ui->modbus_simbat48_discharge_btn->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("Simbat-48: включение ключа Разрядки", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_stand_discharge_key(0);
                ui->modbus_simbat48_discharge_btn->setStyleSheet("");
                syslog("Simbat-48: выключение ключа Разрядки", I);
                state = 1;
            }
        }
        else
            syslog("Плата Simbat-48 не подключена",E);
    }
    else
        syslog("Плата Simbat-48 не подключена",E);
}

void MainWindow::stand_poe_load_on(){

    QString tmp;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_connected(ui->modbus_id->currentIndex())){
            tmp.sprintf("EL-60: включение Нагрузки, слот %d",ui->modbus_id->currentIndex()+1);
            pTestThread->dev->set_mb_el60_out_en(ui->modbus_id->currentIndex(),1);
            syslog(tmp, I);
        }
        else
            syslog("Плата EL-60 не подключена",E);
    }

}

void MainWindow::stand_poe_load_off(){

    QString tmp;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_connected(ui->modbus_id->currentIndex())){
            tmp.sprintf("EL-60: выключение Нагрузки, слот %d",ui->modbus_id->currentIndex()+1);
            pTestThread->dev->set_mb_el60_out_en(ui->modbus_id->currentIndex(),0);
            syslog(tmp, I);
        }
        else
            syslog("Плата EL-60 не подключена",E);
    }
}

void MainWindow::stand_heater1_relay(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_connected(PS2_ADDR)){
            if(state){
                pTestThread->dev->set_stand_heater1_relay(1);
                ui->modbus_ps2_heater1_relay_pb->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("PS-2: включение реле проверки нагревателей", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_stand_heater1_relay(0);
                ui->modbus_ps2_heater1_relay_pb->setStyleSheet("");
                syslog("PS-2: выключение реле проверки нагревателей", I);
                state = 1;
            }
        }
        else
            syslog("Плата PS-2 не подключена",E);
    }
    else
        syslog("Плата PS-2 не подключена",E);
}

void MainWindow::stand_heater2_relay(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_connected(PS3_ADDR)){
            if(state){
                pTestThread->dev->set_stand_heater2_relay(1);
                ui->modbus_ps2_heater_relay2_pb->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("PS-3: включение реле проверки нагревателей", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_stand_heater2_relay(0);
                ui->modbus_ps2_heater_relay2_pb->setStyleSheet("");
                syslog("PS-3: выключение реле проверки нагревателей", I);
                state = 1;
            }
        }
        else
            syslog("Плата PS-3 не подключена",E);
    }
    else
        syslog("Плата PS-3 не подключена",E);
}

void MainWindow::stand_ps2_rload(void){
    QString tmp;
    int rload;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_connected(PS2_ADDR)){
            rload = ui->modbus_ps2_rload_value->value();
            tmp.sprintf("PS-2: установка сопротивления %d Ом",rload);
            pTestThread->dev->set_stand_charge_rload(rload);
            syslog(tmp, I);
        }
        else
            syslog("Плата PS-2 не подключена",E);
    }
    else
        syslog("Плата PS-2 не подключена",E);
}

void MainWindow::stand_simbat24_rload(void){
    QString tmp;
    int rload;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_simbat24_connected()){
            rload = ui->modbus_ps2_rload_value->value();
            tmp.sprintf("Simbat-24: установка сопротивления %d Ом",rload);
            pTestThread->dev->set_stand_charge_rload(rload);
            syslog(tmp, I);
        }
        else
            syslog("Плата Simbat-24 не подключена",E);
    }
    else
        syslog("Плата Simbat-24 не подключена",E);
}

void MainWindow::stand_simbat48_rload(void){
    QString tmp;
    int rload;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_simbat48_connected()){
            rload = ui->modbus_ps2_rload_value_2->value();
            tmp.sprintf("Simbat-48: установка сопротивления %d Ом",rload);
            pTestThread->dev->set_stand_charge_rload(rload);
            syslog(tmp, I);
        }
        else
            syslog("Плата Simbat-48 не подключена",E);
    }
    else
        syslog("Плата Simbat-48 не подключена",E);
}

//установка выходного тока в нагрузке для платы EL-60
void MainWindow::set_el60_current(int addr, int current){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_connected(addr)){
            mb_set_el60_power(addr,current);
        }
        else{
            qDebug() << addr << "is not connected";
        }
    }
}

//установка напряжения имитатора АКБ для платы PS-1
void MainWindow::set_ps1_voltage(int addr, int voltage){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->is_connected(addr)){
            pTestThread->dev->writeModbus(PS1_ADDR+1,MB_PS1_V_BAT,voltage*1000);
        }
        else{
            qDebug() << addr << "is not connected";
        }
    }
}

//разрешение работы с PassivePoE для платы EL-60
void MainWindow::set_el60_passive(int addr, int state){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(state == 0 || state == 1){
            if(pTestThread->dev->is_connected(addr))
                pTestThread->dev->writeModbus(addr+1,MB_EL60_PASSIVE_EN,state);
        }
    }
}

//PS-1 выход AC1
void modbus_dev::set_stand_ac1(int state){
    if(state == 0 || state == 1){
        if(is_connected(PS1_ADDR)){
            if(get_mb_devtype(PS1_ADDR)==DEV_PS1 ){
                qDebug() << "set_stand_ac1" << state;
                writeModbus(PS1_ADDR+1,MB_PS1_AC1_STATE,state);


            }
        }
        if(is_connected(PS2_ADDR)){
            if(get_mb_devtype(PS2_ADDR)==DEV_PS2 || get_mb_devtype(PS1_ADDR)==DEV_PS3){
                qDebug() << "set_stand_ac1" << state;
                writeModbus(PS2_ADDR+1,MB_PS2_AC1_STATE,state);
            }
        }
    }
}

//PS-1  выход AC2
void modbus_dev::set_stand_ac2(int state){
    if(state == 0 || state == 1){
        if(is_connected(PS1_ADDR)){
            if(get_mb_devtype(PS1_ADDR)==DEV_PS1){
                writeModbus(PS1_ADDR+1,MB_PS1_AC2_STATE,state);
            }
        }
        if(is_connected(PS2_ADDR)){
            if(get_mb_devtype(PS2_ADDR)==DEV_PS2 || get_mb_devtype(PS1_ADDR)==DEV_PS3){
                writeModbus(PS2_ADDR+1,MB_PS2_AC2_STATE,state);
            }
        }
    }
}

//PS-1 выход сухого контакта
void modbus_dev::set_stand_dc1(int state){
    if(state == 0 || state == 1){
        if(get_mb_devtype(PS1_ADDR)==DEV_PS1){
            writeModbus(PS1_ADDR+1,MB_PS1_SENSOR1_STATE,state);
        }
        else if(get_mb_devtype(PS2_ADDR)==DEV_PS2){
            writeModbus(PS2_ADDR+1,MB_PS2_SENSOR1_STATE,state);
        }
        else{
            set_mb_io02_out(0,state);
        }
    }
}

//PS-1 выход сухого контакта
void modbus_dev::set_stand_dc2(int state){
    if(state == 0 || state == 1){
        if(get_mb_devtype(PS1_ADDR)==DEV_PS1){
            writeModbus(PS1_ADDR+1,MB_PS1_SENSOR2_STATE,state);
        }
        else if(get_mb_devtype(PS2_ADDR)==DEV_PS2){
            writeModbus(PS2_ADDR+1,MB_PS1_SENSOR2_STATE,state);
        }
        else{
            set_mb_io02_out(1,state);
        }
    }

}

//PS-1 выход АКБ
void modbus_dev::set_stand_akb(int state){
    if(state == 0 || state == 1){
        if(is_connected(PS1_ADDR)){
            writeModbus(PS1_ADDR+1,MB_PS1_AKB_EN,state);//переключаем реле на выход
        }
    }
}

//подключаем нагрузочный резистор для измерения тока зарядки
void modbus_dev::set_ps1_rload(int state){
    if(state == 0 || state == 1){
        if(is_connected(PS1_ADDR)){
            writeModbus(PS1_ADDR+1,MB_PS1_R_BAT,state);//подключаем нагрузочный резистор
        }
    }
}

//подключаем нагрузочный резистор для измерения тока зарядки
void modbus_dev::set_ps1_measurement_direction(int dir){
    if(dir == 0 || dir == 1 || dir==2){
        if(is_connected(PS1_ADDR)){
            writeModbus(PS1_ADDR+1,MB_PS1_AKB_DIRECTION,dir);//подключаем нагрузочный резистор
        }
    }
}


void modbus_dev::set_akb_voltage(int voltage){
    if(is_connected(PS1_ADDR)){
        writeModbus(PS1_ADDR+1,MB_PS1_V_BAT,voltage);
    }
}

void modbus_dev::set_stand_all_off(){
    //QByteArray arr; //warning
    if(is_connected(PS1_ADDR)){
        writeModbus(PS1_ADDR+1,MB_PS1_AC1_STATE,0);
    }
}

int modbus_dev::get_ps1_charge_current(void){

    return get_mb_ps1_current_max();
}

//PS-2 ключ зарядки
void modbus_dev::set_stand_charge_key(int state){

    if(state == 0 || state == 1){

            if(get_simbat_type() == SimbatType::SIMBAT24)
            {
                writeModbus(get_simbat24_slot() + 1 , MB_SIMBAT_CHARGE_STATE, state);

            }else
                if(get_simbat_type() == SimbatType::SIMBAT48)
                {
                    writeModbus(get_simbat48_slot() + 1, MB_SIMBAT_CHARGE_STATE, state);
                }
    }
}

int modbus_dev::get_simbat_type()
{

    if(is_connected(get_simbat24_slot()) || is_connected(get_simbat48_slot()))
        return simbat_test_type;
    else
        return SimbatType::NotConnected;
}

void modbus_dev::set_simbat_type(int type)
{
    if(type == SimbatType::SIMBAT24)
        simbat_test_type = SimbatType::SIMBAT24;
    else
        if(type == SimbatType::SIMBAT48)
            simbat_test_type = SimbatType::SIMBAT48;
        else
            simbat_test_type = SimbatType::NotConnected;
}

void modbus_dev::set_stand_discharge_key(int state){
    if(state == 0 || state == 1){
        if(get_mb_devtype(PS2_ADDR)==DEV_PS2){
            writeModbus(PS2_ADDR+1,MB_PS2_DISCHRG_KEY_STATE,state);
        }
        else{
            if(get_simbat_type() == SimbatType::SIMBAT24)
            {
                writeModbus(get_simbat24_slot() + 1, MB_SIMBAT_DISCHARGE_STATE, state);
            }else
                if(get_simbat_type() == SimbatType::SIMBAT48)
                {
                    writeModbus(get_simbat48_slot() + 1, MB_SIMBAT_DISCHARGE_STATE, state);
                }
        }
    }
}

//установка значения сопротивления нагрузки для узла зарядки
void modbus_dev::set_stand_charge_rload(int rload){

    if(is_connected(PS2_ADDR))
    {
        if(get_mb_devtype(PS2_ADDR)==DEV_PS2 && rload < PS2_RLOAD_MAX && rload > PS2_RLOAD_MIN)
        {
            writeModbus(PS2_ADDR+1,MB_PS2_CHRG_RLOAD,rload);
        }
    }
    if(get_simbat_type() == SimbatType::SIMBAT24)
    {
        if(rload > SIMBAT24_RLOAD_MIN && rload < SIMBAT24_RLOAD_MAX)
        {
            writeModbus(get_simbat24_slot() + 1, MB_SIMBAT_CHARGE_LOAD, rload);
        }
    }
    if(get_simbat_type() == SimbatType::SIMBAT48)
    {
        if(rload > SIMBAT48_RLOAD_MIN && rload < SIMBAT48_RLOAD_MAX)
        {
            writeModbus(get_simbat48_slot() + 1, MB_SIMBAT_CHARGE_LOAD, rload);
        }
    }
}

//управление реле нагревателя
void modbus_dev::set_stand_heater1_relay(int state){
    if(state == 0 || state == 1){
        if(is_connected(PS2_ADDR)){
            if(get_mb_devtype(PS2_ADDR)==DEV_PS2 || get_mb_devtype(PS2_ADDR)==DEV_PS3){
                writeModbus(PS2_ADDR+1,MB_PS2_HEATER_RELAY_STATE,state);
            }
        }
    }
}

void modbus_dev::set_stand_heater2_relay(int state){
    if(state == 0 || state == 1){
        if(is_connected(PS3_ADDR)){
            if(get_mb_devtype(PS2_ADDR)==DEV_PS2 || get_mb_devtype(PS3_ADDR)==DEV_PS3){
                writeModbus(PS3_ADDR+1,MB_PS3_HEATER_RELAY2_STATE,state);

            }
        }
    }
}

//установка максимальной температуры на радиаторе, при превышении отключаем нагрузку
void modbus_dev::set_stand_max_temper(int temper){

    if(temper > 60 && temper < 150){
        if(is_connected(PS2_ADDR)){
            if(get_mb_devtype(PS2_ADDR)==DEV_PS2){
                writeModbus(PS2_ADDR+1,MB_PS2_MAX_TEMPERATURE,temper);
            }
        }else{
            if(get_simbat_type() == SimbatType::SIMBAT24)
            {
                writeModbus(get_simbat24_slot() + 1, MB_SIMBAT_MAX_TEMPER, temper);
            }else
                if(get_simbat_type() == SimbatType::SIMBAT48)
                {
                    writeModbus(get_simbat48_slot() + 1, MB_SIMBAT_MAX_TEMPER, temper);
                }
        }
    }
}

void modbus_dev::clear_mb_ps2_minmax(){
    mb_ps2_charge_voltage = 0;
    mb_ps2_charge_current = 0;
    mb_ps2_charge_voltage_min = 999;
    mb_ps2_charge_voltage_max = 0;
    mb_ps2_discharge_voltage = 0;
    mb_ps2_discharge_current = 0;
    mb_ps2_discharge_voltage_min = 0;
    mb_ps2_discharge_voltage_max = 0;
    mb_ps2_heater_current = 0;
    mb_ps2_temper = 0;
    writeModbus(PS1_ADDR+1,MB_PS2_CLEAR,1);
}

void MainWindow::read_stand_all_slots(){
    pTestThread->dev->set_read_flag(MB_READ_ALL);
    pTestThread->dev->set_current_slot(0);
}

void MainWindow::read_stand_one_slot(){
    pTestThread->dev->set_current_slot(ui->modbus_id->currentIndex());
    pTestThread->dev->set_read_flag(MB_READ_ONE);
}

void MainWindow::stand_io02_out1_clicked(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->io02_is_connected()){
            if(state){
                pTestThread->dev->set_mb_io02_out(0,1);
                ui->modbus_io02_out1->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("IO-02: Выход 1 - замкнут", I);
                //debug_window->debug_set_io02_out1_state(1);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_mb_io02_out(0,0);
                ui->modbus_io02_out1->setStyleSheet("");
                syslog("IO-02: Выход 1 - разомкнут", I);
                //debug_window->debug_set_io02_out1_state(0);
                state = 1;
            }
        }
        else
            syslog("Плата IO-02 не подключена",E);
    }
    else
        syslog("Плата IO-02 не подключена",E);
}

void MainWindow::stand_io02_out2_clicked(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->io02_is_connected()){
            if(state){
                pTestThread->dev->set_mb_io02_out(1,1);
                ui->modbus_io02_out2->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("IO-02: Выход 2 - замкнут", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_mb_io02_out(1,0);
                ui->modbus_io02_out2->setStyleSheet("");
                syslog("IO-02: Выход 2 - разомкнут", I);
                state = 1;
            }
        }
        else
            syslog("Плата IO-02 не подключена",E);
    }
    else
        syslog("Плата IO-02 не подключена",E);
}

//out 3
void MainWindow::stand_io02_out3_clicked(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->io02_is_connected()){
            if(state){
                pTestThread->dev->set_mb_io02_out(2,1);
                ui->modbus_io02_out3->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("IO-02: Выход 3 - замкнут", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_mb_io02_out(2,0);
                ui->modbus_io02_out3->setStyleSheet("");
                syslog("IO-02: Выход 3 - разомкнут", I);
                state = 1;
            }
        }
        else
            syslog("Плата IO-02 не подключена",E);
    }
    else
        syslog("Плата IO-02 не подключена",E);
}

//out 4
void MainWindow::stand_io02_out4_clicked(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->io02_is_connected()){
            if(state){
                pTestThread->dev->set_mb_io02_out(3,1);
                ui->modbus_io02_out4->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("IO-02: Выход 4 - замкнут", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_mb_io02_out(3,0);
                ui->modbus_io02_out4->setStyleSheet("");
                syslog("IO-02: Выход 4 - разомкнут", I);
                state = 1;
            }
        }
        else
            syslog("Плата IO-02 не подключена",E);
    }
    else
        syslog("Плата IO-02 не подключена",E);
}

//out 5
void MainWindow::stand_io02_out5_clicked(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->io02_is_connected()){
            if(state){
                pTestThread->dev->set_mb_io02_out(4,1);
                ui->modbus_io02_out5->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("IO-02: Выход 5 - замкнут", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_mb_io02_out(4,0);
                ui->modbus_io02_out5->setStyleSheet("");
                syslog("IO-02: Выход 5 - разомкнут", I);
                state = 1;
            }
        }
        else
            syslog("Плата IO-02 не подключена",E);
    }
    else
        syslog("Плата IO-02 не подключена",E);
}

//out 6
void MainWindow::stand_io02_out6_clicked(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->io02_is_connected()){
            if(state){
                pTestThread->dev->set_mb_io02_out(5,1);
                ui->modbus_io02_out6->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("IO-02: Выход 6 - замкнут", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_mb_io02_out(5,0);
                ui->modbus_io02_out6->setStyleSheet("");
                syslog("IO-02: Выход 6 - разомкнут", I);
                state = 1;
            }
        }
        else
            syslog("Плата IO-02 не подключена",E);
    }
    else
        syslog("Плата IO-02 не подключена",E);
}

//out 7
void MainWindow::stand_io02_out7_clicked(){
    static bool state = 1;
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->io02_is_connected()){
            if(state){
                pTestThread->dev->set_mb_io02_out(6,1);
                ui->modbus_io02_out7->setStyleSheet("QPushButton { background-color: grey; }");
                syslog("IO-02: Выход 7 - замкнут", I);
                state = 0;
            }
            else
            {
                pTestThread->dev->set_mb_io02_out(7,0);
                ui->modbus_io02_out7->setStyleSheet("");
                syslog("IO-02: Выход 7 - разомкнут", I);
                state = 1;
            }
        }
        else
            syslog("Плата IO-02 не подключена",E);
    }
    else
        syslog("Плата IO-02 не подключена",E);
}

void MainWindow::stand_io02_rs485_test_start(){
    if(prog_sett.stand_type == StandType::typeAPK03){
        if(pTestThread->dev->io02_is_connected()){
            int slot = pTestThread->dev->get_io02_slot();
            pTestThread->dev->writeModbus(slot+1, MB_IO02_RS485_TEST_START, 1);
        }
        else
            syslog("Плата IO-02 не подключена",E);
    }
}
