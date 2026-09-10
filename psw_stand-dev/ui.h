#ifndef _InputDialog_h_
#define _InputDialog_h_

#include "mainwindow.h"
#include <QDialog>
#include <QtGui>
#include <QComboBox>
#include <QRadioButton>
#include <pcap.h>
#include <QLabel>
#include <QSpinBox>

//class QLineEdit;
class InputDialog : public QDialog {
    Q_OBJECT
private:
    QLineEdit* m_ptxtFirstName;
    QLineEdit* m_ptxtLastName;

public:
    InputDialog(QWidget* pwgt = 0);

    QString firstName() const;
    QString lastName () const;
};

class ComSetDialog : public QDialog {
    Q_OBJECT
private:
    QComboBox* m_ptxtPortNum;
    QComboBox* m_ptxtBaudRate;
    QLineEdit* m_ptxtStandID;
    QString port_num,stand_id;
    int stand_type;
    QCheckBox     *m_ptxtAutoconnect;
    QRadioButton  *m_ptxtTypeModbus;
    QRadioButton  *m_ptxtTypeRpsNew;
    QRadioButton  *m_ptxtTypeGen;

public:
    ComSetDialog(QString pn,int autoconnect,int st,QString id);

    QString portNum() const;
    int portAutoconnect() const;
    QString getStandID() const;
    int baudRate () const;
    int standType() const;

};

class DbSetDialog : public QDialog {
    Q_OBJECT

private:
    QRadioButton  *m_ptxtType_http;
    QRadioButton  *m_ptxtType_https;
    QLineEdit     *m_ptxtDB_Host;

public:
    DbSetDialog(int type,QString host);
    int type() const;
    QString host() const;
};


class NetCardSetDialog : public QDialog {
    Q_OBJECT

private:
    QComboBox* m_ptxtPort[PORT_NUM];

public:
    NetCardSetDialog(QString *card_index);

    QLabel* plblPort[PORT_NUM];
    QString get_card(int num);
};

class UserSetDialog : public QDialog {
    Q_OBJECT
private:
    QLineEdit* m_ptxtUserName[USERS_NUM];
    QLabel* plblUserName[PORT_NUM];

public:
    UserSetDialog(QString name[USERS_NUM]);

    QString get_username(int i);
};

class UserDialog : public QDialog {
    Q_OBJECT
private:
    QComboBox* m_ptxtUserName;
public:
    UserDialog(QString name[USERS_NUM]);

    int get_current_username();
};


class PrinterSetDialog : public QDialog {
    Q_OBJECT
private:
    QComboBox* m_ptxtPrinterName;
    QComboBox* m_ptxtReportPrinterName;
    QComboBox* m_ptxtLabelSize;
public:
    PrinterSetDialog(QString name, int labelSize,QString rep_name);

    QString get_printername();
    QString get_report_printername();
    int get_labelSize();
};

class ProfilesSetDialog : public QDialog {
    Q_OBJECT
private:
    QLineEdit* m_ptxtDevName[MAX_DEVICES];
    QLineEdit* m_ptxtConfTest[MAX_DEVICES];
    QLabel plblNum[MAX_DEVICES];
    QLabel title[4];
    QPushButton* pcmdBrowseTest[MAX_DEVICES];
    QPushButton* pcmdDelTest[MAX_DEVICES];
public:
    ProfilesSetDialog(settingsmodel prog_sett);
    QString get_config_test(int i);//get production config

public slots:
    void open_config_test(int);
    void del_config_test(int);

};

//Окно ввода списка устройств
class DevListDialog : public QDialog {
    Q_OBJECT
private:
    QLineEdit* m_ptxtDevId[MAX_DEVICES];
    QLineEdit* m_ptxtDevName[MAX_DEVICES];
public:
    DevListDialog(settingsmodel prog_sett);
    int getDevListId(int i);
    QString getDevListName(int i);
};


class BercutSetDialog : public QDialog {
    Q_OBJECT
private:
    QComboBox* m_ptxtPortNum;
    QComboBox* m_ptxtState;
    QComboBox* m_ptxtType;
    QComboBox* m_ptxtPortNum100;
    QComboBox* m_ptxtState100;
    QComboBox* m_ptxtType100;
    QString port_num;
    int port_state;
    int test_type;
    QString port_num100;
    int port_state100;
    int test_type100;

public:
    BercutSetDialog(QString pn,int state, int type, QString pn100,int state100, int type100);

    //int portNum() const;
    QString portNum() const;
    QString portNum100() const;

    int baudRate () const;
    int getState() const;
    int getType() const;
    int getState100() const;
    int getType100() const;

    void setPortNum(int num);
    void setBaudRate (int rate);
    void setState(int state);
    void setType(int type);
};

class SwitchSetDialog : public QDialog {
    Q_OBJECT
private:
    QComboBox* m_ptxtState;
    QLineEdit* m_ptxtHost;
    QLineEdit* m_ptxtLogin;
    QLineEdit* m_ptxtPass;
    QSpinBox* m_ptrxPortA;
    QSpinBox* m_ptrxPortB;
    QSpinBox* m_ptrxPortSW;
    QSpinBox* m_ptrxPortSFP1;
    QSpinBox* m_ptrxPortSFP2;
    QSpinBox* m_ptrxPortMan;

    QString host_;
    QString login_;
    QString pass_;
    int state_;
    int port_a_;
    int port_b_;
    int port_dut_;
    int port_sfp1_;
    int port_sfp2_;
    int port_man_;
public:
    SwitchSetDialog(int state,QString host,QString login,QString pass,int port_a, int port_b,int port_dut, int port_sfp1, int port_sfp2,int port_man);
    int getState() const;
    QString getHost() const;
    QString getLogin() const;
    QString getPass() const;
    int getPortA() const;
    int getPortB() const;
    int getPortDUT() const;
    int getPortSFP1() const;
    int getPortSFP2() const;
    int getPortMan() const;
};

class TeleportSetDialog : public QDialog {
    Q_OBJECT
private:
    QComboBox* m_ptxtPortNum;
    QComboBox* m_ptxtBaudRate;
    QComboBox* m_ptxtState;
    QString port_num;
    int port_rate;
    int port_state;

public:
    TeleportSetDialog(QString pn,int state);

    QString portNum() const;

    int baudRate () const;
    int getState() const;

    void setPortNum(int num);
    void setBaudRate (int rate);
    void setState(int state);
};

class RpsStandSetDialog : public QDialog {
    Q_OBJECT
private:
    QComboBox* m_ptxtPortName;
    QComboBox* m_ptxtState;
    QString port_name;
    int port_state;

public:
    RpsStandSetDialog(QString pn,int state);
    QString portName() const;
    int getState() const;
};

class PowerSupplySetDialog : public QDialog {
    Q_OBJECT
private:
    QComboBox* m_ptxtState;
    QComboBox* m_ptxtModel;
    QComboBox* m_ptxtPortName;
    int ps_state;
    int ps_model;
    QString port_name;

public:
    PowerSupplySetDialog(int state,int model,QString pn);
    int getState() const;
    int getModel() const;
    QString portName() const;
};


class MbUpdateDialog:public QDialog {
    Q_OBJECT
private:
    QComboBox *m_ptxtSlotNUm;
    QLineEdit *m_ptxtFwPath;
    QLabel *plblProgress;
    QString fw_path;
private slots:
    void open_file();
    void updating_start_pb();
signals:
    void updating_start(int slot, QString file);


public:
    MbUpdateDialog();
    void set_progress();
};

class ProgrammersSetDialog : public QDialog {
    Q_OBJECT
private:
    QLineEdit* m_ptxtDfuPath;
    QLineEdit* m_ptxtAvrPath;
    QComboBox *m_ptxtAvrProgType;

public:
    ProgrammersSetDialog(settingsmodel prog_sett);
    QString get_dfu_path();
    QString get_avr_path();
    int get_avr_programmer();

public slots:
    void open_config_dfu();
    void open_config_avr();
    void avr_path_changed(int index);
};


#endif  //_InputDialog_h_
