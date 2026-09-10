#include "mainwindow.h"
#include <iostream>
#include <QDebug>
#include "ctype.h"
#include <QSerialPort>
#include <QAction>

//работа с COM портом (управление стендом)

void MainWindow::initSerial()
{
    serial = new QSerialPort(); // Новый экземпляр класса AbstractSerial

    connect(this->serial, SIGNAL(readyRead()), this, SLOT(serialRecieve()));
}

void MainWindow::deinitSerial()
{
    if (serial != nullptr)
    {
        if (serial->isOpen()){
            serial->close();
        }
        delete serial;
        serial = nullptr;
    }
}

void MainWindow::serialRecieve() {

    // Заполняем массив данными
    serial->readLine(com_data,16);

    //посылаем в test
    pTestThread->send_com_data(com_data);
}

void MainWindow::com_connect(void){
    //com port connect

    if(prog_sett.stand_type == StandType::typeOld){
        if(prog_sett.connected == 0){
            serial->setPortName(prog_sett.com_port_name);

            if (serial->open(QIODevice::ReadWrite)) {

                serial->setBaudRate(QSerialPort::Baud9600);
                serial->setDataBits(QSerialPort::Data8);
                serial->setParity(QSerialPort::NoParity);
                serial->setStopBits(QSerialPort::OneStop);

                syslog("COM порт открыт",I);
                prog_sett.connected = 1;
                //ui->btn_test->setDisabled(false);
                connectAction->setText("&Отключиться от стенда");
            }
            else{
                syslog("Ошибка открытия COM порта",E);
            }
        }
        else{
            //отключение
            prog_sett.connected = 0;
            connectAction->setText("&Подключиться к стенду");
            syslog("COM прот закрыт",I);
            serial->close();
        }
    }
    else
    {
        if(prog_sett.connected == 0){
            if(pTestThread->dev->com_openned){
                syslog("COM порт стенда открыт",I);
                prog_sett.connected = 1;
                connectAction->setText("&Отключиться от стенда");
                pTestThread->dev->com_disconnect();

            }
            else{
                syslog("Ошибка открытия COM порта стенда",E);
                prog_sett.connected = 0;
                connectAction->setText("&Подключиться к стенду");
                pTestThread->dev->com_connect();
            }
        }
        else{
            //отключение
            prog_sett.connected = 0;
            connectAction->setText("&Подключиться к стенду");
            syslog("COM прот закрыт",I);
            pTestThread->dev->com_disconnect();
        }
    }
}
