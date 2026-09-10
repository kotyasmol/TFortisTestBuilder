#ifndef REPORTPRINTER_H
#define REPORTPRINTER_H

#include <QMainWindow>
#include "ui.h"
#include <QNetworkAccessManager>
#include <QNetworkCookieJar>

namespace Ui {
class ReportPrinter;
}

#define MAX_REPORT_NUM 10000

//формат ответа поиска из БД
struct report_t{
    bool valid;
    int sn;
    int dev_type;
    QString date_work;
    QString user;
};

class ReportPrinter : public QMainWindow
{
    Q_OBJECT

public:
    explicit ReportPrinter(settingsmodel program_sett,QWidget *parent = 0);
    ~ReportPrinter();

public slots:
    void make_report();
    void get_reportprinter_history(long int serial);
    void reportprinter_make_pdf();
    void connectFinished(void);
    void getHistoryFinished(void);
    void reportError(QNetworkReply::NetworkError error);
signals:
    void syslog(QString str,int level);

private:
    Ui::ReportPrinter *ui;
    struct report_t reports[MAX_REPORT_NUM];
    struct settingsmodel progSett;
    struct configmodel config;
    long int serial;
    QString product_date;
    QString user;
    QString stand_id;
    QNetworkAccessManager *webmanager;
    QNetworkReply *webreply;
    QNetworkCookieJar *cookie;
    int connected;
    //TODO Сделать переменные const
    void reportprinter_connect(char *login,char *pass);
};

#endif // REPORTPRINTER_H
