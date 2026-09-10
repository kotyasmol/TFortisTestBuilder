#include "connect_diagram.h"
#include "ui_form.h"
#include "QDebug"

//отрисовка картинки с подключениями шнуров и патч-кордов

Form::Form(configmodel test_config, settingsmodel prog_sett,QWidget *parent) :
    QWidget(parent),
    ui(new Ui::Form)
{
    ui->setupUi(this);

    QString str;
    pcap_if_t *d;
    pcap_if_t *alldevs;
    char errbuf[1024];
    int j;

    /* Retrieve the device list */
    pcap_findalldevs(&alldevs, errbuf);

    this->setWindowTitle(test_config.model_name);

    scene = new QGraphicsScene(ui->graphicsView);
    ui->graphicsView->setScene(scene);

    scene->clear();

    QPen pen1(Qt::black);
    QBrush brush1(Qt::SolidLine);
    QBrush brush2(Qt::SolidLine);

    brush1.setColor(QColor(254,254,254));
    brush2.setColor(QColor(135,135,135));

    //body
    scene->addRect(20,20,60*test_config.port_num+100,200,pen1,brush1);

    //ports & ip
    for(int i=0;i<test_config.port_num;i++){
        scene->addRect(20+50+i*60,200,40,40,pen1,brush2);
        if(test_config.port_poe[i]){
            text = scene->addText("PoE",QFont("Arial",8,-1,false));
            text->setX(20+50+i*60);
            text->setY(200);
            if(test_config.port_poe[i]==24){
                text = scene->addText("24V",QFont("Arial",8,-1,false));
                text->setX(20+50+i*60+10);
                text->setY(210);
            }
        }
        if(test_config.port_sfp[i]){
            text = scene->addText("SFP",QFont("Arial",8,-1,false));
            text->setX(20+50+i*60);
            text->setY(200);
        }
        /* Print the port list */
        j=0;
        for(d=alldevs; d; d=d->next){
            for(pcap_addr_t *a=d->addresses; a!=NULL; a=a->next) {
                if(a->addr->sa_family == AF_INET){
                    str.sprintf("%s",inet_ntoa(((struct sockaddr_in*)a->addr)->sin_addr));
                    if(prog_sett.card_index[test_config.port2stand[i]] == j){
                        text = scene->addText(str,QFont("Arial",8,-1,false));
                        text->setX(20+60+i*60);
                        text->setY(360);
                        text->setRotation(270);
                    }
                    j++;
                }
            }
        }
    }

    //AC1 && AC2
    if(test_config.use_ac1 && test_config.use_ac2){
        scene->addRect(-20,20,40,200,pen1,brush2);
        text = scene->addText("AC1 (красный)",QFont("Arial",12,-1,false));
        text->setX(-20);
        text->setY(200);
        text->setRotation(270);

        scene->addRect(110+test_config.port_num*60,20,40,200,pen1,brush2);
        text = scene->addText("AC2 (черный)",QFont("Arial",12,-1,false));
        text->setX(110+test_config.port_num*60);
        text->setY(200);
        text->setRotation(270);
    }

    //AC1 only
    if(test_config.use_ac1 && test_config.use_ac2==0){
        scene->addRect(110+test_config.port_num*60,20,40,200,pen1,brush2);
        text = scene->addText("AC1 (красный)",QFont("Arial",12,-1,false));
        text->setX(110+test_config.port_num*60);
        text->setY(200);
        text->setRotation(270);
    }

    //АКБ
    if(test_config.test_ups){
        scene->addRect(20,0,100,40,pen1,brush2);
        text = scene->addText("АКБ (-)(+)",QFont("Arial",10,-1,false));
        text->setX(20);
        text->setY(0);
    }

    //нагреватели
    if(test_config.test_ups){
        scene->addRect(test_config.port_num*60-50,0,160,40,pen1,brush2);
        text = scene->addText("Нагреватели",QFont("Arial",10,-1,false));
        text->setX(test_config.port_num*60-50);
        text->setY(0);
    }

    //шторка
    if(test_config.dry_cont_test[0]){
        scene->addRect(30,60,100,40,pen1,brush2);
        text = scene->addText("Шторка",QFont("Arial",10,-1,false));
        text->setX(30);
        text->setY(60);
    }

    //сухой контакт
    if(test_config.dry_cont_test[1]){
        scene->addRect(test_config.port_num*60-50,60,150,40,pen1,brush2);
        text = scene->addText("Сухой контакт",QFont("Arial",10,-1,false));
        text->setX(test_config.port_num*60-50);
        text->setY(60);
    }
}

Form::~Form()
{
    delete ui;
}
