#ifndef SETTINGSMODEL_H
#define SETTINGSMODEL_H

#include "constants.h"
#include <QString>

struct settingsmodel
{
    int use_session_id;
    QString session_id;

    int stand_type;//тип стенда

    QString com_port_name; // имя сом порта // COM1

    int com_autoconnect;//автоматическое открытие порта

    QString bercut_com_port_name; // имя сом порта для беркут для портов 10/100
    int bercut_state;
    int bercut_test_type;

    QString bercut100_com_port_name; // имя сом порта для беркут для портов 1G
    int bercut100_state;
    int bercut100_test_type;

    QString teleport_com_port_name; // имя сом порта для teleport
    int teleport_state;

    QString rps_stand_com_port_name; // имя сом порта для teleport
    int rps_stand_state;

    //настройки соединения с технологическим коммутатором
    int switch_state;
    QString telnet_host;
    QString telnet_login;
    QString telnet_pass;
    int port_a;
    int port_b;
    int port_a100;
    int port_b100;
    int port_dut;
    int port_sfp1;
    int port_sfp2;
    int port_man;

    //настройки DB
    int db_type;//тип подключения: http/https
    QString db_host;//ip адрес БД

    //настройки сетевых карт
    int card_index[PORT_NUM];
    QString  card_ip[PORT_NUM];

    //настройки списка пользователей
    QString username[USERS_NUM];
    QString password[USERS_NUM];

    //настройки принтера этикеток
    QString printer_name;
    int label_size;
    //настройки принтера отчетов
    QString report_printer_name;

    int test_type;//тип тестирования 0 - выпуск, 1- ремонт
    int product_test_type;//тип тестирования для выпуска 0 - первичное тестирование
    //                                                   1 - повторное тестирование

    int connected;//подключение к плате
    int bercut_connected;//подключение к беркуту
    int switch_connected;//подключение к тестовому коммутатору
    int teleport_connected;

    //загруженные профили тестирования
    int last_config_num;//номер профиля, загруженного в последнй раз
    QString config_name[MAX_DEVICES];//имя профиля
    QString config_dir[MAX_DEVICES];//путь до профиля тестирования при выпуске

    //поправочные коэффициенты программы
    float poe_coeff[NUM_POE_LINES];//poe voltage meas
    float heat_curr_coeff;//ups heating
    float charge_curr_coeff;//battery charging current

    //ID стенда
    QString stand_id;

    //Список устройств
    int device_id[MAX_DEVICES];//тип устройства
    QString device_name[MAX_DEVICES];//имя устройства

    int test_mode_state;

    QString dfu_prog_path;//Путь до программатора DFU
    QString avr_prog_path;//Путь до программатора Avr
    int avr_prog_type;//тип программатора

    int power_supply_state;//использовать внешний управляемый БП
    int power_supply_model;//модель БП
    QString power_supply_com_port_name; // имя сом порта для БП

    bool last_theme; //false - светлая, true - темная
};

#endif // SETTINGSMODEL_H
