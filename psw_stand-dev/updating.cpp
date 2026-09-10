//для обновления устройств по сети
#include "mainwindow.h"
#include "ui_mainwindow.h"
#include <QNetworkReply>
#include <QNetworkRequest>
#include "ui.h"

void MainWindow::onUploadProgress(qint64 tmp1,qint64 tmp2){
    //aka progres bar
    if(tmp2)
        pTestThread->uploading++;
    myDebug() << "onUploadProgress" << tmp1  << tmp2;
}

/*окончание загрузки*/
void MainWindow::onFinished(){
    myDebug() << "onFinished";

    qDebug() << webreply->errorString();
    qDebug() << webreply->readAll();

    if(pTestThread->uploading>1){
        pTestThread->uploading = 0;
        syslog("Загрузка завершена",I);
        //confirm();
    }

    if(pTestThread->downloading == 1){
        myDebug() << "Firmware downloaded";
        pTestThread->downloading = 0;
    }
}

void MainWindow::onNetworkError(QNetworkReply::NetworkError error){
    myDebug() << "onNetworkError" << error;
}

void MainWindow::upload(QString ad){

    QFileInfo finfo(ad);
    QString str;

    if(!finfo.exists()){
        syslog("Файл прошивки не найден",E);
        return;
    }

    str.sprintf("http://%s/mngt/update.shtml",DUT_IP_ADDR);
    QNetworkRequest r(QUrl(str.toLocal8Bit().data()));
    QString bound="---------------------------723690991551375881941828858";
    QByteArray data(QString("--"+bound+"\r\n").toUtf8());
    data += "Content-Disposition: form-data; name=\"action\"\r\n\r\n";
    data += "\r\n";
    data += QString("--" + bound + "\r\n").toUtf8();
    data += "Content-Disposition: form-data; name=\"updatefile\"; filename=\""+finfo.fileName()+"\"\r\n";
    data += "Content-Type: application/octet-stream;\r\n\r\n";
    QFile file(finfo.absoluteFilePath());
    file.open(QIODevice::ReadOnly);
    data += file.readAll();
    data += "\r\n";
    data += QString("--" + bound + "\r\n").toUtf8();
    data += QString("--" + bound + "\r\n").toUtf8();
    data += "Content-Disposition: form-data; name=\"updatefile\"; filename=\""+finfo.fileName()+"\"\r\n";
    data += "\r\n";
    r.setRawHeader(QString("Content-Type").toUtf8(),QString("multipart/form-data; boundary=" + bound).toUtf8());
    r.setRawHeader(QString("Content-Length").toUtf8(), QString::number(data.length()).toUtf8());
    webreply = webmanager->post(r,data);
    connect(webreply,SIGNAL(finished()),this,SLOT(onFinished()));
    connect(webreply, SIGNAL(uploadProgress(qint64,qint64)), this, SLOT(onUploadProgress(qint64,qint64)));
    pTestThread->uploading=1;
    qDebug() << "upload(" << ad << ")";
}

/*когда файл прошивки загружен, необходимо послать подтверждение*/
void MainWindow::confirm(){
    QString str3;
    str3.sprintf("http://%s/mngt/update.shtml?Update=Update",DUT_IP_ADDR);
    QNetworkRequest r(QUrl(str3.toLocal8Bit().data()));
    webreply = webmanager->get(r);
    connect(webreply,SIGNAL(finished()),this,SLOT(onFinished()));
    pTestThread->downloading = 1;
    qDebug() << "confirm updating";
}

/*перед загрузкой файла прошивки, необходимо очистить флешку*/
void MainWindow::update_clear(){
    QString str3;
    str3.sprintf("http://%s/clear.shtml",DUT_IP_ADDR);
    QNetworkRequest r(QUrl(str3.toLocal8Bit().data()));
    webreply = webmanager->get(r);
    connect(webreply,SIGNAL(finished()),this,SLOT(onFinished()));
    qDebug() << "update_clear";
}

void MainWindow::GetTestPage(int timeout)
{
    qDebug() << "GetTestPage";

    if(pTestThread->is_model_type_PRO(test_config.model_name) == false)
    {

        timerForWebManager = new QTimer(this);
        timerForWebManager->setSingleShot(true);
        connect(timerForWebManager, &QTimer::timeout, this, &MainWindow::replyFinished);
        timerForWebManager->start(timeout); //timeout

        QString url = QString("http://%1/test.shtml").arg(DUT_IP_ADDR);
        qDebug() << QString("Debug. Get %1 in slot_GetTestPage()").arg(url);

        QNetworkRequest request;
        request.setUrl(QUrl(url));

        if(webmanager->networkAccessible() == QNetworkAccessManager::NotAccessible)
        {
            delete webmanager;
            webmanager = new QNetworkAccessManager(this);
        }

        QNetworkConfigurationManager configManager;
        webmanager->setConfiguration(configManager.defaultConfiguration());

        webreply = webmanager->get(request);

        connect(webreply, &QNetworkReply::finished , this, &MainWindow::replyFinished);
    }
}

void MainWindow::GetUpsStatus()
{
    QString url = QString("http://%1/api/getUpsStatus").arg(DUT_IP_ADDR);
    qDebug() << QString("Debug. Get %1 in slot_GetUpsStatus()").arg(url);

    QNetworkRequest request;
    request.setUrl(QUrl(url));

    webmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);
    webreply = webmanager->get(request);

    connect(webreply, &QNetworkReply::finished , this, &MainWindow::GetUpsStatusReplyFinished);
}

void MainWindow::GetIrpStatus()
{
    QString url = QString("http://%1/api/isUps").arg(DUT_IP_ADDR);
    qDebug() << QString("Debug. Get %1 in GetIrpStatus()").arg(url);

    QNetworkRequest request;
    request.setUrl(QUrl(url));

    webmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);
    webreply = webmanager->get(request);

    connect(webreply, &QNetworkReply::finished , this, &MainWindow::GetIrpStatusReplyFinished);
}

void MainWindow::GetUpsVoltage()
{
    QString url = QString("http://%1/api/getUpsVoltage").arg(DUT_IP_ADDR);
    qDebug() << QString("Debug. Get %1 in GetUpsVoltage()").arg(url);

    QNetworkRequest request;
    request.setUrl(QUrl(url));

    webmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);
    webreply = webmanager->get(request);

    connect(webreply, &QNetworkReply::finished , this, &MainWindow::GetUpsVoltageReplyFinished);
}

/*запрос на получение test.shtml*/
void MainWindow::get_test_shtml(){
    QString str;

    qDebug() << "get_test_shtml";
    pTestThread->nettest.help_loaded = false;
    pTestThread->nettest.parsed = false;
    webmanager->setNetworkAccessible(QNetworkAccessManager::NotAccessible);
    Sleep(100);
    webmanager->setNetworkAccessible(QNetworkAccessManager::Accessible);
    Sleep(100);
    str.sprintf("http://%s/test.shtml",DUT_IP_ADDR);
    webreply = webmanager->get(QNetworkRequest(QUrl(str)));

    syslog("Запрос test.shtml",I);
}

/*запрос на получение help.html*/
void MainWindow::get_help_html(){
    QString str;
    str.sprintf("http://%s/help/info_help.html",DUT_IP_ADDR);
    webreply = webmanager->get(QNetworkRequest(QUrl(str)));
    connect(webreply,SIGNAL(finished()),this,SLOT(waitHelpFinished()));
    connect(webreply, SIGNAL(uploadProgress(qint64,qint64)), this, SLOT(onUploadProgress(qint64,qint64)));
    connect(webreply,SIGNAL(error(QNetworkReply::NetworkError)),this,SLOT(onNetworkError(QNetworkReply::NetworkError)));
    syslog("Проверка файлов справки...",I);
}
