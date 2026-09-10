#include <winsock2.h>
#include <QtGui>
#include "ui.h"
#include "mainwindow.h"
#include "ui_mainwindow.h"
#include <QLabel>
#include <pcap.h>
#include <QPrinterInfo>
#include <QPrinter>
#include <QMessageBox>
#include <QGraphicsScene>
#include <QGraphicsTextItem>
#include <QNetworkInterface>
#include <QMenu>
#include <QMenuBar>
#include <QSerialPortInfo>
#include <QApplication>

#include "functions.h"

//формы настройки

//отрисовка подключенных слотов в стенд
void MainWindow::repaint_stand_slots(void){

    if(prog_sett.stand_type == StandType::typeAPK03){

        qDebug() << "repaint_stand_slots";

        for(int i=0;i<STAND_SLOTS_NUM;i++){

            if(pTestThread->dev->is_connected(i)){

                dev_buttons[i/2]->setHidden(false);
                dev_buttons[i/2]->setText(Functions::convert_mb_dev_type(pTestThread->dev->get_mb_devtype(i)));

                if(ui->modbus_id->currentIndex() == i)
                    show_modbus_table(i);

            }
        }
    }
}

//отрисовка подключенных слотов в стенд
void MainWindow::repaint_stand_one_slot(void){
    int slot;
    if(prog_sett.stand_type == StandType::typeAPK03){
        slot = ui->modbus_id->currentIndex();
        qDebug() << "repaint_stand_one_slot" << slot;

        read_stand(slot);
    }
}

//отрисовка таблицы для переменных
void MainWindow::create_modbus_table(){
    QString temp;
    qDebug() << "create_modbus_table";

    tableInfoWidget = new QTableWidget(20,2);
    table_layout = new QHBoxLayout();
    table_layout->addWidget(tableInfoWidget);
    ui->modbus_table->setLayout(table_layout);

    tableInfoWidget->setAutoScroll(true);

    ui->modbus_id->clear();
    for(int i=0;i<STAND_SLOTS_NUM;i++){

        temp.sprintf("%d",i+1);
        ui->modbus_id->addItem(temp);

    }

    //capion
    QTableWidgetItem *item10 = new QTableWidgetItem();
    item10->setText(tr("Переменная"));
    tableInfoWidget->setHorizontalHeaderItem(0,item10);
    tableInfoWidget->setColumnWidth(0,270);
    QTableWidgetItem *item11 = new QTableWidgetItem();
    item11->setText(tr("Значение"));
    tableInfoWidget->setHorizontalHeaderItem(1,item11);
    tableInfoWidget->setColumnWidth(1,100);

    //create all widgets
    for(int i=0;i<MODBUS_TABLE_SIZE;i++){
        item0[i] = new QTableWidgetItem();
        item1[i] = new QTableWidgetItem();
        tableInfoWidget->setItem(i,0, item0[i]);
        tableInfoWidget->setItem(i,1, item1[i]);
    }
}

void MainWindow::show_modbus_table(int slot){
    QString str;

    qDebug() << "show_modbus_table";
    if(prog_sett.stand_type != StandType::typeAPK03)
        return;
    //clear
    for(int i=0;i<MODBUS_TABLE_SIZE;i++){
        item0[i]->setText("");
        item1[i]->setText("");
    }

    if(pTestThread->dev->is_connected(slot) && (ui->modbus_id->currentIndex()==slot)){

        //тип устройства
        item0[0]->setText("Тип устройства");
        item1[0]->setText(Functions::convert_mb_dev_type(pTestThread->dev->get_mb_devtype(slot)));

        //ID
        item0[1]->setText("ID устройства");
        str.sprintf("%d",slot+1);
        item1[1]->setText(str);

        if(pTestThread->dev->get_mb_devtype(slot) == DEV_EL60){

            item0[2]->setText("Напряжение PoE, В");

            str.sprintf("%d.%d",pTestThread->dev->get_mb_el60_voltage(slot)/1000,pTestThread->dev->get_mb_el60_voltage(slot)%1000);
            item1[2]->setText(str);

            //3-ток
            item0[3]->setText("Ток PoE, mA");

            str.sprintf("%d",pTestThread->dev->get_mb_el60_current(slot));
            item1[3]->setText(str);
            //item1[3]->setTextColor(Qt::black);

            //4-мощность
            item0[4]->setText("Мощность PoE, мВт");
            str.sprintf("%d",(pTestThread->dev->get_mb_el60_current(slot)*pTestThread->dev->get_mb_el60_voltage(slot))/1000);
            item1[4]->setText(str);
            //item1[4]->setTextColor(Qt::black);

            //5-температура
            item0[5]->setText("Температура");
            str.sprintf("%d",pTestThread->dev->get_mb_el60_temper(slot));
            item1[5]->setText(str);
            //item1[5]->setTextColor(Qt::black);

            //6-автономный режим
            item0[6]->setText("Автономный режим");
            str.sprintf("%d",pTestThread->dev->get_mb_el60_offline_mode(slot));
            item1[6]->setText(str);
            //item1[6]->setTextColor(Qt::black);

            //7-ток в автономном режиме
            item0[7]->setText("Мощность в авт. режиме, мВт");
            str.sprintf("%d",pTestThread->dev->get_mb_el60_offline_current(slot));
            item1[7]->setText(str);
            //item1[7]->setTextColor(Qt::black);

            //8-ключ включения нагрузки
            item0[8]->setText("Включение нагрузки");
            str.sprintf("%d",pTestThread->dev->get_mb_el60_out_en(slot));
            item1[8]->setText(str);
            //item1[8]->setTextColor(Qt::black);

            //9-ключ включения байпаса PoE
            item0[9]->setText("Байпас для Passive PoE");
            str.sprintf("%d",pTestThread->dev->get_mb_el60_passive(slot));
            item1[9]->setText(str);
            //item1[9]->setTextColor(Qt::black);
        }
        else if(pTestThread->dev->get_mb_devtype(slot) == DEV_PS1){
            //0
            item0[0]->setText("Фактическое напряжение АКБ, В");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_ps1_voltage()/1000,pTestThread->dev->get_mb_ps1_voltage()%1000);
            item1[0]->setText(str);
            //item1[0]->setTextColor(Qt::black);

            //1
            item0[1]->setText("Напряжение уставки АКБ, В");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_ps1_set_voltage()/1000,pTestThread->dev->get_mb_ps1_set_voltage()%1000);
            item1[1]->setText(str);
            //item1[1]->setTextColor(Qt::black);

            //2-ток
            item0[2]->setText("Ток АКБ, mA");

            str.sprintf("%d",pTestThread->dev->get_mb_ps1_current());
            item1[2]->setText(str);
            //item1[2]->setTextColor(Qt::black);

            //3-ток - мин
            item0[3]->setText("Минимальный ток зарядки АКБ, mA");

            str.sprintf("%d",pTestThread->dev->get_mb_ps1_current_min());
            item1[3]->setText(str);
            //item1[3]->setTextColor(Qt::black);

            //4-ток - макс
            item0[4]->setText("Максимальный ток АКБ, mA");
            str.sprintf("%d",pTestThread->dev->get_mb_ps1_current_max());
            item1[4]->setText(str);
            //item1[4]->setTextColor(Qt::black);

            //5-ток нагревателя
            item0[5]->setText("Ток нагревателей, mA");
            str.sprintf("%d",pTestThread->dev->get_ps1_heating_current());
            item1[5]->setText(str);
            //item1[5]->setTextColor(Qt::black);
        }
        else if(pTestThread->dev->get_mb_devtype(slot) == DEV_PS2)
        {
            //0-наряжение зарядки
            item0[0]->setText("Напряжение зарядки АКБ, В");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_charge_voltage()/1000,pTestThread->dev->get_mb_charge_voltage()%1000);
            item1[0]->setText(str);
            //item1[0]->setTextColor(Qt::black);

            //1-ток зарядки
            item0[1]->setText("Ток зарядки АКБ, А");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_charge_current()/1000,pTestThread->dev->get_mb_charge_current()%1000);
            item1[1]->setText(str);
            //item1[1]->setTextColor(Qt::black);

            //2-Напряжение зарядки мин
            item0[2]->setText("Напряжение зарядки АКБ мин., В");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_ps2_charge_voltage_min()/1000,pTestThread->dev->get_mb_ps2_charge_voltage_min()%1000);
            item1[2]->setText(str);
            //item1[2]->setTextColor(Qt::black);

            //3-Напряжение зарядки макс
            item0[3]->setText("Напряжение зарядки АКБ макс., В");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_ps2_charge_voltage_max()/1000,pTestThread->dev->get_mb_ps2_charge_voltage_max()%1000);
            item1[3]->setText(str);
            //item1[3]->setTextColor(Qt::black);

            //4-сопротивление нагрузки
            item0[4]->setText("Сопротивление нагрузки, Ом");
            str.sprintf("%d",pTestThread->dev->get_mb_ps2_charge_rload());
            item1[4]->setText(str);
            //item1[4]->setTextColor(Qt::black);

            //5-ключ зарядка
            item0[5]->setText("Ключ зарядки");
            str.sprintf("%d",pTestThread->dev->get_mb_ps2_charge_key_state());
            item1[5]->setText(str);
            //item1[5]->setTextColor(Qt::black);

            //6-ключ разрядка
            item0[6]->setText("Ключ разрядки");
            str.sprintf("%d",pTestThread->dev->get_mb_ps2_discharge_key_state());
            item1[6]->setText(str);
            //item1[6]->setTextColor(Qt::black);

            //7-наряжение разрядки
            item0[7]->setText("Напряжение разрядки АКБ, В");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_ps2_discharge_voltage()/1000,pTestThread->dev->get_mb_ps2_discharge_voltage()%1000);
            item1[7]->setText(str);
            //item1[7]->setTextColor(Qt::black);

            //8-ток разрядки
            item0[8]->setText("Ток разрядки АКБ, А");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_ps2_discharge_current()/1000,pTestThread->dev->get_mb_ps2_discharge_current()%1000);
            item1[8]->setText(str);
            //item1[8]->setTextColor(Qt::black);

            //9-Напряжение зарядки мин
            item0[9]->setText("Напряжение разрядки АКБ мин., В");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_ps2_discharge_voltage_min()/1000,pTestThread->dev->get_mb_ps2_discharge_voltage_min()%1000);
            item1[9]->setText(str);
            //item1[9]->setTextColor(Qt::black);

            //10-Напряжение зарядки макс
            item0[10]->setText("Напряжение разрядки АКБ макс., В");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_ps2_discharge_voltage_max()/1000,pTestThread->dev->get_mb_ps2_discharge_voltage_max()%1000);
            item1[10]->setText(str);
            //item1[10]->setTextColor(Qt::black);

            //11-температура
            item0[11]->setText("Температура");
            str.sprintf("%d",pTestThread->dev->get_mb_ps2_temper());
            item1[11]->setText(str);
            //item1[11]->setTextColor(Qt::black);
        }
        else if(pTestThread->dev->get_mb_devtype(slot) == DEV_PS3)
        {
            //реле AC1
            item0[0]->setText("Реле AC1");
            str.sprintf("%d",pTestThread->dev->get_mb_ps3_ac1_state());
            item1[0]->setText(str);
            //item1[0]->setTextColor(Qt::black);

            //реле AC2
            item0[1]->setText("Реле AC2");
            str.sprintf("%d",pTestThread->dev->get_mb_ps3_ac2_state());
            item1[1]->setText(str);
            //item1[1]->setTextColor(Qt::black);

            //реле Heater 1
            item0[2]->setText("Реле Heater 1");
            str.sprintf("%d",pTestThread->dev->get_mb_ps3_heater1_relay());
            item1[2]->setText(str);
            //item1[2]->setTextColor(Qt::black);

            //реле Heater 2
            item0[3]->setText("Реле Heater 2");
            str.sprintf("%d",pTestThread->dev->get_mb_ps3_heater2_relay());
            item1[3]->setText(str);
            //item1[3]->setTextColor(Qt::black);

            //0-ток нагревателя 1
            item0[4]->setText("Ток нагревателя 1, mA");
            str.sprintf("%d",pTestThread->dev->get_mb_ps3_heater1_current());
            item1[4]->setText(str);
            //item1[4]->setTextColor(Qt::black);

            //1-ток нагревателя 2
            item0[5]->setText("Ток нагревателя 2, mA");
            str.sprintf("%d",pTestThread->dev->get_mb_ps3_heater2_current());
            item1[5]->setText(str);
            //item1[5]->setTextColor(Qt::black);
        }
        else if(pTestThread->dev->get_mb_devtype(slot) == DEV_EL60V5){

            item0[2]->setText("Напряжение PoE А");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_el60v5_voltage_a(slot)/1000,
                        pTestThread->dev->get_mb_el60v5_voltage_a(slot)%1000);
            item1[2]->setText(str);

            item0[3]->setText("Напряжение PoE B");
            str.sprintf("%d.%d",pTestThread->dev->get_mb_el60v5_voltage_b(slot)/1000,
                        pTestThread->dev->get_mb_el60v5_voltage_b(slot)%1000);
            item1[3]->setText(str);

            //ток
            item0[4]->setText("Ток PoE A, mA");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_current_a(slot));
            item1[4]->setText(str);
            //item1[4]->setTextColor(Qt::black);

            item0[5]->setText("Ток PoE B, mA");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_current_b(slot));
            item1[5]->setText(str);
            //item1[5]->setTextColor(Qt::black);

            //мощность в нагрузке
            item0[6]->setText("Фактическая нагрузка PoE A, mВт");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_current_a(slot)*
                        pTestThread->dev->get_mb_el60v5_voltage_a(slot)/1000 );
            item1[6]->setText(str);
            //item1[6]->setTextColor(Qt::black);

            item0[7]->setText("Фактическая нагрузка PoE B, mВт");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_current_b(slot)*
                        pTestThread->dev->get_mb_el60v5_voltage_b(slot)/1000);
            item1[7]->setText(str);
            //item1[7]->setTextColor(Qt::black);

            //установленная мощность в нагрузке
            item0[8]->setText("Нагрузка PoE A, mВт");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_out_pwr_a(slot));
            item1[8]->setText(str);
            //item1[8]->setTextColor(Qt::black);

            item0[9]->setText("Нагрузка PoE B, mВт");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_out_pwr_b(slot));
            item1[9]->setText(str);
            //item1[9]->setTextColor(Qt::black);

            //ключ включения нагрузки
            item0[10]->setText("Ключ нагрузки PoE A");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_out_en_a(slot));
            item1[10]->setText(str);
            //item1[10]->setTextColor(Qt::black);

            item0[11]->setText("Ключ нагрузки PoE B");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_out_en_b(slot));
            item1[11]->setText(str);
            //item1[11]->setTextColor(Qt::black);

            //температура
            item0[12]->setText("Температура радиатора A");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_temper_a(slot));
            item1[12]->setText(str);
            //item1[12]->setTextColor(Qt::black);

            item0[13]->setText("Температура радиатора B");
            str.sprintf("%d",pTestThread->dev->get_mb_el60v5_temper_b(slot));
            item1[13]->setText(str);
        }
        else if(pTestThread->dev->get_mb_devtype(slot) == DEV_IO02){
            item0[2]->setText("Input 1");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(0));
            item1[2]->setText(str);

            item0[3]->setText("Input 2");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(1));
            item1[3]->setText(str);

            item0[4]->setText("Input 3");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(2));
            item1[4]->setText(str);

            item0[5]->setText("Input 4");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(3));
            item1[5]->setText(str);

            item0[6]->setText("Input 5");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(4));
            item1[6]->setText(str);

            item0[7]->setText("Input 6");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(5));
            item1[7]->setText(str);

            item0[8]->setText("Input 7");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(6));
            item1[8]->setText(str);

            item0[9]->setText("Input 8");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(7));
            item1[9]->setText(str);

            item0[10]->setText("Input 9");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(8));
            item1[10]->setText(str);

            item0[11]->setText("Input 10");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(9));
            item1[11]->setText(str);

            item0[12]->setText("Input 11");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_input(10));
            item1[12]->setText(str);

            item0[13]->setText("Output 1");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_output(0));
            item1[13]->setText(str);

            item0[14]->setText("Output 2");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_output(1));
            item1[14]->setText(str);

            item0[15]->setText("Output 3");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_output(2));
            item1[15]->setText(str);

            item0[16]->setText("Output 4");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_output(3));
            item1[16]->setText(str);

            item0[17]->setText("Output 5");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_output(4));
            item1[17]->setText(str);

            item0[18]->setText("Output 6");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_output(5));
            item1[18]->setText(str);

            item0[19]->setText("Output 7");
            str.sprintf("%d",pTestThread->dev->get_mb_io02_output(6));
            item1[19]->setText(str);
        }
        else if(pTestThread->dev->get_mb_devtype(slot) == DEV_SIMBAT24){

            item0[2]->setText("Ключ зарядки");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_state(slot));
            item1[2]->setText(str);

            item0[3]->setText("Напряженние зарядки");
            str.sprintf("%d",pTestThread->dev-> get_mb_simbat_charge_voltage(slot));
            item1[3]->setText(str);

            item0[4]->setText("Ток зарядки");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_current(slot));
            item1[4]->setText(str);

            item0[5]->setText("Напряжение зарядки максимальное");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_voltage_max(slot));
            item1[5]->setText(str);

            item0[6]->setText("Напряжение зарядки минимальное");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_voltage_min(slot));
            item1[6]->setText(str);

            item0[7]->setText("Установка сопротивления для контроля тока");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_rload(slot));
            item1[7]->setText(str);

            item0[8]->setText("Ключ разрядки");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_state(slot));
            item1[8]->setText(str);

            item0[9]->setText("Напряжение разрядки");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_voltage(slot));
            item1[9]->setText(str);

            item0[10]->setText("Ток разрядки");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_current(slot));
            item1[10]->setText(str);

            item0[11]->setText("Напряжение разрядки максимальное");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_voltage_max(slot));
            item1[11]->setText(str);

            item0[12]->setText("Напряжение разрядки минимальное");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_voltage_min(slot));
            item1[12]->setText(str);

            item0[13]->setText("Температура 1");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_temper1(slot));
            item1[13]->setText(str);

            item0[14]->setText("Температура 2");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_temper2(slot));
            item1[14]->setText(str);

            item0[15]->setText("Температура включения турбовентиляторов");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_max_temper(slot));
            item1[15]->setText(str);

            item0[16]->setText("Сигнал включения турбовентиляторов");
            str.sprintf("%d",pTestThread->dev->get_mb_simbat_fan(slot));
            item1[16]->setText(str);
        }
        else
            if(pTestThread->dev->get_mb_devtype(slot) == DEV_SIMBAT48){

                item0[2]->setText("Ключ зарядки");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_state(slot));
                item1[2]->setText(str);

                item0[3]->setText("Напряженние зарядки");
                str.sprintf("%d",pTestThread->dev-> get_mb_simbat_charge_voltage(slot));
                item1[3]->setText(str);

                item0[4]->setText("Ток зарядки");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_current(slot));
                item1[4]->setText(str);

                item0[5]->setText("Напряжение зарядки максимальное");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_voltage_max(slot));
                item1[5]->setText(str);

                item0[6]->setText("Напряжение зарядки минимальное");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_voltage_min(slot));
                item1[6]->setText(str);

                item0[7]->setText("Установка сопротивления для контроля тока");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_charge_rload(slot));
                item1[7]->setText(str);

                item0[8]->setText("Ключ разрядки");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_state(slot));
                item1[8]->setText(str);

                item0[9]->setText("Напряжение разрядки");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_voltage(slot));
                item1[9]->setText(str);

                item0[10]->setText("Ток разрядки");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_current(slot));
                item1[10]->setText(str);

                item0[11]->setText("Напряжение разрядки максимальное");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_voltage_max(slot));
                item1[11]->setText(str);

                item0[12]->setText("Напряжение разрядки минимальное");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_discharge_voltage_min(slot));
                item1[12]->setText(str);

                item0[13]->setText("Температура 1");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_temper1(slot));
                item1[13]->setText(str);

                item0[14]->setText("Температура 2");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_temper2(slot));
                item1[14]->setText(str);

                item0[15]->setText("Температура включения турбовентиляторов");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_max_temper(slot));
                item1[15]->setText(str);

                item0[16]->setText("Сигнал включения турбовентиляторов");
                str.sprintf("%d",pTestThread->dev->get_mb_simbat_fan(slot));
                item1[16]->setText(str);
            }
            else{
                item0[0]->setText(tr("Соединение не установлено"));
            }
    }
}

void MainWindow::create_ui_menu(void){
    exitAction = new QAction(tr("&Выход"), this);
    connectAction = new QAction(tr("&Подключиться к стенду"), this);
    ComSetAction = new QAction(tr("&Настройка стенда"), this);
    DatabaseSetAction = new QAction(tr("&Настройка сервера"), this);
    DeviceBrowser = new QAction(tr("&База данных устройств"),this);
    PrintReports = new QAction(tr("&Печать отчетов"),this);
    NetCardSettAction = new QAction(tr("Настройка сетевых карт"), this);
    PrinterSetAction = new QAction(tr("Настройка принтера"), this);
    ProfileSetAction = new QAction(tr("Настройка профилей тестирования"), this);
    BercutSetAction = new QAction(tr("Настройка Генератора трафика"), this);
    PowerSupplySetAction = new QAction(tr("Настройка внешнего БП"), this);
    SwitchSetAction = new QAction(tr("Настройки промежуточного Коммутатора"), this);
    TeleportSetAction = new QAction(tr("Настройки Teleport"), this);
    ProgrammersSetAction = new QAction(tr("Настройки программаторов"), this);
    FirmwareViewAction = new QAction(tr("Открыть папку с Прошивками"), this);
    ManualViewAction = new QAction(tr("Инструкция на стенд"), this);
    ChangeThemeAction = new QAction(tr("Сменить тему"), this);

    //set menu
    //menu File
    fileMenu = menuBar()->addMenu(tr("&Файл"));
    fileMenu->addAction(exitAction);
    //menu connections
    fileMenu = menuBar()->addMenu(tr("&Подключения"));
    fileMenu->addAction(connectAction);

    //menu Settings
    fileMenu = menuBar()->addMenu(tr("&Настройки"));
    fileMenu->addAction(ComSetAction);
    fileMenu->addAction(DatabaseSetAction);
    fileMenu->addAction(NetCardSettAction);
    fileMenu->addAction(PrinterSetAction);
    fileMenu->addAction(ProfileSetAction);
    fileMenu->addAction(ChangeThemeAction);
    fileMenu->addSeparator();
    fileMenu->addAction(BercutSetAction);
    fileMenu->addAction(SwitchSetAction);
    fileMenu->addAction(TeleportSetAction);
    fileMenu->addSeparator();
    fileMenu->addAction(ProgrammersSetAction);
    fileMenu->addAction(FirmwareViewAction);

    //device browser
    fileMenu = menuBar()->addMenu(tr("&База данных"));
    fileMenu->addAction(DeviceBrowser);
    fileMenu->addAction(PrintReports);

    //manual
    fileMenu = menuBar()->addMenu(tr("Инструкции"));
    fileMenu->addAction(ManualViewAction);

    connect(exitAction, SIGNAL(triggered()), this, SLOT(exit_app()));
    connect(ComSetAction, SIGNAL(triggered()), this, SLOT(com_sett()));
    connect(connectAction, SIGNAL(triggered()), this, SLOT(com_connect()));
    connect(DatabaseSetAction, SIGNAL(triggered()), this, SLOT(db_sett()));
    connect(NetCardSettAction,SIGNAL(triggered()),SLOT(net_card_sett()));
    connect(PrinterSetAction,SIGNAL(triggered()),SLOT(printer_sett()));
    connect(ProfileSetAction,SIGNAL(triggered()),SLOT(profiles_sett()));
    connect(DeviceBrowser,SIGNAL(triggered()),SLOT(show_database_form()));
    connect(PrintReports,SIGNAL(triggered()),SLOT(print_reports_list()));
    connect(BercutSetAction,SIGNAL(triggered()),SLOT(bercut_sett()));
    connect(SwitchSetAction,SIGNAL(triggered()),SLOT(switch_sett()));
    connect(TeleportSetAction,SIGNAL(triggered()),SLOT(teleport_sett()));
    connect(PowerSupplySetAction,SIGNAL(triggered()),SLOT(power_supply_sett()));

    connect(ProgrammersSetAction,SIGNAL(triggered()),SLOT(programmers_sett()));
    connect(FirmwareViewAction,SIGNAL(triggered()),SLOT(programmers_fw_view()));

    connect(ManualViewAction,SIGNAL(triggered()),SLOT(manual_view()));
    connect(ChangeThemeAction,SIGNAL(triggered()),SLOT(changeTheme()));
}

void MainWindow::set_stage_result(int num,int state){
    QString style;
    switch(state){
    case NO_ACTIVE: style.append("background-color: rgb(95, 101, 109);");break;
    case GREY:      style.append("background-color: rgb(155, 195, 155);");break;
    case GREEN:  style.append("background-color: rgb(48, 170, 48);");break;
    case RED:       style.append("background-color: rgb(200, 48, 48);");break;
    }
    if(prog_sett.test_type==TYPE_PRODUCTION){
        switch(num){
        case StageTypes::selfTest:
        {
            if(state == 2){
                ui->selftestOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else{
                if(state == 3)
                {
                    ui->selftestOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_selftest_btn->setVisible(true);
                }
            }

            break;
        }
        case StageTypes::updateFirmware:
        {
            if(state == 2)
            {
                ui->updateOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else
                if(state == 3)
                {
                    ui->updateOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_update_btn->setVisible(true);

                }

            break;
        }
        case StageTypes::ups:
        {
            if(state == 2)
            {
                ui->upsOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else
                if(state == 3)
                {
                    ui->upsOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_ups_btn->setVisible(true);

                }

            break;
        }
        case StageTypes::setMac:
        {
            if(state == 2)
            {
                ui->sendMacOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else
                if(state == 3)
                {
                    ui->sendMacOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_mac_btn->setVisible(true);

                }

            break;
        }
        case StageTypes::printLabel:
        {
            if(state == 2)
            {
                ui->printOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else
                if(state == 3)
                {
                    ui->printOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_label_btn->setVisible(true);

                }

            break;
        }
        case StageTypes::sendReport:
        {
            if(state == 2)
            {
                ui->reportOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else
                if(state == 3)
                {
                    ui->reportOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_report_btn->setVisible(true);

                }
            break;
        }

        case StageTypes::inOut:
        {
            if(state == 2)
            {
                ui->inOutOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else
                if(state == 3)
                {
                    ui->inOutOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_inout_btn->setVisible(true);

                }
            break;
        }
        case StageTypes::acBackup:
        {
            if(state == 2)
            {
                ui->acBackupOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else
                if(state == 3)
                {
                    ui->acBackupOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_ps_btn->setVisible(true);

                }
            break;
        }
        case StageTypes::heater:
        {
            if(state == 2)
            {
                ui->heaterOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
            else
                if(state == 3)
                {
                    ui->heaterOk->setStyleSheet("image: url(:/images/fail.png);");
                    ui->restart_from_heater_btn->setVisible(true);

                }

            break;
        }
        }
    }
    else if(prog_sett.test_type == TYPE_REPAIR){
        switch(num){
        case StageTypes::selfTest:
        {
            ui->p2_rp->setStyleSheet(style);

            break;
        }
        case StageTypes::updateFirmware:
        {
            ui->p3_rp->setStyleSheet(style);

            break;
        }
        case StageTypes::ups:
        {
            ui->p5_rp->setStyleSheet(style);

            break;
        }
        case StageTypes::setMac:
        {
            ui->p6_rp->setStyleSheet(style);

            break;
        }
        case StageTypes::printLabel:
        {
            ui->p7_rp->setStyleSheet(style);

            break;
        }
        case StageTypes::sendReport:
        {
            ui->p8_rp->setStyleSheet(style);

            break;
        }
        case StageTypes::heater:
        {
            ui->p9_rp->setStyleSheet(style);

            break;
        }
        }
    }
    else if(prog_sett.test_type == TYPE_TELEPORT){
        switch(num){
        case 1: ui->p1_tlp->setStyleSheet(style); break;
        case 0: ui->p2_tlp->setStyleSheet(style); break;
        case 2: ui->p3_tlp->setStyleSheet(style); break;
        case 3: ui->p4_tlp->setStyleSheet(style); break;
        case 4: ui->p5_tlp->setStyleSheet(style); break;
        case 5: ui->p6_tlp->setStyleSheet(style); break;
        case 6: ui->p7_tlp->setStyleSheet(style); break;
        case 7: ui->p8_tlp->setStyleSheet(style); break;
        }
    }
}

void MainWindow::set_poeport_result(int num, int state){
    QString style;

    switch(state){
    case NO_ACTIVE: style.append("background-color: rgb(95, 101, 109);");break;
    case GREY:      style.append("background-color: rgb(155, 195, 155);");break;
    case GREEN:  style.append("background-color: rgb(48, 170, 48);");break;
    case RED:       style.append("background-color: rgb(200, 48, 48);");break;
    }

    if(prog_sett.test_type==TYPE_PRODUCTION){

        if(state == GREEN)
        {
            if(poe_progress < poe_count)
            poe_progress++;

            if(poe_progress == poe_count)
            {
                ui->poeOk->setVisible(true);
                ui->poeOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
        }else{
            if(state == RED)
            {
                poe_progress++;

                ui->poeOk->setVisible(true);
                ui->poeOk->setStyleSheet("image: url(:/images/fail.png);");
                ui->restart_from_poe_btn->setVisible(true);
            }
        }

        if(poe_count > 0 && poe_count <= 16){
            QString poe_prog = QString("PoE: %1 из %2").arg(poe_progress).arg(poe_count);
            ui->poeLabel->setText(poe_prog);
        }

    }
    else{

        if(state == GREEN){
            ui->rpPoeProgressBar->setValue( ui->rpPoeProgressBar->value() + ui->rpPoeProgressBar->maximum() / poe_count);
            if(ui->rpPoeProgressBar->value() == ui->rpPoeProgressBar->maximum()){
                QString success = "QProgressBar{text-align: center;color: white;} QProgressBar::chunk{background-color: rgb(48, 170, 48);}";
                ui->rpPoeProgressBar->setStyleSheet(success);
            }
        }else{
            if(state == RED){
                poe_progress++;
                QString error = "QProgressBar{text-align: center;} QProgressBar::chunk{background-color: rgb(200, 48, 48);}";
                ui->rpPoeProgressBar->setStyleSheet(error);
            }
        }

        if(poe_count > 0 && poe_count <= 16){
            QString poe_progress = QString("PoE: %1 из %2").arg(ui->rpPoeProgressBar->value()).arg(poe_count);
            ui->rpPoeProgressBar->setFormat(poe_progress);
        }
    }
}

void MainWindow::set_dataport_result(int num, int state){
    QString style;

    switch(state){
    case NO_ACTIVE: style.append("background-color: rgb(95, 101, 109);");break;
    case GREY:      style.append("background-color: rgb(155, 195, 155);");break;
    case GREEN:  style.append("background-color: rgb(48, 170, 48);");break;
    case RED:       style.append("background-color: rgb(200, 48, 48);");break;

    }
    if(prog_sett.test_type==TYPE_PRODUCTION){

        if(state == GREEN){
            data_progress++;
            if(data_progress == data_count){

                ui->dataOk->setVisible(true);
                ui->dataOk->setStyleSheet("image: url(:/images/sucess.png);");
            }
        }else{

            if(state == RED){
                data_progress++;

                ui->dataOk->setVisible(true);
                ui->dataOk->setStyleSheet("image: url(:/images/fail.png);");
                ui->restart_from_data_btn->setVisible(true);
            }
        }

        if(data_count > 0 && data_count <= 16 && data_progress !=0){
            QString data_text = QString("Data: %1 из %2").arg(data_progress).arg(data_count);
            ui->dataLabel->setText(data_text);
        }
    }else{

        if(state == GREEN){
            ui->rpDataProgressBar->setValue(ui->rpDataProgressBar->value() + ui->rpDataProgressBar->maximum() / data_count);
            if(ui->rpDataProgressBar->value() == ui->rpDataProgressBar->maximum()){
                QString success = "QProgressBar{text-align: center;color: white;} QProgressBar::chunk{background-color: rgb(48, 170, 48);}";
                ui->rpDataProgressBar->setStyleSheet(success);
            }
        }else{
            if(state == RED){
                ui->rpDataProgressBar->setValue(ui->rpDataProgressBar->value() + ui->rpDataProgressBar->maximum() / data_count);
                QString error = "QProgressBar{text-align: center;} QProgressBar::chunk{background-color: rgb(200, 48, 48);}";
                ui->rpDataProgressBar->setStyleSheet(error);
            }
        }
        if(data_count > 0 && data_count <= 16){
            QString data_progress = QString("Data: %1 из %2").arg(ui->rpDataProgressBar->value()).arg(data_count);
            ui->rpDataProgressBar->setFormat(data_progress);
        }
    }
}

void MainWindow::set_tlp_input_result(int num,int state){
    QString style;
    switch(state){
    case NO_ACTIVE: style.append("background-color: rgb(95, 101, 109);");break;
    case GREY:      style.append("background-color: rgb(155, 195, 155);");break;
    case GREEN:  style.append("background-color: rgb(48, 170, 48);");break;
    case RED:       style.append("background-color: rgb(200, 48, 48);");break;
    }

    switch(num){
    case 0: ui->p21_tlp->setStyleSheet(style); break;
    case 1: ui->p22_tlp->setStyleSheet(style); break;
    case 2: ui->p23_tlp->setStyleSheet(style); break;
    case 3: ui->p24_tlp->setStyleSheet(style); break;
    case 4: ui->p25_tlp->setStyleSheet(style); break;
    case 5: ui->p26_tlp->setStyleSheet(style); break;
    case 6: ui->p27_tlp->setStyleSheet(style); break;
    case 7: ui->p28_tlp->setStyleSheet(style); break;
    case 8: ui->p29_tlp->setStyleSheet(style); break;
    case 9: ui->p210_tlp->setStyleSheet(style); break;
    default:
        break;
    }
}

void MainWindow::set_tlp_output_result(int num,int state){
    QString style;
    switch(state){
    case NO_ACTIVE: style.append("background-color: rgb(95, 101, 109);");break;
    case GREY:      style.append("background-color: rgb(155, 195, 155);");break;
    case GREEN:  style.append("background-color: rgb(48, 170, 48);");break;
    case RED:       style.append("background-color: rgb(200, 48, 48);");break;
    }

    switch(num){
    case 0: ui->p31_tlp->setStyleSheet(style); break;
    case 1: ui->p32_tlp->setStyleSheet(style); break;
    case 2: ui->p33_tlp->setStyleSheet(style); break;
    case 3: ui->p34_tlp->setStyleSheet(style); break;
    case 4: ui->p35_tlp->setStyleSheet(style); break;
    case 5: ui->p36_tlp->setStyleSheet(style); break;
    case 6: ui->p37_tlp->setStyleSheet(style); break;
    case 7: ui->p38_tlp->setStyleSheet(style); break;
    case 8: ui->p39_tlp->setStyleSheet(style); break;
    case 9: ui->p310_tlp->setStyleSheet(style); break;
    default:
        break;
    }
}

void MainWindow::set_rps_result(int num,int state){
    QString style;
    switch(state){
    case NO_ACTIVE: style.append("background-color: rgb(95, 101, 109);");break;
    case GREY:      style.append("background-color: rgb(155, 195, 155);");break;
    case GREEN:  style.append("background-color: rgb(48, 170, 48);");break;
    case RED:       style.append("background-color: rgb(200, 48, 48);");break;
    }

    switch(num){
    case 0:
        ui->rpsNewStageRkn->setStyleSheet(style);
        break;
    case 1:
        ui->rpsNewStageSelftest->setStyleSheet(style);
        break;
    case 2:
        ui->rpsNewStageAkb->setStyleSheet(style);
        break;
    case 3:
        ui->rpsNewStageACDC->setStyleSheet(style);
        break;
    default:
        break;
    }
}

void MainWindow::rps_stand_painting(int stage){
    qDebug() << "rps_stand_painting";
    int dia;
    QPen pen(Qt::red, 5, Qt::DotLine, Qt::RoundCap, Qt::RoundJoin);
    QBrush brush;

    //если ничего не нужно рисовать, то очищаем
    if(stage == RpsStandStage::None){
        if(ui->rpsGgraphicsViewNew->scene())
            ui->rpsGgraphicsViewNew->scene()->deleteLater();

        scene = new QGraphicsScene;
        scene->addPixmap(QPixmap(":/images/stand.png"));

        scene_new = new QGraphicsScene;
        scene_new->addPixmap(QPixmap(":/images/rpsstand.png"));
    }

    //рисуем кружочки
    switch(stage){
    case RpsStandStage::None:
        break;
    case RpsStandStage::AKB1Polarity:
        dia = 20;
        scene_new->addEllipse(125-dia/2,115-dia/2,dia,dia,pen,brush);
        break;
    case RpsStandStage::AKB2Polarity:
        dia = 20;
        scene_new->addEllipse(145-dia/2,115-dia/2,dia,dia,pen,brush);
        break;
    case RpsStandStage::LedCPU:
        dia = 20;
        scene_new->addEllipse(145-dia/2,205-dia/2,dia,dia,pen,brush);
        break;
    case RpsStandStage::BtnColdStart:
        dia = 20;
        scene_new->addEllipse(119-dia/2,248-dia/2,dia,dia,pen,brush);
        break;
    case RpsStandStage::BtnStop:
        dia = 20;
        scene->addEllipse(106-dia/2,250-dia/2,dia,dia,pen,brush);
        scene_new->addEllipse(119-dia/2,230-dia/2,dia,dia,pen,brush);
        break;
    case RpsStandStage::LedBat:
        dia = 20;
        scene_new->addEllipse(121-dia/2,262-dia/2,dia,dia,pen,brush);
        break;
    case RpsStandStage::Led52V:
        dia = 20;
        scene_new->addEllipse(121-dia/2,289-dia/2,dia,dia,pen,brush);
        break;
    case RpsStandStage::LedAlarm:
        dia = 20;
        break;
    case RpsStandStage::LedNorm:
        dia = 20;
        break;
    case BtnPower:
        dia = 70;
        break;
    case BtnRKN:
        dia = 70;
        break;
    case PreheatingJumper:
        dia = 20;
        scene_new->addEllipse(450-dia/2,100-dia/2,dia,dia,pen,brush);
        break;

    }
    ui->rpsGgraphicsViewNew->setScene(scene_new);
}

void MainWindow::SetStagesToDefault()
{
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

    ui->selftestOk->setStyleSheet("");
    ui->updateOk->setStyleSheet("");
    ui->heaterOk->setStyleSheet("");
    ui->poeOk->setStyleSheet("");
    ui->acBackupOk->setStyleSheet("");
    ui->inOutOk->setStyleSheet("");
    ui->dataOk->setStyleSheet("");
    ui->upsOk->setStyleSheet("");
    ui->sendMacOk->setStyleSheet("");
    ui->printOk->setStyleSheet("");
    ui->reportOk->setStyleSheet("");

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

//открыть мануал
void MainWindow::manual_view(){
    QDesktopServices::openUrl(QUrl::fromLocalFile("file:/C:/FortTelecom/Launcher/TFortisStand/manual/manual.pdf"));
}
