#ifndef DEVICEBASE_H
#define DEVICEBASE_H

#include <QMainWindow>
#include <QTableWidget>
#include <QSqlDatabase>
#include <QHBoxLayout>
#include "mainwindow.h"

namespace Ui {
    class DeviceBase;
}

class DeviceBase : public QMainWindow
{
    Q_OBJECT


public:
    explicit DeviceBase(struct sett prog_sett,QWidget *parent = 0);
    //void get_dev_name(int i,QString *tmp);
    ~DeviceBase();

public slots:
    void save_edit();
    void search();
    void show_table();
    void show_table50();
    void onCellClicked(int row,int coll);
    void delete_item();
    void print();
    void adddevice(void);

private:
    Ui::DeviceBase *ui;
    QTableWidget *tableWidget;
    QSqlDatabase *dev_db;
    QHBoxLayout *layout;
    int current_row;
    struct device_info_t dev_info;
    settingsmodel progSett;


    void get_info( struct device_info_t *info,int row);
    void show_capture();
    int print_label(struct device_info_t *device_info,int num);

};




#endif // DEVICEBASE_H
