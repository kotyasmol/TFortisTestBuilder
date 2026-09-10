#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "stdio.h"
#include "math.h"
#include <QtXml/QtXml>
#include <QtXml/QDomElement>
#include <QFileDialog>
#include <QMessageBox>
#include <QFile>
#include "qttelnet.h"
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include "telnet.h"

//Telnet для управления промежуточным коммутатором

telnet::telnet(QString ip, QString login_, QString pass_){
    telnet_dev = new QtTelnet;

    connect(telnet_dev, SIGNAL(message(QString)), //warning | const QString & -> QString
            this, SLOT(telnetMessage(QString)));
    connect(telnet_dev, SIGNAL(loginRequired()),
            this, SLOT(telnetLoginRequired()));
    connect(telnet_dev, SIGNAL(loginFailed()),
            this, SLOT(telnetLoginFailed()));
    connect(telnet_dev, SIGNAL(loggedOut()),
            this, SLOT(telnetLoggedOut()));
    connect(telnet_dev, SIGNAL(loggedIn()),
            this, SLOT(telnetLoggedIn()));
    connect(telnet_dev, SIGNAL(connectionError(QAbstractSocket::SocketError)),
            this, SLOT(telnetConnectionError(QAbstractSocket::SocketError)));

    telnet_timer = new QTimer;
    telnet_timer->setInterval(10000);
    connect(telnet_timer, SIGNAL(timeout()),  SLOT(telnet_timer_stop()));
    telnet_timer->start();

    qDebug()<<"telnet_connect"<<ip;
    telnet_ip = ip;
    login = login_;
    pass = pass_;
    telnet_dev->connectToHost(telnet_ip);
    telnet_dev->login(login,pass);
}

telnet::~telnet() {
    qDebug() << "destructor ~telnet()";
    telnet_dev->deleteLater();
}

void telnet::telnet_timer_stop(){
    qDebug() << "telnet_timer_stop";
    if(connected==0){
        telnet_connect(telnet_ip);
    }
    telnet_timer->setInterval(TELNET_INTERVAL);
}

void telnet::telnet_connect(QString host){
    qDebug()<<"telnet_connect"<<host;
    telnet_dev->connectToHost(host);
    telnet_dev->login(login,pass);
}

void telnet::set_telnet_master_ports(int a, int b, int sw, int sfp1, int sfp2){
    port_a = a;
    port_b = b;
    port_sw = sw;
    port_sfp1 = sfp1;
    port_sfp2 = sfp2;
}

void telnet::telnetMessage(const QString str){
    qDebug()<<"telnetMessage"<<str;
    connected = 1;
    telnet_timer->start();//restart timer
}

void telnet::telnetLoginRequired(){
    qDebug()<<"telnetLoginRequired";
}
void telnet::telnetLoginFailed(){
    qDebug()<<"telnetLoginFailed";
    connected = 0;
}
void telnet::telnetLoggedOut(){
    qDebug()<<"telnetLoggedOut";
    connected = 0;
}
void telnet::telnetLoggedIn(){
    qDebug()<<"telnetLoggedIn";
}

void telnet::telnetConnectionError(QAbstractSocket::SocketError err){
    qDebug()<<"telnetConnectionError"<<err;
    if(err != QAbstractSocket::SocketError::RemoteHostClosedError)
        emit syslog("Нет подключения по Telnet к технологическому коммутатору",E);
    connected = 0;
}

void telnet::telnetWrite(QString data){
    telnet_dev->sendData(data);
    qDebug() << "telnetWrite" << data;
}

//настройка пар для тестирования передачей данных
void telnet::telnet_config_pair(int in,int out){
    QString data;
    if(is_connected()) {
        data.sprintf("Настройка портов промежуточного коммутатора %d-%d",in,out);
        emit syslog(data, I);

        qDebug() << "telnet_config_pair" << in << out;

        data.sprintf("config vlan vlanid 2 add forbidden 1-22\r\n");
        telnetWrite(data);
        data.sprintf("config vlan vlanid 3 add forbidden 1-22\r\n");
        telnetWrite(data);

        data.sprintf("config vlan vlanid 2 delete 1-22\r\n");
        telnetWrite(data);
        data.sprintf("config vlan vlanid 3 delete 1-22\r\n");
        telnetWrite(data);

        telnetWrite("\r\n\r\n");
        telnetWrite("clear fdb all\r\n");
        for(int i=0;i<PORT_NUM;i++){
            if(i==in){
                data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",in+1,in+1,port_a);
                telnetWrite(data);
                data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",port_a,port_a,in+1);
                telnetWrite(data);

                data.sprintf("config vlan vlanid 2 add untagged %d,%d\r\n",in+1,port_a);
                telnetWrite(data);

            }
            else if(i == out){
                data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",out+1,out+1,port_b);
                telnetWrite(data);
                data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",port_b,port_b,out+1);
                telnetWrite(data);

                data.sprintf("config vlan vlanid 3 add untagged %d,%d\r\n",out+1,port_b);
                telnetWrite(data);

            }
            else{
                data.sprintf("config traffic_segmentation %d forward_list null\r\n",i+1);
                telnetWrite(data);
            }
        }

        if(in == port_sfp1 && out == port_sfp2){
            data.sprintf("config ports %d,%d state enable\r\n",port_sfp1, port_sfp2);
            telnetWrite(data);
            data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",port_sfp1,port_sfp1,port_a);
            telnetWrite(data);
            data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",port_sfp2,port_sfp2,port_b);
            telnetWrite(data);

            data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",port_a,port_sfp1,port_a);
            telnetWrite(data);
            data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",port_b,port_sfp2,port_b);
            telnetWrite(data);

            data.sprintf("config vlan vlanid 2 add untagged %d,%d\r\n",port_sfp1,port_a);
            telnetWrite(data);
            data.sprintf("config vlan vlanid 3 add untagged %d,%d\r\n",port_sfp2,port_b);
            telnetWrite(data);
        }
        else{
            data.sprintf("config traffic_segmentation %d forward_list null\r\n",port_sfp1);
            telnetWrite(data);
            data.sprintf("config traffic_segmentation %d forward_list null\r\n",port_sfp2);
            telnetWrite(data);
        }

    }
    else{
        emit syslog("Нет подключения по Telnet к технологическому коммутатору",E);
        qDebug() << "Нет подключения по Telnet к технологическому коммутатору";
    }
}

//настройка портов для доступа к web интерфейсу
//ports - порты коммутатора
//порт промежуточного коммутатора, в который подключается сетевая карта ПК
void telnet::telnet_config_sw(int* ports,int port_sw){
    QString data,ports_list,tmp;
    int ok = 0;

    qDebug() << "telnet_config_sw ";

    data.sprintf("config vlan vlanid 2 add forbidden 1-22\r\n");
    telnetWrite(data);
    data.sprintf("config vlan vlanid 3 add forbidden 1-22\r\n");
    telnetWrite(data);

    data.sprintf("config vlan vlanid 2 delete 1-22\r\n");
    telnetWrite(data);
    data.sprintf("config vlan vlanid 3 delete 1-22\r\n");
    telnetWrite(data);

    telnetWrite("\r\n\r\nclear fdb all\r\n");
    for(int i=0;i<PORT_NUM;i++){
        if(ports[i] && ok==0){
            data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",i+1,i+1,port_sw);
            telnetWrite(data);
            ports_list.append(tmp.sprintf(",%d",i+1));
            ok=1;

            data.sprintf("config vlan vlanid 2 add untagged %d,%d\r\n",i+1,port_sw);
            telnetWrite(data);
        }
        else{
            data.sprintf("config traffic_segmentation %d forward_list null\r\n",i+1);
            telnetWrite(data);
        }
    }
    data.sprintf("config traffic_segmentation %d forward_list null\r\n",port_a);
    telnetWrite(data);
    data.sprintf("config traffic_segmentation %d forward_list null\r\n",port_b);
    telnetWrite(data);



    data.sprintf("config traffic_segmentation %d forward_list %d ",port_sw,port_sw);
    data.append(ports_list);
    data.append("\r\n");
    telnetWrite(data);
}

void telnet::telnet_config_chain(QString str){
    qDebug() << "telnet_config_chain";
    telnetWrite(str);
}


void MainWindow::telnet_config_pair(int rx,int tx){
    QString str;
    syslog(str.sprintf("Направление передачи %d->%d",tx+1,rx+1),I);
    if(test_config.ports_speed[rx]==1000 && test_config.ports_speed[tx]==1000)
        telnet_dev->telnet_config_pair(prog_sett.port_sfp1,prog_sett.port_sfp2);
    else
        telnet_dev->telnet_config_pair(rx,tx);

}
void MainWindow::telnet_config_sw(int *ports,int port_sw){
    qDebug() << "MainWindow::telnet_config_sw";
    syslog("Настройка портов промежуточного коммутатора",I);
    telnet_dev->telnet_config_sw(ports,port_sw);
}

void MainWindow::telnet_config_chain(QString str){
    qDebug() << "MainWindow::telnet_config_chain";

    //загружаем конфигурацию, заменяем переменные:
    //GEN_A
    //GEN_B
    //SFP1
    //SFP2
    //MAN_DUT
    if(str.contains("GEN_A",Qt::CaseInsensitive))
    {
        str.replace("GEN_A",QString("%1").arg(prog_sett.port_a),Qt::CaseInsensitive);
    }
    if(str.contains("GEN_B",Qt::CaseInsensitive))
    {
        str.replace("GEN_B",QString("%1").arg(prog_sett.port_b),Qt::CaseInsensitive);
    }
    if(str.contains("SFP1",Qt::CaseInsensitive))
    {
        str.replace("SFP1",QString("%1").arg(prog_sett.port_sfp1),Qt::CaseInsensitive);
    }
    if(str.contains("SFP2",Qt::CaseInsensitive))
    {
        str.replace("SFP2",QString("%1").arg(prog_sett.port_sfp2),Qt::CaseInsensitive);
    }
    if(str.contains("MAN_DUT",Qt::CaseInsensitive))
    {
        str.replace("MAN_DUT",QString("%1").arg(prog_sett.port_dut),Qt::CaseInsensitive);
    }

    telnet_dev->telnet_config_chain(str);
}

void telnet::telnet_first_config(int port_sw){
    QString data;
    for(int i=1;i<PORT_NUM;i++){
        data.sprintf("config traffic_segmentation %d forward_list null\r\n",i+1);
        telnetWrite(data);
    }
    data.sprintf("config traffic_segmentation %d forward_list %d,%d\r\n",1,1,port_sw);
    telnetWrite(data);
}


bool telnet::is_connected(){
    return connected;
}
