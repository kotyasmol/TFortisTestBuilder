#ifndef TELNET_H
#define TELNET_H

#include <QThread>
#include <QObject>
#include <QModbusDevice>
#include <QModbusClient>
#include <QModbusRtuSerialMaster>
#include <QTimer>
#include <QtTelnet>

#define TELNET_INTERVAL 1000

class telnet:public QThread {
    Q_OBJECT
public:
    telnet(QString ip, QString login_, QString pass_);
    virtual ~telnet();
    bool is_connected();
    void telnet_config_pair(int in,int out);
    void telnet_config_sw(int* ports,int port_sw);
    void telnet_config_chain(QString);
    void telnet_first_config(int port_sw);
    void telnet_connect(QString host);
    void set_telnet_master_ports(int a, int b, int sw,int sfp1, int sfp2);

signals:
    void syslog(QString,int);
public slots:
    void telnetMessage(const QString str);
    void telnetLoginRequired();
    void telnetLoginFailed();
    void telnetLoggedOut();
    void telnetLoggedIn();
    void telnetConnectionError(QAbstractSocket::SocketError err);
    void telnetWrite(QString data);
    void telnet_timer_stop();

private:
    QtTelnet *telnet_dev;
    QTimer *telnet_timer;
    QString telnet_ip;
    int port_a;
    int port_b;
    int port_sw;
    int port_sfp1;
    int port_sfp2;
    QString login;
    QString pass;
    int connected;
};

#endif // TELNET_H
