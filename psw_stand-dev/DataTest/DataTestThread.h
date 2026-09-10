#ifndef GENTHREAD_H
#define GENTHREAD_H
#include <QThread>
#include <QObject>
#include <pcap.h>
#include <QDateTime>
#include "../telnet.h"

#define BERCUT_MAX_LOST 10//максимальное сисло потерянных пакетов,
//с которыми тест считаем пройденным

struct port_if_t{
    unsigned char ip[4];
    unsigned char mac[6];
    QString if_name;
    int valid;
};

struct ports_pair_t{
    int in;//rx interface num
    int out;//tx interface num
    int in_valid;//valid flag
    int out_valid;//valid flag
    QString in_ip;
    QString out_ip;
};

struct capture_result1_t{
    long transmitted_pkts;
    long recieved_bytes;//число принятых байт
    long recieved_pkts;//число принятых пакетов
    long tiemout_pkts;//число непринятых пакетов
    QDateTime start_time;//начало теста
    QDateTime stop_time;//завершение теста
    long speed;
};

enum port_status_t{
    NO_TEST,
    WAIT_TEST,
    TEST_OK,
    TEST_FAIL
};

class GenThread: public QThread {
    Q_OBJECT
public:
    GenThread();
    virtual ~GenThread();
    void set_from(port_if_t from);
    void set_to(port_if_t to);
    port_if_t get_from(void);
    port_if_t get_to(void);
    long unsigned get_transmitted_pkt(void);
    void setStartFlag(int flag);

    port_if_t from;
    port_if_t to;

public slots:
    void process();
    void stop();
    void start_();

signals:
    void generator_print_msg(QString str);

private:
    void make_packet_data(port_if_t *from, port_if_t *to);
    int running;

    pcap_t *pcapd_gen;
    unsigned char packet[65535];
    struct capture_result1_t capture_result;
    int start_flag;
    void syslog(QString text);

};

class RcvThread: public QThread {
    Q_OBJECT
public:
    RcvThread();
    virtual ~RcvThread();

    void set_to(/*pcap_if_t *to*/port_if_t to);
    /*pcap_if_t * */port_if_t get_to(void);
    void setStartFlag(int flag);
    long unsigned get_recieved_pkt(void);
    QDateTime get_start_datetime();
    QDateTime get_stop_datetime();
    int running;
    port_if_t to;

public slots:
    void process();
    void stop();
    void start_();

private:
    int start_capture(port_if_t *to);
    pcap_t *fp;
    pcap_t *pcapd_capt;
    struct capture_result1_t capture_result;
    int start_flag;
    void syslog(QString text);
};

class DataTestThread: public QThread {
    Q_OBJECT
public:
    DataTestThread();
    virtual ~DataTestThread();

    void data_start();

    void set_type(int type);
    void set_ports(ports_pair_t *ports_);
    void get_ports(ports_pair_t *ports_);
    void get_card_if_str(pcap_if_t *addr,QString *str);
    void get_card_pair_str(ports_pair_t ports_,QString *str);
    void get_card_str(int port,QString *str);

    int get_ports_status(int port);
    void set_ports_status(int port, port_status_t status);
    long unsigned get_transmitted_pkt(int port);
    long unsigned get_recieved_pkt(int port);
    long unsigned get_recieved_speed(int port);

    bool is_running(void);
    bool is_finished(void);
    int running;
    int bercut_running;
    port_status_t bercut_status;
    int ports_status[100];
    telnet *telnet_dev;

public slots:
    void process();
    void stop();

signals:
    void finished();
    void generator_print_msg(QString str);
    void telnet_config_pair(int, int);
    void telnet_config_sw(int *, int);
    void telnet_config_chain(QString str);
    void bercut_start();
    void set_bercut_port_state(int,int);

private:
    ports_pair_t ports[100];
    int test_type;
    pcap_if_t *alldev;
    GenThread *pGenerThread;
    RcvThread *pRecieveThread;
    struct capture_result1_t capture_result[100];
    bool data_test_start;
    void syslog(QString text);

};

#endif // GENTHREAD_H
