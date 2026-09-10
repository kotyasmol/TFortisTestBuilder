#include <winsock2.h>
#include <QtGui>
#include "ui.h"
#include "mainwindow.h"
#include "ui_mainwindow.h"
#include <QLabel>
#include <pcap.h>

#include <QPrinterInfo>
#include <QPrinter>
#include <QMessageBox>
#include <QGraphicsScene>
#include <QGraphicsTextItem>
#include <QNetworkInterface>
#include <QMenu>
#include <QMenuBar>
#include <QSerialPortInfo>

//формы настройки

InputDialog::InputDialog(QWidget* pwgt/*= 0*/)
    : QDialog(pwgt, Qt::WindowTitleHint | Qt::WindowSystemMenuHint)
{
    m_ptxtFirstName = new QLineEdit;
    m_ptxtLastName  = new QLineEdit;

    QLabel* plblFirstName    = new QLabel("&First Name");
    QLabel* plblLastName     = new QLabel("&Last Name");

    plblFirstName->setBuddy(m_ptxtFirstName);
    plblLastName->setBuddy(m_ptxtLastName);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;
    ptopLayout->addWidget(plblFirstName, 0, 0);
    ptopLayout->addWidget(plblLastName, 1, 0);
    ptopLayout->addWidget(m_ptxtFirstName, 0, 1);
    ptopLayout->addWidget(m_ptxtLastName, 1, 1);
    ptopLayout->addWidget(pcmdOk, 2,0);
    ptopLayout->addWidget(pcmdCancel, 2, 1);
    setLayout(ptopLayout);
}

QString InputDialog::firstName() const
{
    return m_ptxtFirstName->text();
}

QString InputDialog::lastName() const
{
    return m_ptxtLastName->text ();
}

/// диалог выбора com порта
ComSetDialog::ComSetDialog(QString pn,int autoconnect, int st,QString id)
{
    port_num = pn;
    stand_type = st;
    stand_id = id;

    this->setWindowTitle(tr("Настройка стенда"));
    this->setMinimumWidth(300);
    this->setMinimumHeight(150);

    //заполнение списка COM портов
    m_ptxtPortNum = new QComboBox;
    const auto infos = QSerialPortInfo::availablePorts();
    for (const QSerialPortInfo &info : infos) {
        m_ptxtPortNum->addItem(info.portName());
        if(strcmp(info.portName().toLocal8Bit().data(),port_num.toLocal8Bit().data())==0){
            m_ptxtPortNum->setCurrentText(info.portName());
        }else
        {
            m_ptxtPortNum->setCurrentText("");
        }
    }

    m_ptxtAutoconnect = new QCheckBox;
    if(autoconnect)
        m_ptxtAutoconnect->setChecked(true);
    m_ptxtTypeModbus = new QRadioButton;
    m_ptxtTypeRpsNew = new QRadioButton;
    m_ptxtTypeGen = new QRadioButton;
    m_ptxtStandID= new QLineEdit(stand_id);

    switch(stand_type){
    case StandType::typeAPK03:
        m_ptxtTypeModbus->setChecked(true);
        break;
    case StandType::typeRPSNew:
        m_ptxtTypeRpsNew->setChecked(true);
        break;
    case StandType::typeAPK02:
        m_ptxtTypeGen->setChecked(true);
        break;

    }

    QLabel* plblPortNum        = new QLabel("&Порт");
    QLabel* plblAutoconnect    = new QLabel("Автоматическое переподключение");

    QLabel* plblStandTypeGen    = new QLabel("АПК-Стенд-02");
    QLabel* plblStandTypeModbus = new QLabel("АПК-Стенд-03");
    QLabel* plblStandTypeRpsNew    = new QLabel("Стенд RPS-01");
    QLabel* plblStandId    = new QLabel("Идентификатор стенда");
    plblPortNum->setBuddy(m_ptxtPortNum);
    plblStandId->setBuddy(m_ptxtStandID);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblPortNum, 0, 0);
    ptopLayout->addWidget(m_ptxtPortNum, 0, 1);

    ptopLayout->addWidget(plblAutoconnect, 1, 0);
    ptopLayout->addWidget(m_ptxtAutoconnect, 1, 1);

    ptopLayout->addWidget(plblStandTypeGen, 2, 0);
    ptopLayout->addWidget(m_ptxtTypeGen, 2, 1);
    ptopLayout->addWidget(plblStandTypeModbus, 4, 0);
    ptopLayout->addWidget(m_ptxtTypeModbus, 4,1);
    ptopLayout->addWidget(plblStandTypeRpsNew, 5, 0);
    ptopLayout->addWidget(m_ptxtTypeRpsNew, 5, 1);
    ptopLayout->addWidget(plblStandId, 6, 0);
    ptopLayout->addWidget(m_ptxtStandID, 6, 1);
    ptopLayout->addWidget(pcmdOk, 7,0);
    ptopLayout->addWidget(pcmdCancel, 7, 1);
    setLayout(ptopLayout);
}

QString ComSetDialog::portNum() const
{
    return m_ptxtPortNum->currentText();
}

int ComSetDialog::portAutoconnect() const
{
    return m_ptxtAutoconnect->isChecked();
}

int ComSetDialog::baudRate()const
{
    return m_ptxtBaudRate->currentIndex();
}

int ComSetDialog::standType()const
{
    if(m_ptxtTypeModbus->isChecked())
        return StandType::typeAPK03;
    else if(m_ptxtTypeRpsNew->isChecked())
        return StandType::typeRPSNew;
    else if(m_ptxtTypeGen->isChecked())
        return StandType::typeAPK02;
    else
        return StandType::typeAPK03;
}

QString ComSetDialog::getStandID() const
{
    return m_ptxtStandID->text();
}

// диалог настройки БД
DbSetDialog::DbSetDialog(int type,QString host){

    m_ptxtType_http = new QRadioButton;
    m_ptxtType_https = new QRadioButton;

    m_ptxtDB_Host     = new QLineEdit;

    this->setWindowTitle(tr("Настройки подключения к серверу"));
    this->setMinimumWidth(350);
    this->setMinimumHeight(150);

    if(type == DB_HTTP)
        m_ptxtType_http->setChecked(true);
    if(type == DB_HTTPS)
        m_ptxtType_https->setChecked(true);
    m_ptxtDB_Host->setText(host);

    QLabel* plblDB_Type1    = new QLabel("http");
    QLabel* plblDB_Type2    = new QLabel("https");
    QLabel* plblDB_Host    = new QLabel("Host");

    plblDB_Type1->setBuddy(m_ptxtType_http);
    plblDB_Type2->setBuddy(m_ptxtType_https);
    plblDB_Host->setBuddy(m_ptxtDB_Host);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblDB_Type1, 0, 0);
    ptopLayout->addWidget(plblDB_Type2, 1, 0);
    ptopLayout->addWidget(plblDB_Host, 2, 0);
    ptopLayout->addWidget(m_ptxtType_http, 0, 1);
    ptopLayout->addWidget(m_ptxtType_https, 1, 1);
    ptopLayout->addWidget(m_ptxtDB_Host, 2, 1);
    ptopLayout->addWidget(pcmdOk, 3,0);
    ptopLayout->addWidget(pcmdCancel, 3, 1);
    setLayout(ptopLayout);
}

int DbSetDialog::type() const
{
    if(m_ptxtType_http->isChecked())
        return DB_HTTP;
    else if(m_ptxtType_https->isChecked())
        return DB_HTTPS;
    else return 0;
}

QString DbSetDialog::host() const
{
    return m_ptxtDB_Host->text();
}



//диалог выбора сетевых интерфейса
NetCardSetDialog::NetCardSetDialog(QString *card_index)
{
    char temp[64];
    int i,j;


    this->setWindowTitle(tr("Настройка сетевых карт"));
    this->setMinimumWidth(400);
    this->setMinimumHeight(300);

    QComboBox allPorts;
    for(i = 0;i<DATA_TEST_LINES;i++){
        m_ptxtPort[i] = new QComboBox;
    }

    QList<QNetworkInterface> q;
    q = QNetworkInterface::allInterfaces();
    i=0;
    for(int j=0;j<q.at(0).allAddresses().size();j++){
        if(q.at(0).allAddresses().at(j).isInSubnet(QHostAddress("192.168.0.0"),16)){
            allPorts.addItem(q.at(0).allAddresses().at(j).toString());
            i++;
        }
    }

    //set list for all ports
    for(i = 0;i<DATA_TEST_LINES;i++){
        m_ptxtPort[i]->addItem("");
        m_ptxtPort[i]->setCurrentIndex(0);
        for(j = 0;j<allPorts.count();j++){
            if(!allPorts.itemText(j).isEmpty()){
                m_ptxtPort[i]->addItem(allPorts.itemText(j));

                if(!allPorts.itemText(j).isEmpty() && !card_index[i].isEmpty() && allPorts.itemText(j).compare(card_index[i],Qt::CaseInsensitive)==0)
                    m_ptxtPort[i]->setCurrentIndex(j+1);

                qDebug() << allPorts.itemText(j) << card_index[i];
            }
        }
    }

    //create lables
    for(i = 0;i<DATA_TEST_LINES;i++){
        sprintf(temp,"&Card %d",i+1);
        plblPort[i] = new QLabel(temp);
        plblPort[i]->setBuddy(m_ptxtPort[i]);
    }

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    for(i=0;i<DATA_TEST_LINES;i++){
        ptopLayout->addWidget(plblPort[i], i, 0);
        ptopLayout->addWidget(m_ptxtPort[i], i, 1);
    }
    ptopLayout->addWidget(pcmdOk, DATA_TEST_LINES,0);
    ptopLayout->addWidget(pcmdCancel, DATA_TEST_LINES, 1);
    setLayout(ptopLayout);
}

QString NetCardSetDialog::get_card(int num){
    return m_ptxtPort[num]->currentText();
}

/****************************************************************************************/
//окно настройки имени пользователя

UserSetDialog::UserSetDialog(QString name[USERS_NUM]){
    char temp[1024];
    this->setWindowTitle(tr("Настройка имени пользователя"));
    this->setMinimumWidth(400);
    this->setMinimumHeight(300);

    for(int i=0;i<USERS_NUM;i++){
        sprintf(temp,"%d",i+1);
        plblUserName[i] = new QLabel(temp);
        //plblUserName[i]->setBuddy();
        m_ptxtUserName[i] = new QLineEdit(name[i]);
    }

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    for(int i=0;i<USERS_NUM;i++){
        ptopLayout->addWidget(plblUserName[i], i, 0);
        ptopLayout->addWidget(m_ptxtUserName[i], i, 1);
    }
    ptopLayout->addWidget(pcmdOk, USERS_NUM,0);
    ptopLayout->addWidget(pcmdCancel, USERS_NUM, 1);
    setLayout(ptopLayout);
}

QString UserSetDialog::get_username(int i){
    return m_ptxtUserName[i]->text();
}

/****************************************************************************************/

//окно аутентификации

UserDialog::UserDialog(QString name[USERS_NUM]){
    this->setWindowTitle(tr("Представтесь:"));
    this->setMinimumWidth(400);
    //this->setMinimumHeight(300);

    m_ptxtUserName = new QComboBox;

    for(int i = 0;i<USERS_NUM;i++){
        //if(name[i].isEmpty()==false)
        m_ptxtUserName->addItem(name[i]);
    }

    QLabel* plblUserName    = new QLabel("Имя пользователя");

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblUserName, 0, 0);
    ptopLayout->addWidget(m_ptxtUserName, 0, 1);
    ptopLayout->addWidget(pcmdOk, 1,1);
    setLayout(ptopLayout);
}

int UserDialog::get_current_username(){
    return m_ptxtUserName->currentIndex();
}

/****************************************************************************************/

PrinterSetDialog::PrinterSetDialog(QString name, int labelSize,QString rep_name){
    this->setWindowTitle(tr("Настройки принтера"));
    this->setMinimumWidth(200);
    this->setMinimumHeight(200);
    m_ptxtPrinterName = new QComboBox;
    m_ptxtReportPrinterName = new QComboBox;
    m_ptxtLabelSize = new QComboBox;

    int i=0;
    QPrinterInfo PrinterInfo;
    QStringList pinfo;

    pinfo= PrinterInfo.availablePrinterNames();

    for(i=0;i<pinfo.count();i++){
        m_ptxtPrinterName->addItem(pinfo.at(i));

        if(!pinfo.at(i).isEmpty()){
            if(pinfo.at(i).compare(name)==0)
                m_ptxtPrinterName->setCurrentIndex(i);
        }
    }

    for(i=0;i<pinfo.count();i++){
        m_ptxtReportPrinterName->addItem(pinfo.at(i));
        if(rep_name.length()){
            if(pinfo.at(i).compare(rep_name)==0)
                m_ptxtReportPrinterName->setCurrentIndex(i);
        }
    }

    m_ptxtLabelSize->addItem("20x30");
    m_ptxtLabelSize->addItem("13x25");
    for(i=0;i<2;i++){
        if(i == labelSize){
            m_ptxtLabelSize->setCurrentIndex(i);
        }
    }

    QLabel* plblPrinterName    = new QLabel("Принтер этикеток");
    QLabel* plblReportPrinterName    = new QLabel("Принтер отчетов");
    QLabel* plblLabelSize    = new QLabel("Размер этикетки");

    plblPrinterName->setBuddy(m_ptxtPrinterName);
    plblReportPrinterName->setBuddy(m_ptxtReportPrinterName);
    plblLabelSize->setBuddy(m_ptxtLabelSize);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblPrinterName, 0, 0);
    ptopLayout->addWidget(m_ptxtPrinterName, 0, 1);
    ptopLayout->addWidget(plblReportPrinterName, 1, 0);
    ptopLayout->addWidget(m_ptxtReportPrinterName, 1, 1);
    ptopLayout->addWidget(plblLabelSize, 2, 0);
    ptopLayout->addWidget(m_ptxtLabelSize, 2, 1);
    ptopLayout->addWidget(pcmdOk, 3,0);
    ptopLayout->addWidget(pcmdCancel, 3, 1);
    setLayout(ptopLayout);
}

QString PrinterSetDialog::get_printername(){
    return m_ptxtPrinterName->currentText();
}

QString PrinterSetDialog::get_report_printername(){
    return m_ptxtReportPrinterName->currentText();
}

int PrinterSetDialog::get_labelSize(){
    return m_ptxtLabelSize->currentIndex();
}

/*окно настройки профилей тестирования*/
ProfilesSetDialog::ProfilesSetDialog(settingsmodel prog_sett){
    QString str;
    this->setWindowTitle(tr("Настройка профилей тестирования"));

    this->setMinimumWidth(800);
    this->setMaximumWidth(1200);

    this->setMinimumHeight(600);
    this->setMaximumHeight(1200);

    for(int i=0;i<MAX_DEVICES;i++){
        if(prog_sett.config_dir[i].isEmpty())
            m_ptxtDevName[i] = new QLineEdit();
        else
            m_ptxtDevName[i] = new QLineEdit(prog_sett.config_name[i]);
        m_ptxtDevName[i]->setEnabled(false);
        m_ptxtConfTest[i] = new QLineEdit(prog_sett.config_dir[i]);
        pcmdBrowseTest[i] = new QPushButton("Обзор");
        pcmdDelTest[i]    = new QPushButton("Удалить");

    }

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    QSignalMapper* signalMapper = new QSignalMapper(this);
    QSignalMapper* signalMapper3 = new QSignalMapper(this);

    for (int i = 0; i < MAX_DEVICES; i++)
    {
        connect(pcmdBrowseTest[i], SIGNAL(clicked()), signalMapper, SLOT(map()));
        signalMapper -> setMapping(pcmdBrowseTest[i], i);

        connect(pcmdDelTest[i], SIGNAL(clicked()), signalMapper3, SLOT(map()));
        signalMapper3 -> setMapping(pcmdDelTest[i], i);
    }

    connect(signalMapper,  SIGNAL(mapped(int)), this, SLOT(open_config_test(int)));
    connect(signalMapper3, SIGNAL(mapped(int)), this, SLOT(del_config_test(int)));

    //Layout setup
    QWidget *central = new QWidget;
    QGridLayout* ptopLayout = new QGridLayout(central);
    central->setLayout(ptopLayout);

    QScrollArea *scrollArea = new QScrollArea;
    scrollArea->setWidget(central);
    scrollArea->setWidgetResizable(true);

    QVBoxLayout *mainLayout = new QVBoxLayout();
    setLayout(mainLayout);
    mainLayout->addWidget(scrollArea);

    QWidget *buttonWidget = new QWidget;
    QHBoxLayout *buttonLayout = new QHBoxLayout(buttonWidget);
    buttonWidget->setLayout(buttonLayout);

    //table title
    buttonLayout->addWidget(pcmdOk, 0);
    buttonLayout->addWidget(pcmdCancel, 0);

    mainLayout->addWidget(buttonWidget);

    title[0].setText("№");
    title[0].setFixedWidth(20);
    ptopLayout->addWidget(&title[0],1,0);
    title[1].setText("Имя");
    ptopLayout->addWidget(&title[1],1,1);
    title[2].setText("Конфигурация тестирования");
    ptopLayout->addWidget(&title[2],1,4);

    for(int i=0;i<MAX_DEVICES;i++){
        plblNum[i].setText(str.sprintf("%d",i+1));
        plblNum[i].setFixedWidth(20);
        ptopLayout->addWidget(&plblNum[i], i+2,0);

        m_ptxtDevName[i]->setFixedWidth(100);
        ptopLayout->addWidget(m_ptxtDevName[i], i+2,1);

        //конфигурация выпуска
        ptopLayout->addWidget(pcmdBrowseTest[i], i+2,2);
        ptopLayout->addWidget(m_ptxtConfTest[i], i+2,3);
        ptopLayout->addWidget(pcmdDelTest[i], i+2,4);
    }

    setLayout(ptopLayout);
}

//загрузка профиля
void ProfilesSetDialog::open_config_test(int id){
    m_ptxtConfTest[id]->setText(QFileDialog::getOpenFileName(this,tr("Open Settings JSON"), "", tr("JSON Files (*.json)")));
}

//удаление профиля
void ProfilesSetDialog::del_config_test(int id){
    //QUrl url("file:/"+m_ptxtConfTest[id]->text());
    //QDesktopServices::openUrl(url);
    m_ptxtConfTest[id]->clear();
    m_ptxtDevName[id]->clear();
}

QString ProfilesSetDialog::get_config_test(int i){
    return m_ptxtConfTest[i]->text();
}

/*окно настройки списка устройств*/
DevListDialog::DevListDialog(settingsmodel prog_sett){
    QString str;
    this->setWindowTitle(tr("Настройка списка устройств"));
    this->setMinimumWidth(300);
    this->setMaximumWidth(600);

    this->setMinimumHeight(600);
    this->setMaximumHeight(1200);

    QLabel* plblDev[MAX_DEVICES];

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    for(int i=0;i<MAX_DEVICES;i++){
        str.sprintf("%d",prog_sett.device_id[i]);
        m_ptxtDevId[i] = new QLineEdit(str);
        m_ptxtDevId[i]->setFixedWidth(100);
        m_ptxtDevId[i]->setMinimumHeight(20);

        m_ptxtDevName[i] = new QLineEdit(prog_sett.device_name[i]);
        m_ptxtDevName[i]->setMinimumHeight(20);

        str.sprintf("%d",i);
        plblDev[i] = new QLabel(str);
    }

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QWidget *central = new QWidget;
    QGridLayout* ptopLayout = new QGridLayout(central);
    central->setLayout(ptopLayout);

    QScrollArea * scrollArea = new QScrollArea;
    scrollArea->setWidget(central);
    scrollArea->setWidgetResizable(true);

    QVBoxLayout *mainLayout = new QVBoxLayout();
    setLayout(mainLayout);
    mainLayout->addWidget(scrollArea);

    //table title
    for(int i=0;i<MAX_DEVICES;i++){
        ptopLayout->addWidget(plblDev[i],i,0);
        ptopLayout->addWidget(m_ptxtDevId[i],i,1);
        ptopLayout->addWidget(m_ptxtDevName[i],i,2);
    }

    QWidget *buttonWidget = new QWidget;
    QHBoxLayout *buttonLayout = new QHBoxLayout(buttonWidget);
    buttonWidget->setLayout(buttonLayout);

    //ok cancel buttons
    buttonLayout->addWidget(pcmdOk, MAX_DEVICES);
    buttonLayout->addWidget(pcmdCancel, MAX_DEVICES);

    mainLayout->addWidget(buttonWidget);
    //setLayout(ptopLayout);
}

int DevListDialog::getDevListId(int i){
    bool ok;
    return m_ptxtDevId[i]->text().toInt(&ok,10);
}

QString DevListDialog::getDevListName(int i){
    return m_ptxtDevName[i]->text();
}

/*окно настройки BERcut*/
// диалог выбора com порта для беркута
BercutSetDialog::BercutSetDialog(QString pn, int state, int type,QString pn100, int state100, int type100)
{
    QString str;
    int i;

    port_num = pn;
    port_num100 = pn100;
    port_state = state;
    port_state100 = state100;
    test_type = type;
    test_type100 = type100;

    this->setWindowTitle(tr("Настройка BERcut"));
    this->setMinimumWidth(300);
    this->setMinimumHeight(150);

    m_ptxtState = new QComboBox;
    m_ptxtState->addItem("Выключить");
    m_ptxtState->addItem("Включить");
    if(port_state)
        m_ptxtState->setCurrentIndex(1);
    else
        m_ptxtState->setCurrentIndex(0);

    //заполнение списка COM портов
    m_ptxtPortNum = new QComboBox;
    const auto infos = QSerialPortInfo::availablePorts();
    for (const QSerialPortInfo &info : infos) {
        m_ptxtPortNum->addItem(info.portName());
        if(strcmp(info.portName().toLocal8Bit().data(),port_num.toLocal8Bit().data())==0){
            m_ptxtPortNum->setCurrentText(info.portName());
        }
    }

    m_ptxtState100 = new QComboBox;
    m_ptxtState100->addItem("Выключить");
    m_ptxtState100->addItem("Включить");
    if(port_state100)
        m_ptxtState100->setCurrentIndex(1);
    else
        m_ptxtState100->setCurrentIndex(0);

    m_ptxtPortNum100 = new QComboBox;
    for(i=0;i<100;i++){
        str.sprintf("COM%d",i);
        m_ptxtPortNum100->addItem(str);
        if(strcmp(str.toLocal8Bit().data(),port_num100.toLocal8Bit().data())==0){
            m_ptxtPortNum100->setCurrentIndex(i);
        }
    }

    m_ptxtType = new QComboBox;
    m_ptxtType->addItem("RFC2544");
    m_ptxtType->addItem("TX Gen");
    if(type == BERCUT_RFC2544)
        m_ptxtType->setCurrentIndex(0);
    else if(BERCUT_TXGEN)
        m_ptxtType->setCurrentIndex(1);

    m_ptxtType100 = new QComboBox;
    m_ptxtType100->addItem("RFC2544");
    m_ptxtType100->addItem("TX Gen");
    if(type100 == BERCUT_RFC2544)
        m_ptxtType100->setCurrentIndex(0);
    else if(BERCUT_TXGEN)
        m_ptxtType100->setCurrentIndex(1);

    QLabel* plblGen1000    = new QLabel("Генератор для портов 1000");

    QLabel* plblState    = new QLabel("&Состояние");
    QLabel* plblPortNum    = new QLabel("&COM Порт");
    QLabel* plblType    = new QLabel("&Тип теста");

    plblPortNum->setBuddy(m_ptxtPortNum);
    plblState->setBuddy(m_ptxtState);
    plblType->setBuddy(m_ptxtType);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblGen1000,4,0);
    ptopLayout->addWidget(plblState, 5, 0);
    ptopLayout->addWidget(m_ptxtState, 5, 1);
    ptopLayout->addWidget(plblPortNum, 6, 0);
    ptopLayout->addWidget(m_ptxtPortNum, 6, 1);
    ptopLayout->addWidget(plblType, 7, 0);
    ptopLayout->addWidget(m_ptxtType, 7, 1);
    ptopLayout->addWidget(pcmdOk, 8,0);
    ptopLayout->addWidget(pcmdCancel, 8, 1);
    setLayout(ptopLayout);
}

QString BercutSetDialog::portNum() const
{
    return m_ptxtPortNum->currentText();
}

QString BercutSetDialog::portNum100() const
{
    return m_ptxtPortNum100->currentText();
}

void BercutSetDialog::setPortNum(int num){
    port_num = num;
}

void BercutSetDialog::setType(int type){
    test_type = type;
}

int BercutSetDialog::getType() const{
    return m_ptxtType->currentIndex();
}
int BercutSetDialog::getType100() const{
    return m_ptxtType100->currentIndex();
}

int BercutSetDialog::getState() const{
    return m_ptxtState->currentIndex();
}
int BercutSetDialog::getState100() const{
    return m_ptxtState100->currentIndex();
}

void BercutSetDialog::setState(int state){
    port_state = state;
}

/*окно настройки промежуточного коммутатора*/
SwitchSetDialog::SwitchSetDialog(int state,QString host,QString login,QString pass,int port_a, int port_b,
                                 int port_dut, int port_sfp1, int port_sfp2,int port_man)
{
    state_ = state;
    host_ = host;
    login_= login;
    pass_ = pass;
    port_a_ = port_a;
    port_b_ = port_b;
    port_dut_ = port_dut;
    port_sfp1_ = port_sfp1;
    port_sfp2_ = port_sfp2;
    port_man_ = port_man;

    this->setWindowTitle(tr("Настройка технологичческого Коммутатора"));
    this->setMinimumWidth(300);
    this->setMinimumHeight(150);

    m_ptxtState = new QComboBox;
    m_ptxtState->addItem("Выключен");
    m_ptxtState->addItem("Включен");
    if(state_)
        m_ptxtState->setCurrentIndex(1);
    else
        m_ptxtState->setCurrentIndex(0);
    m_ptxtHost = new QLineEdit(host_);
    m_ptxtLogin= new QLineEdit(login_);
    m_ptxtPass = new QLineEdit(pass_);
    m_ptrxPortA = new QSpinBox();
    m_ptrxPortA->setValue(port_a);
    m_ptrxPortB = new QSpinBox();
    m_ptrxPortB->setValue(port_b);
    m_ptrxPortSW = new QSpinBox();
    m_ptrxPortSW->setValue(port_dut);
    m_ptrxPortSFP1 = new QSpinBox();
    m_ptrxPortSFP1->setValue(port_sfp1);
    m_ptrxPortSFP2 = new QSpinBox();
    m_ptrxPortSFP2->setValue(port_sfp2);
    m_ptrxPortMan = new QSpinBox();
    m_ptrxPortMan->setValue(port_man);

    QLabel* plblState    = new QLabel("&Состояние");
    QLabel* plblHost    = new QLabel("&Хост");
    QLabel* plblLogin    = new QLabel("&Логин");
    QLabel* plblPass    = new QLabel("&Пароль");
    QLabel* plblPortA    = new QLabel("&Порт Генератора A");
    QLabel* plblPortB    = new QLabel("&Порт Генератора B");
    QLabel* plblPortSW    = new QLabel("&Порт для DUT");
    QLabel* plblPortSFP1    = new QLabel("&Порт для SFP1");
    QLabel* plblPortSFP2    = new QLabel("&Порт для SFP2");
    QLabel* plblPortMan    = new QLabel("&Порт Managment");

    plblState->setBuddy(m_ptxtState);
    plblHost->setBuddy(m_ptxtHost);
    plblLogin->setBuddy(m_ptxtLogin);
    plblPass->setBuddy(m_ptxtPass);
    plblPortA->setBuddy(m_ptrxPortA);
    plblPortB->setBuddy(m_ptrxPortB);
    plblPortSW->setBuddy(m_ptrxPortSW);
    plblPortSFP1->setBuddy(m_ptrxPortSFP1);
    plblPortSFP2->setBuddy(m_ptrxPortSFP2);
    plblPortMan->setBuddy(m_ptrxPortMan);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblState, 0, 0);
    ptopLayout->addWidget(m_ptxtState, 0, 1);
    ptopLayout->addWidget(plblHost, 1, 0);
    ptopLayout->addWidget(m_ptxtHost, 1, 1);
    ptopLayout->addWidget(plblLogin, 2, 0);
    ptopLayout->addWidget(m_ptxtLogin, 2, 1);
    ptopLayout->addWidget(plblPass, 3, 0);
    ptopLayout->addWidget(m_ptxtPass, 3, 1);
    ptopLayout->addWidget(plblPortA, 4, 0);
    ptopLayout->addWidget(m_ptrxPortA, 4, 1);
    ptopLayout->addWidget(plblPortB, 5, 0);
    ptopLayout->addWidget(m_ptrxPortB, 5, 1);
    ptopLayout->addWidget(plblPortSW, 6, 0);
    ptopLayout->addWidget(m_ptrxPortSW, 6, 1);
    ptopLayout->addWidget(plblPortSFP1, 7, 0);
    ptopLayout->addWidget(m_ptrxPortSFP1, 7, 1);
    ptopLayout->addWidget(plblPortSFP2, 8, 0);
    ptopLayout->addWidget(m_ptrxPortSFP2, 8, 1);
    ptopLayout->addWidget(plblPortMan, 9, 0);
    ptopLayout->addWidget(m_ptrxPortMan, 9, 1);
    ptopLayout->addWidget(pcmdOk, 10,0);
    ptopLayout->addWidget(pcmdCancel, 10, 1);
    setLayout(ptopLayout);
}

int SwitchSetDialog::getState() const
{
    return m_ptxtState->currentIndex();
}

QString SwitchSetDialog::getHost() const
{
    return m_ptxtHost->text();
}

QString SwitchSetDialog::getLogin() const
{
    return m_ptxtLogin->text();
}

QString SwitchSetDialog::getPass() const
{
    return m_ptxtPass->text();
}

int SwitchSetDialog::getPortA() const
{
    return m_ptrxPortA->value();
}

int SwitchSetDialog::getPortB() const
{
    return m_ptrxPortB->value();
}

int SwitchSetDialog::getPortDUT() const
{
    return m_ptrxPortSW->value();
}

int SwitchSetDialog::getPortSFP1() const
{
    return m_ptrxPortSFP1->value();
}

int SwitchSetDialog::getPortSFP2() const
{
    return m_ptrxPortSFP2->value();
}
int SwitchSetDialog::getPortMan() const
{
    return m_ptrxPortMan->value();
}

void MainWindow::send_confirm(QString text,int conf){
    QMessageBox ms;
    QAbstractButton *yes;
    QAbstractButton *no;
    if(conf){
        yes = ms.addButton("Да",QMessageBox::YesRole);
        no = ms.addButton("Нет",QMessageBox::NoRole);
    }
    else
        yes = ms.addButton("Ok",QMessageBox::YesRole);
    ms.setText(text);
    ms.exec();
    if(ms.clickedButton() == yes)
        pTestThread->confirm_status = 1;
    else
        pTestThread->confirm_status = 0;
    pTestThread->confirmed = 1;
    qDebug() << "done" << pTestThread->confirmed;
}

/*окно настройки com порта для Teleport*/
// диалог выбора com порта
TeleportSetDialog::TeleportSetDialog(QString pn, int state)
{
    QString str;

    port_num = pn;
    port_state = state;

    this->setWindowTitle(tr("Настройка Teleport"));
    this->setMinimumWidth(300);
    this->setMinimumHeight(150);

    m_ptxtState = new QComboBox;
    m_ptxtState->addItem("Выключить");
    m_ptxtState->addItem("Включить");
    if(port_state)
        m_ptxtState->setCurrentIndex(1);
    else
        m_ptxtState->setCurrentIndex(0);

    m_ptxtPortNum = new QComboBox;
    const auto infos = QSerialPortInfo::availablePorts();
    for (const QSerialPortInfo &info : infos) {
        m_ptxtPortNum->addItem(info.portName());
        if(strcmp(info.portName().toLocal8Bit().data(),port_num.toLocal8Bit().data())==0){
            m_ptxtPortNum->setCurrentText(info.portName());
        }
    }

    QLabel* plblPortNum    = new QLabel("&COM Порт");
    QLabel* plblState    = new QLabel("&Состояние");

    plblPortNum->setBuddy(m_ptxtPortNum);
    plblState->setBuddy(m_ptxtState);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblState, 0, 0);
    ptopLayout->addWidget(m_ptxtState, 0, 1);
    ptopLayout->addWidget(plblPortNum, 1, 0);
    ptopLayout->addWidget(m_ptxtPortNum, 1, 1);
    ptopLayout->addWidget(pcmdOk, 2,0);
    ptopLayout->addWidget(pcmdCancel, 2, 1);
    setLayout(ptopLayout);
}

QString TeleportSetDialog::portNum() const
{
    return m_ptxtPortNum->currentText();
}

void TeleportSetDialog::setPortNum(int num){
    port_num = num;
}

void TeleportSetDialog::setBaudRate(int rate){
    port_rate = rate;
}

int TeleportSetDialog::getState() const{
    qDebug() << "port_state" << port_state;
    return m_ptxtState->currentIndex();
}

void TeleportSetDialog::setState(int state){
    port_state = state;
}

//настройка подключения к Стенду RPS-1
// диалог выбора com порта
RpsStandSetDialog::RpsStandSetDialog(QString pn, int state)
{
    QString str;

    port_name = pn;
    port_state = state;

    this->setWindowTitle(tr("Настройка Стенда RPS-1"));
    this->setMinimumWidth(300);
    this->setMinimumHeight(150);

    m_ptxtState = new QComboBox;
    m_ptxtState->addItem("Выключить");
    m_ptxtState->addItem("Включить");
    if(port_state)
        m_ptxtState->setCurrentIndex(1);
    else
        m_ptxtState->setCurrentIndex(0);

    m_ptxtPortName = new QComboBox;
    const auto infos = QSerialPortInfo::availablePorts();
    for (const QSerialPortInfo &info : infos) {
        m_ptxtPortName->addItem(info.portName());
        if(strcmp(info.portName().toLocal8Bit().data(),port_name.toLocal8Bit().data())==0){
            m_ptxtPortName->setCurrentText(info.portName());
        }
    }

    QLabel* plblPortNum    = new QLabel("&COM Порт");
    QLabel* plblState    = new QLabel("&Состояние");

    plblPortNum->setBuddy(m_ptxtPortName);
    plblState->setBuddy(m_ptxtState);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblState, 0, 0);
    ptopLayout->addWidget(m_ptxtState, 0, 1);
    ptopLayout->addWidget(plblPortNum, 1, 0);
    ptopLayout->addWidget(m_ptxtPortName, 1, 1);
    ptopLayout->addWidget(pcmdOk, 2,0);
    ptopLayout->addWidget(pcmdCancel, 2, 1);
    setLayout(ptopLayout);
}

QString RpsStandSetDialog::portName() const
{
    return m_ptxtPortName->currentText();
}

int RpsStandSetDialog::getState() const{
    qDebug() << "port_state" << port_state;
    return m_ptxtState->currentIndex();
}

//Настройка подключения к внешнему БП
PowerSupplySetDialog::PowerSupplySetDialog(int state,int model,QString port)
{
    QString str;

    port_name = port;
    ps_model = model;
    ps_state = state;

    this->setWindowTitle(tr("Настройка внешнего БП"));
    this->setMinimumWidth(300);
    this->setMinimumHeight(150);

    m_ptxtState = new QComboBox;
    m_ptxtState->addItem("Выключить");
    m_ptxtState->addItem("Включить");
    if(ps_state)
        m_ptxtState->setCurrentIndex(1);
    else
        m_ptxtState->setCurrentIndex(0);

    m_ptxtModel = new QComboBox;
    m_ptxtModel->addItem("Default");
    m_ptxtModel->setCurrentIndex(ps_model);

    m_ptxtPortName = new QComboBox;

    const auto infos = QSerialPortInfo::availablePorts();
    for (const QSerialPortInfo &info : infos) {
        m_ptxtPortName->addItem(info.portName());
        if(strcmp(info.portName().toLocal8Bit().data(),port_name.toLocal8Bit().data())==0){
            m_ptxtPortName->setCurrentText(info.portName());
        }
    }
    QLabel* plblState    = new QLabel("&Состояние");
    QLabel* plblModel    = new QLabel("&Модель БП");
    QLabel* plblPortNum    = new QLabel("&COM Порт");

    plblPortNum->setBuddy(m_ptxtPortName);
    plblModel->setBuddy(m_ptxtModel);
    plblState->setBuddy(m_ptxtState);

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    ptopLayout->addWidget(plblState, 0, 0);
    ptopLayout->addWidget(m_ptxtState, 0, 1);
    ptopLayout->addWidget(plblModel, 1, 0);
    ptopLayout->addWidget(m_ptxtModel, 1, 1);
    ptopLayout->addWidget(plblPortNum, 2, 0);
    ptopLayout->addWidget(m_ptxtPortName, 2, 1);
    ptopLayout->addWidget(pcmdOk, 3,0);
    ptopLayout->addWidget(pcmdCancel, 3, 1);
    setLayout(ptopLayout);
}

QString PowerSupplySetDialog::portName() const
{
    return m_ptxtPortName->currentText();
}

int PowerSupplySetDialog::getState() const{
    qDebug() << "state" << ps_state;
    return m_ptxtState->currentIndex();
}
int PowerSupplySetDialog::getModel() const{
    qDebug() << "port_state" << ps_model;
    return m_ptxtModel->currentIndex();
}

/*окно Обновление ПО Стенда*/
MbUpdateDialog::MbUpdateDialog(){
    QString str;
    this->setWindowTitle(tr("Обновление ПО Стенда"));
    this->setMinimumWidth(200);
    this->setMinimumHeight(100);

    m_ptxtSlotNUm = new QComboBox;
    for(int i=0;i<STAND_SLOTS_NUM;i++){
        str.sprintf("%d",i+1);
        m_ptxtSlotNUm->addItem(str);
    }

    m_ptxtFwPath = new QLineEdit();

    QPushButton* pcmdBrowsePath    = new QPushButton("&Обзор");

    QPushButton* pcmdStartUpdate    = new QPushButton("&Начать обновление");

    QLabel* plblSlotNum    = new QLabel("Номер слота");

    plblProgress    = new QLabel("");

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    connect(pcmdBrowsePath, SIGNAL(clicked()), SLOT(open_file()));
    connect(pcmdStartUpdate, SIGNAL(clicked()), SLOT(updating_start_pb()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    //table title

    ptopLayout->addWidget(plblSlotNum, 0,0);
    ptopLayout->addWidget(m_ptxtSlotNUm, 0, 1);
    ptopLayout->addWidget(pcmdBrowsePath,1,0);
    ptopLayout->addWidget(m_ptxtFwPath,1,1);
    ptopLayout->addWidget(plblProgress,2,1);
    ptopLayout->addWidget(pcmdStartUpdate,3,1);

    setLayout(ptopLayout);
}

void MbUpdateDialog::set_progress(){
    plblProgress->setText("....");
}

void MbUpdateDialog::open_file(){
    fw_path=QFileDialog::getOpenFileName(this,tr("Open IMG"), "", tr("IMG Files (*.img)"));
    m_ptxtFwPath->setText(fw_path);
}

void MbUpdateDialog::updating_start_pb(){
    emit updating_start(m_ptxtSlotNUm->currentIndex(),fw_path);
}

/*настройка программаторов*/
ProgrammersSetDialog::ProgrammersSetDialog(settingsmodel prog_sett){
    QString str;
    this->setWindowTitle(tr("Настройка Программаторов"));
    this->setMinimumWidth(200);
    this->setMinimumHeight(100);

    m_ptxtDfuPath = new QLineEdit(prog_sett.dfu_prog_path);
    m_ptxtAvrPath = new QLineEdit(prog_sett.avr_prog_path);

    m_ptxtAvrProgType = new QComboBox;
    m_ptxtAvrProgType->addItem("Atmel-ICE");
    m_ptxtAvrProgType->addItem("AVRISP mkII");
    m_ptxtAvrProgType->addItem("AS-Kit AS4");
    m_ptxtAvrProgType->setCurrentIndex(prog_sett.avr_prog_type);

    QPushButton* pcmdBrowseDfu    = new QPushButton("&Обзор");
    QPushButton* pcmdBrowseAvr    = new QPushButton("&Обзор");

    QLabel* plblDfuPath    = new QLabel("DFU: путь до папки с DfuSeCommand.exe");
    QLabel* plblAvrPath    = new QLabel("Atmel: путь до папки с atprogram.exe или asisp.exe");
    QLabel* plblAvrProgType    = new QLabel("Atmel: тип программатора");

    QPushButton* pcmdOk     = new QPushButton("&Ok");
    QPushButton* pcmdCancel = new QPushButton("&Cancel");

    connect(pcmdOk, SIGNAL(clicked()), SLOT(accept()));
    connect(pcmdCancel, SIGNAL(clicked()), SLOT(reject()));

    connect(m_ptxtAvrProgType, SIGNAL(currentIndexChanged(int)), SLOT(avr_path_changed(int)));

    connect(pcmdBrowseDfu, SIGNAL(clicked()), SLOT(open_config_dfu()));
    connect(pcmdBrowseAvr, SIGNAL(clicked()), SLOT(open_config_avr()));

    //Layout setup
    QGridLayout* ptopLayout = new QGridLayout;

    //table title
    ptopLayout->addWidget(plblDfuPath, 0,0);
    ptopLayout->addWidget(m_ptxtDfuPath, 0, 1);
    ptopLayout->addWidget(pcmdBrowseDfu, 0, 2);

    ptopLayout->addWidget(plblAvrProgType, 1,0);
    ptopLayout->addWidget(m_ptxtAvrProgType, 1, 1);

    ptopLayout->addWidget(plblAvrPath, 2,0);
    ptopLayout->addWidget(m_ptxtAvrPath, 2, 1);
    ptopLayout->addWidget(pcmdBrowseAvr, 2, 2);



    ptopLayout->addWidget(pcmdOk, 3,0);
    ptopLayout->addWidget(pcmdCancel, 3, 1);

    setLayout(ptopLayout);
}

void ProgrammersSetDialog::open_config_dfu(){
    m_ptxtDfuPath->setText(QFileDialog::getExistingDirectory(this, tr("Open Directory"),
                                                             m_ptxtDfuPath->text(),/*QFileDialog::ShowDirsOnly | */QFileDialog::DontResolveSymlinks));
}
void ProgrammersSetDialog::open_config_avr(){
    m_ptxtAvrPath->setText(QFileDialog::getExistingDirectory(this, tr("Open Directory"),
                                                             m_ptxtAvrPath->text(),/*QFileDialog::ShowDirsOnly | */QFileDialog::DontResolveSymlinks));
}

QString ProgrammersSetDialog::get_dfu_path(){
    return m_ptxtDfuPath->text();
}

QString ProgrammersSetDialog::get_avr_path(){
    return m_ptxtAvrPath->text();
}

int ProgrammersSetDialog::get_avr_programmer(){
    if(m_ptxtAvrProgType->currentIndex() == 0)
        return AvrProgTypes::ATMELICE;
    else if(m_ptxtAvrProgType->currentIndex() == 1)
        return AvrProgTypes::AVRMK2;
    else if(m_ptxtAvrProgType->currentIndex() == 2)
        return AvrProgTypes::AS4;
    else return 0;
}
void ProgrammersSetDialog::avr_path_changed(int index){
    if(m_ptxtAvrProgType->currentIndex() == 2){
        m_ptxtAvrPath->setText(QString("C:/FortTelecom/Launcher/TFortisStandNew/firmwares"));
    }
    else if(m_ptxtAvrProgType->currentIndex() == 1 || m_ptxtAvrProgType->currentIndex() == 0){
        m_ptxtAvrPath->setText(QString("C:/Program Files (x86)/Atmel/Studio/7.0"));
    }
}
