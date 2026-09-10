#ifndef DFUMULTILOADTHREAD_H
#define DFUMULTILOADTHREAD_H


#include <QThread>
#include <QObject>
#include <QProcess>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QNetworkAccessManager>
#include <QUdpSocket>
#include <QSqlDatabase>
#include "DataTest/DataTestThread.h"
#include "DataTest/BercutThread.h"
#include <QTimer>
#include "configmodel.h"
#include "modbus_dev.h"
#include "settingsmodel.h"
#include "telnet.h"
#include "constants.h"
#include "debugwindow.h"

class dfuMultiLoadThread : public QThread
{
    Q_OBJECT

public:
    dfuMultiLoadThread();
    virtual ~dfuMultiLoadThread();

    QProcess *dfu_multi_load_processes[5];

    void runMultiLoad(QProcess *processes[], QString devs[], QString dfuPath);
};

#endif // DFUMULTILOADTHREAD_H
