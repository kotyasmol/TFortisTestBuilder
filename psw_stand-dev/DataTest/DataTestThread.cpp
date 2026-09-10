#include <QDebug>
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

#include "DataTestThread.h"
#include "telnet.h"

static unsigned short Crc16_1(unsigned char * pcBlock, unsigned short len);
//по номеру в списке интерфейсов получаем указатель на интерфейс

/***********************ДАТА ТЕСТ*************************/
DataTestThread::DataTestThread(){
    //QString str; //unused
    data_test_start = 0;
    // Retrieve the device list
    //syslog("DataTestThread constructor");

    QThread* gener_thread = new QThread;
    pGenerThread = new GenThread();
    pGenerThread->moveToThread(gener_thread);
    connect(gener_thread, SIGNAL(started()), pGenerThread, SLOT(process()));
    connect(pGenerThread, SIGNAL(finished()), gener_thread, SLOT(quit()));
    connect(pGenerThread, SIGNAL(finished()), pGenerThread, SLOT(deleteLater()));
    connect(pGenerThread, SIGNAL(finished()), gener_thread, SLOT(deleteLater()));
    gener_thread->start();

    QThread* recieve_thread = new QThread;
    pRecieveThread = new RcvThread();
    pRecieveThread->moveToThread(recieve_thread);
    connect(recieve_thread, SIGNAL(started()), pRecieveThread, SLOT(process()));
    connect(pRecieveThread, SIGNAL(finished()), recieve_thread, SLOT(quit()));
    connect(pRecieveThread, SIGNAL(finished()), pRecieveThread, SLOT(deleteLater()));
    connect(pRecieveThread, SIGNAL(finished()), recieve_thread, SLOT(deleteLater()));
    recieve_thread->start();
}

DataTestThread::~DataTestThread() {
    pcap_freealldevs(alldev);
}

void DataTestThread::data_start(){
    qDebug() << "DataTestThread::data_start";
    data_test_start = true;
    running = 1;
}

void DataTestThread::process(){
    running = 0;
    QString str;
    quint64 time_interval;

    while(1)
    {
        if(data_test_start)
        {
            //syslog("run DataTestThread+++++++++++++++++++++++++++");

            for(int i=0;i<PORT_NUM;i++){
                capture_result[i].transmitted_pkts = 0;
                capture_result[i].recieved_pkts = 0;
                capture_result[i].tiemout_pkts = 0;
                ports_status[i] = WAIT_TEST;
            }

            //syslog("Generation Start");

            if(test_type == TYPE_SOFT_GEN){

                for(int i=0;i<PORT_NUM;i++){

                    if(ports[i].in_valid && ports[i].out_valid){

                        set_ports_status(i,TEST_FAIL);


                        qDebug() << "TYPE_SOFT_GEN";

                        qDebug() << "start" << i << ports[i].in_ip << ports[i].out_ip;
                        emit generator_print_msg("Запуск генерации");

                        pGenerThread->set_from(get_card_if_ip(ports[i].out_ip));
                        pGenerThread->set_to(get_card_if_ip(ports[i].in_ip));
                        pRecieveThread->set_to(get_card_if_ip(ports[i].in_ip));

                        if(get_card_if_ip(ports[i].out_ip).valid==0   ||
                                get_card_if_ip(ports[i].in_ip).valid==0 ||
                                get_card_if_ip(ports[i].in_ip).valid ==0){
                            running = 0;
                            data_test_start = false;
                            break;
                        }

                        pRecieveThread->start_();
                        Sleep(1);
                        pGenerThread->start_();

                        int cnt = 0;
                        while(pRecieveThread->running){
                            Sleep(1);
                            cnt++;
                            if(cnt>15000 || pRecieveThread->get_recieved_pkt() > CAPTURE_LEN )
                                break;
                        }
                        pGenerThread->stop();
                        pRecieveThread->stop();

                        //get status
                        capture_result[i].recieved_pkts = pRecieveThread->get_recieved_pkt();
                        capture_result[i].transmitted_pkts = pGenerThread->get_transmitted_pkt();
                        capture_result[i].stop_time = pRecieveThread->get_stop_datetime();
                        capture_result[i].start_time = pRecieveThread->get_start_datetime();

                        qDebug() << "RX" << capture_result[i].recieved_pkts;
                        qDebug() << "TX" << capture_result[i].transmitted_pkts;

                        if(capture_result[i].recieved_pkts < CAPTURE_LEN){
                            str.sprintf("Ошибка. Повторный запуск... ");
                            syslog(str);
                            set_ports_status(i,TEST_FAIL);
                        }
                        else{
                            time_interval = capture_result[i].stop_time.toMSecsSinceEpoch()-
                                    capture_result[i].start_time.toMSecsSinceEpoch();
                            capture_result[i].speed = ((capture_result[i].recieved_pkts*1472000)/(time_interval*1024));
                            str = "Успешно";
                            syslog(str);
                            set_ports_status(i,TEST_OK);
                        }
                        Sleep(1000);
                        qDebug() << "stop";
                    }
                }
            }
            else if(test_type == TYPE_HARD_GEN)
            {
                for(int i=0;i<PORT_NUM;i++)
                {
                    if(ports[i].in_valid && ports[i].out_valid)
                    {
                        set_ports_status(i, TEST_FAIL);

                        //настраиваем порты промежуточного коммутатора
                        qDebug() << "start TYPE_HARD_GEN";
                        qDebug() << "Ports" << i << ports[i].in << ports[i].out;

                        emit telnet_config_pair(ports[i].in,ports[i].out);
                        Sleep(5000);
                        qDebug() << "running = " << running;
                        emit bercut_start();
                        qDebug() << "bercut_start";
                        Sleep(1000);
                        int cnt = 150;

                        QElapsedTimer timerBercut;
                        timerBercut.start();

                        while(bercut_running && cnt){
                            Sleep(1000);
                            qApp->processEvents();
                            cnt--;
                        }

                        qDebug() << "time left " << timerBercut.elapsed();

                        bercut_running = 0;
                        qDebug() << "bercut_stopped";
                        set_ports_status(i,bercut_status);

                        Sleep(100);
                        qDebug() << "stop";
                    }
                }
                qDebug() << "STOP_HARD_GEN";
            }
            else if(test_type == TYPE_HARD_CHAIN){
                qDebug() << "TYPE_HARD_CHAIN";
                set_ports_status(0,TEST_FAIL);
                running = 1;
                Sleep(1000);
                emit bercut_start();
                qDebug() << "bercut_start";
                Sleep(1000);
                while(bercut_running){
                    Sleep(1000);
                    qDebug() << "bercut_running";
                }
                qDebug() << "bercut_stopped";
                set_ports_status(0,bercut_status);
            }
            qDebug() << "Generation Stop";
            running = 0;
            data_test_start = false;

            //syslog("stop DataTestThread+++++++++++++++++++++++++++");
        }

        QThread::msleep(5);
        qApp->processEvents();
    }
}

void DataTestThread::stop(){
    data_test_start = false;
    running = 0;
    pRecieveThread->running = 0;
    //syslog("DataTestThread::stop");
}

void DataTestThread::set_type(int type){
    test_type = type;
}

//устанавливаем для каких портов проводить тестирование
void DataTestThread::set_ports(ports_pair_t *ports_){
    qDebug() << "set ports";

    for (int i = 0; i < PORT_NUM; i++)
    {
        ports[i].in = ports_[i].in;
        ports[i].in_valid = ports_[i].in_valid;

        ports[i].out = ports_[i].out;
        ports[i].out_valid = ports_[i].out_valid;

        ports[i].in_ip = ports_[i].in_ip;
        ports[i].out_ip = ports_[i].out_ip;

        if(ports[i].in_valid && ports[i].out_valid)
            qDebug() << i << "In" <<  ports[i].in <<ports[i].in_ip << "Out" << ports[i].out << ports[i].out_ip;
    }
}

//устанавливаем для каких портов проводить тестирование
void DataTestThread::get_ports(ports_pair_t *ports_){
    for(int i=0;i<STAND_PORT_NUM;i++){
        ports_[i] = ports[i];
    }
}

bool DataTestThread::is_running(void)
{
    if (running == 1)
        return true;
    else
        return false;
}

bool DataTestThread::is_finished(void){
    if(running == 0)
        return true;
    else
        return false;
}

long unsigned DataTestThread::get_transmitted_pkt(int port){
    return capture_result[port].transmitted_pkts;
}

long unsigned DataTestThread::get_recieved_pkt(int port){   
    return capture_result[port].recieved_pkts;
}
long unsigned DataTestThread::get_recieved_speed(int port){
    return capture_result[port].speed;
}


int DataTestThread::get_ports_status(int port){
    return ports_status[port];
}

void DataTestThread::set_ports_status(int port, port_status_t status){
    ports_status[port] = status;
    qDebug() << "set port" << port <<  "status" << status;
}

/*******************ПЕРЕДАТЧИК****************************/

GenThread::GenThread(){

}
GenThread::~GenThread(){

}

void GenThread::process(){

    while(1){
        if(running == 1){
            capture_result.transmitted_pkts = 0;
            //syslog("run GenThread");


            char errbuf[256];

            make_packet_data(&from,&to);


            qDebug() << "GenThread: from->name " << from.if_name;
            if((pcapd_gen = pcap_open_live(from.if_name.toLocal8Bit().data(),65535,1,1000,errbuf))==NULL){
                //syslog("Error open interface");
                qDebug() << errbuf;
                running = 0;
            }
            while(running){
                //отправляем
                if (pcap_sendpacket(pcapd_gen,packet,1514) != 0){
                    qDebug() << "Error sending the packet:" << pcap_geterr(pcapd_gen);
                    //syslog("Error sending the packet:");
                }else{
                    capture_result.transmitted_pkts++;
                }
            }
            pcap_close(pcapd_gen);
            //syslog("GenThread stop generation");
            start_flag = 0;
            running = 0;
        }
        Sleep(1);
    }
}

void GenThread::set_from(port_if_t addr){
    if(addr.valid){
        from = addr;
    }
   // else
        //syslog("GenThread::set_from NULL");
}

void GenThread::set_to(port_if_t addr){
    if(addr.valid){
        to = addr;
    }
   // else
        //syslog("GenThread::set_to NULL");
}

void GenThread::stop(){
    //syslog("GenThread::stop");
    running = 0;
}

void GenThread::start_(){
    //syslog("GenThread::start");
    running = 1;
    capture_result.transmitted_pkts = 0;
}

long unsigned GenThread::get_transmitted_pkt(void){
    return capture_result.transmitted_pkts;
}

///////////////////////
/// \brief send_data
/// \param from
/// \param to
/// \param fp
///
///////////////////////

void GenThread::make_packet_data(port_if_t *from, port_if_t *to){

    int i;
    unsigned short chS = 0xae21;
    unsigned char temp[1024];

    int identification = 1234;

    //MAC-адрес получателя
    packet[0]=to->mac[0];
    packet[1]=to->mac[1];
    packet[2]=to->mac[2];
    packet[3]=to->mac[3];
    packet[4]=to->mac[4];
    packet[5]=to->mac[5];

    //MAC-адрес отправителя
    packet[6]=from->mac[0];
    packet[7]=from->mac[1];
    packet[8]=from->mac[2];
    packet[9]=from->mac[3];
    packet[10]=from->mac[4];
    packet[11]=from->mac[5];

    packet[12]=0x08;//ethernet
    packet[13]=0x00;

    //Формирование IP-заголовка
    packet[14]=0x45;
    packet[15]=0x00;

    //Длинна пакета
    packet[16] = (unsigned char)(1500>>8);
    packet[17] = (unsigned char)1500;

    packet[18]=(unsigned char)(identification >>8); //id
    packet[19]=(unsigned char)(identification);

    packet[20]=0; //фрагментацию отключаем
    packet[21]=0;

    packet[22]=128; //ttl

    packet[23]=17;    //udp

    packet[24]=0; //контрольная сумма
    packet[25]=0;

    //От кого
    //src
    packet[26] = from->ip[0];
    packet[27] = from->ip[1];
    packet[28] = from->ip[2];
    packet[29] = from->ip[3];
    qDebug() << "IP src " <<  packet[26] <<  packet[27] <<  packet[28] <<  packet[29];

    //куда
    //dst
    packet[30] = to->ip[0];
    packet[31] = to->ip[1];
    packet[32] = to->ip[2];
    packet[33] = to->ip[3];
    qDebug() << "IP dst" <<  packet[30] <<  packet[31] <<  packet[32] <<  packet[33];

    int j=0;
    for(int i=0;i<20;i++){
        if((i!=10) && (i!=11)){
            temp[j] = packet[14+i];
            j++;
        }
    }

    chS = Crc16_1(temp,18);//считаем контрольную сумму

    packet[24] = (unsigned char)(chS>>8);
    packet[25] = (unsigned char)chS;

    //****************************************************
    //udp header
    packet[34] = (unsigned char)(SRC_PORT >> 8);
    packet[35] = (unsigned char)(SRC_PORT);

    packet[36] = (unsigned char)(DST_PORT >> 8);
    packet[37] = (unsigned char)(DST_PORT);

    packet[38] = (unsigned char)((1480)>>8);
    packet[39] = (unsigned char)(1480);

    packet[40] = 0;//csum
    packet[41] = 0;

    for(i=42; i<1514; i++)
    {
        packet[i]= 'A';
    }

    chS=Crc16_1((&packet[42]),1480);

    packet[40] = (unsigned char)(chS>>8);
    packet[41] = (unsigned char)chS;

    qDebug() << "Packet maked";
}

void GenThread::syslog(QString text){
    FILE *file;
    qDebug() << text;

    emit generator_print_msg(text);

    QDateTime now = QDateTime::currentDateTime();
    file = fopen ("genhread_log.txt", "a");
    fprintf(file,"%02d.%02d.%02d %02d:%02d:%02d %s\r\n",now.date().day(),now.date().month(),now.date().year(),
            now.time().hour(),now.time().minute(),now.time().second(),text.toLocal8Bit().data());
    fclose(file);
}

static unsigned short Crc16_1(unsigned char * pcBlock, unsigned short len)
{

    quint32 crc = 0;
    quint32 crc_add;
    for(int i=0;i<(len/2);i++){
        crc += pcBlock[i*2]<<8 | pcBlock[i*2+1];
    }
    crc_add = (crc & 0xFFFF0000)>>16;
    crc &= 0xFFFF;
    crc = crc+crc_add;
    return (unsigned short)(~crc);
}

/*******************ПРИЕМНИК*******************************/
RcvThread::RcvThread(){

}
RcvThread::~RcvThread(){

}

void RcvThread::process(){

    while(1){
        if(running==1){
            qDebug() << "run RcvThread+++++++++++++++++";
            //syslog("run RcvThread");
            capture_result.recieved_pkts = 0;
            capture_result.tiemout_pkts = 0;
            start_capture(&to);
            start_flag = 0;
        }
        Sleep(100);
    }
}

void RcvThread::stop(){
    //syslog("RcvThread::stop");
    running = 0;
}

void RcvThread::start_(){
    //syslog("RcvThread::start");
    running = 1;
    capture_result.recieved_pkts = 0;
}

void RcvThread::set_to(port_if_t addr){
    if(addr.valid)
        to = addr;
    //else
        //syslog("RcvThread::set_to NULL");
}

port_if_t RcvThread::get_to(void){
    return to;
}

long unsigned RcvThread::get_recieved_pkt(void){
    return capture_result.recieved_pkts;
}

QDateTime RcvThread::get_start_datetime(void){
    return capture_result.start_time;
}

QDateTime RcvThread::get_stop_datetime(void){
    return capture_result.stop_time;
}

/*запуск прослушивания на интерфейсе to*/
int RcvThread::start_capture(port_if_t *to){
    /*static */struct bpf_program fcode;
    /*static */bpf_u_int32 NetMask;
    /*static */char filter[254];
    char errbuf[1024];
    int i;
    int ret;

    const u_char *pkt_data;
    pcap_pkthdr *header;

    //syslog( "start_capture");
    qDebug() << "RcvThread: to->name " << to->if_name;


    if((pcapd_capt = pcap_open_live(to->if_name.toLocal8Bit().data(),65535,1,1000,errbuf))==NULL){
        //syslog("Error open interface");
        running = 0;
        pcap_close(pcapd_capt);
        return 1;
    }

    NetMask=0xffffff;
    qDebug() << NetMask << "NetMask";

    //compile the filter
    sprintf(filter,"udp port 43962 and ip dst host %d.%d.%d.%d",to->ip[0],to->ip[1],to->ip[2],to->ip[3]);
    //sprintf(filter,"udp port 43962");

    //syslog(filter);

    qDebug()<< "pcap_compile" << filter;
    if(pcap_compile(pcapd_capt, &fcode, filter, 1, NetMask) < 0)
    {
        //syslog("Error compiling filter: wrong syntax");
        pcap_close(pcapd_capt);
        running = 0;
        return 1;
    }
    //set the filter
    qDebug()<< "pcap_setfilter";
    if(pcap_setfilter(pcapd_capt, &fcode)<0)
    {
        //syslog("Error setting the filter");
        pcap_close(pcapd_capt);
        running  = 0;
        return 1;
    }

    //syslog("start capture");
    i=0;
    while(running==1){
        ret = pcap_next_ex(pcapd_capt, &header,&pkt_data);
        if(ret>0){
            capture_result.recieved_bytes +=1514;
            capture_result.recieved_pkts++;
            //start time on first packet
            if(i==0){
                capture_result.start_time = QDateTime::currentDateTime();
                qDebug() << capture_result.start_time;
            }
            i++;
        }

        if(ret == 0){
            capture_result.tiemout_pkts++;
            if(capture_result.tiemout_pkts>=CAPTURE_MAXTIMOUT_LEN){
                running = 0;
                //syslog("stop: timout");
                break;
            }
        }

        if(i>=CAPTURE_LEN){
            capture_result.stop_time = QDateTime::currentDateTime();
            qDebug() << capture_result.stop_time;
            qDebug() << "stop: > capture len" << i;
            running = 0;
            break;
        }
    }
    //stop capture
    pcap_close(pcapd_capt);
    running = 0;

    //syslog("stop recieved");

    return 0;
}

void RcvThread::syslog(QString text){
    FILE *file;
    qDebug() << text;

    //generator_print_msg(text);

    QDateTime now = QDateTime::currentDateTime();
    file = fopen ("rcvhread_log.txt", "a");
    fprintf(file,"%02d.%02d.%02d %02d:%02d:%02d %s\r\n",now.date().day(),now.date().month(),now.date().year(),
            now.time().hour(),now.time().minute(),now.time().second(),text.toLocal8Bit().data());
    fclose(file);
}

#if 0
void packet_handler(u_char *arg, const struct pcap_pkthdr* pkthdr, const u_char* packet){
    qDebug() << "callback";
}

/*запуск прослушивания на интерфейсе to*/
int RcvThread::start_capture(pcap_if_t *to){
    static struct bpf_program fcode;
    static bpf_u_int32 NetMask;
    static char filter[254];
    char errbuf[1024];
    int i;
    int ret;

    const u_char *pkt_data;
    pcap_pkthdr *header;

    qDebug() << "start_capture";

    running = 1;

    if((pcapd_capt = pcap_open_live(to->name,65535,0,10,errbuf))==NULL){
        qDebug() << "Error open interface";
        running = 0;
        return 1;
    }

    for(pcap_addr_t *a=to->addresses; a!=NULL; a=a->next) {
        if(a->addr->sa_family == AF_INET){
            u_char * ip = (u_char *)&(((struct sockaddr_in *)a->addr)->sin_addr.s_addr);
            qDebug() << "IP dst capt" <<  ip[0] <<  ip[1] <<  ip[2] <<  ip[3];
        }
    }

    //set filter
    if(to->addresses != NULL)
        /* Retrieve the mask of the first address of the interface */
        NetMask=((struct sockaddr_in *)(to->addresses->netmask))->sin_addr.S_un.S_addr;
    else
        /* If the interface is without addresses we suppose to be in a C class network */
        NetMask=0xffffff;

    //compile the filter
    //sprintf(filter,"ip and udp and port 43962");
    sprintf(filter,"udp port 43962");


    if(pcap_compile(pcapd_capt, &fcode, filter, 1, NetMask) < 0)
    {
        qDebug() << "Error compiling filter: wrong syntax";
        pcap_close(pcapd_capt);
        running = 0;
        return 1;
    }
    //set the filter
    if(pcap_setfilter(pcapd_capt, &fcode)<0)
    {
        qDebug() << "Error setting the filter";
        pcap_close(pcapd_capt);
        running  = 0;
        return 1;
    }

    //start capture
    i=0;

    ret = pcap_loop(pcapd_capt,-1,packet_handler,NULL);
    qDebug() << "pcap_dispatch" << ret;
    while(running){}
    /*while(running){
        ret = pcap_next_ex(pcapd_capt, &header,&pkt_data);
        if(ret>0){
            capture_result.recieved_bytes +=1514;
            capture_result.recieved_pkts++;
            //start time on first packet
            if(i==0)
                capture_result.start_time = QDateTime::currentDateTime();
            i++;
        }

        if(ret == 0){
            capture_result.tiemout_pkts++;
            if(capture_result.tiemout_pkts>=CAPTURE_MAXTIMOUT_LEN){
                running = 0;
                qDebug() << "stop: timout";
                break;
            }
        }

        if(i>=CAPTURE_LEN){
            capture_result.stop_time = QDateTime::currentDateTime();
            qDebug() << "stop: > capture len" << i;
            running = 0;
            break;
        }
    }*/
    //stop capture
    pcap_close(pcapd_capt);
    running = 0;

    qDebug() << "stop recieved";

    return 0;
}

#endif

//по IP адресу получаем структуру с информацией об интерфейсе
port_if_t get_card_if_ip(QString addr){
    QString outstr;
    port_if_t ifaddr;
    pcap_if_t *d;
    char errbuf[1024];
    pcap_if_t *alldevs;
    char tmp[10];

    qDebug() << "get_card_if_ip" << addr;
    QList<QNetworkInterface> q;
    q = QNetworkInterface::allInterfaces();
    for(int j=0;j<q.size();j++){
        for(int i=0;i<q.at(j).addressEntries().size();i++){
            if(q.at(j).addressEntries().at(i).ip().toString().compare(addr,Qt::CaseInsensitive)==0){
                outstr.clear();
                outstr.append(q.at(j).hardwareAddress());
                ifaddr.ip[3] = (unsigned char)(q.at(j).addressEntries().at(i).ip().toIPv4Address());
                ifaddr.ip[2] = (unsigned char)(q.at(j).addressEntries().at(i).ip().toIPv4Address()>>8);
                ifaddr.ip[1] = (unsigned char)(q.at(j).addressEntries().at(i).ip().toIPv4Address()>>16);
                ifaddr.ip[0] = (unsigned char)(q.at(j).addressEntries().at(i).ip().toIPv4Address()>>24);

                for(int i=0;i<6;i++){
                    tmp[0] = outstr.toLocal8Bit().data()[i*3];
                    tmp[1] = outstr.toLocal8Bit().data()[i*3+1];
                    tmp[2] = 0;
                    ifaddr.mac[i] = (unsigned char)strtol(tmp,NULL,16);
                }
                break;
            }
        }
    }

    pcap_findalldevs(&alldevs, errbuf);
    if(alldevs==NULL){
        qDebug() << "alldev==NULL";
        pcap_freealldevs(alldevs);
        ifaddr.valid = 0;
        return ifaddr;
    }
    /* Print the list */
    for(d=alldevs; d!=NULL; d=d->next){
        for(pcap_addr_t *a=d->addresses; a!=NULL; a=a->next) {
            if(a->addr->sa_family == AF_INET){
                if(addr.compare(inet_ntoa(((struct sockaddr_in*)a->addr)->sin_addr),Qt::CaseInsensitive)==0){
                    ifaddr.if_name.clear();
                    ifaddr.if_name.append(d->name);
                    pcap_freealldevs(alldevs);

                    ifaddr.valid = 1;
                    qDebug() << "ip" << ifaddr.ip[0] << ifaddr.ip[1] << ifaddr.ip[2] << ifaddr.ip[3];
                    qDebug() << "mac" << ifaddr.mac[5] << ifaddr.mac[4] << ifaddr.mac[3] << ifaddr.mac[2] << ifaddr.mac[1] << ifaddr.mac[0];
                    qDebug() << "name" << ifaddr.if_name;
                    return ifaddr;
                }
            }
        }
    }
    qDebug() << "Addr no found";
    pcap_freealldevs(alldevs);

    ifaddr.valid = 0;
    return ifaddr;
}

void DataTestThread::syslog(QString text){
    FILE *file;
    qDebug() << text;

    emit generator_print_msg(text);

    QDateTime now = QDateTime::currentDateTime();
    file = fopen ("datathread_log.txt", "a");
    fprintf(file,"%02d.%02d.%02d %02d:%02d:%02d %s\r\n",now.date().day(),now.date().month(),now.date().year(),
            now.time().hour(),now.time().minute(),now.time().second(),text.toLocal8Bit().data());
    fclose(file);
}
