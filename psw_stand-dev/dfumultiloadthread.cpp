#include <stdlib.h>
#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "functions.h"

#include <QProcess>
#include <qdebug.h>
#include <QPrinter>
#include <QPrintDialog>
#include <QPrinterInfo>

dfuMultiLoadThread::dfuMultiLoadThread()
{


}

dfuMultiLoadThread::~dfuMultiLoadThread()
{


}

void dfuMultiLoadThread::runMultiLoad(QProcess *processes[], QString devNums[], QString dfuPath)
{
    QByteArray output[5];
    QString line[5];
    QString text[5];
    QStringList splittedText[5];
    QRegExp rx("\n");
    QString error;

    //C:/Repo/psw_stand/dfu-util/win64/dfu-util.exe -a 0 -d 314B:0106 -n 16 -s 0x08000000:leave -t 4096 -D C:/FortTelecom/Launcher/TFortisStand/firmwares/sw407/sw407_0.2.9_15.07.2022_boot1.6.bin

//    for(int i = 0; i < 5; i++)
//    {
//        if(devNums[i] != "")
//        {
//            processes[i] = new QProcess(this);
//            processes[i]->setProcessChannelMode(QProcess::MergedChannels);

//            //multi_dfu_load_command[i]->start("C:/Repo/psw_stand/dfu-util/win64/dfu-util.exe", QStringList() << "-a" << "0" << "-d" << "314B:0101" << "-n" << devNums[i] << "-s" << "0x08000000:leave" << "-t" << "4096" << "-D" << dfuPath);
//            processes[i]->start(QString("C:/Repo/psw_stand/dfu-util/win64/dfu-util.exe -a 0 -n %1 -s 0x08000000:leave -D \"C:/FortTelecom/Launcher/TFortisStandNew/firmwares/sw407/sw407_0.2.9_31.08.2022_boot1.6.bin\"").arg(devNums[i]));

//            if(!processes[0] -> waitForStarted())
//            {
//                qDebug() << "proc didnt start";
//                return;
//            }
//        }
//    }

    for(int i = 0; i < 5; i++)
    {
        if(devNums[i] != "")
        {
            processes[i]->waitForFinished();
        }
    }

    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";
    qDebug() << "/////////////////////////////////";

}
