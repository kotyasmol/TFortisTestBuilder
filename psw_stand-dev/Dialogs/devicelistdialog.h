#ifndef DEVICELISTDIALOG_H
#define DEVICELISTDIALOG_H

#include <QDialog>
#include <QMap>
#include "TestThread.h" // Нужен тут для типа sett. struct sett prog_sett,

namespace Ui {
class DeviceListDialog;
}

class DeviceListDialog : public QDialog
{
    Q_OBJECT

public:
    explicit DeviceListDialog(settingsmodel prog_sett, QWidget *parent = nullptr);
    ~DeviceListDialog();

public slots:
    //! Сохраненяет список устройств в файл
    void SaveDeviceListSlot();

private:
    Ui::DeviceListDialog *ui;
    QMap<int, QString> deviceList;

    //! Заполняет устройствами таблицу в окне
    void FillTheTable(settingsmodel prog_sett);
};

#endif // DEVICELISTDIALOG_H
