#include <stdlib.h>
#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "functions.h"
#include "stdio.h"
#include "math.h"
#include <QtXml/QtXml>
#include <QtXml/QDomElement>
#include <QFileDialog>
#include <QMessageBox>
#include <QFile>
#include <QProcess>
#include <QDebug>
#include <QPrinter>
#include <QPrintDialog>
#include <QPrinterInfo>
#include <winnt.h>
#include <winspool.h>
#include <windef.h>

//печать этикеток

int RawDataToPrinter(QString szPrinterName, QByteArray ba){
    BOOL     bStatus = FALSE;
    DOC_INFO_1 DocInfo;
    DWORD      dwJob = 0L;
    DWORD      dwBytesWritten = 0L;
    HANDLE     hPrinter;
    wchar_t  *name = new wchar_t[szPrinterName.length()+1];

    szPrinterName.toWCharArray(name);
    name[szPrinterName.length()/*+1*/] = 0;

    myDebug() << "opening printer" << szPrinterName;
    myDebug() << name;

    bStatus = OpenPrinter(name,&hPrinter, NULL);

    if (bStatus) {
        qDebug() << "Printer opened";

        //Так работает в VS
        WCHAR docName[] = L"My Document";
        WCHAR dataType[] = L"RAW";
        DocInfo.pDocName = docName;
        DocInfo.pOutputFile = NULL;
        DocInfo.pDatatype = dataType;

        //Так работает в QTCreator
        /*DocInfo.pDocName = L"My Document";
        DocInfo.pOutputFile = NULL;
        DocInfo.pDatatype = L"RAW";*/

        dwJob = StartDocPrinter( hPrinter, 1, (LPBYTE)&DocInfo );
        if (dwJob > 0) {
            myDebug() << "Job is set.";
            bStatus = StartPagePrinter(hPrinter);
            if (bStatus) {
                myDebug() << "Writing text to printer";
                bStatus = WritePrinter(hPrinter,ba.data(),ba.length(),&dwBytesWritten);
                EndPagePrinter(hPrinter);
            } else {
                EndDocPrinter( hPrinter );
                ClosePrinter( hPrinter );
                myDebug() << "could not start printer";
                return 1;
            }
            EndDocPrinter(hPrinter);
            myDebug() << "closing doc";
        } else {
            ClosePrinter( hPrinter );
            myDebug() << "Couldn't create job";
            return 2;
        }
        ClosePrinter(hPrinter);
        myDebug() << "closing printer";
    } else {
        myDebug() << "Could not open printer" << bStatus;
        return 3;
    }
    if (dwBytesWritten != ba.length()) {
        myDebug() << "Wrong number of bytes" ;
        return 4;
    } else {
        myDebug() << "bytes written is correct " << QString::number(ba.length()) ;
    }
    ClosePrinter( hPrinter );
    return 0;
}

void MainWindow::print_label_retry(void){
    struct label_info_t label;
    label.retry = true;
    label.num = 1;
    print_label(&label);
}

void MainWindow::print_label(struct label_info_t *label_info){
    QString tmp;
    QString str;
    int tabsize;
    int error;
    static struct label_info_t label_info_temp;

    //qDebug() << label_info->dev_name;

    if(label_info != NULL && label_info->retry==0){
        label_info_temp.dev_name = label_info->dev_name;
        label_info_temp.dev_type = label_info->dev_type;
        for(int i=0;i<6;i++){label_info_temp.mac[i]=label_info->mac[i];}
        label_info_temp.num = label_info->num;
        label_info_temp.serial_num = label_info->serial_num;
        label_info_temp.equipment_field_use = label_info->equipment_field_use;
        label_info_temp.equipment_type = label_info->equipment_type;
        label_info_temp.equipment_str = label_info->equipment_str;
    }
    else{
        label_info_temp.num = label_info->num;
        qDebug() << "print_label retry";
        //syslog("print_label retry",I);
    }

    //tmp.sprintf("print_label %d",label_info_temp.equipment_field_use);
    //syslog(tmp,I);


    //qDebug() << "print_label" << label_info_temp.dev_type <<label_info_temp.serial_num;

    if(prog_sett.printer_name.isEmpty()){
        if(label_info != NULL && label_info->retry==0)
            label_info->print_status = false;
        return;
    }

    //label 13*25 mm
    str.clear();
    str.append("^XA^MD10^FO");
    //определяем размер отступа
    tabsize = 640-label_info_temp.dev_name.length()*9*0.9;
    tmp.sprintf("%d,35^A0,36,25^FD",tabsize);
    str.append(tmp);
    str.append(label_info_temp.dev_name);
    str.append("^FS");
    if(test_config.send_mac == 1){
        str.append("^FO510,70^A0,25,20^FDMAC: ");
        tmp.sprintf("%02X:%02X:%02X:%02X:%02X:%02X",label_info_temp.mac[0],label_info_temp.mac[1],
                label_info_temp.mac[2],label_info_temp.mac[3],label_info_temp.mac[4],label_info_temp.mac[5]);
        str.append(tmp);
        str.append("^FS");
    }

    str.append("^FO510,95^A0,25,20^FDSN: ");
    tmp.sprintf("%05d",label_info_temp.serial_num);
    str.append(tmp);
    if(label_info_temp.equipment_field_use && label_info_temp.equipment_str.length() ){
        str.append(" type: ");
        str.append(label_info_temp.equipment_str);
    }
    str.append("^FS^FO510,117^BY2^BCN,50,N,N,N^FD>:");
    if(label_info_temp.equipment_field_use){
        tmp.sprintf("%02d%03d%05d",label_info_temp.equipment_type,label_info_temp.dev_type,label_info_temp.serial_num);
    }
    else{
        tmp.sprintf("%03d%05d",label_info_temp.dev_type,label_info_temp.serial_num);
    }
    str.append(tmp);
    str.append("^FS^XZ ");

    tmp.clear();
    for(int i=0;i<label_info_temp.num;i++){
        tmp.append(str);
    }

    qDebug()<< tmp;
    syslog(tmp,I);

    error = RawDataToPrinter(prog_sett.printer_name,tmp.toLocal8Bit());
    if(error){
        if(label_info != NULL && label_info->retry==0)
            label_info->print_status = false;

        syslog("Ошибка принтера",I);
        switch(error){
        case 1: syslog("could not start printer",E);break;
        case 2: syslog("Couldn't create job",E);break;
        case 3: syslog("Could not open printer",E);break;
        case 4: syslog("Wrong number of bytes",E);break;

        }
        return;
    }

    syslog("Печать этикетки: OK",I);
    if(label_info != NULL && label_info->retry==0)
        label_info->print_status = true;


}

/*запуск печати этикеток*/
void MainWindow::id_label_print(void){
    pLabelThread->label_num = ui->id_label_num->value();
    pLabelThread->start();
}

void MainWindow::print_id_label(int id){
    QString str,tmp;

    if(id <= 99999999){
        str.clear();
        str.append("^XA");
        str.append("^FS^FO515,40^BY2^BCN,85,N,N,N^FD");
        tmp.sprintf("%08d",id);
        str.append(tmp);
        str.append("^FS^FO570,135^A0,36,30^FD");
        str.append(tmp);
        str.append("^FS^XZ");
        qDebug() << str;

        if(RawDataToPrinter(prog_sett.printer_name,str.toLocal8Bit())){
            pLabelThread->print_status = false;
            return;
        }
        pLabelThread->print_status = true;
    }
    else{
        pLabelThread->print_status = false;
    }
}

void MainWindow::show_label_print_rezult(QString str){
    ui->id_label_result->setText(str);
    qDebug() << "show_label_print_rezult" << str;
}

void MainWindow::marker_label_print(void){
    QString tmp;
    QString tmp2;
    QString str;

    str.clear();
    str.append("^XA");
    str.append("^FS^FO515,50^A0,");
    tmp2.sprintf("%d,%d",int(1.2*ui->marker_label_size->value()),ui->marker_label_size->value());
    str.append(tmp2);
    str.append("^FD");

    tmp = ui->marker_label_text->text();

    if(tmp.length() < 20){
        str.append(tmp);
        str.append("^FS^XZ");
        if(RawDataToPrinter(prog_sett.printer_name,str.toLocal8Bit())){
            ui->marker_label_result->setText("Printer.Error");
            return;
        }
        ui->marker_label_result->setText("Ok");
    }else{
        ui->marker_label_result->setText("Длинная строка");
    }
}

void MainWindow::PrintLabel()
{
    ui->printLabel_btn->setEnabled(false);

    ui->printResult_lbl->clear();
    int countLabel = ui->countLabel->value();
    syslog(QString("Печатаем %1 этикеток").arg(countLabel), I);

    open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]); //Открываем конфиг для устройства
    test_report.clear();//очищаем отчет
    prog_sett.test_type=TYPE_PRODUCTION;
    prog_sett.product_test_type = TYPE_FIRST;

    pTestThread->setStatusSendReport(true); // Передаем статус, что отправка отчета требуется.
    pTestThread->set_prog_sett(prog_sett);//передаём настройки программы
    pTestThread->set_test_config(test_config);//передаём профиль тестирования

    for (int i = 0; i < countLabel; i++)
    {
        pTestThread->start_test();

        ui->printResult_lbl->setText(QString("Осталось %1").arg(countLabel-i));
        // Пауза пока тест идет
        while (pTestThread->TestIsRunning())
        {
            QThread::msleep(5);
            qApp->processEvents();
        }
    }
    ui->printLabel_btn->setEnabled(true);
    ui->printResult_lbl->setText("Печать окончена");
}

//выбор файла
void MainWindow::PrintLabelFromFilePath(){
    ui->serialPrintFromFilePath->setText(QFileDialog::getOpenFileName(this,tr("Open txt"), "", tr("Text Files (*.txt)")));
}
//печать списка этикеток из файла (после сканирования )
void MainWindow::PrintLabelFromFile(){
    QFile file(ui->serialPrintFromFilePath->text());
    int sernum;
    bool ok;
    if(file.open(QIODevice::ReadOnly |QIODevice::Text)){
        while(!file.atEnd())
        {
            //читаем строку
            QString str = file.readLine();
            //Делим строку на слова разделенные пробелом
            QStringList lst = str.split(" ");
            sernum = lst.at(0).toLong(&ok,10);
            if(ok){
                label_info.mac[0] = device_info.mac[0] = 0xC0;
                label_info.mac[1] = device_info.mac[1] = 0x11;
                label_info.mac[2] = device_info.mac[2] = 0xA6;

                label_info.mac[3] = device_info.mac[3] = (quint8)test_config.model_num;
                label_info.mac[4] = device_info.mac[4] = (quint8)(sernum >> 8);
                label_info.mac[5] = device_info.mac[5] = (quint8)(sernum);

                label_info.dev_type = test_config.model_num;
                label_info.serial_num = sernum;

                if(test_config.printable_name.isEmpty())
                    label_info.dev_name = test_config.model_name;
                else
                    label_info.dev_name = test_config.printable_name;
                label_info.num = ui->serialPrintFromFileCount->value();
                print_label(&label_info);
            }
        }
    }else{
        syslog("Не удалось открыть файл",E);
    }

}
