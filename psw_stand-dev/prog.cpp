#include "mainwindow.h"
#include "functions.h"
#include <QDebug>
#include <QThread>
#include <QProcess>
#include <QNetworkInterface>
#include <QObject>
#include "ui_mainwindow.h"
#include "ui.h"
#include "prog.h"

//меню настройки программаторов
void MainWindow::programmers_sett(){
    ProgrammersSetDialog *pProgrammersSetDialog = new ProgrammersSetDialog(prog_sett);
    if(pProgrammersSetDialog->exec() == QDialog::Accepted){
        prog_sett.dfu_prog_path.clear();
        prog_sett.dfu_prog_path.append(pProgrammersSetDialog->get_dfu_path());
        prog_sett.avr_prog_path.clear();
        prog_sett.avr_prog_path.append(pProgrammersSetDialog->get_avr_path());
        prog_sett.avr_prog_type = pProgrammersSetDialog->get_avr_programmer();
        save_last_config();
    }
    delete pProgrammersSetDialog;
}

//обзор папки с прошивками
void MainWindow::programmers_fw_view(){
    QDesktopServices::openUrl(QUrl::fromLocalFile("C:/FortTelecom/Launcher/TFortisStandNew/firmwares"));
}

void MainWindow::programmers_dfu_start(){
    QStringList list;
    //open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]);
    if(test_config.config_loaded == 1){
        list.append(prog_sett.dfu_prog_path);
        list.append(test_config.dfu_path);
        qDebug() << list;
        dfu_command->start("start.bat",list);
    }
    else
        syslog("Ошибка программирования",E);
}

void MainWindow::programmers_avr_start(){
    QStringList list;
    qDebug() << "programmers_avr_start";
    open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]);
    if(test_config.config_loaded == 1){
        list.append(prog_sett.avr_prog_path);
        if(prog_sett.avr_prog_type == AvrProgTypes::ATMELICE){
            list.append("atmelice");
            list.append(test_config.firmware_path);
            dfu_command->start("startavr.bat",list);
        }
        else if(prog_sett.avr_prog_type == AvrProgTypes::AVRMK2){
            list.append("avrispmk2");
            list.append(test_config.firmware_path);
            dfu_command->start("startavr.bat",list);
        }
        else if(prog_sett.avr_prog_type == AvrProgTypes::AS4){
            list.append(test_config.as4_autoprogram_path);
            qDebug() << list;
            dfu_command->start("startavras4.bat",list);
        }
    }
    else
        syslog("Ошибка программирования",E);
}
