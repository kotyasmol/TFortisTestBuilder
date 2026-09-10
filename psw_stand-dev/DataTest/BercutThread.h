#ifndef BERCUTTHREAD_H
#define BERCUTTHREAD_H
#include <QThread>
#include <QObject>
#include <QSerialPort>
#include "../telnet.h"

#define RFC2544_STOP        0
#define RFC2544_PROCESS     1
#define RFC2544_PASSED      2
#define RFC2544_ERROR       3

//тип теста
#define BERCUT_RFC2544      0
#define BERCUT_TXGEN        1

//установка направления передачи
#define BERCUT_DIR_A2B      0
#define BERCUT_DIR_B2A      1

#define TXGEN_MAX_ERROR     0.1//0.002

class BercutThread: public QThread {
    Q_OBJECT
public:
    BercutThread();
    virtual ~BercutThread();
    void bercut_stop();
    int bercut_is_running();
    void bercutSerialRecieve(QString str);

public slots:
    void process();
    void bercut_start(int test_type_);
    void timer_timeout();

signals:
    void finished();
    void syslog(QString str,int level);
    void bercutSerialWrite(QString data);
    void bercutSerialClear();
    void bercutTestCompleat(int);

private:
    void parse_test_result();
    int parse_rfc2544_test_result();
    int parse_hw_type();
    void config_txgen(int direction);
    void start_txgen();
    void start_rfc2544();
    void config_rfc2544();
    int running;
    int test_type;
    char bercut_com_data[64000];
    long int bercut_port1_rx;
    long int bercut_port1_tx;
    long int bercut_port2_rx;
    long int bercut_port2_tx;

    int rfc2544_status;
    float rfc2544_result;
    //telnet *telnet_dev;
    //QTimer *timer;
    bool login_ok;

    //! Пауза без блокировки
    void Pause(int msec);
};

#endif // BERCUTTHREAD_H
