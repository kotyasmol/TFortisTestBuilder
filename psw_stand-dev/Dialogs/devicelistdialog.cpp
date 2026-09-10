#include "devicelistdialog.h"
#include "ui_devicelistdialog.h"

#include <QDebug>
#include <QMapIterator>

DeviceListDialog::DeviceListDialog(settingsmodel prog_sett, QWidget *parent) :
    QDialog(parent), ui(new Ui::DeviceListDialog)
{
    ui->setupUi(this);
    this->setWindowTitle("Список оборудования");
    ui->tableWidget->verticalHeader()->setDefaultSectionSize(24); //Устанавиливаем высоту строк в таблице

    //ui->tableWidget->clear();                                   //Очищаем таблицу
    ui->tableWidget->setRowCount(MAX_DEVICES);                    //Добавляем строки

    FillTheTable(prog_sett);                                      //Заполняем таблицу в окне
}

void DeviceListDialog::FillTheTable(settingsmodel prog_sett)
{
    for (int i = 0; i < MAX_DEVICES; i++)
    {
        auto deviceId = prog_sett.device_id[i];
        auto deviceName = prog_sett.device_name[i];

        ui->tableWidget->setItem(i, 0, new QTableWidgetItem(QString::number(deviceId)));
        ui->tableWidget->setItem(i, 1, new QTableWidgetItem(deviceName));
    }
}

void DeviceListDialog::SaveDeviceListSlot()
{
    qDebug() << "Вызван SaveDeviceList";
}

DeviceListDialog::~DeviceListDialog()
{
    delete ui;
}

