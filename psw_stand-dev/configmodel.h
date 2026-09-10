#ifndef CONFIGMODEL_H
#define CONFIGMODEL_H

#include "constants.h"
#include <QString>

struct configmodel
{                           
    int config_loaded;                                 //конфиг загружен
    int is_poe_load_configured;                        //провели конфигурацию PoE

    //характеристики самого теста
    QString config_version;                            //версия теста
    int model_num;
    int model_num_mac_print;
    QString model_name;
    QString printable_name;

    int equipment_field_use;                           //флаг использования поля исполнения в этикетках
    int equipment_type;                                //тип исполнения
    QString equipment_str;                             //описание исполнения

    int start_delay;                                   //задержка при включении
    int use_ac1;                                       //подавать питание на AC1
    int use_ac2;                                       //подавать питание на AC2
    int dfu_load;                                      //загружать ли dfu
    QString dfu_path;                                  //путь до файла dfu

    int load_help;
    int firmware_check;
    int firmware_load_first;                           //загружать прошивку в самую первую очередь//не анализировать test.shtml//
    int firmware_load;                                 //загружать ли прошивку
    QString firmware_vers;                                 //версия прошивки
    QString firmware_path;                             //путь до прошивки DFU или Hex
    QString as4_autoprogram_path;                      //путь до файла автопрошивки (для программатора AS4)
    int buildin_test;                                  // 1 - загружаем test.shtml и парсим результат
    int dry_cont_test[3];
    int ac_backup_test;                                // тест отключения правого блока питания

    int poe_test;                                      //тестирование PoE
    int poe_line_test[PORT_NUM];
    int poe_line_testCanOff[PORT_NUM];                  //выключение PoE при проверке ИБП
    int poe_line_passive[PORT_NUM];                     //пассивное PoE на порту
    int poe_line_power[PORT_NUM];
    int poe_line_min[PORT_NUM];
    int poe_line_max[PORT_NUM];
    int poe_line_test_result[PORT_NUM];

    int data_test;                                     //проводить ли тестирование передачей данных
    //int data_test_bercut;                              //флаг признака тестирования через bercut
    QString data_test_matrix[PORT_NUM][2];//парное тестирование портов//ip адреса сетевых карт

    int data_test_chain;//1-флаг тестирования цепочкой, 0 - попарное тестирование портов
    QString switch_config_normal;//конфигурация промежуточного коммутатора в нормальном режиме
    QString switch_config_chain;//конфигурация промежуточного коммутатора в режиме шлейфа

    int data_test_ports[PORT_NUM];
    int ports_speed[PORT_NUM];//скорость порта
    int send_mac;

    int test_ups;//тест UPS

    int charge_CC_test;
    int charge_CC_resistance;
    int charge_CC_current_min;
    int charge_CC_current_max;
    int charge_CC_voltage_min;
    int charge_CC_voltage_max;

    int charge_CV_test;
    int charge_CV_resistance;
    int charge_CV_current_min;
    int charge_CV_current_max;
    int charge_CV_voltage_min;
    int charge_CV_voltage_max;

    int discharge_power_supply_voltage;//напряжение на блоке питания иммитатора АКБ при разрядке

    int test_heating;//тест нагревательных элементов
    int heating_curr_min;//минимальный ток через нагреватели
    int heating_curr_max;

    int test_heating2;//тест нагревательных элементов
    int heating2_curr_min;//минимальный ток через нагреватели
    int heating2_curr_max;

    int test_charging;//тестирование зарядки АКБ
    int chrg_curr_min;//min ток зарядки
    int chrg_curr_max;//max ток зарядки

    int ups_rload;//подключение нагрузочного резистора

    int print_label;
    int label_num;

    QString pre_comment;//комментарий до теста
    QString post_comment;//комментарий после теста

    int serial_num;
    QString id;//идентификатор

    //для отображения подключений
    int port_num;//число портов
    int port_poe[PORT_NUM];//1 - проверка poe
    int port_sfp[PORT_NUM];
    int port2stand[PORT_NUM];// соответствие порту коммутатора порта стенда

    int tlp_inputs[TLP_INPUTS_NUM];
    int tlp_outputs[TLP_OUTPUTS_NUM];
    int tlp_rs485;

    int i2c_test;//тестирование I2C

    int hw_check;
    int hw_vers;

    int last_theme; //установленная тема
};

//конфигурация для стенда RPS-01
struct configmodel_rps
{
    int config_loaded;//конфиг загружен
    int preheating_test;//тестирование preheating
    int rkn_test;//тестировать ли RKN
    int buildin_test;//проводить ли самотестирование
    int charging_test;
    int temper_min;//min/max пороги для проверки датчика температуры
    int temper_max;
    int akb_voltage_ac_min;//напряжение на акб при зарядке
    int akb_voltage_ac_max;
    int akb_charge_voltage_min;//напряжение зарядки
    int akb_charge_voltage_max;
    int load_XX_test;
    int load_XX_current_min;//ток разрядки на ХХ
    int load_XX_current_max;
    int load_16ohm_test;
    int load_16ohm_current_min;//ток разрядки на 16 ом
    int load_16ohm_current_max;
    int load_16ohm_voltage_min;//напряжение разрядки на 16 ом
    int load_16ohm_voltage_max;
    int load_22ohm_test;
    int load_22ohm_current_min;//ток разрядки на 22 ом
    int load_22ohm_current_max;
    int load_22ohm_voltage_min;//напряжение разрядки на 22 ом
    int load_22ohm_voltage_max;
    int load_68ohm_test;
    int load_68ohm_current_min;//ток разрядки на 68 ом
    int load_68ohm_current_max;
    int load_68ohm_voltage_min;//напряжение разрядки на 68 ом
    int load_68ohm_voltage_max;
    int rps_read_delay;//задержка на считывание значений с платы RPS

    //stand New
    int rkn_startup_time_min;//время старта RKN при подаче питания
    int rkn_startup_time_max;
    int rkn_disable_time; //максимальное время на отключение
    int relay1_test;
    int relay2_test;
    int fw_version;//версия ПО на плате RPS

    int rkn_380v_test;//тестировать ли подачей 380В
    int preheating_position;//положение джампера

    int load_100ohm_test;
    int load_100ohm_current_min;
    int load_100ohm_current_max;
    int load_100ohm_voltage_min;
    int load_100ohm_voltage_max;

    int load_70ohm_test;
    int load_70ohm_current_min;
    int load_70ohm_current_max;
    int load_70ohm_voltage_min;
    int load_70ohm_voltage_max;

    int load_50ohm_test;
    int load_50ohm_current_min;
    int load_50ohm_current_max;
    int load_50ohm_voltage_min;
    int load_50ohm_voltage_max;

    int load_30ohm_test;
    int load_30ohm_current_min;
    int load_30ohm_current_max;
    int load_30ohm_voltage_min;
    int load_30ohm_voltage_max;

    int load_20ohm_test;
    int load_20ohm_current_min;
    int load_20ohm_current_max;
    int load_20ohm_voltage_min;
    int load_20ohm_voltage_max;

    int load_15ohm_test;
    int load_15ohm_current_min;
    int load_15ohm_current_max;
    int load_15ohm_voltage_min;
    int load_15ohm_voltage_max;

    int load_15ohm_rps01_current_min;
    int load_15ohm_rps01_current_max;
    int load_15ohm_rps01_voltage_min;
    int load_15ohm_rps01_voltage_max;
};

#endif // CONFIGMODEL_H
