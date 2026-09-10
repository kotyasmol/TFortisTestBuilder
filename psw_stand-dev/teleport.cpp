#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "stdio.h"
#include "string.h"
#include "math.h"
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
#include <QNetworkRequest>
#include <pcap.h>
#include <QNetworkInterface>
#include <QTextCodec>
#include <QSerialPort>
#include <QSerialPortInfo>

//тестирование Teleport
void MainWindow::teleport_start(){
    int inputs;
    int outputs;
    int i;

    qDebug() << "teleport_start";
    syslog("запуск теста",I);
    if(ui->main_tab->currentIndex()==3){
        if(open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()])==0){

            prog_sett.test_type=TYPE_TELEPORT;

            for(i=0;i<8;i++){
                set_stage_result(i,NO_ACTIVE);
            }

            if(test_config.buildin_test)
                set_stage_result(0,GREY);

            inputs = 0;
            for(i=0;i<TLP_INPUTS_NUM;i++){
                if(test_config.tlp_inputs[i]){
                    inputs++;
                    set_tlp_input_result(i,GREY);
                }
                else{
                    set_tlp_input_result(i,NO_ACTIVE);
                }
            }
            if(inputs)
                set_stage_result(1,GREY);

            outputs = 0;
            for(i=0;i<TLP_OUTPUTS_NUM;i++){
                if(test_config.tlp_outputs[i]){
                    outputs++;
                    set_tlp_output_result(i,GREY);
                }
                else{
                    set_tlp_output_result(i,NO_ACTIVE);
                }
            }
            if(outputs)
                set_stage_result(2,GREY);


            if(test_config.tlp_rs485)
                set_stage_result(3,GREY);


            if((test_config.firmware_load)||(test_config.firmware_load_first))
                set_stage_result(4,GREY);

            if(test_config.send_mac)
                set_stage_result(5,GREY);

            //print label
            if(test_config.print_label)
                set_stage_result(6,GREY);

            //save to database
            set_stage_result(7,GREY);

            ui->test_result_tlp->clear();

            test_report.clear();//очищаем отчет
            pTestThread->set_prog_sett(prog_sett);//передаём настройки программы
            pTestThread->set_test_config(test_config);//передаём профильтестирования

            pTestThread->start_test();

            ui->tlpStart->setDisabled(true);
        }
    }
}

void MainWindow::teleport_com_connect(void){
    teleport_serial = new QSerialPort();
    teleport_serial->setPortName(prog_sett.teleport_com_port_name);
    qDebug() << "teleport_com_connect" << prog_sett.teleport_com_port_name;
    if (teleport_serial->open(QIODevice::ReadWrite)) {
        teleport_serial->setBaudRate(QSerialPort::Baud9600);
        teleport_serial->setDataBits(QSerialPort::Data8);
        teleport_serial->setParity(QSerialPort::NoParity);
        teleport_serial->setStopBits(QSerialPort::OneStop);
        teleport_serial->setFlowControl(QSerialPort::NoFlowControl);
        if(teleport_serial->isOpen()){
            qDebug()<<"teleport порт открыт";
            prog_sett.teleport_connected = 1;
            connect(teleport_serial, SIGNAL(readyRead()),SLOT(teleportSerialRecieve()));
            syslog("TeleportCOM порт открыт",I);
        }
    }
    else{
        prog_sett.teleport_connected = 0;
        qDebug()<<"Ошибка открытия TeleportCOM порта";
        syslog("Ошибка открытия TeleportCOM порта",E);
    }
}

void MainWindow::tlp_send_rs485_hello(){
    qDebug() << "Hello Teleport";
    if(teleport_serial != nullptr && teleport_serial->isOpen())
        teleport_serial->write("Hello Teleport\r\n",16);
    else
        syslog("Com-порт для RS485 не открыт", E);
}

void MainWindow::teleportSerialRecieve(void){
    QByteArray arr;
    qDebug() << "MainWindow::teleportSerialRecieve";
    // Заполняем массив данными
    arr = teleport_serial->readAll();

    pTestThread->teleport_com_data.append(arr);
    qDebug() << pTestThread->teleport_com_data;
}
