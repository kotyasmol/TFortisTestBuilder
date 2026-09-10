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

void TestThread::send_set_mac(QUdpSocket *socket,struct device_info_t *device_info){
    static QHostAddress hostaddrr;
    static char tmp[64];

    myDebug() << "send_set_mac ";
    for(int i = 0;i<6;i++)
        qDebug() << device_info->mac[i];
    tmp[0] = 'C';
    tmp[1] = 'O';
    tmp[2] = 'N';
    tmp[3] = 'F';
    tmp[4] = 'I';
    tmp[5] = 'G';
    tmp[6] = 0;
    tmp[7] = 0;
    tmp[8] = 0;
    tmp[9] = 0;
    tmp[10] = 'm';
    tmp[11] = 'w';
    for(int i = 0;i<6;i++)
        tmp[12+i] = device_info->mac[i];
    tmp[18] = 'K';
    tmp[19] = 'r';
    tmp[20] = '2';

    hostaddrr.setAddress(DUT_IP_ADDR);

    socket->writeDatagram(tmp,21, hostaddrr, 0xABBA);

    qDebug() << tmp;
}

void MainWindow::mac_sender_tools(){
    long sernum;
    bool ok;
    ui->test_result_ms->setText("");
    sernum = ui->serial_ms->text().toLong(&ok,10);
    if(ok){


        open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]);
        if(test_config.config_loaded == 1){

            if (test_config.model_num_mac_print > 0)
                label_info.dev_type = test_config.model_num_mac_print;
            else
                label_info.dev_type = test_config.model_num;
            label_info.mac[0] = device_info.mac[0] = 0xC0;
            label_info.mac[1] = device_info.mac[1] = 0x11;
            label_info.mac[2] = device_info.mac[2] = 0xA6;

            label_info.mac[3] = device_info.mac[3] = (quint8)label_info.dev_type;
            label_info.mac[4] = device_info.mac[4] = (quint8)(sernum >> 8);
            label_info.mac[5] = device_info.mac[5] = (quint8)(sernum);

            label_info.equipment_field_use = test_config.equipment_field_use;
            label_info.equipment_type = test_config.equipment_type;
            label_info.equipment_str = test_config.equipment_str;

            qDebug() << label_info.mac[3] << label_info.dev_type;
            qDebug() << test_config.model_name;
            qDebug() << test_config.printable_name;

            if(ui->mac_set->isChecked()){

                if(pTestThread->is_model_type_PRO(test_config.model_name)){
                    QString mac_address = mac_address.sprintf("%02X:%02X:%02X:%02X:%02X:%02X",
                                                              device_info.mac[0],device_info.mac[1],
                                                              device_info.mac[2],device_info.mac[3],
                                                              device_info.mac[4],device_info.mac[5]);
                    QString model_name = test_config.model_name;
                    pTestThread->setProMacAddress(mac_address, model_name);
                }
                else{
                    pTestThread->send_set_mac(socket,&device_info);
                }



                pTestThread->clear_arp_case();
            }
            if(ui->mac_print_label->isChecked()){
                if (test_config.model_num_mac_print > 0)
                    label_info.dev_type = test_config.model_num_mac_print;
                else
                    label_info.dev_type = test_config.model_num;

                label_info.serial_num = sernum;

                if(test_config.printable_name.isEmpty())
                    label_info.dev_name = test_config.model_name;
                else
                    label_info.dev_name = test_config.printable_name;
                label_info.num = ui->mac_label_num->value();

                label_info.equipment_field_use = test_config.equipment_field_use;
                label_info.equipment_type = test_config.equipment_type;
                label_info.equipment_str = test_config.equipment_str;

                print_label(&label_info);
            }
        }
    }
}

void MainWindow::print_label_tools(){
    quint32 serial_from,serial_to,sernum;
    bool ok;

    open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]);
    if(ui->print_label_from->text().isEmpty()!=true && ui->print_label_to->text().isEmpty()!=true){


        serial_from = ui->print_label_from->text().toLong(&ok,10);
        if(ok==false){
            syslog("Введите серийный номер FROM",E);
            return;
        }

        serial_to = ui->print_label_to->text().toLong(&ok,10);
        if(ok==false){
            syslog("Введите серийный номер TO",E);
            return;
        }

        if(serial_to<=serial_from){
            syslog("Ошибка: FROM>=TO",E);
            return;
        }

        for(quint32 i=serial_from;i<=serial_to;i++){
            ui->test_result_ms->setText("");
            sernum = i;

            //mac
            label_info.mac[0] = device_info.mac[0] = 0xC0;
            label_info.mac[1] = device_info.mac[1] = 0x11;
            label_info.mac[2] = device_info.mac[2] = 0xA6;
            if (test_config.model_num_mac_print > 0)
                label_info.mac[3] = device_info.mac[3] = (quint8)test_config.model_num_mac_print;
            else
                label_info.mac[3] = device_info.mac[3] = (quint8)test_config.model_num;
            label_info.mac[4] = device_info.mac[4] = (quint8)(sernum >> 8);
            label_info.mac[5] = device_info.mac[5] = (quint8)(sernum);

            //dev type
            if (test_config.model_num_mac_print > 0)
                label_info.dev_type = test_config.model_num_mac_print;
            else
                label_info.dev_type = test_config.model_num;
            //serial num
            label_info.serial_num = sernum;

            //model name
            if(test_config.printable_name.isEmpty())
                label_info.dev_name = test_config.model_name;
            else
                label_info.dev_name = test_config.printable_name;
            label_info.num = ui->print_label_num->value();
            print_label(&label_info);
        }
    }
}
