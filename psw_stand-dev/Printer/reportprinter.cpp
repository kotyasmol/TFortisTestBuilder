#include "reportprinter.h"
#include "ui_reportprinter.h"
#include "mainwindow.h"
#include "functions.h"
#include <QSqlDatabase>
#include <QSqlError>
#include <QSqlQuery>
#include <qDebug>
#include <QMessageBox>
#include <QPrinter>
#include <QTextDocument>

//печать отчётов в PDF

ReportPrinter::ReportPrinter(settingsmodel program_sett,QWidget *parent) :
    QMainWindow(parent),
    ui(new Ui::ReportPrinter)
{
    QString tmp_dev_name;

    ui->setupUi(this);

    webmanager = new QNetworkAccessManager(this);
    cookie = new QNetworkCookieJar(this);


    memcpy(&progSett,&program_sett,sizeof(progSett));

    /*заполенение типа устройств*/
    for(int i=0;i<MAX_DEVICES;i++){
        if(get_dev_name(progSett, i ,&tmp_dev_name))
            if(!tmp_dev_name.isEmpty())
                ui->report_type->addItem(tmp_dev_name);
    }
    connect(ui->report_make,SIGNAL(clicked()),SLOT(make_report()));
}

ReportPrinter::~ReportPrinter()
{
    delete ui;
}

void ReportPrinter::make_report(){
    //long int serial_barcode;
    bool ok;

    serial = ui->report_serial->text().toLong(&ok,10)+get_dev_type_by_name(progSett,ui->report_type->currentText())*100000;

    qDebug() << serial;

    if(connected==0){
        char login[] = "alex";
        char pass[] = "alex";
        reportprinter_connect(login, pass);
    }
    else{
        get_reportprinter_history(serial);
    }
}

void ReportPrinter::reportprinter_connect(char *login,char *pass){
    QString request;
    request.sprintf("http://%s/api/api.svc/connect?login=%s&password=%s&timezone=5",
                    progSett.db_host.toLocal8Bit().data(),login,pass);
    qDebug() << request;
    webmanager->connectToHost(progSett.db_host);

    webreply = webmanager->get(QNetworkRequest(QUrl(request)));

    connect(webreply,SIGNAL(finished()),SLOT(connectFinished()));
    connect(webreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
}

void ReportPrinter::get_reportprinter_history(long int serial){
    QString str;
    str.sprintf("http://%s/api/api.svc/getdevicehistorylist?findParam=serial&findVal=%lu",
                progSett.db_host.toLocal8Bit().data(),serial);
    webmanager->connectToHost(progSett.db_host);
    webmanager->setCookieJar(cookie);
    webreply = webmanager->get(QNetworkRequest(QUrl(str)));
    connect(webreply,SIGNAL(finished()),SLOT(getHistoryFinished()));
    connect(webreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
    qDebug() << str;
}

void ReportPrinter::reportprinter_make_pdf(){
    QString str;
    QTextDocument *document = new QTextDocument();
    long type;
    bool ok;

    QSizeF paperSize;

    qDebug() << paperSize;

    QString html;

    type = get_dev_type_by_name(progSett,ui->report_type->currentText());

    qDebug() << type;

    QPrinter printer(QPrinter::HighResolution);
    printer.setPaperSize(QPrinter::A4);
    printer.setOutputFormat(QPrinter::PdfFormat);
    printer.setOutputFileName("PSW_Stand_report.pdf");

    paperSize.setWidth(printer.width());
    paperSize.setHeight(printer.height());

    html.append(" <style> \
                p {font-size: 12pt; }\
                td {font-size: 12pt; }\
                </style>");
                html.append("<p align='center' ><b>Протокол приёмо-сдаточных испытаний</b></p>");
            html.append("<p align='center' ><b>от ");
    html.append(product_date);
    html.append("</p><table width=\"100%\">"
                "<tr><td>Наименование изделия:</td><td>");
    html.append(ui->report_type->currentText());
    html.append("</td></tr>"
                "<tr><td>Серийный номер изделия:</td><td>");
    if(serial>99999)
        str.sprintf("%05lu",serial-100000*type);
    else
        str.sprintf("%05lu",serial);
    html.append(str);
    html.append("</td></tr>"
                "<tr><td>Идентификатор стенда:</td><td>АПК СТЕНД-01 (№");
    stand_id.toDouble(&ok);
    if(stand_id.isEmpty() || ok == false)
        html.append("1");
    else
        html.append(stand_id);
    html.append(")</td></tr>"
                "<tr><td></td></tr>"
                "<tr><td>Перечень проверок:</td><td>"
                "</td></tr>"
                "</table>");
    html.append("<p align=\"left\"><table border=\"1\" width=\"100%\" cellpadding=\"10\">"
                "<tr><td>Контролируемая характеристика</td><td>");

    if(ui->report_type->currentText().contains("SG",Qt::CaseInsensitive)){
        html.append("Требования ТУ 27.12.23-14-80080065-2017"
                    "</td><td>Результаты испытаний</td><td>Выводы</td></tr>");

        html.append("<tr><td>Проверка внешнего вида"
                    "</td><td>п.1.1.1 (4.7)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка передачи данных"
                    "</td><td>(4.1)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка комплектности"
                    "</td><td>п.1.7 (4.7)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка маркировки"
                    "</td><td>п.1.8 (4.7)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка индивидуальной упаковки"
                    "</td><td>п.1.9 (4.7)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("</table>");

        html.append("<br><table width=\"100%\">"
                    "<tr><td>Заключение о работоспособности: </td><td>");
        html.append("<b>устройство соответствует требованиям ТУ 27.12.23-14-80080065-2017</b>");
    }
    if(ui->report_type->currentText().contains("SWU",Qt::CaseInsensitive)){
        html.append("Требования ИЛПГ.300514.011 ТУ"
                    "</td><td>Результаты испытаний</td><td>Выводы</td></tr>");

        html.append("<tr><td>Проверка внешнего вида"
                    "</td><td>п.1.1.1 (1.4)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка работы встроенного ПО"
                    "</td><td>п.1.2.3 (4.2)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка передачи данных"
                    "</td><td>п.1.2.1 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка индикации"
                    "</td><td>п.1.2.2 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка комплектности"
                    "</td><td>п.1.7 (4.2)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка маркировки"
                    "</td><td>п.1.8 (4.2)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка индивидуальной упаковки"
                    "</td><td>п.1.9 (4.2)</td><td>соотв.</td><td>соотв.</td></tr>");
        if(ui->report_type->currentText().contains("SWU-16T",Qt::CaseInsensitive)){
            html.append("<tr><td>Проверка перехода на резервное питание"
                        "</td><td>п.1.2.10 (4.17)</td><td>соотв.</td><td>соотв.</td></tr>");
        }
        html.append("</table>");

        html.append("<br><table width=\"100%\">"
                    "<tr><td>Заключение о работоспособности: </td><td>");
        html.append("<b>устройство соответствует требованиям ИЛПГ.300514.011 ТУ</b>");
    }
    if(ui->report_type->currentText().contains("PSW",Qt::CaseInsensitive)){
        html.append("Требования ИЛПГ.300409.003 ТУ"
                    "</td><td>Результаты испытаний</td><td>Выводы</td></tr>");

        html.append("<tr><td>Проверка внешнего вида"
                    "</td><td>п.1.1.1, 2.1 (4.2)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка выполнения требований стандарта IEEE802.3af или IEEE802.3at"
                    "</td><td>п.1.2.1 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка поддержки Passive PoE"
                    "</td><td>п.1.2.2 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка заявленного бюджета PoE"
                    "</td><td>п.1.2.3 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");

        html.append("<tr><td>Проверка передачи данных"
                    "</td><td>п.1.2.4 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        if(ui->report_type->currentText().contains("UPS",Qt::CaseInsensitive)){
            html.append("<tr><td>Проверка работоспособности при отключении "
                        "питания 220В"
                        "</td><td>п.1.2.5 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        }
        html.append("<tr><td>Проверка индикации"
                    "</td><td>п.1.2.6 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка комплектности"
                    "</td><td>п.1.7 (4.2)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка маркировки"
                    "</td><td>п.1.8 (4.2)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка индивидуальной упаковки"
                    "</td><td>п.1.9 (4.2)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("</table>");


        html.append("<br><table width=\"100%\">"
                    "<tr><td>Заключение о работоспособности: </td><td>");
        html.append("<b>устройство соответствует требованиям ИЛПГ.300409.003 ТУ</b>");
    }

    if(ui->report_type->currentText().contains("SWD",Qt::CaseInsensitive)){
        html.append("Требования ИЛПГ.300514.010 ТУ"
                    "</td><td>Результаты испытаний</td><td>Выводы</td></tr>");
        html.append("<tr><td>Проверка внешнего вида"
                    "</td><td>п.1.1.1, 1.4 (4.6)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка взаимодействия с устройствами типа PoE PD в соответствии с IEEE 802.3 af"
                    "</td><td>п.1.2.1 (4.4)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка уровня напряжения для питания устройства типа PoE PD при потребляемой мощности 10 Вт"
                    "</td><td>п.1.2.2 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка индикации подачи выходного напряжения по каждому каналу и индикации подключения к сети ~220 В."
                    "</td><td>п.1.2.3 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка работы при изменении переменного напряжения питающей сети от 187 до 242 В"
                    "</td><td>п.1.2.4 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка передачи данных"
                    "</td><td>п.1.2.7 (4.3)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка комплектности"
                    "</td><td>п.1.7 (4.6)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка маркировки"
                    "</td><td>п.1.8 (4.6)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("<tr><td>Проверка упаковки"
                    "</td><td>п.1.9 (4.6)</td><td>соотв.</td><td>соотв.</td></tr>");
        html.append("</table>");


        html.append("<br><table width=\"100%\">"
                    "<tr><td>Заключение о работоспособности: </td><td>");
        html.append("<b>устройство соответствует требованиям ИЛПГ.300514.010 ТУ</b>");
    }

    html.append("</td></tr>"
                "<tr><td></td><td>");

    html.append("</td></tr>");
    html.append("<tr><td>Оператор проверки:</td><td>");
    html.append("_________(");
    if(user.isEmpty())
        html.append("Галаев П.В.");
    else
        html.append(user);
    html.append(")</td></tr>");

    html.append("</table>");
    html.append("<p align='right' >МП</p>");

    document->setHtml(html);

    document->print(&printer);

    QUrl url("file:PSW_Stand_report.pdf");
    QDesktopServices::openUrl(url);
}

//slots
void ReportPrinter::connectFinished(void){

    QByteArray arr;
    if(webreply->error()==QNetworkReply::NoError){
        arr = webreply->readAll();
        qDebug()<< arr.data();
        if(arr.length()){
            if(strcmp(arr.data(),"NoAuth")==0){
                emit syslog("Ошибка авторизации",E);
                connected = 0;
            }else{
                cookie = webmanager->cookieJar();
                connected = 1;
                get_reportprinter_history(serial);
            }

        }
        else{
            emit syslog("Пустой ответ",E);
        }
    }
    else{
        qDebug() << "connectFinished" << webreply->error();
    }
}

void ReportPrinter::getHistoryFinished(void){
    QByteArray arr;
    QJsonObject json;
    QJsonParseError  parseError;

    if(webreply->error()==QNetworkReply::NoError){
        arr = webreply->readAll();
        qDebug()<<"getHistoryFinished"<< arr.data();
        QJsonDocument jsonDoc = QJsonDocument::fromJson(arr, &parseError);
        if(parseError.error == QJsonParseError::NoError){
            qDebug() << jsonDoc.isArray();
            if(jsonDoc.array().count()){
                json = jsonDoc.array().at(0).toObject();

                product_date = json["device"].toObject()["creation_time"].toString();
                product_date.replace("00:00:00","");
                product_date =  product_date.left(product_date.indexOf(' ',0,Qt::CaseInsensitive));

                user=json["actions"].toArray().at(1).toObject()["action_text"].toString();
                if(user.contains("Пользователь",Qt::CaseInsensitive)){
                    user =  user.right(user.length() - user.indexOf("Пользователь:",0,Qt::CaseInsensitive)-13);
                }
                else
                    user.clear();
                qDebug() << "user" << user;

                stand_id=json["actions"].toArray().at(1).toObject()["action_text"].toString();
                if(stand_id.contains("Стенд:",Qt::CaseInsensitive)){
                    stand_id = stand_id.mid(stand_id.indexOf("Стенд: ",0,Qt::CaseInsensitive)+7,2);
                }
                else
                    stand_id.clear();
                qDebug() << "Стенд" << stand_id;


                reportprinter_make_pdf();
            }
            else{
                ui->report_result->setText("Устройство не найдено");
            }
        }
        else{
            qDebug() <<"parseError"<< parseError.error;
        }
    }
    else{
        qDebug() << "getHistoryFinished" << webreply->error();
    }
}

void ReportPrinter::reportError(QNetworkReply::NetworkError error){
    qDebug()<< error;
}
