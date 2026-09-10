#include "mainwindow.h"
#include "ui_mainwindow.h"
//#include <abstractserial.h>
#include <iostream>
#include <QDebug>
#include "ctype.h"
#include <QSerialPort>
#include "BercutThread.h"
#include "telnet.h"


void MainWindow::bercut_test_start(void){
    qDebug() << "bercut_test_start";
    pTestThread->bercut_start(prog_sett.bercut_test_type);
    ui->bercut_start->setDisabled(true);
    ui->gen_result->setText("<font color = \"grey\">Тест выполняется...</font>");

    qDebug() << "running = " << pTestThread->pDataTestThread->is_running();
    if(pTestThread->pDataTestThread->is_running())
    {
        pTestThread->pDataTestThread->bercut_running = 1;
    }
}

void MainWindow::bercut_timer_timeout(){
    pTestThread->pBercutThread->timer_timeout();
}

void MainWindow::bercut_test_stop(int status){
    qDebug() << "bercut_test_stop";
    pTestThread->pBercutThread->bercut_stop();
    ui->bercut_start->setDisabled(false);
    if(status){
        ui->gen_result->setText("<font color = \"green\">Тест пройден</font>");
    }
    else{
        ui->gen_result->setText("<font color = \"red\">Тест не пройден</font>");
    }

    //если в тесте передачи данных
    if(pTestThread->pDataTestThread->is_running()){
        //передаём флаг остановки теста
        pTestThread->pDataTestThread->bercut_running = 0;
        if(status)
            pTestThread->pDataTestThread->bercut_status = TEST_OK;
        else
            pTestThread->pDataTestThread->bercut_status = TEST_FAIL;
    }
}

void MainWindow::bercut_test_stop_btn(void){
    bercut_test_stop(false);
}

void MainWindow::bercut_com_connect(){    
    bercut_serial = new QSerialPort();
    bercut_serial->setPortName(prog_sett.bercut_com_port_name);
    qDebug() << "bercut_com_connect" << prog_sett.bercut_com_port_name;
    if (bercut_serial->open(QIODevice::ReadWrite)) {
        bercut_serial->setBaudRate(QSerialPort::Baud115200);
        bercut_serial->setDataBits(QSerialPort::Data8);
        bercut_serial->setParity(QSerialPort::NoParity);
        bercut_serial->setStopBits(QSerialPort::OneStop);
        bercut_serial->setFlowControl(QSerialPort::NoFlowControl);
        if(bercut_serial->isOpen()){
            qDebug()<<"BercutCOM порт открыт";
            prog_sett.bercut_connected = 1;
            connect(bercut_serial, SIGNAL(readyRead()),SLOT(bercutSerialRecieve()));
            syslog("BercutCOM порт открыт",I);
            ui->bercut_stop->setDisabled(true);
        }
    }
    else{
        prog_sett.bercut_connected = 0;
        qDebug()<<"Ошибка открытия COM порта Генератора трафика";
        syslog("Ошибка открытия COM порта Генератора трафика",E);
        ui->bercut_start->setDisabled(true);
        ui->bercut_stop->setDisabled(true);
    }
}

void MainWindow::bercut100_com_connect(){
    bercut100_serial = new QSerialPort();
    bercut100_serial->setPortName(prog_sett.bercut100_com_port_name);
    qDebug() << "bercut100_com_connect" << prog_sett.bercut100_com_port_name;
    if (bercut100_serial->open(QIODevice::ReadWrite)) {
        bercut100_serial->setBaudRate(QSerialPort::Baud115200);
        bercut100_serial->setDataBits(QSerialPort::Data8);
        bercut100_serial->setParity(QSerialPort::NoParity);
        bercut100_serial->setStopBits(QSerialPort::OneStop);
        bercut100_serial->setFlowControl(QSerialPort::NoFlowControl);
        if(bercut100_serial->isOpen()){
            qDebug()<<"Bercut100COM порт открыт";
            //prog_sett.bercut100_connected = 1;
            connect(bercut100_serial, SIGNAL(readyRead()),SLOT(bercutSerialRecieve()));
            syslog("Bercut100COM порт открыт",I);
        }
    }
    else{
        prog_sett.bercut_connected = 0;
        qDebug()<<"Ошибка открытия Bercut100COM порта";
        syslog("Ошибка открытия Bercut100COM порта",E);
    }
}

void MainWindow::bercutSerialRecieve() {
    QString str;
    int len;

    memset(bercut_com_data,0,sizeof(bercut_com_data));

    // Заполняем массив данными
    len = bercut_serial->read(bercut_com_data,64000);
    qDebug() << bercut_com_data;

    str.fromLocal8Bit(bercut_com_data,len);
    pTestThread->pBercutThread->bercutSerialRecieve(bercut_com_data);
}

void MainWindow::bercutSerialClear() {
    memset(bercut_com_data,0,sizeof(bercut_com_data));
}

void TestThread::bercutSerialRecieve() {
    qDebug() << "TestThread::bercutSerialRecieve";
}

void BercutThread::bercutSerialRecieve(QString str) {
    strcat(bercut_com_data,str.toLocal8Bit().data());
}

void MainWindow::bercutSerialWrite(QString data) {
    qDebug()<<"bercutSerialWrite" << data;
    bercut_serial->write(data.toLocal8Bit().data(),data.length());
}

void TestThread::bercut_start(int test_type){
    pBercutThread->bercut_start(test_type);
}

//взаимодействие с Беркут-ЕТ
BercutThread::BercutThread(){
    qDebug() << "BercutThread constructor";
}

BercutThread::~BercutThread() {
    qDebug() << "BercutThread destructor";

}

void BercutThread::process(){
    QString str;
    double error;

    login_ok = false;
    while(1){
        if(running==1)
        {
            emit bercutSerialClear();
            memset(bercut_com_data,0,sizeof(bercut_com_data));
            Pause(1000);

            emit syslog("Запуск теста", I);

            qDebug() << "bercut process start";
            if(!login_ok)
            {
                emit bercutSerialWrite("admin\r\n");
                Sleep(200);
                emit bercutSerialWrite("PleaseChangeTheAdminPassword\r\n");
                Sleep(200);
                emit bercutSerialWrite("et\r\n");
                Sleep(200);
                emit bercutSerialWrite("show version\r\n");
                Sleep(100);
                if(parse_hw_type()==FAIL){
                    emit syslog("Ошибка ответа от прибора. Повторите попытку, если не поможет, перезагрузите Генератор",E);
                    running = 0;
                    continue;
                }

                login_ok = true;
            }
            Sleep(100);
            emit bercutSerialWrite("config\r\n");
            Sleep(100);
            if(test_type == BERCUT_RFC2544)
                config_rfc2544();
            else if(test_type == BERCUT_TXGEN)
                config_txgen(BERCUT_DIR_A2B);
            Sleep(100);
            emit bercutSerialWrite("exit\r\n");
            Sleep(100);

            if(test_type == BERCUT_RFC2544){
                start_rfc2544();
            }
            else if(test_type == BERCUT_TXGEN){
                start_txgen();
            }

            if(test_type == BERCUT_RFC2544){
                Sleep(2000);
                while(parse_rfc2544_test_result()==RFC2544_PROCESS){
                    Sleep(2000);
                    emit bercutSerialClear();
                    memset(bercut_com_data,0,sizeof(bercut_com_data));
                    Sleep(100);
                    emit bercutSerialWrite("rfc2544 results show\r\n");
                    Sleep(100);
                    if(rfc2544_result>0){
                        str.sprintf("Тест выполняется %f",rfc2544_result);
                        emit syslog(str,I);
                        qDebug() << str;
                        //для ускорения теста, если меньше 50%, останавливаем тест
                        if(rfc2544_result<50){
                            rfc2544_status = RFC2544_ERROR;
                            break;
                        }
                    }
                }
                switch(rfc2544_status){
                case RFC2544_STOP:
                    emit syslog("Ошибка запуска теста, повторите тест",E);
                    emit bercutTestCompleat(0);
                    break;
                case RFC2544_PROCESS:
                    emit syslog("Тест ещё выполняется",E);
                    emit bercutTestCompleat(0);
                    break;
                case RFC2544_PASSED:
                    if(rfc2544_result >=99.8){
                        emit syslog("Тест пройден",I);
                        emit bercutTestCompleat(1);
                    }
                    else{
                        emit syslog("Тест не пройден",E);
                        emit bercutTestCompleat(0);
                    }

                    break;
                case RFC2544_ERROR:
                    emit syslog("Ошибка теста, повторите тест",E);
                    emit bercutTestCompleat(0);
                    break;
                }
            }
            else if(test_type == BERCUT_TXGEN){
                Pause(12000);
                emit bercutSerialWrite("txgen stop\r\n");
                Sleep(100);
                emit bercutSerialClear();
                memset(bercut_com_data,0,sizeof(bercut_com_data));
                Sleep(100);
                emit bercutSerialWrite("statistics show\r\n");
                Sleep(100);
                parse_test_result();
                if(bercut_port2_tx>1000){
                    error=(std::abs((bercut_port2_tx-bercut_port1_rx)))/bercut_port2_tx;    //fabs -> std::abs
                    qDebug() << "процент потерь" <<error;
                    emit syslog(str.sprintf("Процент потерь %f",error*100),I);

                    if(error < TXGEN_MAX_ERROR){
                        emit bercutSerialClear();
                        memset(bercut_com_data,0,sizeof(bercut_com_data));
                        Sleep(100);
                        //если тест прошёл, меняем направление передачи
                        Sleep(100);
                        emit bercutSerialWrite("config\r\n");
                        Sleep(300);
                        config_txgen(BERCUT_DIR_B2A);
                        Sleep(300);
                        emit bercutSerialWrite("exit\r\n");
                        Sleep(100);

                        //запускаем тест заново
                        emit syslog("Смена направления передачи",I);
                        start_txgen();
                        Sleep(15000);
                        emit bercutSerialWrite("txgen stop\r\n");
                        Sleep(100);
                        emit bercutSerialClear();
                        memset(bercut_com_data,0,sizeof(bercut_com_data));
                        Sleep(300);
                        emit bercutSerialWrite("statistics show\r\n");
                        Sleep(300);
                        parse_test_result();
                        if(bercut_port1_tx>1000){
                            error=(std::abs((bercut_port1_tx-bercut_port2_rx)))/bercut_port1_tx;    //fabs -> std::abs
                            qDebug() << "процент потерь" <<error;
                            emit syslog(str.sprintf("Процент потерь %f",error*100),I);

                            if(error < 0.0002){
                                emit bercutTestCompleat(1);
                            }
                            else{
                                emit bercutTestCompleat(0);
                            }
                        }
                        else{
                            emit syslog("Ошибка, принято менее 1000 Байт",E);
                            emit bercutTestCompleat(0);
                        }

                    }
                    else{
                        emit syslog("Тест не пройден",E);
                        emit bercutTestCompleat(0);
                    }
                }
                else{
                    emit syslog("Ошибка, принято менее 1000 Байт",E);
                    emit bercutTestCompleat(0);
                }

            }
            qDebug() << "bercut process stop";
            running = 0;
        }
        Sleep(1000);
    }
    emit finished();
}

void BercutThread::timer_timeout(void){
    qDebug() << "timer_timeout";
    login_ok = false;
}

void BercutThread::config_rfc2544(){
    Sleep(100);
    emit bercutSerialWrite("rfc2544 topology tx a\r\n");
    Sleep(100);
    emit bercutSerialWrite("rfc2544 topology rx b\r\n");
    Sleep(100);
}

void BercutThread::Pause(int msec)
{
    QElapsedTimer timer;
    timer.start();
    while (timer.elapsed() < msec)
    {
        QThread::msleep(5);
        qApp->processEvents();
    }
}

void BercutThread::start_rfc2544(){
    emit bercutSerialWrite("rfc2544 start\r\n");
    Sleep(5000);
    emit bercutSerialWrite("rfc2544 results show\r\n");
}

void BercutThread::config_txgen(int direction){
    if(direction == BERCUT_DIR_A2B)
        emit bercutSerialWrite("txgen port b\r\n");
    else
        emit bercutSerialWrite("txgen port a\r\n");
}

void BercutThread::start_txgen(){
    emit bercutSerialWrite("txgen stop\r\n");
    Sleep(300);
    emit bercutSerialWrite("statistics clear\r\n");
    Sleep(300);
    emit bercutSerialWrite("txgen start\r\n");
    Sleep(300);
}


#if 0
void BercutThread::process(){
    QString str;
    double error;

    bool login_ok = false;
    while(1){
        if(running==1){

            syslog("Запуск теста",C);
            qDebug() << "bercut process start";
            if(login_ok == false){
                emit bercutSerialWrite("admin\r\n");
                Sleep(200);
                emit bercutSerialWrite("PleaseChangeTheAdminPassword\r\n");
                Sleep(200);
                emit bercutSerialWrite("et\r\n");
                Sleep(200);
                emit bercutSerialWrite("show version\r\n");
                Sleep(100);
                if(parse_hw_type()==FAIL){
                    syslog("Ошибка ответа от прибора",E);
                    running = 0;
                    continue;
                }
                login_ok = true;
            }
            emit bercutSerialWrite("config\r\n");
            Sleep(200);
            emit bercutSerialWrite("txgen port a\r\n");
            Sleep(200);
            emit bercutSerialWrite("txgen rate 100\r\n");
            Sleep(200);
            emit bercutSerialWrite("txgen frame type constant\r\n");
            Sleep(200);
            emit bercutSerialWrite("txgen frame constant 1522\r\n");
            Sleep(200);
            emit bercutSerialWrite("exit\r\n");
            Sleep(200);
            emit bercutSerialWrite("statistics clear\r\n");
            Sleep(200);
            emit bercutSerialWrite("txgen start\r\n");
            Sleep(30000);
            emit bercutSerialWrite("txgen stop\r\n");
            Sleep(200);

            memset(bercut_com_data,0,sizeof(bercut_com_data));
            emit bercutSerialWrite("statistics show\r\n");
            Sleep(200);
            parse_test_result();
            str.sprintf("RX         %li          %li",bercut_port1_rx,bercut_port2_rx);
            syslog(str,I);
            //qDebug() << str;
            str.sprintf("TX         %li          %li",bercut_port1_tx,bercut_port2_tx);
            syslog(str,I);
            //qDebug() << str;

            if(bercut_port1_tx>100000){
                error=(fabs((bercut_port1_tx-bercut_port2_rx)))/bercut_port1_tx;


                qDebug() << "процент потерь" <<error;
                syslog(str.sprintf("Процент потерь %f%",error*100),I);

                if(error < 0.0002){
                    syslog("Смена направления генерации",C);

                    //смена направления генерации
                    emit bercutSerialWrite("config\r\n");
                    Sleep(100);
                    emit bercutSerialWrite("txgen port b\r\n");
                    Sleep(100);
                    emit bercutSerialWrite("exit\r\n");
                    Sleep(100);
                    emit bercutSerialWrite("statistics clear\r\n");
                    Sleep(100);
                    emit bercutSerialWrite("txgen start\r\n");
                    Sleep(30000);
                    emit bercutSerialWrite("txgen stop\r\n");
                    Sleep(100);
                    memset(bercut_com_data,0,sizeof(bercut_com_data));
                    emit bercutSerialWrite("statistics show\r\n");
                    Sleep(100);
                    parse_test_result();
                    str.sprintf("RX         %li          %li",bercut_port1_rx,bercut_port2_rx);
                    syslog(str,I);
                    qDebug() << str;
                    str.sprintf("TX         %li          %li",bercut_port1_tx,bercut_port2_tx);
                    syslog(str,I);
                    qDebug() << str;

                    if(bercut_port2_tx>100000){
                        error=(fabs((bercut_port2_tx-bercut_port1_rx)))/bercut_port2_tx;
                        qDebug() << "процент потерь" <<error;
                        syslog(str.sprintf("Процент потерь %f%",error*100),I);

                        if(error < 0.0002){
                            syslog("Тест пройден",C);
                            bercutTestCompleat(1);
                        }
                        else{
                            syslog("Тест не пройден",E);
                            bercutTestCompleat(0);
                        }
                    }
                    else{
                        syslog("Ошибка измерения, повторите тест",E);
                        bercutTestCompleat(0);
                    }
                }
                else{
                    syslog("Тест не пройден",E);
                    bercutTestCompleat(0);
                }
            }
            else{
                syslog("Ошибка измерения, повторите тест",E);
                bercutTestCompleat(0);
            }


            qDebug() << "bercut process stop";
            running = 0;

        }
        Sleep(1000);
    }
    emit finished();
}

#endif


int BercutThread::parse_rfc2544_test_result(){
    QString str;
    int ptr1,ptr2;
    bool ok;

    qDebug() << "parse_rfc2544_test_result";

    str.append(bercut_com_data);

    ptr1 = str.indexOf("Throughput",0, Qt::CaseSensitive);
    str.remove(0,ptr1+strlen("Throughput"));
    ptr1 = str.indexOf("Throughput",0, Qt::CaseSensitive);
    str.remove(0,ptr1+strlen("Throughput"));
    ptr1 = str.indexOf("Frame Rate,% Mb/s L1 Mb/s L2 Mb/s L3 Mb/s L4 Frm/s Status\r\n",0, Qt::CaseSensitive);
    str.remove(0,ptr1+strlen("Frame Rate,% Mb/s L1 Mb/s L2 Mb/s L3 Mb/s L4 Frm/s Status\r\n"));

    ptr2 = str.indexOf("Latency",0, Qt::CaseSensitive);
    str.remove(ptr2,str.length()-(ptr2+strlen("Latency")));

    qDebug() << str.length() << str;

    if(str.contains("Disabled",Qt::CaseInsensitive))
        rfc2544_status = RFC2544_STOP;
    else if(str.contains("Running",Qt::CaseInsensitive)){
        rfc2544_status = RFC2544_PROCESS;
        ptr1 = str.indexOf(" ",0);
        str = str.right(str.length() - ptr1-1);
        ptr2 = str.indexOf(" ",0);
        if((ptr2-ptr1) <= 1)
            str = str.right(str.length() - ptr2-1);
        ptr1 = str.indexOf(" ",0);
        str = str.left(ptr1);
        qDebug() <<"Process"<< ptr1 <<str;
        rfc2544_result = str.toFloat(&ok);
        if(!ok)
            rfc2544_result = 0;
    }
    else if(str.contains("Passed",Qt::CaseInsensitive)){
        rfc2544_status = RFC2544_PASSED;
        ptr1 = str.indexOf(" ",0);
        str = str.right(str.length() - ptr1-1);
        ptr2 = str.indexOf(" ",0);
        if((ptr2-ptr1) <= 1)
            str = str.right(str.length() - ptr2-1);

        ptr1 = str.indexOf(" ",0);
        str = str.left(ptr1);

        qDebug() <<"Passed"<< ptr1 <<str;
        rfc2544_result = str.toFloat(&ok);
        if(ok)
            rfc2544_status = RFC2544_PASSED;
        else
            rfc2544_status = RFC2544_ERROR;
    }
    else if(str.contains("Cancelled",Qt::CaseInsensitive))
        rfc2544_status = RFC2544_ERROR;

    return rfc2544_status;
}

void BercutThread::parse_test_result(){
    QString str,str_rx1,str_tx1,str_rx2,str_tx2;
    int rx_ptr;
    long int rx1,tx1;
    long int rx2,tx2;
    bool ok;
    qDebug() << bercut_com_data;

    str.append(bercut_com_data);

    rx_ptr = str.indexOf("Rx bytes",0, Qt::CaseInsensitive);
    str.remove(0,rx_ptr+strlen("Rx bytes"));

    //rx1
    str_rx1.clear();
    rx_ptr = 0;
    for(int i=0;i<str.length();i++){
        if(str.at(i).isNumber())
            str_rx1.append(str.at(i));
        else if(str_rx1.length())
            break;
        rx_ptr++;
    }
    rx1 = str_rx1.toLong(&ok,10);
    if(ok){
    }

    //rx2
    str_rx2.clear();
    for(int i=rx_ptr;i<str.length();i++){
        if(str.at(i).isNumber())
            str_rx2.append(str.at(i));
        else if(str_rx2.length())
            break;
    }
    rx2 = str_rx2.toLongLong(&ok,10);

    rx_ptr = str.indexOf("Tx bytes",0, Qt::CaseInsensitive);
    str.remove(0,rx_ptr+strlen("Tx bytes"));

    //tx1
    str_tx1.clear();
    rx_ptr = 0;
    for(int i=0;i<str.length();i++){
        if(str.at(i).isNumber())
            str_tx1.append(str.at(i));
        else if(str_tx1.length())
            break;
        rx_ptr++;
    }
    tx1 = str_tx1.toLong(&ok,10);
    if(ok){
    }

    //tx2
    str_tx2.clear();
    for(int i=rx_ptr;i<str.length();i++){
        if(str.at(i).isNumber())
            str_tx2.append(str.at(i));
        else if(str_tx2.length())
            break;
    }
    tx2 = str_tx2.toLong(&ok,10);

    bercut_port1_rx = rx1;
    bercut_port1_tx = tx1;
    bercut_port2_rx = rx2;
    bercut_port2_tx = tx2;

    qDebug() << "bercut_port1_rx" << bercut_port1_rx;
    qDebug() << "bercut_port1_tx" << bercut_port1_tx;
    qDebug() << "bercut_port2_rx" << bercut_port2_rx;
    qDebug() << "bercut_port2_tx" << bercut_port2_tx;

}

int BercutThread::parse_hw_type(){
    QString str;
    str.append(bercut_com_data);
    if(str.contains("M716",Qt::CaseInsensitive)){
        return OK;
    }
    else
        return FAIL;
}

void BercutThread::bercut_start(int test_type_){
    qDebug() << "bercut_start";
    running = 1;
    test_type = test_type_;
}

void BercutThread::bercut_stop(){
    qDebug() << "bercut_stop";
    running = 0;
}

int BercutThread::bercut_is_running(){
    return running;
}
