#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "stdio.h"
#include "math.h"
#include <QtXml/QtXml>
#include <QtXml/QDomElement>
#include <QFileDialog>
#include <QMessageBox>
#include <QFile>

//сетевое взаисодействие с тестируемым устройством

void MainWindow::create_UDPsocket(int port){
    socket = new QUdpSocket(this);
    QHostAddress hostaddr;
    _port = port;
    hostaddr.setAddress(DUT_IP_ADDR);
    socket->bind(QHostAddress::Any,PSW_PORT);
    connect(socket, SIGNAL(readyRead()), SLOT(read()));
}

void MainWindow::read(void) {
    int i;

    QByteArray datagram;
    datagram.resize(socket->pendingDatagramSize());
    QHostAddress *address = new QHostAddress();
    socket->readDatagram(datagram.data(), datagram.size(), address);
    QDataStream in(&datagram, QIODevice::ReadOnly);

    if(datagram.data()[0]=='C'){
        if(datagram.data()[1]=='1'){
            myDebug() << "connected ";
            pTestThread->nettest.connected = 1;
        }
    }

    in_msg.type = datagram.data()[0];
    if(in_msg.type == PSW_RESPONSE){

        in_msg.dev_type = datagram.data()[1];
        for(i=0;i<4;i++){
            in_msg.ip[i] = datagram.data()[2+i];
        }
    }

    if(datagram.data()[10] == 'm'){
        if(datagram.data()[11] == 'r'){
            ui->test_result_ms->setText("<font color=\"green\">MAC changed: Ok</font>");
        }
    }

}

void MainWindow::replyFinished()
{
    qDebug() << "replyFinished";

    if (QObject::sender() == timerForWebManager)                            // Если слот сработал по таймеру
    {
        qDebug() << "Timeout waiting Webreply";
        webreply->abort();
        pTestThread->setTestPageRequestResult(TestPageRequestResult::FAILURE); //Устанавливаем результат запроса тестовой страницы
    }
    else
    {
        if (timerForWebManager->isActive())
            timerForWebManager->stop();

        if(webreply->error() == QNetworkReply::NoError)
        {
            pTestThread->setTestPageRequestResult(TestPageRequestResult::SUCCESS);  //Устанавливаем результат запроса тестовой страницы
            pTestThread->setTestPageData(webreply->readAll());                      // Передаем данные страницы

        }
        else
        {
            pTestThread->setTestPageRequestResult(TestPageRequestResult::FAILURE); //Устанавливаем результат запроса тестовой страницы
            QString error = QString("Ошибка подключения к %1: %2 %3").arg(DUT_IP_ADDR).arg(webreply->error()).arg(webreply->errorString());

            qDebug() << QString("QNetworkReply error: %1 %2").arg(webreply->error()).arg(webreply->errorString());
            if (webreply->error() == 204)
            {
                syslog("Устройство требует аутентификации", E);
            }
        }
    }

    pTestThread->setWebmanagerFinished();                                   // Помечаем, что webmanager окончил свою работу.

    connect(webreply, &QNetworkReply::finished, this, &QObject::deleteLater);
}

void MainWindow::GetIrpStatusReplyFinished()
{
    qDebug() << "IrpStatusReplyFinished";

    if(webreply->error() == QNetworkReply::NoError)
    {
        auto result = webreply->readAll();
        int irpStatus = result.toInt();
        pTestThread->SetIrpStatus(irpStatus);
        qDebug("irpStatus %d", irpStatus);
    }
    else
    {
        QString error = QString("Ошибка подключения к %1: %2 %3").arg(DUT_IP_ADDR).arg(webreply->error()).arg(webreply->errorString());
        qDebug() << QString("QNetworkReply error: %1 %2").arg(webreply->error()).arg(webreply->errorString());
    }

    pTestThread->setWebmanagerFinished();                                   // Помечаем, что webmanager окончил свою работу.

    connect(webreply, &QNetworkReply::finished, this, &QObject::deleteLater);
}

void MainWindow::GetUpsVoltageReplyFinished()
{
    qDebug() << "GetUpsVoltageReplyFinished";

    if(webreply->error() == QNetworkReply::NoError)
    {
        auto result = webreply->readAll();
        double upsVoltage = result.toDouble();
        pTestThread->SetUpsVoltage(upsVoltage);
        qDebug("upsVoltage %f", upsVoltage);
    }
    else
    {
        QString error = QString("Ошибка подключения к %1: %2 %3").arg(DUT_IP_ADDR).arg(webreply->error()).arg(webreply->errorString());
        qDebug() << QString("QNetworkReply error: %1 %2").arg(webreply->error()).arg(webreply->errorString());
    }

    pTestThread->setWebmanagerFinished();                                   // Помечаем, что webmanager окончил свою работу.

    connect(webreply, &QNetworkReply::finished, this, &QObject::deleteLater);
}

void MainWindow::GetUpsStatusReplyFinished()
{
    qDebug() << "upsStatusReplyFinished";

    if(webreply->error() == QNetworkReply::NoError)
    {
        auto result = webreply->readAll();
        int upsStatus = result.toInt();
        pTestThread->SetUpsStatus(upsStatus);
        qDebug("upsStatus %d", upsStatus);
    }
    else
    {
        QString error = QString("Ошибка подключения к %1: %2 %3").arg(DUT_IP_ADDR).arg(webreply->error()).arg(webreply->errorString());
        qDebug() << QString("QNetworkReply error: %1 %2").arg(webreply->error()).arg(webreply->errorString());
    }

    pTestThread->setWebmanagerFinished();                                   // Помечаем, что webmanager окончил свою работу.

    connect(webreply, &QNetworkReply::finished, this, &QObject::deleteLater);
}

void MainWindow::waitHelpFinished(){
    if(webreply->error()==QNetworkReply::NoError){
        pTestThread->nettest.help_loaded = true;
        syslog("Файлы справки загрузились",I);
    }
    else{
        pTestThread->nettest.help_loaded = false;
        syslog("Ошибка подключения к 192.168.0.1",E);
    }
    pTestThread->set_test_shtml(true);
}

void MainWindow::test_mode_pressed()
{
    if(prog_sett.test_mode_state != 0 )
    {
        ui->test_mode_btn->setText("Вкл. Test mode");

        prog_sett.test_mode_state = 0;

        //отключение тестового режима
        set_sw_test_mode(prog_sett.test_mode_state);

        if(prog_sett.switch_state == 1)
        {
            telnet_config_chain(test_config.switch_config_normal);
            telnet_config_sw(test_config.data_test_ports,prog_sett.port_dut);
        }
    }
    else
    {
        ui->test_mode_btn->setText("Выкл. Test mode");
        //включение тестового режима
        prog_sett.test_mode_state = 1;
        //DuT to test mode
        set_sw_test_mode(prog_sett.test_mode_state);

        if(prog_sett.switch_state == 1)
            telnet_config_chain(test_config.switch_config_chain); //установка промежуточный коммутатор в режим шлейфа
    }
}

//перевод в тестовый режим коммутаторы для проверки передачи данных шлейфом и телепорты для проверки выхода
void MainWindow::set_sw_test_mode(int state){
    QString request;
    request.sprintf("http://%s/test.shtml?test=%d",DUT_IP_ADDR,state);
    qDebug() << request;

    reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));  // Отправляем строку на устройство
    connect(reportreply, SIGNAL(finished()), SLOT(setTestModeFinished()));
}

//управление выходом на плате RPS-1 при проверке UPS+
void MainWindow::set_ups_plus_out(int state){
    QString request;
    request.sprintf("http://%s/test.shtml?set_mb_output=%d",DUT_IP_ADDR,state);
    qDebug() << request;
    reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));  // Отправляем строку на устройство
    connect(reportreply, SIGNAL(finished()), SLOT(setTestModeFinished()));
}

void MainWindow::setTestModeFinished()
{
//    if(reportreply->error() == QNetworkReply::NoError)
//    {
//        //syslog("Тестовый режим включен", I);
//        //NOTE Тут нужно проверять состояние реальным запросом к коммутатору.
//        if (prog_sett.test_mode_state == 1)
//            syslog("Тестовый режим включен", I);
//        else
//            syslog("Тестовый режим выключен", I);
//    }
//    else
//    {
//        syslog(QString("Установка Test mode. Ответа от устройства нет. Код ошибки: %1.").arg(reportreply->error()),E);

        //NOTE Тут нужно проверять состояние реальным запросом к коммутатору.

        //          if (prog_sett.test_mode_state == 1)
        //          {                                                      // Если пытались включить режим
        //              prog_sett.test_mode_state = 0;                     // Устанавливаем статус установки в 0
        //              ui->test_mode_btn->setText("Вкл. Test mode");      // Возвращаем надпись на кнопке
        //          }
        //          else
        //          {                                                      // Если пытались вЫключить режим
        //              prog_sett.test_mode_state = 1;                     // Устанавливаем статус установки в 1
        //              ui->test_mode_btn->setText("Выкл. Test mode");     // Возвращаем надпись на кнопке
        //          }
    //}
}

//установка тестового режима для TLP
void MainWindow::set_tlp_test_mode(int state){
    QString request;
    request.sprintf("http://%s/test.shtml?test=%d",DUT_IP_ADDR,state);
    reportmanager->get(QNetworkRequest(QUrl(request)));
    qDebug() << request;
}

//установка значения выхода
void MainWindow::set_tlp_output_state(int port,int state){
    QString request;
    request.sprintf("http://%s/test.shtml?DO%d=%d",DUT_IP_ADDR,port,state);
    reportmanager->get(QNetworkRequest(QUrl(request)));
    qDebug() << request;
}

void MainWindow::PoeDisable(int lineIndex)
{
    QString url = QString("http://%1/api/setPoe?port=%2&state=0").arg(DUT_IP_ADDR).arg(lineIndex);
    qDebug() << QString("Debug. Get %1").arg(url);

    QNetworkRequest request;
    request.setUrl(QUrl(url));

    if(webmanager->networkAccessible() == QNetworkAccessManager::NotAccessible)
    {
        delete webmanager;
        webmanager = new QNetworkAccessManager(this);
    }

    QNetworkConfigurationManager configManager;
    webmanager->setConfiguration(configManager.defaultConfiguration());

    webreply = webmanager->get(request);
}
