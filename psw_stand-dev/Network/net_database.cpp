#include "mainwindow.h"
#include "ui_mainwindow.h"
#include <QSqlDatabase>
#include <QSqlError>
#include <QSqlQuery>
#include "devicebase.h"
#include <QMessageBox>
#include "Printer/reportprinter.h"
#include "TestThread.h"
#include <QSslSocket>

/**********************************************************************/
//запрос серийного номера по CPU ID
void MainWindow::get_serial_num(QString dev_name, QString cpu_id){
    QString request;

    qDebug() << "MainWindow::get_serial_num" << dev_name;

    pTestThread->nettest.serial_num = 0;
    pTestThread->nettest.get_serial = false;

    if(pTestThread->db_connected)
    {
        if(prog_sett.db_type == DB_HTTP){
            if(cpu_id.isEmpty())
            {
                request.sprintf("http://%s/api/api.svc/getSerialNum?devType=%s",
                                prog_sett.db_host.toLocal8Bit().data(), dev_name.toLocal8Bit().data());
            }
            else
            {
                request.sprintf("http://%s/api/api.svc/getSerialNum?devType=%s&cpuId=%s",
                                prog_sett.db_host.toLocal8Bit().data(), dev_name.toLocal8Bit().data(), cpu_id.toLocal8Bit().data());
            }

            if(request.contains('+',Qt::CaseInsensitive))
                request.replace('+',"%2B",Qt::CaseInsensitive);

            syslog(request, I);

            if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
                reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

            reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

            connect(reportreply,SIGNAL(finished()),SLOT(getSerialFinished()));
            connect(reportreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
        }
        else{
            if(cpu_id.isEmpty())
            {
                request.sprintf("https://%s/api/api.svc/getSerialNum?devType=%s",
                                prog_sett.db_host.toLocal8Bit().data(), dev_name.toLocal8Bit().data());
            }
            else
            {
                request.sprintf("https://%s/api/api.svc/getSerialNum?devType=%s&cpuId=%s",
                                prog_sett.db_host.toLocal8Bit().data(), dev_name.toLocal8Bit().data(), cpu_id.toLocal8Bit().data());
            }

            if(request.contains('+',Qt::CaseInsensitive))
                request.replace('+',"%2B",Qt::CaseInsensitive);

            syslog(request, I);

            if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
                reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

            reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

            connect(reportreply,SIGNAL(finished()),SLOT(getSerialFinished()));
            connect(reportreply, SIGNAL(sslErrors(QList<QSslError>)),this,SLOT(reportSslConnectError(QList<QSslError>)));//warning | const QList -> QList
        }
    }
    else
    {
        syslog("Отсутствует связь с сервером",E);
    }
}

//отправка отчёта о тестировании на сервер
void MainWindow::set_test_result(void)
{
    if (prog_sett.test_type == TYPE_REPAIR && !ui->save_rp->isChecked())
    {
        syslog("Отправка отчета на сервер не требуется", I);
        return;
    }

    QString url,file_name,tmp;
    QNetworkRequest r;
    FILE *file;

    if(prog_sett.db_type == DB_HTTP){

        qDebug() << "Вызван метод set_test_result";

        url.sprintf("http://%s/api/Api.svc/result.json",prog_sett.db_host.toLocal8Bit().data());
        r.setUrl(QUrl(url));
        QString bound="---------------------------723690991551375881941828858";
        QByteArray data(QString("--"+bound+"\r\n").toUtf8());
        data += "Content-Disposition: form-data; name=\"action\"\r\n\r\n";
        data += "\r\n";
        data += QString("--" + bound + "\r\n").toUtf8();
        data += "Content-Disposition: form-data; name=\"updatefile\"; filename=\"result.json\"\r\n";
        data += "Content-Type: application/octet-stream;\r\n\r\n";
        data += test_report;
        data += "\r\n";
        data += QString("--" + bound + "\r\n").toUtf8();
        data += QString("--" + bound + "\r\n").toUtf8();
        data += "Content-Disposition: form-data; name=\"result\"; filename=\"result.json\"\r\n";
        data += "\r\n";
        r.setRawHeader(QString("Content-Type").toUtf8(),QString("multipart/form-data; boundary=" + bound).toUtf8());
        r.setRawHeader(QString("Content-Length").toUtf8(), QString::number(data.length()).toUtf8());
        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->post(r,data);

        connect(reportreply,SIGNAL(finished()),SLOT(setTestResultFinished()));
        connect(reportreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
    }
    else{
        qDebug() << "Вызван метод set_test_result https";

        url.sprintf("https://%s/api/Api.svc/result.json",prog_sett.db_host.toLocal8Bit().data());
        r.setUrl(QUrl(url));
        QString bound="---------------------------723690991551375881941828858";
        QByteArray data(QString("--"+bound+"\r\n").toUtf8());
        data += "Content-Disposition: form-data; name=\"action\"\r\n\r\n";
        data += "\r\n";
        data += QString("--" + bound + "\r\n").toUtf8();
        data += "Content-Disposition: form-data; name=\"updatefile\"; filename=\"result.json\"\r\n";
        data += "Content-Type: application/octet-stream;\r\n\r\n";
        data += test_report;
        data += "\r\n";
        data += QString("--" + bound + "\r\n").toUtf8();
        data += QString("--" + bound + "\r\n").toUtf8();
        data += "Content-Disposition: form-data; name=\"result\"; filename=\"result.json\"\r\n";
        data += "\r\n";
        r.setRawHeader(QString("Content-Type").toUtf8(),QString("multipart/form-data; boundary=" + bound).toUtf8());
        r.setRawHeader(QString("Content-Length").toUtf8(), QString::number(data.length()).toUtf8());
        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->post(r,data);

        connect(reportreply,SIGNAL(finished()),SLOT(setTestResultFinished()));
        connect(reportreply, SIGNAL(sslErrors(QList<QSslError>)),this,SLOT(reportSslConnectError(QList<QSslError>)));//warning | const QList -> QList
    }

    //пишем в файл
    QDateTime now = QDateTime::currentDateTime();
    file_name.sprintf("reports\\%s-%02d-%02d-%02d.txt",
                      test_config.model_name.toLocal8Bit().data(),now.date().day(),now.date().month(),now.date().year());

    file = fopen (file_name.toLocal8Bit().data(), "a+");
    if(file == nullptr)
        syslog("Не удалось открыть файл для записи результата теста",E);
    else
    {
        tmp.sprintf("\r\nТестирование SN:%d, Время %02d:%02d:%02d\r\n",
                    test_config.serial_num,now.time().hour(),now.time().minute(),
                    now.time().second());
        fprintf(file,tmp.toLocal8Bit().data());
        fprintf(file,test_report.toLocal8Bit().data());
        tmp.sprintf("---\r\n");
        fprintf(file,tmp.toLocal8Bit().data());
        fclose(file);
    }

}

void MainWindow::reportReplyFinished(){

    qDebug() << reportreply;

    if(reportreply->error()==QNetworkReply::NoError){
        myDebug() << "reportReplyFinished";
    }
    else{
        syslog("Ошибка подключения к БД",E);
        myDebug() << "QNetworkReply error: " << reportreply->error();
    }
}

void MainWindow::reportConnectFinished(){
    qDebug() << "reportConnectFinished";
    if(reportreply->error()==QNetworkReply::NoError){
        syslog("Подключено к серверу",I);
        pTestThread->db_connected = 1;
    }else{
        syslog("Нет подключения к серверу",E);
        pTestThread->db_connected = 0;
    }
}

void MainWindow:: reportConnectError(QNetworkReply::NetworkError error){
    qDebug()<< "reportConnectError" <<error;
    pTestThread->db_connected = 0;
}
void MainWindow:: reportSslConnectError(const QList<QSslError> &errors){
    qDebug()<< "reportSslConnectError" << errors;
    syslog("Не подключено к серверу",E);
    pTestThread->db_connected = 0;
}

void MainWindow:: reportError(QNetworkReply::NetworkError error){
    qDebug()<< error;
    QString str;
    syslog(str.sprintf("сетевая ошибка %d",error),E);
}

void MainWindow::getSerialFinished()
{
    bool ok;
    if(reportreply->error() == QNetworkReply::NoError)
    {
        QByteArray arr = reportreply->readAll();
        syslog(arr,I);
        if(arr.length() > 0)
        {
            long serial = arr.toLong(&ok, 10);

            if(ok && serial > 0)
            {
                pTestThread->nettest.get_serial = true;
                pTestThread->nettest.serial_num = serial;
                qDebug() <<  "getSerialFinished" << arr.constData() << pTestThread->nettest.serial_num;
            }
            else
            {
                syslog("некорректный формат серийного номера",E);
            }
        }
        else
        {
            syslog("пустой ответ",E);
        }
    }
    else
    {
        qDebug() << "getSerialFinished" << reportreply->error();
        syslog(reportreply->errorString(),E);
    }
}

/*окончание загрузки*/
void MainWindow::setTestResultFinished(){
    qDebug() << "setTestResultFinished onFinished";
    QByteArray data;

    if(reportreply->error()==QNetworkReply::NoError){

        data = reportreply->readAll();
        qDebug() << data.data();


        if(strncmp(data.data(),"Ok",2)==0){
            pTestThread->nettest.send_report = true;
            qDebug() << "Ok";
        }else{
            qDebug() << "Error";
        }
        pTestThread->nettest.send_report_rezult = true;
    }
    else{
        syslog(reportreply->errorString(),E);
        qDebug() << reportreply->error();
    }
}

/**********************************************************************/
//получение уникального идентификатора для маркировки оборудования
void MainWindow::get_next_ident(void){
    QString request;

    if(prog_sett.db_type == DB_HTTP){
        request.sprintf("http://%s/api/Api.svc/getNextIdent", prog_sett.db_host.toLocal8Bit().data());


        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

        connect(reportreply,SIGNAL(finished()),SLOT(getNextIdentFinished()));
        connect(reportreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
    }
    else{
        request.sprintf("https://%s/api/Api.svc/getNextIdent", prog_sett.db_host.toLocal8Bit().data());
        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);
        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));
        connect(reportreply,SIGNAL(finished()),SLOT(getNextIdentFinished()));
        connect(reportreply, SIGNAL(sslErrors(QList<QSslError>)),this,SLOT(reportSslConnectError(QList<QSslError>)));//warning | const QList -> QList
    }
}

void MainWindow::getNextIdentFinished(){
    bool ok;
    QByteArray array;
    long id;
    syslog("Ожидание ID окончено", I);
    if (reportreply->error() == QNetworkReply::NoError)
    {
        syslog("Ошибок получения ID нет", I);

        array = reportreply->readAll();
        id = array.toLong(&ok,10);

        if(ok && id)
        {
            pLabelThread->id = id;
            pLabelThread->status = 1;

            pTestThread->nettest.id = id;
            pTestThread->nettest.get_id = true;

            qDebug() << "getNextIdentFinished" << id;
        }
        else
            qDebug() << "getNextIdentFinished error";
    }
    else
    {
        syslog(QString("Код ошибки получения ID: %1").arg(reportreply->error()), E);
    }
}

//отправить запрос к серверу на добавление существующего оборудования
void MainWindow::set_serial_num(int serial,int type,int id,QString date){
    QString request,type_str;
    get_dev_name(prog_sett,type,&type_str);

    if(prog_sett.db_type == DB_HTTP){
        request.sprintf("http://%s/api/Api.svc/CreateDeviceFromPSWDataBase"
                        "?serial=%d&identifier=%d&deviceType=%s&date=%s",
                        prog_sett.db_host.toLocal8Bit().data(),
                        serial,id,type_str.toLocal8Bit().data(),date.toLocal8Bit().data());

        if(request.contains('+',Qt::CaseInsensitive)){
            request.replace('+',"%2B",Qt::CaseInsensitive);
        }

        qDebug() << request;
        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

        connect(reportreply,SIGNAL(finished()),SLOT(setSerialFinished()));
    }
    else{
        request.sprintf("https://%s/api/Api.svc/CreateDeviceFromPSWDataBase"
                        "?serial=%d&identifier=%d&deviceType=%s&date=%s",
                        prog_sett.db_host.toLocal8Bit().data(),
                        serial,id,type_str.toLocal8Bit().data(),date.toLocal8Bit().data());

        if(request.contains('+',Qt::CaseInsensitive)){
            request.replace('+',"%2B",Qt::CaseInsensitive);
        }

        qDebug() << request;
        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

        connect(reportreply,SIGNAL(finished()),SLOT(setSerialFinished()));
        connect(reportreply, SIGNAL(sslErrors(QList<QSslError>)),this,SLOT(reportSslConnectError(QList<QSslError>)));//warning | const QList -> QList
    }
}

void MainWindow::setSerialFinished(){
    QByteArray data;
    qDebug()<<"setSerialFinished";
    if(reportreply->error()==QNetworkReply::NoError){
        data = reportreply->readAll();
        if(strstr(data.data(),"Ok")!= NULL){
            pTestThread->nettest.set_serial = true;
            qDebug() << "set Ok";
        }else{
            qDebug() << data.data();
            qDebug() << "set Error";
        }
    }
}

/**********************************************************************/
void MainWindow::check_serial_num(QString cpu_id){
    QString request;
    //QString dev_name;//unused


    pTestThread->nettest.serial_num = 0;
    pTestThread->nettest.get_serial = false;

    if(prog_sett.db_type == DB_HTTP){

        request.sprintf("http://%s/api/api.svc/getExistsSerialNum?cpuId=%s",
                        prog_sett.db_host.toLocal8Bit().data(),cpu_id.toLocal8Bit().data());

        qDebug() << request;

        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

        connect(reportreply,SIGNAL(finished()),SLOT(checkSerialFinished()));
        connect(reportreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
    }
    else if(prog_sett.db_type == DB_HTTPS){

        request.sprintf("https://%s/api/api.svc/getExistsSerialNum?cpuId=%s",
                        prog_sett.db_host.toLocal8Bit().data(),cpu_id.toLocal8Bit().data());

        if(request.contains('+',Qt::CaseInsensitive))
        {
            request.replace('+',"%2B",Qt::CaseInsensitive);
        }
        qDebug() << "getLastSerialNum" << request;
        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

        connect(reportreply,SIGNAL(finished()),SLOT(checkSerialFinished()));
        connect(reportreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
        connect(reportreply, SIGNAL(sslErrors(QList<QSslError>)),this,SLOT(reportSslConnectError(QList<QSslError>)));//warning | const QList -> QList
    }
}

void MainWindow::checkSerialFinished(){
    bool ok;
    QByteArray arr;

    if(reportreply->error()==QNetworkReply::NoError){
        pTestThread->nettest.get_serial = true;
        arr = reportreply->readAll();
        syslog(arr,I);

        pTestThread->nettest.serial_num = arr.toLong(&ok,10);

        qDebug() << "checkSerialFinished" << pTestThread->nettest.serial_num;
        if((ok == false)||(pTestThread->nettest.serial_num == 0)){
            pTestThread->nettest.serial_num = 0;
            pTestThread->nettest.get_serial = false;
        }
    }
}

//получение посленнего серийного номера по типу оборудования
void MainWindow::get_last_serial_pressed(){
    open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]); //Открываем конфиг для устройства
    if(test_config.config_loaded){
        getLastSerialNum(test_config.model_name);
    }
    else
        syslog("Конфигурация не загружена",E);
}

void MainWindow::getLastSerialNum(QString devType){
    QString request;

    if(prog_sett.db_type == DB_HTTP){
        request.sprintf("http://%s/api/Api.svc/getLastSerialNum?devType=",prog_sett.db_host.toLocal8Bit().data());
        request.append(devType);
        if(request.contains('+',Qt::CaseInsensitive))
        {
            request.replace('+',"%2B",Qt::CaseInsensitive);
        }
        qDebug() << "getLastSerialNum" << request;
        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

        connect(reportreply,SIGNAL(finished()),SLOT(getLastSerialFinished()));
        connect(reportreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
    }
    else
    {
        request.sprintf("https://%s/api/Api.svc/getLastSerialNum?devType=",prog_sett.db_host.toLocal8Bit().data());
        request.append(devType);
        if(request.contains('+',Qt::CaseInsensitive))
        {
            request.replace('+',"%2B",Qt::CaseInsensitive);
        }
        qDebug() << "getLastSerialNum" << request;
        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

        connect(reportreply,SIGNAL(finished()),SLOT(getLastSerialFinished()));
        connect(reportreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportError(QNetworkReply::NetworkError)));
        connect(reportreply, SIGNAL(sslErrors(QList<QSslError>)),this,SLOT(reportSslConnectError(QList<QSslError>)));//warning | const QList -> QList

    }
}

void MainWindow::getLastSerialFinished(){
    bool ok;
    QByteArray arr;
    QString str;

    if(reportreply->error()==QNetworkReply::NoError){
        pTestThread->nettest.get_serial = true;
        arr = reportreply->readAll();
        syslog(arr,I);

        pTestThread->nettest.serial_num = arr.toLong(&ok,10);

        qDebug() << "checkSerialFinished" << pTestThread->nettest.serial_num;
        if((ok == false)||(pTestThread->nettest.serial_num == 0)){
            pTestThread->nettest.serial_num = 0;
            pTestThread->nettest.get_serial = false;
            ui->LastSerialResult->setText("Ошибка");
        }else{
            str.sprintf("%u",pTestThread->nettest.serial_num);
            ui->LastSerialResult->setText(str);
        }
    }
}

void MainWindow::connect_database(){
    QString request;
    if(prog_sett.db_host.isEmpty()){
        syslog("Не указан адрес сервера",E);
        return;
    }

    if(prog_sett.db_type == DB_HTTP){
        request.sprintf("http://%s/api/Api.svc/ping", prog_sett.db_host.toLocal8Bit().data());

        qDebug() << request;

        if(reportmanager->networkAccessible()==QNetworkAccessManager::NotAccessible)
            reportmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);

        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));

        connect(reportreply,SIGNAL(finished()),SLOT(reportConnectFinished()));
        connect(reportreply,SIGNAL(error(QNetworkReply::NetworkError)),SLOT(reportConnectError(QNetworkReply::NetworkError)));
    }
    else{

        qDebug() << QString("SSL version use for build: ") << QSslSocket::sslLibraryBuildVersionString();
        qDebug() << QString("SSL version use for run-time: ") << QSslSocket::sslLibraryVersionString();
        qDebug() << QString("SSL support: ") << QSslSocket::supportsSsl();

        QSslSocket *socket = new QSslSocket();
        QObject::connect(socket,&QSslSocket::encrypted,[=](){qDebug() << socket->peerCertificate() << " cert";});
        socket->connectToHostEncrypted(prog_sett.db_host, 443);

        request.sprintf("https://%s/api/Api.svc/ping", prog_sett.db_host.toLocal8Bit().data());
        qDebug() << request;
        reportreply = reportmanager->get(QNetworkRequest(QUrl(request)));
        connect(reportreply,SIGNAL(finished()),SLOT(reportConnectFinished()));
        connect(reportreply, SIGNAL(error(QNetworkReply::NetworkError)),this,SLOT(reportConnectError(QNetworkReply::NetworkError)));
        connect(reportreply, SIGNAL(sslErrors(QList<QSslError>)),this,SLOT(reportSslConnectError(QList<QSslError>)));//warning | const QList -> QList
    }
}

void MainWindow::show_database_form(){
    qDebug() << "show_database_form";
    QUrl url("http://"+prog_sett.db_host);
    QDesktopServices::openUrl(url);
}

//печать отчётов о тестировании в PDF
void MainWindow::print_reports_list(){
    ReportPrinter *d = new ReportPrinter(prog_sett);
    d->show();
}
