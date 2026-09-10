#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "stdio.h"
#include "math.h"
#include <QtXml/QtXml>
#include <QtXml/QDomElement>
#include <QFileDialog>
#include <QMessageBox>
#include <QFile>
#include "ui.h"
#include <QByteArray>
#include <QPrintDialog>
#include <QPrinter>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <iterator>
#include "Dialogs/connect_diagram.h"
#include <Dialogs/devicelistdialog.h>
#include <QApplication>
#include <QStyleFactory>

//хранение настроек

static settingsmodel parse_json(QJsonObject jsonObject);
static QJsonObject save_ps_json(settingsmodel *configuration);
extern int is_poe_load_configured;

void MainWindow::refresh_device_list_ui(){
    ui->device_list->clear();
    for(int i = 0; i < MAX_DEVICES; i++){
        if(!prog_sett.config_dir[i].isEmpty()){
            //заполнение типа устройства (из конфига)
            open_config_name(&test_config,prog_sett.config_dir[i]);
            prog_sett.device_id[i] = test_config.model_num;
            prog_sett.config_name[i] = test_config.printable_name;

            ui->device_list->addItem(current_file_name);
            qDebug() <<test_config.config_loaded<< test_config.model_name<<test_config.model_num;
            qDebug() << prog_sett.device_name[i] << prog_sett.device_id[i];
        }

    }
}

//смотрим временный файл, и открываем прошлую конфигурацию теста
void MainWindow::open_last_config(){
    QString fileName;

    fileName.append("tmp_sett.bak");
    QFile file(fileName);

    qDebug() << "open_last_config";

    if(file.open(QIODevice::ReadOnly)) {
        QJsonParseError  parseError;
        QJsonDocument jsonDoc = QJsonDocument::fromJson(file.readAll(), &parseError);
        if(parseError.error == QJsonParseError::NoError)
        {
            if(jsonDoc.isObject())
            {
                prog_sett = parse_json(jsonDoc.object());
                syslog("Настройки загружены", I);

                //add preloadeg configs
                ui->device_list->clear();  //очищаем список устройств
                refresh_device_list_ui();

                //connect to database
                ui->id_rp->setDisabled(false);
                ui->serial->setDisabled(false);
            }
        }else
        {
            qDebug() << parseError.errorString();
            qDebug() << parseError.offset;
            syslog("Настройки не загружены",I);
        }
        file.close();
    }
    else
    {
        syslog("Настройки не загружены",I);
    }
}

//сохраняем конфигурационный файл
void MainWindow::save_last_config(){
    QString fileName;
    QJsonDocument doc;
    QJsonObject settings;

    qDebug() << "save_last_config";

    fileName.append("tmp_sett.bak");

    QFile file(fileName);
    file.open(QIODevice::WriteOnly);

    settings = save_ps_json(&prog_sett);

    doc.setObject(settings);

    file.write(doc.toJson());

    file.close();
}

//загружаем конфигурацию
int MainWindow::open_config_name(struct configmodel *test_config,QString name){
    char str[256];
    struct configmodel config_tmp;
    if(name.isEmpty()){
        syslog("Пустой путь до файла конфигурации",E);
        return -1;
    }
    QFile file(name);
    QFileInfo fileInfo(file.fileName());
    current_file_name = fileInfo.fileName();
    current_file_name.chop(5); //удаление .json

    if(file.open(QIODevice::ReadOnly)) {
        QJsonParseError  parseError;
        QJsonDocument jsonDoc = QJsonDocument::fromJson(file.readAll(), &parseError);
        if(parseError.error == QJsonParseError::NoError)
        {
            if(jsonDoc.isObject())
            {

                config_tmp = parse_config_json(jsonDoc.object());
                memcpy(test_config,&config_tmp,sizeof(config_tmp));
                test_config->config_loaded = 1;
                is_poe_load_configured = 0;
                syslog("Конфигурация загружена",I);
                //pTestThread->initTestStruct();
                sprintf(str,"%s  --  %s",
                        WINDOW_TITLE,name.toLocal8Bit().data());
                this->setWindowTitle(str);
            }
        }
        else{
            syslog("Некорректная конфигурация",E);
            sprintf(str,"%s : %d",parseError.errorString().toLocal8Bit().data(),parseError.offset);
            syslog(str,E);
        }
        file.close();
        return 0;
    }
    else
        return -1;
}

//загружаем конфигурацию для стенда RPS
int MainWindow::open_config_rps(QString name){
    char str[256];
    myDebug() << "open_config_rps";
    if(name.isEmpty())
        return -1;

    QFile file(name);

    if(file.open(QIODevice::ReadOnly)) {
        QJsonParseError  parseError;
        QJsonDocument jsonDoc = QJsonDocument::fromJson(file.readAll(), &parseError);
        if(parseError.error == QJsonParseError::NoError)
        {
            if(jsonDoc.isObject())
            {

                test_config_rps = parse_config_rps_json(jsonDoc.object());
                test_config_rps.config_loaded = 1;
                syslog("Конфигурация загружена",I);
                sprintf(str,"%s  --  %s",
                        WINDOW_TITLE,name.toLocal8Bit().data());
                this->setWindowTitle(str);
                if(test_config.test_ups)
                {
                    if(test_config.charge_CV_voltage_min < SIMBAT_GET_TYPE_VOLTAGE)
                    {
                        pTestThread->dev->set_simbat_type(SimbatType::SIMBAT24);
                        syslog("Используется SIMBAT24",I);

                    }
                    else
                    {
                        pTestThread->dev->set_simbat_type(SimbatType::SIMBAT48);
                        syslog("Используется SIMBAT48",I);
                    }
                }else
                {
                    pTestThread->dev->set_simbat_type(SimbatType::NotConnected);
                }
                file.close();
                return 0;
            }
        }
        else{
            qDebug() << parseError.errorString();
            qDebug() << parseError.offset;
            syslog("Некорректная конфигурация",E);
            file.close();
            return -1;
        }

        return 0;
    }
    else
        return -1;
}

QString MainWindow::get_rps_config_path(){
    for(int i=0;i<MAX_DEVICES;i++){
        if(prog_sett.config_name[i].contains("RPS-01",Qt::CaseInsensitive)==0){
            return prog_sett.config_dir[i];
        }else return 0;
    }
    syslog("Не найдена конфигурация для проверки RPS-01",E);
    return 0;
}

//настройки программы. парсинг файла
static settingsmodel parse_json(QJsonObject jsonObject){
    static settingsmodel ret_config;
    //QJsonArray array;
    QJsonObject json;
    int i;

    qDebug()<<"parse_json";

    //номер сом порта
    ret_config.com_port_name.clear();
    ret_config.com_port_name.append(jsonObject["com_port_num"].toString());

    //автоматическое подключение
    ret_config.com_autoconnect = jsonObject["com_autoconnect"].toInt();

    //тип стенда
    ret_config.stand_type = jsonObject["stand_type"].toInt();

    //номер сом порта генератора трафика 100М
    ret_config.bercut100_com_port_name.clear();
    ret_config.bercut100_com_port_name.append(jsonObject["bercut100_com_port_name"].toString());
    ret_config.bercut100_state = jsonObject["bercut100_state"].toInt();
    ret_config.bercut100_test_type = jsonObject["bercut100_test_type"].toInt();

    //номер сом порта генератора трафика 1000М
    ret_config.bercut_com_port_name.clear();
    ret_config.bercut_com_port_name.append(jsonObject["bercut_com_port_name"].toString());
    ret_config.bercut_state = jsonObject["bercut_state"].toInt();
    ret_config.bercut_test_type = jsonObject["bercut_test_type"].toInt();

    //промежуточный коммутатор
    ret_config.switch_state = jsonObject["telnet_state"].toInt();
    ret_config.telnet_host.clear();
    ret_config.telnet_host.append(jsonObject["telnet_host"].toString());
    ret_config.telnet_login.clear();
    ret_config.telnet_login.append(jsonObject["telnet_login"].toString());
    ret_config.telnet_pass.clear();
    ret_config.telnet_pass.append(jsonObject["telnet_pass"].toString());

    //порты в промежуточном коммутаторе для беркута 1000
    ret_config.port_a = jsonObject["telnet_port_a"].toInt();
    ret_config.port_b = jsonObject["telnet_port_b"].toInt();

    //порты в промежуточном коммутаторе для беркута 10/100
    ret_config.port_a100 = jsonObject["telnet_port_a100"].toInt();
    ret_config.port_b100 = jsonObject["telnet_port_b100"].toInt();

    //порт для доступа к тестируемому коммутатору
    ret_config.port_dut = jsonObject["telnet_port_dut"].toInt();

    //порт для удалённого управления промежуточным коммутатором
    ret_config.port_man = jsonObject["telnet_port_man"].toInt();

    //порт для SFP
    ret_config.port_sfp1 = jsonObject["telnet_port_sfp1"].toInt();
    ret_config.port_sfp2 = jsonObject["telnet_port_sfp2"].toInt();

    //номер сом порта teleport
    ret_config.teleport_com_port_name.clear();
    ret_config.teleport_com_port_name.append(jsonObject["teleport_com_port_name"].toString());
    ret_config.teleport_state = jsonObject["teleport_state"].toInt();

    //настройки стенд RPS-1
    ret_config.rps_stand_com_port_name.clear();
    ret_config.rps_stand_com_port_name.append(jsonObject["rps_stand_com_port_name"].toString());
    ret_config.rps_stand_state = jsonObject["rps_stand_state"].toInt();

    //ID стенда
    ret_config.stand_id.clear();
    ret_config.stand_id.append(jsonObject["stand_id"].toString());

    //db type
    ret_config.db_type = jsonObject["db_type"].toInt();
    //db_host
    ret_config.db_host.clear();
    ret_config.db_host.append(jsonObject["db_host"].toString());

    //настройка сетевых карт
    json = jsonObject["card_ip"].toObject();
    i=0;
    for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<DATA_TEST_LINES;  ++iter,i++)
    {
        ret_config.card_ip[i] = (*iter).toString();
    }

    //usernames
    json = jsonObject["username"].toObject();
    i=0;
    for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<USERS_NUM;  ++iter,i++)
    {
        ret_config.username[i] = (*iter).toString();
    }

    //password
    json = jsonObject["password"].toObject();
    i=0;
    for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<USERS_NUM;  ++iter,i++)
    {
        ret_config.password[i] = (*iter).toString();
    }

    //printername
    ret_config.printer_name.clear();
    ret_config.printer_name.append(jsonObject["printer_name"].toString());

    //report_printer_name
    ret_config.report_printer_name.clear();
    ret_config.report_printer_name.append(jsonObject["report_printer_name"].toString());

    //label size
    ret_config.label_size = jsonObject["label_size"].toInt();

    //last dev
    ret_config.last_config_num = jsonObject["last_config_num"].toInt();

    //имя конфигурации
    json = jsonObject["config_name"].toObject();
    i=0;
    for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<MAX_DEVICES;  ++iter,i++)
    {
        ret_config.config_name[i].clear();
        ret_config.config_name[i].append((*iter).toString());
    }

    //путь до файла
    json = jsonObject["config_dir"].toObject();
    i=0;
    for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<MAX_DEVICES;  ++iter,i++)
    {
        ret_config.config_dir[i].clear();
        ret_config.config_dir[i].append((*iter).toString());
    }

    //список оборудования
    json = jsonObject["device_id"].toObject();
    i=0;
    for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<MAX_DEVICES;  ++iter,i++)
    {
        ret_config.device_id[iter.key().toInt()] = (*iter).toInt();
    }
    json = jsonObject["device_name"].toObject();
    i=0;
    for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<MAX_DEVICES;  ++iter,i++)
    {
        ret_config.device_name[iter.key().toInt()].clear();
        ret_config.device_name[iter.key().toInt()].append((*iter).toString());
    }

    //путь до файла DFU prog
    ret_config.dfu_prog_path.clear();
    ret_config.dfu_prog_path.append(jsonObject["dfu_prog_path"].toString());

    //путь до файла AVR prog
    ret_config.avr_prog_path.clear();
    ret_config.avr_prog_path.append(jsonObject["avr_prog_path"].toString());

    ret_config.avr_prog_type = jsonObject["avr_prog_type"].toInt();

    ret_config.power_supply_state = jsonObject["power_supply_state"].toInt();
    ret_config.power_supply_model = jsonObject["power_supply_model"].toInt();
    ret_config.power_supply_com_port_name.clear();
    ret_config.power_supply_com_port_name.append(jsonObject["power_supply_com_port_name"].toString());
    ret_config.last_theme = jsonObject["theme"].toBool();
    return ret_config;
}
configmodel MainWindow::parse_config_json(QJsonObject jsonObject){
    static configmodel ret_config;
    QJsonObject json;
    int i;

    for (int i = 0; i < PORT_NUM; i++)
    {
        ret_config.poe_line_test[i] = 0;
    }

    ret_config.equipment_field_use = 0;
    ret_config.equipment_str.clear();
    ret_config.equipment_type = 0;

    //номер модели для самотестирования
    if(jsonObject["model_num"].isDouble()){
        ret_config.model_num = jsonObject["model_num"].toInt();
    }
    else
    {
        ret_config.model_num = 0;
        syslog(QString("Переменная не найдена: model_num"), D);
    }

    //номер модели для установки MAC адреса и печати этикетки
    if(jsonObject["model_num_mac_print"].isDouble()){
        ret_config.model_num_mac_print = jsonObject["model_num_mac_print"].toInt();
    }
    else
    {
        ret_config.model_num_mac_print = 0;
        syslog(QString("Переменная не найдена: model_num_mac_print"), D);
    }

    //символьное название модели
    if(jsonObject["model_name"].isString()){
        ret_config.model_name = "";
        ret_config.model_name.append(jsonObject["model_name"].toString());
    }
    else{
        syslog(QString("Переменная не найдена: model_name"),D);
        ret_config.model_name = "";
    }

    //название модели для печати этикетки
    if(jsonObject["printable_name"].isString()){
        ret_config.printable_name = "";
        ret_config.printable_name.append(jsonObject["printable_name"].toString());
    }
    else
    {
        ret_config.printable_name = "";
        syslog(QString("Переменная не найдена: printable_name"), D);
    }

    //флаг использования дополнительного поля Исполнение
    if(jsonObject["equipment_field_use"].isDouble()){
        ret_config.equipment_field_use = jsonObject["equipment_field_use"].toInt();
    }
    else
    {
        ret_config.equipment_field_use = 0;
        syslog(QString("Переменная не найдена: equipment_field_use"), D);
    }

    //тип исполнения
    if(jsonObject["equipment_type"].isDouble()){
        ret_config.equipment_type = jsonObject["equipment_type"].toInt();
    }
    else
    {
        ret_config.equipment_type = 0;
        syslog(QString("Переменная не найдена: equipment_type"), D);
    }

    //описание исполнения
    if(jsonObject["equipment_str"].isString()){
        ret_config.equipment_str = "";
        ret_config.equipment_str.append(jsonObject["equipment_str"].toString());
    }
    else
    {
        ret_config.equipment_str = "";
        syslog(QString("Переменная не найдена: equipment_str"), D);
    }


    //версия теста
    if(jsonObject["config_version"].isString()){
        ret_config.config_version.clear();
        ret_config.config_version.append(jsonObject["config_version"].toString());
    }
    else
        syslog(QString("Переменная не найдена: config_version"),D);

    //задержка при включении
    if(jsonObject["start_delay"].isDouble()){
        ret_config.start_delay = jsonObject["start_delay"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: start_delay"),D);


    //загружать ли файл справки
    if(jsonObject["load_help"].isDouble()){
        ret_config.load_help = jsonObject["load_help"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: load_help"),D);
        ret_config.load_help = 0;
    }


    //загружать прошивку в самую первую очередь
    if(jsonObject["firmware_chek"].isDouble()){
        ret_config.firmware_check = jsonObject["firmware_chek"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: firmware_chek"),D);
        ret_config.firmware_check = 0;
    }

    //загружать прошивку в самую первую очередь
    if(jsonObject["firmware_load_first"].isDouble()){
        ret_config.firmware_load_first = jsonObject["firmware_load_first"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: firmware_load_first"),D);
        ret_config.firmware_load_first = 0;
    }

    //загружать ли прошивку через web
    if(jsonObject["firmware_load"].isDouble()){
        ret_config.firmware_load = jsonObject["firmware_load"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: firmware_load"),D);
        ret_config.firmware_load = 0;
    }

    //версия прошивки
    if(jsonObject["firmware_vers"].isDouble()){
        ret_config.firmware_vers = jsonObject["firmware_vers"].toInt();
        qDebug() << "firmware_vers" << ret_config.firmware_vers;
    }
    else
        syslog(QString("Переменная не найдена: firmware_vers"),D);

    //путь к файлу прошивки
    if(jsonObject["firmware_path"].isString()){
        ret_config.firmware_path.clear();
        ret_config.firmware_path.append(jsonObject["firmware_path"].toString());
    }
    else
        syslog(QString("Переменная не найдена: firmware_path"),D);

    //путь до файла автопрошивки (для программатора AS4)
    if(jsonObject["as4_autoprogram_path"].isString()){
        ret_config.as4_autoprogram_path.clear();
        ret_config.as4_autoprogram_path.append(jsonObject["as4_autoprogram_path"].toString());
    }
    else
        syslog(QString("Переменная не найдена: as4_autoprogram_path"),D);


    //путь к файлу прошивки DFU
    if(jsonObject["dfu_path"].isString()){
        ret_config.dfu_path.clear();
        ret_config.dfu_path.append(jsonObject["dfu_path"].toString());
    }
    else
        syslog(QString("Переменная не найдена: dfu_path"),D);

    if(jsonObject["dfu_load"].isDouble()){
        ret_config.dfu_load = jsonObject["dfu_load"].toInt();
    }
    else{
        ret_config.dfu_load = 0;
        syslog(QString("Переменная не найдена: dfu_load"),D);
    }

    //получать ли через веб результаты самотестирования
    if(jsonObject["buildin_test"].isDouble()){
        ret_config.buildin_test = jsonObject["buildin_test"].toInt();
    }
    else{
        ret_config.buildin_test = 0;
        syslog(QString("Переменная не найдена: buildin_test"),D);
    }

    //подавать ли питание на AC1
    if(jsonObject["use_ac1"].isDouble()){
        ret_config.use_ac1 = jsonObject["use_ac1"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: use_ac1"),D);
        ret_config.use_ac1 = 0;
    }

    //подавать ли питание на AC2
    if(jsonObject["use_ac2"].isDouble()){
        ret_config.use_ac2 = jsonObject["use_ac2"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: use_ac2"),D);
        ret_config.use_ac2 = 0;
    }

    //тест сухих контактов
    if(jsonObject["dry_cont_test"].isObject()){
        json = jsonObject["dry_cont_test"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end()&& i<3;  ++iter,i++)
        {
            ret_config.dry_cont_test[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: dry_cont_test"),D);


    // Тест работы правого блока питания в качестве резервного
    if(jsonObject["ac_backup_test"].isDouble()){
        ret_config.ac_backup_test = jsonObject["ac_backup_test"].toInt();
    }
    else
    {
        syslog(QString("Переменная не найдена: ac_backup_test"),D);
        ret_config.ac_backup_test = 0;
    }

    //тест poe
    if(jsonObject["poe_test"].isDouble()){
        ret_config.poe_test = jsonObject["poe_test"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: poe_test"),D);

    if(jsonObject["poe_line_test"].isObject()){

        json = jsonObject["poe_line_test"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end();  ++iter,i++)
        {
            ret_config.poe_line_test[i] = 0;
            ret_config.poe_line_test[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: poe_line_test"),D);

    if(jsonObject["poe_line_passive"].isObject()){

        json = jsonObject["poe_line_passive"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end();  ++iter,i++)
        {
            ret_config.poe_line_passive[i] = 0;
            ret_config.poe_line_passive[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: poe_line_passive"),D);

    //каналы PoE при проверке резервирования БП
    if(jsonObject["poe_line_testCanOff"].isObject()){
        json = jsonObject["poe_line_testCanOff"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end();  ++iter,i++)
        {
            ret_config.poe_line_testCanOff[i] = 0;
            ret_config.poe_line_testCanOff[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: poe_line_testCanOff"),D);

    //установка мощности PoE
    if(jsonObject["poe_line_power"].isObject()){
        json = jsonObject["poe_line_power"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end();  ++iter,i++)
        {
            ret_config.poe_line_power[i] = 0;
            ret_config.poe_line_power[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: poe_line_power"),D);

    //min voltage
    if(jsonObject["poe_line_min"].isObject()){
        json = jsonObject["poe_line_min"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<PORT_NUM;  ++iter,i++)
        {
            ret_config.poe_line_min[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: poe_line_min"),D);

    //max voltage
    if(jsonObject["poe_line_max"].isObject()){
        json = jsonObject["poe_line_max"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<PORT_NUM;  ++iter,i++)
        {
            ret_config.poe_line_max[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: poe_line_max"),D);

    //тест передачей данных
    if(jsonObject["data_test"].isDouble()){
        ret_config.data_test = jsonObject["data_test"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: config_version"),D);

    //тестирование bercut

    //тестирование цепочкой через промежуточный коммутатор
    if(jsonObject["data_test_chain"].isDouble()){
        ret_config.data_test_chain = jsonObject["data_test_chain"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: data_test_chain"),D);

    //тест передачей данных - ports
    if(jsonObject["data_test_ports"].isObject()){
        json = jsonObject["data_test_ports"].toObject();
        i = 0;
        for(QJsonObject::iterator iter = json.begin(); iter != json.end() && i < PORT_NUM;  ++iter, i++)
        {
            ret_config.data_test_ports[i] = (*iter).toInt();
        }
        while (i < PORT_NUM)
        {
            ret_config.data_test_ports[i] = 0;
            i++;
        }
    }
    else
        syslog(QString("Переменная не найдена: data_test_ports"),D);

    //тест передачей данных - скорость порта
    if(jsonObject["ports_speed"].isObject()){
        json = jsonObject["ports_speed"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<PORT_NUM;  ++iter,i++)
        {
            ret_config.ports_speed[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: ports_speed"),D);

    //конфигурация промежуточного коммутатора в режиме шлейфа
    if(jsonObject["switch_config_chain"].isString()){
        ret_config.switch_config_chain.clear();
        ret_config.switch_config_chain.append(jsonObject["switch_config_chain"].toString());
    }
    else
        syslog(QString("Переменная не найдена: switch_config_chain"),D);

    //конфигурация промежуточного коммутатора в нормальном режиме
    if(jsonObject["switch_config_normal"].isString()){
        ret_config.switch_config_normal.clear();
        ret_config.switch_config_normal.append(jsonObject["switch_config_normal"].toString());
    }
    else
        syslog(QString("Переменная не найдена: switch_config_normal"),D);

    //нужна ли прошивка мас адреса
    if(jsonObject["send_mac"].isDouble()){
        ret_config.send_mac = jsonObject["send_mac"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: send_mac"),D);

    //тестирование UPS
    if(jsonObject["test_ups"].isDouble()){
        ret_config.test_ups = jsonObject["test_ups"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: test_ups"),D);

    //подключение нагрузочного резистора
    if(jsonObject["ups_rload"].isDouble()){
        ret_config.ups_rload = jsonObject["ups_rload"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: ups_rload"),D);

    //тестирование нагревателей
    if(jsonObject["test_heating"].isDouble()){
        ret_config.test_heating = jsonObject["test_heating"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: test_heating"),D);

    if(jsonObject["heating_curr_min"].isDouble()){
        ret_config.heating_curr_min = jsonObject["heating_curr_min"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: heating_curr_min"),D);

    if(jsonObject["heating_curr_max"].isDouble()){
        ret_config.heating_curr_max = jsonObject["heating_curr_max"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: heating_curr_max"),D);

    if(jsonObject["test_heating2"].isDouble()){
        ret_config.test_heating2 = jsonObject["test_heating2"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: test_heating2"),D);

    if(jsonObject["heating2_curr_min"].isDouble()){
        ret_config.heating2_curr_min = jsonObject["heating2_curr_min"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: heating2_curr_min"),D);

    if(jsonObject["heating2_curr_max"].isDouble()){
        ret_config.heating2_curr_max = jsonObject["heating2_curr_max"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: heating2_curr_max"),D);

    //мнинмальный ток нагревателей
    if(jsonObject["chrg_curr_min"].isDouble()){
        ret_config.chrg_curr_min = jsonObject["chrg_curr_min"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: chrg_curr_min"),D);
        ret_config.chrg_curr_min = 0;
    }

    //максимальный ток нагревателей
    if(jsonObject["chrg_curr_max"].isDouble()){
        ret_config.chrg_curr_max = jsonObject["chrg_curr_max"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: chrg_curr_max"),D);
        ret_config.chrg_curr_max = 0;
    }

    //тестирование зарядки
    if(jsonObject["test_charging"].isDouble()){
        ret_config.test_charging = jsonObject["test_charging"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: test_charging"),D);
        ret_config.test_charging = 0;
    }

    if(jsonObject["chrg_curr_min"].isDouble()){
        ret_config.chrg_curr_min = jsonObject["chrg_curr_min"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: chrg_curr_min"),D);
        ret_config.chrg_curr_min = 0;
    }
    if(jsonObject["chrg_curr_max"].isDouble()){
        ret_config.chrg_curr_max = jsonObject["chrg_curr_max"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: chrg_curr_max"),D);
        ret_config.chrg_curr_max = 0;
    }

    //тестирование режима зарядки постоянным током
    if(jsonObject["charge_CC_test"].isDouble()){
        ret_config.charge_CC_test = jsonObject["charge_CC_test"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CC_test"),D);
        ret_config.charge_CC_test = 0;
    }
    //сопротивление на иммитаторе нагрузки
    if(jsonObject["charge_CC_resistance"].isDouble()){
        ret_config.charge_CC_resistance = jsonObject["charge_CC_resistance"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CC_resistance"),D);
        ret_config.charge_CC_resistance = 0;
    }

    if(jsonObject["charge_CC_current_min"].isDouble()){
        ret_config.charge_CC_current_min = jsonObject["charge_CC_current_min"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CC_current_min"),D);
        ret_config.charge_CC_current_min = 0;
    }

    if(jsonObject["charge_CC_current_max"].isDouble()){
        ret_config.charge_CC_current_max = jsonObject["charge_CC_current_max"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CC_current_max"),D);
        ret_config.charge_CC_current_max = 0;
    }

    if(jsonObject["charge_CC_voltage_min"].isDouble()){
        ret_config.charge_CC_voltage_min = jsonObject["charge_CC_voltage_min"].toInt();
    }
    else{
        ret_config.charge_CC_voltage_min = 0;
        syslog(QString("Переменная не найдена: charge_CC_voltage_min"),D);
    }

    if(jsonObject["charge_CC_voltage_max"].isDouble()){
        ret_config.charge_CC_voltage_max = jsonObject["charge_CC_voltage_max"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CC_voltage_max"),D);
        ret_config.charge_CC_voltage_max = 0;
    }

    if(jsonObject["charge_CV_test"].isDouble()){
        ret_config.charge_CV_test = jsonObject["charge_CV_test"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CV_test"),D);
        ret_config.charge_CV_test = 0;
    }

    if(jsonObject["charge_CV_resistance"].isDouble()){
        ret_config.charge_CV_resistance = jsonObject["charge_CV_resistance"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CV_resistance"),D);
        ret_config.charge_CV_resistance = 0;
    }

    if(jsonObject["charge_CV_current_min"].isDouble()){
        ret_config.charge_CV_current_min = jsonObject["charge_CV_current_min"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CV_current_min"),D);
        ret_config.charge_CV_current_min = 0;
    }

    if(jsonObject["charge_CV_current_max"].isDouble()){
        ret_config.charge_CV_current_max = jsonObject["charge_CV_current_max"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CV_current_max"),D);
        ret_config.charge_CV_current_max = 0;
    }

    if(jsonObject["charge_CV_voltage_min"].isDouble()){
        ret_config.charge_CV_voltage_min = jsonObject["charge_CV_voltage_min"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CV_voltage_min"),D);
        ret_config.charge_CV_voltage_min = 0;
    }

    if(jsonObject["charge_CV_voltage_max"].isDouble()){
        ret_config.charge_CV_voltage_max = jsonObject["charge_CV_voltage_max"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: charge_CV_voltage_max"),D);
        ret_config.charge_CV_voltage_max = 0;
    }

    if(jsonObject["discharge_power_supply_voltage"].isDouble()){
        ret_config.discharge_power_supply_voltage = jsonObject["discharge_power_supply_voltage"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: discharge_power_supply_voltage"),D);
        ret_config.discharge_power_supply_voltage = 0;
    }

    //печатать этикетки
    if(jsonObject["print_label"].isDouble()){
        ret_config.print_label = jsonObject["print_label"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: print_label"),D);
        ret_config.print_label = 0;
    }

    //число этикеток
    if(jsonObject["label_num"].isDouble()){
        ret_config.label_num = jsonObject["label_num"].toInt();
    }
    else{
        syslog(QString("Переменная не найдена: label_num"),D);
        ret_config.label_num = 0;
    }

    //комментарий перед тестом
    if(jsonObject["pre_comment"].isString()){
        ret_config.pre_comment.clear();
        ret_config.pre_comment.append(jsonObject["pre_comment"].toString());
    }
    else
        syslog(QString("Переменная не найдена: pre_comment"),D);

    //комментарий после теста
    if(jsonObject["post_comment"].isString()){
        ret_config.post_comment.clear();
        ret_config.post_comment.append(jsonObject["post_comment"].toString());
    }
    else
        syslog(QString("Переменная не найдена: post_comment"),D);

    //для схемы подключений
    //число портов
    if(jsonObject["port_num"].isDouble()){
        ret_config.port_num = jsonObject["port_num"].toInt();
    }
    else
        syslog(QString("Переменная не найдена: port_num"),D);
    //port_poe
    if(jsonObject["port_poe"].isObject()){
        json = jsonObject["port_poe"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<PORT_NUM;  ++iter,i++)
        {
            ret_config.port_poe[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: port_poe"),D);

    //port_sfp
    if(jsonObject["port_sfp"].isObject()){
        json = jsonObject["port_sfp"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<PORT_NUM;  ++iter,i++)
        {
            ret_config.port_sfp[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: port_sfp"),D);

    //port2stand
    if(jsonObject["port2stand"].isObject()){
        json = jsonObject["port2stand"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end() && i<PORT_NUM;  ++iter,i++)
        {
            ret_config.port2stand[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: port2stand"),D);

    //переменные для TLP
    //inputs
    if(jsonObject["tlp_inputs"].isObject()){
        json = jsonObject["tlp_inputs"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end();  ++iter,i++)
        {
            ret_config.tlp_inputs[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: tlp_inputs"),D);

    //outputs
    if(jsonObject["tlp_outputs"].isObject()){
        json = jsonObject["tlp_outputs"].toObject();
        i=0;
        for(QJsonObject::iterator iter = json.begin();iter!=json.end();  ++iter,i++)
        {
            ret_config.tlp_outputs[i] = (*iter).toInt();
        }
    }
    else
        syslog(QString("Переменная не найдена: tlp_outputs"),D);

    //rs-485
    if(jsonObject["tlp_rs485"].isDouble())
    {
        ret_config.tlp_rs485 = jsonObject["tlp_rs485"].toInt();
    }
    else
    {
        ret_config.tlp_rs485 = 0;
        syslog(QString("Переменная не найдена: tlp_rs485"),D);
    }

    //i2c
    if(jsonObject["i2c_test"].isDouble())
    {
        ret_config.i2c_test = jsonObject["i2c_test"].toInt();
    }
    else
    {
        ret_config.i2c_test = 0;
        syslog(QString("Переменная не найдена: i2c_test"),D);
    }

    //hw_check
    if(jsonObject["hw_check"].isDouble())
    {
        ret_config.hw_check = jsonObject["hw_check"].toInt();
    }
    else
    {
        ret_config.hw_check = 0;
        syslog(QString("Переменная не найдена: hw_check"),D);
    }

    //hw_vers
    if(jsonObject["hw_vers"].isDouble())
    {
        ret_config.hw_vers = jsonObject["hw_vers"].toInt();
    }
    else
    {
        ret_config.hw_vers = 0;
        syslog(QString("Переменная не найдена: hw_vers"),D);
    }

    return ret_config;
}

configmodel_rps MainWindow::parse_config_rps_json(QJsonObject jsonObject){
    static configmodel_rps ret_config;
    QJsonObject json;

    //тестирование узла Prehaeting
    if(jsonObject["preheating_test"].isDouble()){
        ret_config.preheating_test = jsonObject["preheating_test"].toInt();
    }
    else
    {
        ret_config.preheating_test = 0;
        syslog(QString("Переменная не найдена: preheating_test"), D);
    }

    //флаг тестирования RKN
    if(jsonObject["rkn_test"].isDouble()){
        ret_config.rkn_test = jsonObject["rkn_test"].toInt();
    }
    else
    {
        ret_config.rkn_test = 0;
        syslog(QString("Переменная не найдена: rkn_test"), D);
    }

    //время старта RKN
    if(jsonObject["rkn_startup_time_min"].isDouble()){
        ret_config.rkn_startup_time_min = jsonObject["rkn_startup_time_min"].toInt();
    }
    else
    {
        ret_config.rkn_startup_time_min = 0;
        syslog(QString("Переменная не найдена: rkn_startup_time_min"), D);
    }
    if(jsonObject["rkn_startup_time_max"].isDouble()){
        ret_config.rkn_startup_time_max = jsonObject["rkn_startup_time_max"].toInt();
    }
    else
    {
        ret_config.rkn_startup_time_max = 0;
        syslog(QString("Переменная не найдена: rkn_startup_time_max"), D);
    }
    //время выключения
    if(jsonObject["rkn_disable_time"].isDouble()){
        ret_config.rkn_disable_time = jsonObject["rkn_disable_time"].toInt();
    }
    else
    {
        ret_config.rkn_disable_time = 0;
        syslog(QString("Переменная не найдена: rkn_disable_time"), D);
    }

    //флаг этапа самотестирования
    if(jsonObject["buildin_test"].isDouble()){
        ret_config.buildin_test = jsonObject["buildin_test"].toInt();
    }
    else
    {
        ret_config.buildin_test = 0;
        syslog(QString("Переменная не найдена: buildin_test"), D);
    }

    //темпреатура на плате rps
    if(jsonObject["temper_min"].isDouble()){
        ret_config.temper_min = jsonObject["temper_min"].toInt();
    }
    else
    {
        ret_config.temper_min = 0;
        syslog(QString("Переменная не найдена: temper_min"), D);
    }
    if(jsonObject["temper_max"].isDouble()){
        ret_config.temper_max = jsonObject["temper_max"].toInt();
    }
    else
    {
        ret_config.temper_max = 0;
        syslog(QString("Переменная не найдена: temper_max"), D);
    }

    if(jsonObject["relay1_test"].isDouble()){
        ret_config.relay1_test = jsonObject["relay1_test"].toInt();
    }
    else
    {
        ret_config.relay1_test = 0;
        syslog(QString("Переменная не найдена: relay1_test"), D);
    }
    if(jsonObject["relay2_test"].isDouble()){
        ret_config.relay2_test = jsonObject["relay2_test"].toInt();
    }
    else
    {
        ret_config.relay2_test = 0;
        syslog(QString("Переменная не найдена: relay2_test"), D);
    }

    //минимальная версия ПО
    if(jsonObject["fw_version"].isDouble()){
        ret_config.fw_version = jsonObject["fw_version"].toInt();
    }
    else
    {
        ret_config.fw_version = 0;
        syslog(QString("Переменная не найдена: fw_version"), D);
    }

    //флаг проверки узла заряда
    if(jsonObject["charging_test"].isDouble()){
        ret_config.charging_test = jsonObject["charging_test"].toInt();
    }
    else
    {
        ret_config.charging_test = 0;
        syslog(QString("Переменная не найдена: charging_test"), D);
    }

    //напряжение на акб при зарядке
    if(jsonObject["akb_voltage_ac_min"].isDouble()){
        ret_config.akb_voltage_ac_min = jsonObject["akb_voltage_ac_min"].toInt();
    }
    else
    {
        ret_config.akb_voltage_ac_min = 0;
        syslog(QString("Переменная не найдена: akb_voltage_ac_min"), D);
    }

    if(jsonObject["akb_voltage_ac_max"].isDouble()){
        ret_config.akb_voltage_ac_max = jsonObject["akb_voltage_ac_max"].toInt();
    }
    else
    {
        ret_config.akb_voltage_ac_max = 0;
        syslog(QString("Переменная не найдена: akb_voltage_ac_max"), D);
    }

    //напряжение зарядки
    if(jsonObject["akb_charge_voltage_min"].isDouble()){
        ret_config.akb_charge_voltage_min = jsonObject["akb_charge_voltage_min"].toInt();
    }
    else
    {
        ret_config.akb_charge_voltage_min = 0;
        syslog(QString("Переменная не найдена: akb_charge_voltage_min"), D);
    }
    if(jsonObject["akb_charge_voltage_max"].isDouble()){
        ret_config.akb_charge_voltage_max = jsonObject["akb_charge_voltage_max"].toInt();
    }
    else
    {
        ret_config.akb_charge_voltage_max = 0;
        syslog(QString("Переменная не найдена: akb_charge_voltage_max"), D);
    }

    //тестирование на ХХ
    if(jsonObject["load_XX_test"].isDouble()){
        ret_config.load_XX_test = jsonObject["load_XX_test"].toInt();
    }
    else
    {
        ret_config.load_XX_test = 0;
        syslog(QString("Переменная не найдена: load_XX_test"), D);
    }
    if(jsonObject["load_XX_current_min"].isDouble()){
        ret_config.load_XX_current_min = jsonObject["load_XX_current_min"].toInt();
    }
    else
    {
        ret_config.load_XX_current_min = 0;
        syslog(QString("Переменная не найдена: load_XX_current_min"), D);
    }
    if(jsonObject["load_XX_current_max"].isDouble()){
        ret_config.load_XX_current_max = jsonObject["load_XX_current_max"].toInt();
    }
    else
    {
        ret_config.load_XX_current_max = 0;
        syslog(QString("Переменная не найдена: load_XX_current_max"), D);
    }
    //ток разрядки на 16 ом
    if(jsonObject["load_16ohm_test"].isDouble()){
        ret_config.load_16ohm_test = jsonObject["load_16ohm_test"].toInt();
    }
    else
    {
        ret_config.load_16ohm_test = 0;
        syslog(QString("Переменная не найдена: load_16ohm_test"), D);
    }
    if(jsonObject["load_16ohm_current_min"].isDouble()){
        ret_config.load_16ohm_current_min = jsonObject["load_16ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_16ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_16ohm_current_min"), D);
    }
    if(jsonObject["load_16ohm_current_max"].isDouble()){
        ret_config.load_16ohm_current_max = jsonObject["load_16ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_16ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_16ohm_current_max"), D);
    }
    if(jsonObject["load_16ohm_voltage_min"].isDouble()){
        ret_config.load_16ohm_voltage_min = jsonObject["load_16ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_16ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_16ohm_voltage_min"), D);
    }
    if(jsonObject["load_16ohm_voltage_max"].isDouble()){
        ret_config.load_16ohm_voltage_max = jsonObject["load_16ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_16ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_16ohm_voltage_max"), D);
    }

    //ток разрядки на 22 ом
    if(jsonObject["load_22ohm_test"].isDouble()){
        ret_config.load_22ohm_test = jsonObject["load_22ohm_test"].toInt();
    }
    else
    {
        ret_config.load_22ohm_test = 0;
        syslog(QString("Переменная не найдена: load_22ohm_test"), D);
    }
    if(jsonObject["load_22ohm_current_min"].isDouble()){
        ret_config.load_22ohm_current_min = jsonObject["load_22ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_22ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_22ohm_current_min"), D);
    }
    if(jsonObject["load_22ohm_current_max"].isDouble()){
        ret_config.load_22ohm_current_max = jsonObject["load_22ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_22ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_22ohm_current_max"), D);
    }
    if(jsonObject["load_22ohm_voltage_min"].isDouble()){
        ret_config.load_22ohm_voltage_min = jsonObject["load_22ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_22ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_22ohm_voltage_min"), D);
    }
    if(jsonObject["load_22ohm_voltage_max"].isDouble()){
        ret_config.load_22ohm_voltage_max = jsonObject["load_22ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_22ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_22ohm_voltage_max"),D);
    }
    //ток разрядки на 68 ом
    if(jsonObject["load_68ohm_test"].isDouble()){
        ret_config.load_68ohm_test = jsonObject["load_68ohm_test"].toInt();
    }
    else
    {
        ret_config.load_68ohm_test = 0;
        syslog(QString("Переменная не найдена: load_68ohm_test"), D);
    }
    if(jsonObject["load_68ohm_current_min"].isDouble()){
        ret_config.load_68ohm_current_min = jsonObject["load_68ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_68ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_68ohm_current_min"), D);
    }
    if(jsonObject["load_68ohm_current_max"].isDouble()){
        ret_config.load_68ohm_current_max = jsonObject["load_68ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_68ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_68ohm_current_max"), D);
    }
    if(jsonObject["load_68ohm_voltage_min"].isDouble()){
        ret_config.load_68ohm_voltage_min = jsonObject["load_68ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_68ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_68ohm_voltage_min"), D);
    }
    if(jsonObject["load_68ohm_voltage_max"].isDouble()){
        ret_config.load_68ohm_voltage_max = jsonObject["load_68ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_68ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_68ohm_voltage_max"), D);
    }
    //100
    //ток разрядки на 100 ом
    if(jsonObject["load_100ohm_test"].isDouble()){
        ret_config.load_100ohm_test = jsonObject["load_100ohm_test"].toInt();
    }
    else
    {
        ret_config.load_100ohm_test = 0;
        syslog(QString("Переменная не найдена: load_100ohm_test"), D);
    }
    if(jsonObject["load_100ohm_current_min"].isDouble()){
        ret_config.load_100ohm_current_min = jsonObject["load_100ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_100ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_100ohm_current_min"), D);
    }
    if(jsonObject["load_100ohm_current_max"].isDouble()){
        ret_config.load_100ohm_current_max = jsonObject["load_100ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_100ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_100ohm_current_max"), D);
    }
    if(jsonObject["load_100ohm_voltage_min"].isDouble()){
        ret_config.load_100ohm_voltage_min = jsonObject["load_100ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_100ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_100ohm_voltage_min"), D);
    }
    if(jsonObject["load_100ohm_voltage_max"].isDouble()){
        ret_config.load_100ohm_voltage_max = jsonObject["load_100ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_100ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_100ohm_voltage_max"), D);
    }
    //70
    //ток разрядки на 70 ом
    if(jsonObject["load_70ohm_test"].isDouble()){
        ret_config.load_70ohm_test = jsonObject["load_70ohm_test"].toInt();
    }
    else
    {
        ret_config.load_70ohm_test = 0;
        syslog(QString("Переменная не найдена: load_70ohm_test"), D);
    }
    if(jsonObject["load_70ohm_current_min"].isDouble()){
        ret_config.load_70ohm_current_min = jsonObject["load_70ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_70ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_70ohm_current_min"), D);
    }
    if(jsonObject["load_70ohm_current_max"].isDouble()){
        ret_config.load_70ohm_current_max = jsonObject["load_70ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_70ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_70ohm_current_max"), D);
    }
    if(jsonObject["load_70ohm_voltage_min"].isDouble()){
        ret_config.load_70ohm_voltage_min = jsonObject["load_70ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_70ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_70ohm_voltage_min"), D);
    }
    if(jsonObject["load_70ohm_voltage_max"].isDouble()){
        ret_config.load_70ohm_voltage_max = jsonObject["load_70ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_70ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_70ohm_voltage_max"), D);
    }

    //50
    //ток разрядки на 50 ом
    if(jsonObject["load_50ohm_test"].isDouble()){
        ret_config.load_50ohm_test = jsonObject["load_50ohm_test"].toInt();
    }
    else
    {
        ret_config.load_50ohm_test = 0;
        syslog(QString("Переменная не найдена: load_50ohm_test"), D);
    }
    if(jsonObject["load_50ohm_current_min"].isDouble()){
        ret_config.load_50ohm_current_min = jsonObject["load_50ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_50ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_50ohm_current_min"), D);
    }
    if(jsonObject["load_50ohm_current_max"].isDouble()){
        ret_config.load_50ohm_current_max = jsonObject["load_50ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_50ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_50ohm_current_max"), D);
    }
    if(jsonObject["load_50ohm_voltage_min"].isDouble()){
        ret_config.load_50ohm_voltage_min = jsonObject["load_50ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_50ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_50ohm_voltage_min"), D);
    }
    if(jsonObject["load_50ohm_voltage_max"].isDouble()){
        ret_config.load_50ohm_voltage_max = jsonObject["load_50ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_50ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_50ohm_voltage_max"), D);
    }

    //30
    //ток разрядки на 30 ом
    if(jsonObject["load_30ohm_test"].isDouble()){
        ret_config.load_30ohm_test = jsonObject["load_30ohm_test"].toInt();
    }
    else
    {
        ret_config.load_30ohm_test = 0;
        syslog(QString("Переменная не найдена: load_30ohm_test"), D);
    }
    if(jsonObject["load_30ohm_current_min"].isDouble()){
        ret_config.load_30ohm_current_min = jsonObject["load_30ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_30ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_30ohm_current_min"), D);
    }
    if(jsonObject["load_30ohm_current_max"].isDouble()){
        ret_config.load_30ohm_current_max = jsonObject["load_30ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_30ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_30ohm_current_max"), D);
    }
    if(jsonObject["load_30ohm_voltage_min"].isDouble()){
        ret_config.load_30ohm_voltage_min = jsonObject["load_30ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_30ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_30ohm_voltage_min"), D);
    }
    if(jsonObject["load_30ohm_voltage_max"].isDouble()){
        ret_config.load_30ohm_voltage_max = jsonObject["load_30ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_30ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_30ohm_voltage_max"), D);
    }

    //20
    //ток разрядки на 20 ом
    if(jsonObject["load_20ohm_test"].isDouble()){
        ret_config.load_20ohm_test = jsonObject["load_20ohm_test"].toInt();
    }
    else
    {
        ret_config.load_20ohm_test = 0;
        syslog(QString("Переменная не найдена: load_20ohm_test"), D);
    }
    if(jsonObject["load_20ohm_current_min"].isDouble()){
        ret_config.load_20ohm_current_min = jsonObject["load_20ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_20ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_20ohm_current_min"), D);
    }
    if(jsonObject["load_20ohm_current_max"].isDouble()){
        ret_config.load_20ohm_current_max = jsonObject["load_20ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_20ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_20ohm_current_max"), D);
    }
    if(jsonObject["load_20ohm_voltage_min"].isDouble()){
        ret_config.load_20ohm_voltage_min = jsonObject["load_20ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_20ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_20ohm_voltage_min"), D);
    }
    if(jsonObject["load_20ohm_voltage_max"].isDouble()){
        ret_config.load_20ohm_voltage_max = jsonObject["load_20ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_20ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_20ohm_voltage_max"), D);
    }

    //15
    //ток разрядки на 15 ом
    if(jsonObject["load_15ohm_test"].isDouble()){
        ret_config.load_15ohm_test = jsonObject["load_15ohm_test"].toInt();
    }
    else
    {
        ret_config.load_15ohm_test = 0;
        syslog(QString("Переменная не найдена: load_15ohm_test"), D);
    }
    if(jsonObject["load_15ohm_current_min"].isDouble()){
        ret_config.load_15ohm_current_min = jsonObject["load_15ohm_current_min"].toInt();
    }
    else
    {
        ret_config.load_15ohm_current_min = 0;
        syslog(QString("Переменная не найдена: load_15ohm_current_min"), D);
    }
    if(jsonObject["load_15ohm_current_max"].isDouble()){
        ret_config.load_15ohm_current_max = jsonObject["load_15ohm_current_max"].toInt();
    }
    else
    {
        ret_config.load_15ohm_current_max = 0;
        syslog(QString("Переменная не найдена: load_15ohm_current_max"), D);
    }
    if(jsonObject["load_15ohm_voltage_min"].isDouble()){
        ret_config.load_15ohm_voltage_min = jsonObject["load_15ohm_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_15ohm_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_15ohm_voltage_min"), D);
    }
    if(jsonObject["load_15ohm_voltage_max"].isDouble()){
        ret_config.load_15ohm_voltage_max = jsonObject["load_15ohm_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_15ohm_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_15ohm_voltage_max"), D);
    }
    //for rps-01
    if(jsonObject["load_15ohm_rps01_current_min"].isDouble()){
        ret_config.load_15ohm_rps01_current_min = jsonObject["load_15ohm_rps01_current_min"].toInt();
    }
    else
    {
        ret_config.load_15ohm_rps01_current_min = 0;
        syslog(QString("Переменная не найдена: load_15ohm_rps01_current_min"), D);
    }
    if(jsonObject["load_15ohm_rps01_current_max"].isDouble()){
        ret_config.load_15ohm_rps01_current_max = jsonObject["load_15ohm_rps01_current_max"].toInt();
    }
    else
    {
        ret_config.load_15ohm_rps01_current_max = 0;
        syslog(QString("Переменная не найдена: load_15ohm_rps01_current_max"), D);
    }
    if(jsonObject["load_15ohm_rps01_voltage_min"].isDouble()){
        ret_config.load_15ohm_rps01_voltage_min = jsonObject["load_15ohm_rps01_voltage_min"].toInt();
    }
    else
    {
        ret_config.load_15ohm_rps01_voltage_min = 0;
        syslog(QString("Переменная не найдена: load_15ohm_rps01_voltage_min"), D);
    }
    if(jsonObject["load_15ohm_rps01_voltage_max"].isDouble()){
        ret_config.load_15ohm_rps01_voltage_max = jsonObject["load_15ohm_rps01_voltage_max"].toInt();
    }
    else
    {
        ret_config.load_15ohm_rps01_voltage_max = 0;
        syslog(QString("Переменная не найдена: load_15ohm_rps01_voltage_max"), D);
    }

    //максимальная задержка на чтение парамеров
    if(jsonObject["rps_read_delay"].isDouble()){
        ret_config.rps_read_delay = jsonObject["rps_read_delay"].toInt();
    }
    else
    {
        ret_config.rps_read_delay = 0;
        syslog(QString("Переменная не найдена: rps_read_delay"), D);
    }

    //380В тест
    if(jsonObject["rkn_380v_test"].isDouble()){
        ret_config.rkn_380v_test = jsonObject["rkn_380v_test"].toInt();
    }
    else
    {
        ret_config.rkn_380v_test = 0;
        syslog(QString("Переменная не найдена: rkn_380v_test"), D);
    }

    //положение джампера Preheating
    if(jsonObject["preheating_position"].isDouble()){
        ret_config.preheating_position = jsonObject["preheating_position"].toInt();
    }
    else
    {
        ret_config.preheating_position = 0;
        syslog(QString("Переменная не найдена: preheating_position"), D);
    }

    return ret_config;
}

static QJsonObject save_ps_json(settingsmodel *configuration){
    QString str;

    static QJsonObject json;
    QJsonObject card_index_json,username_json,password_json,config_name_json,
            config_dir_json,poe_coeff_json,
            device_id_json,device_name_json;

    json["com_port_num"] = configuration->com_port_name;
    json["com_autoconnect"] = configuration->com_autoconnect;

    json["bercut_com_port_name"] = configuration->bercut_com_port_name;
    json["bercut_state"] = configuration->bercut_state;
    json["bercut_test_type"] = configuration->bercut_test_type;

    json["bercut100_com_port_name"] = configuration->bercut100_com_port_name;
    json["bercut100_state"] = configuration->bercut100_state;
    json["bercut100_test_type"] = configuration->bercut100_test_type;

    json["telnet_state"] = configuration->switch_state;
    json["telnet_host"] = configuration->telnet_host;
    json["telnet_login"] = configuration->telnet_login;
    json["telnet_pass"] = configuration->telnet_pass;
    json["telnet_port_a"] = configuration->port_a;
    json["telnet_port_b"] = configuration->port_b;
    json["telnet_port_a100"] = configuration->port_a100;
    json["telnet_port_b100"] = configuration->port_b100;
    json["telnet_port_dut"] = configuration->port_dut;
    json["telnet_port_man"] = configuration->port_man;
    json["telnet_port_sfp1"] = configuration->port_sfp1;
    json["telnet_port_sfp2"] = configuration->port_sfp2;

    json["teleport_com_port_name"] = configuration->teleport_com_port_name;
    json["teleport_state"] = configuration->teleport_state;

    //настройки стенд RPS-1
    json["rps_stand_com_port_name"] = configuration->rps_stand_com_port_name;
    json["rps_stand_state"] = configuration->rps_stand_state;


    json["stand_id"] = configuration->stand_id;
    json["stand_type"] = configuration->stand_type;

    json["db_type"] = configuration->db_type;
    json["db_host"] = configuration->db_host;


    json["printer_name"] = configuration->printer_name;
    json["report_printer_name"] = configuration->report_printer_name;

    json["label_size"] = configuration->label_size;

    json["last_config_num"] = configuration->last_config_num;

    for(int i=0;i<DATA_TEST_LINES;i++){
        str.sprintf("%02d",i);
        card_index_json[str] = configuration->card_ip[i];
    }
    json["card_ip"] = card_index_json;


    for(int i=0;i<USERS_NUM;i++){
        if(!configuration->username[i].isEmpty()){
            str.sprintf("%02d",i);
            username_json[str] = configuration->username[i];
        }
    }
    json["username"] = username_json;

    for(int i=0;i<USERS_NUM;i++){
        if(!configuration->password[i].isEmpty()){
            str.sprintf("%02d",i);
            password_json[str] = configuration->password[i];
        }
    }
    json["password"] = password_json;


    for(int i=0;i<MAX_DEVICES;i++){
        if(!configuration->config_name[i].isEmpty()){
            str.sprintf("%02d",i);
            config_name_json[str] = configuration->config_name[i];
        }
    }
    json["config_name"] = config_name_json;


    for(int i=0;i<MAX_DEVICES;i++){
        if(!configuration->config_dir[i].isEmpty()){
            str.sprintf("%02d",i);
            config_dir_json[str] = configuration->config_dir[i];
        }
    }
    json["config_dir"] = config_dir_json;

    for(int i=0;i<NUM_POE_LINES;i++){
        str.sprintf("%02d",i);
        poe_coeff_json[str] = configuration->poe_coeff[i];
    }
    json["poe_coeff"] = poe_coeff_json;

    //настройки списка оборудования
    for(int i=0;i<MAX_DEVICES;i++){
        str.sprintf("%02d",i);
        device_id_json[str] = configuration->device_id[i];
    }
    json["device_id"] = device_id_json;
    for(int i=0;i<MAX_DEVICES;i++){
        str.sprintf("%02d",i);
        device_name_json[str] = configuration->device_name[i];
    }
    json["device_name"] = device_name_json;

    //путь до файла DFU prog
    json["dfu_prog_path"] = configuration->dfu_prog_path;

    //путь до файла AVR prog
    json["avr_prog_path"] = configuration->avr_prog_path;

    json["avr_prog_type"] = configuration->avr_prog_type;

    //настройка внешнего БП
    json["power_supply_state"] = configuration->power_supply_state;
    json["power_supply_model"] = configuration->power_supply_model;
    json["power_supply_com_port_name"] = configuration->power_supply_com_port_name;

    json["theme"] = configuration->last_theme;

    return json;
}

void MainWindow::com_sett(void){
    QMessageBox ms;
    ComSetDialog* pComSetDialog = new ComSetDialog(prog_sett.com_port_name,prog_sett.com_autoconnect, prog_sett.stand_type,prog_sett.stand_id);
    if (pComSetDialog->exec() == QDialog::Accepted) {
        if(prog_sett.com_port_name != pComSetDialog->portNum()){
            prog_sett.com_port_name = pComSetDialog->portNum();

            save_last_config();
            QAbstractButton *yes = ms.addButton("Да",QMessageBox::YesRole);
            ms.setText("Настройки применились, перезапустить программу?");
            ms.exec();
            if(ms.clickedButton() == yes)
                exit_app();
        }

        if(prog_sett.stand_type != pComSetDialog->standType()){
            prog_sett.stand_type = pComSetDialog->standType();
            save_last_config();
            QAbstractButton *yes = ms.addButton("Да",QMessageBox::YesRole);
            ms.setText("Настройки применились, перезапустить программу?");
            ms.exec();
            if(ms.clickedButton() == yes)
                exit_app();
        }

        prog_sett.com_autoconnect = pComSetDialog->portAutoconnect();
        prog_sett.stand_id = pComSetDialog->getStandID();
        save_last_config();
    }
    delete pComSetDialog;

    myDebug() << "com_sett";
}

html_parse_struct ret_config;

void clear_old_parse_struct(){
    //memset(&ret_config,0,sizeof(ret_config));
    ret_config.temperature = 0;
    ret_config.humidity = 0;
}


//диалог настройки соединения с DB
void MainWindow::db_sett(){
    DbSetDialog* pDbSetDialog = new DbSetDialog(
                prog_sett.db_type,
                prog_sett.db_host);

    if (pDbSetDialog->exec() == QDialog::Accepted) {
        prog_sett.db_type = pDbSetDialog->type();
        prog_sett.db_host = pDbSetDialog->host();

        save_last_config();
    }
    delete pDbSetDialog;
}

html_parse_struct traverseNodeHP(const QDomNode& node)
{
    static html_parse_struct ret_config;
    bool ok;
    int arc_cnt=0,i;
    char temp[256];

    QDomNode domNode = node.firstChild();
    while(!domNode.isNull()) {
        if(domNode.isElement()) {
            QDomElement domElement = domNode.toElement();
            if(!domElement.isNull()) {
                if(domElement.tagName() == "selftest") {

                }
                else {
                    //тип устройства
                    if(QString::compare(domElement.tagName(), "dev_type", Qt::CaseInsensitive)==0){
                        ret_config.dev_type = domElement.text().toShort(&ok,10);
                        myDebug() << ret_config.dev_type;
                        arc_cnt++;
                    }

                    //нет ошибок
                    if(QString::compare(domElement.tagName(), "init_ok", Qt::CaseInsensitive)==0){
                        ret_config.init_ok = domElement.text().toShort(&ok,10);
                        myDebug() << ret_config.init_ok;
                        arc_cnt++;
                    }
                    //версия ПО
                    if(QString::compare(domElement.tagName(), "firmvare_vers", Qt::CaseInsensitive)==0){
                        ret_config.firmvare_vers.clear();
                        ret_config.firmvare_vers.append(domElement.text());
                        arc_cnt++;
                    }

                    //версия платы
                    if(QString::compare(domElement.tagName(), "hw_vers", Qt::CaseInsensitive)==0){
                        ret_config.hw_vers = domElement.text().toInt(&ok,16);
                        arc_cnt++;
                    }

                    //версия бута
                    if(QString::compare(domElement.tagName(), "boot_vers", Qt::CaseInsensitive)==0){
                        ret_config.boot_vers = domElement.text().toLong(&ok,16);
                        arc_cnt++;
                    }

                    //серийный номер
                    if(QString::compare(domElement.tagName(), "serial_num", Qt::CaseInsensitive)==0){
                        ret_config.serial_num = domElement.text().toLong(&ok,10);
                        qDebug() << "ret_config.serial_num" << ret_config.serial_num;
                        arc_cnt++;
                    }

                    //MAC
                    if(QString::compare(domElement.tagName(), "default_mac", Qt::CaseInsensitive)==0){
                        ret_config.default_mac.clear();
                        ret_config.default_mac.append(domElement.text());
                        arc_cnt++;
                    }

                    //CPU_ID
                    if(QString::compare(domElement.tagName(), "cpu_id", Qt::CaseInsensitive)==0){
                        ret_config.cpu_id.clear();
                        ret_config.cpu_id.append(domElement.text());
                        arc_cnt++;
                    }

                    //link
                    for(i=0;i<PORT_NUM;i++){
                        sprintf(temp,"link_%d",i);
                        if(QString::compare(domElement.tagName(), temp, Qt::CaseInsensitive)==0){
                            ret_config.link[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                    }

                    //poe_a_state
                    for(i=0;i<PORT_NUM;i++){
                        sprintf(temp,"poe_a_%d_state",i+1);
                        if(QString::compare(domElement.tagName(), temp, Qt::CaseInsensitive)==0){
                            ret_config.poe_a_st[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                    }

                    //poe_b_state
                    for(i=0;i<PORT_NUM;i++){
                        sprintf(temp,"poe_b_%d_state",i+1);
                        if(QString::compare(domElement.tagName(), temp, Qt::CaseInsensitive)==0){
                            ret_config.poe_b_st[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                    }

                    //poe_a_voltage
                    for(i=0;i<PORT_NUM;i++){
                        sprintf(temp,"poe_a_%d_v",i+1);
                        if(QString::compare(domElement.tagName(), temp, Qt::CaseInsensitive)==0){
                            ret_config.poe_a_v[i] = domElement.text().toFloat(&ok);
                            arc_cnt++;
                        }
                    }

                    //poe_b_voltage
                    for(i=0;i<PORT_NUM;i++){
                        sprintf(temp,"poe_b_%d_v",i+1);
                        if(QString::compare(domElement.tagName(), temp, Qt::CaseInsensitive)==0){
                            ret_config.poe_b_v[i] = domElement.text().toFloat(&ok);
                            arc_cnt++;
                        }
                    }

                    //poe_a_current
                    for(i=0;i<PORT_NUM;i++){
                        sprintf(temp,"poe_a_%d_c",i+1);
                        if(QString::compare(domElement.tagName(), temp, Qt::CaseInsensitive)==0){
                            ret_config.poe_a_c[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                    }

                    //poe_b_current
                    for(i=0;i<PORT_NUM;i++){
                        sprintf(temp,"poe_b_%d_c",i+1);
                        if(QString::compare(domElement.tagName(), temp, Qt::CaseInsensitive)==0){
                            ret_config.poe_b_c[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                    }

                    //номер версии платы
                    if(QString::compare(domElement.tagName(), "board_version", Qt::CaseInsensitive)==0){
                        ret_config.board_version = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //poe контроллер
                    if(QString::compare(domElement.tagName(), "poe_controller", Qt::CaseInsensitive)==0){
                        ret_config.poe_controller = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //сухой контакт 0
                    if(QString::compare(domElement.tagName(), "sensor_0", Qt::CaseInsensitive)==0){
                        ret_config.sensor_0 = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //сухой контакт 1
                    if(QString::compare(domElement.tagName(), "sensor_1", Qt::CaseInsensitive)==0){
                        ret_config.sensor_1 = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //сухой контакт 2
                    if(QString::compare(domElement.tagName(), "sensor_2", Qt::CaseInsensitive)==0){
                        ret_config.sensor_2 = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    for(i=0;i<PORT_NUM;i++){
                        //sfp present
                        sprintf(temp,"sfp_%d_pres",i+1);
                        if(QString::compare(domElement.tagName(),temp, Qt::CaseInsensitive)==0){
                            ret_config.sfp_pres[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                        //sfp sd
                        sprintf(temp,"sfp_%d_sd",i+1);
                        if(QString::compare(domElement.tagName(),temp, Qt::CaseInsensitive)==0){
                            ret_config.sfp_sd[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                        //sfp id
                        sprintf(temp,"sfp_%d_id",i+1);
                        if(QString::compare(domElement.tagName(),temp, Qt::CaseInsensitive)==0){
                            ret_config.sfp_id[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                    }
                    //marvell ID
                    if(QString::compare(domElement.tagName(), "marvell_id", Qt::CaseInsensitive)==0){
                        ret_config.marvell_id = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }


                    //adc_1_0
                    if(QString::compare(domElement.tagName(), "adc_1_0", Qt::CaseInsensitive)==0){
                        ret_config.adc_1_0 = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //adc_1_2
                    if(QString::compare(domElement.tagName(), "adc_1_2", Qt::CaseInsensitive)==0){
                        ret_config.adc_1_2 = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //adc_1_8
                    if(QString::compare(domElement.tagName(), "adc_1_8", Qt::CaseInsensitive)==0){
                        ret_config.adc_1_8 = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //adc_1_5
                    if(QString::compare(domElement.tagName(), "adc_1_5", Qt::CaseInsensitive)==0){
                        ret_config.adc_1_5 = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //adc_2_5
                    if(QString::compare(domElement.tagName(), "adc_2_5", Qt::CaseInsensitive)==0){
                        ret_config.adc_2_5 = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //ups_det
                    if(QString::compare(domElement.tagName(), "ups_det", Qt::CaseInsensitive)==0){
                        ret_config.ups_det = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //akb_det
                    if(QString::compare(domElement.tagName(), "akb_det", Qt::CaseInsensitive)==0){
                        ret_config.akb_det = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //ups_rez
                    if(QString::compare(domElement.tagName(), "ups_rez", Qt::CaseInsensitive)==0){
                        ret_config.ups_rez = domElement.text().toInt(&ok,10);
                        arc_cnt++;
                    }

                    //akb_voltage
                    if(QString::compare(domElement.tagName(), "akb_voltage", Qt::CaseInsensitive)==0){
                        ret_config.akb_voltage = domElement.text().toFloat(&ok);
                        arc_cnt++;
                    }

                    //akb_voltage_chg
                    if(QString::compare(domElement.tagName(), "akb_voltage_chg", Qt::CaseInsensitive)==0){
                        ret_config.akb_voltage_chg = domElement.text().toFloat(&ok);
                        arc_cnt++;
                    }

                    //inputs
                    for(i=0;i<TLP_INPUTS_NUM;i++){
                        sprintf(temp,"input_%d",i);
                        if(QString::compare(domElement.tagName(), temp, Qt::CaseInsensitive)==0){
                            ret_config.tlp_input[i] = domElement.text().toInt(&ok,10);
                            arc_cnt++;
                        }
                    }
                    if(QString::compare(domElement.tagName(), "temperature", Qt::CaseInsensitive) == 0){
                        ret_config.temperature = domElement.text().toFloat(&ok);
                        arc_cnt++;
                    }
                    if(QString::compare(domElement.tagName(), "humidity", Qt::CaseInsensitive) == 0){
                        ret_config.humidity = domElement.text().toFloat(&ok);
                        arc_cnt++;
                    }

                    //hw_vers
                    if(QString::compare(domElement.tagName(), "hw_vers", Qt::CaseInsensitive) == 0){
                        ret_config.hw_vers = domElement.text().toFloat(&ok);
                        arc_cnt++;
                    }
                }
            }
        }


        traverseNodeHP(domNode);
        domNode = domNode.nextSibling();
    }


    return ret_config;
}

//интерфейс настройки сетевых карт
void MainWindow::net_card_sett(){
    int i;
    NetCardSetDialog* pNetCardSetDialog = new NetCardSetDialog(prog_sett.card_ip);
    if(pNetCardSetDialog->exec() == QDialog::Accepted){
        for(i=0;i<DATA_TEST_LINES;i++){
            prog_sett.card_ip[i] = pNetCardSetDialog->get_card(i);
        }
        save_last_config();
    }
    delete pNetCardSetDialog;
}

//окно настройки принтера
void MainWindow::printer_sett(){
    PrinterSetDialog *pPrinterSetDialog = new PrinterSetDialog(prog_sett.printer_name, prog_sett.label_size,prog_sett.report_printer_name);
    if(pPrinterSetDialog->exec() == QDialog::Accepted){
        prog_sett.printer_name.clear();
        prog_sett.printer_name.append(pPrinterSetDialog->get_printername());
        prog_sett.label_size = pPrinterSetDialog->get_labelSize();
        prog_sett.report_printer_name.clear();
        prog_sett.report_printer_name.append(pPrinterSetDialog->get_report_printername());
        save_last_config();
    }
    delete pPrinterSetDialog;
}

//окно настройки профилей тестирования
void MainWindow::profiles_sett(){
    ProfilesSetDialog *pProfilesSetDialog = new ProfilesSetDialog(prog_sett);
    if(pProfilesSetDialog->exec() == QDialog::Accepted){
        for(int i=0;i<MAX_DEVICES;i++){
            prog_sett.config_dir[i].clear();
            prog_sett.config_dir[i].append(pProfilesSetDialog->get_config_test(i));
        }
        save_last_config();
        refresh_device_list_ui();
    }
    delete pProfilesSetDialog;
}

//нажатие открыть конфигурацию в контекстном меню списка устройств
void MainWindow::deviceMenuHandler(void){

    //если это вкладка ремонт
    if(ui->main_tab->currentIndex() == 1){
        QUrl url2("file:/"+prog_sett.config_dir[ui->device_list->currentRow()]);
        QDesktopServices::openUrl(url2);
        qDebug() << "deviceMenuHandler";
    }
    else{
        QUrl url1("file:/"+prog_sett.config_dir[ui->device_list->currentRow()]);
        QDesktopServices::openUrl(url1);
        qDebug() << "deviceMenuHandler";
    }
}

//открывает окно настройки списка оборудования - не используется, тип берётся из конфига
void MainWindow::dev_list_sett_slot(){
    //новый Мой код:
    //    deviceListDialog = new DeviceListDialog(prog_sett);
    //    deviceListDialog->show();

    //старый Сашин код:
    DevListDialog *pDevListDialog = new DevListDialog(prog_sett);

    if(pDevListDialog->exec() == QDialog::Accepted)
    {
        for(int i=0;i<MAX_DEVICES;i++)
        {
            prog_sett.device_id[i] = pDevListDialog->getDevListId(i);
            prog_sett.device_name[i] = pDevListDialog->getDevListName(i);
        }

        save_last_config();
    }
    delete pDevListDialog;
}

void MainWindow::showConnectMenuHandler(void){
    open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]);
    Form *d = new Form(test_config,prog_sett);
    d->show();
    qDebug()<< "showConnectMenuHandler";
}

//окно настройки BERcut
void MainWindow::bercut_sett(){
    BercutSetDialog *pBercutSetDialog = new BercutSetDialog(prog_sett.bercut_com_port_name,prog_sett.bercut_state, prog_sett.bercut_test_type,
                                                            prog_sett.bercut100_com_port_name,prog_sett.bercut100_state, prog_sett.bercut100_test_type);
    if(pBercutSetDialog->exec() == QDialog::Accepted){
        prog_sett.bercut_com_port_name = pBercutSetDialog->portNum();
        prog_sett.bercut_state = pBercutSetDialog->getState();
        prog_sett.bercut_test_type  = pBercutSetDialog->getType();
        prog_sett.bercut100_com_port_name = pBercutSetDialog->portNum100();
        prog_sett.bercut100_state = pBercutSetDialog->getState100();
        prog_sett.bercut100_test_type  = pBercutSetDialog->getType100();
        save_last_config();
    }
    delete pBercutSetDialog;
}

//окно настройки промежуточного коммутатора
void MainWindow::switch_sett(){
    SwitchSetDialog *pSwitchSetDialog = new SwitchSetDialog(
                prog_sett.switch_state,prog_sett.telnet_host,
                prog_sett.telnet_login,prog_sett.telnet_pass,
                prog_sett.port_a,prog_sett.port_b, prog_sett.port_dut,
                prog_sett.port_sfp1,prog_sett.port_sfp2,
                prog_sett.port_man);
    if(pSwitchSetDialog->exec() == QDialog::Accepted){
        prog_sett.switch_state = pSwitchSetDialog->getState();
        prog_sett.telnet_host  = pSwitchSetDialog->getHost();
        prog_sett.telnet_login = pSwitchSetDialog->getLogin();
        prog_sett.telnet_pass  = pSwitchSetDialog->getPass();
        prog_sett.port_a = pSwitchSetDialog->getPortA();
        prog_sett.port_b = pSwitchSetDialog->getPortB();
        prog_sett.port_dut = pSwitchSetDialog->getPortDUT();
        prog_sett.port_sfp1 = pSwitchSetDialog->getPortSFP1();
        prog_sett.port_sfp2 = pSwitchSetDialog->getPortSFP2();
        prog_sett.port_man = pSwitchSetDialog->getPortMan();
        save_last_config();
    }
    delete pSwitchSetDialog;
}

//окно настройки Teleport
void MainWindow::teleport_sett(){
    TeleportSetDialog *pTeleportSetDialog = new TeleportSetDialog(prog_sett.teleport_com_port_name,prog_sett.teleport_state);
    if(pTeleportSetDialog->exec() == QDialog::Accepted){
        prog_sett.teleport_com_port_name = pTeleportSetDialog->portNum();
        prog_sett.teleport_state = pTeleportSetDialog->getState();
        save_last_config();
    }
    delete pTeleportSetDialog;
}

//настройка автономного стенда RPS-1
void MainWindow::rps_stand_sett(){
    RpsStandSetDialog *pRpsStandSetDialog = new RpsStandSetDialog(prog_sett.rps_stand_com_port_name,prog_sett.rps_stand_state);
    if(pRpsStandSetDialog->exec() == QDialog::Accepted){
        prog_sett.rps_stand_com_port_name = pRpsStandSetDialog->portName();
        prog_sett.rps_stand_state = pRpsStandSetDialog->getState();
        save_last_config();
    }
    delete pRpsStandSetDialog;
}

//настройка внешнего управляемого БП
void MainWindow::power_supply_sett(){
    PowerSupplySetDialog *pPowerSupplySetDialog = new PowerSupplySetDialog(prog_sett.power_supply_state,
                                                                           prog_sett.power_supply_model,
                                                                           prog_sett.power_supply_com_port_name);
    if(pPowerSupplySetDialog->exec() == QDialog::Accepted){
        prog_sett.power_supply_state = pPowerSupplySetDialog->getState();
        prog_sett.power_supply_model = pPowerSupplySetDialog->getModel();
        prog_sett.power_supply_com_port_name = pPowerSupplySetDialog->portName();
        save_last_config();
    }
    delete pPowerSupplySetDialog;
}

bool MainWindow::changeTheme()
{
    qDebug()<<"Установка темы";

    QPalette *dark_palette = new QPalette;

    dark_palette->setColor(QPalette::Window, QColor(28,33,40));
    dark_palette->setColor(QPalette::WindowText,Qt::white);
    dark_palette->setColor(QPalette::Base,QColor(45,51,59));
    dark_palette->setColor(QPalette::AlternateBase, QColor(45,51,59));
    dark_palette->setColor(QPalette::ToolTipBase,QColor(45,51,59));
    dark_palette->setColor(QPalette::ToolTipText,Qt::white);
    dark_palette->setColor(QPalette::Text,Qt::white);
    dark_palette->setColor(QPalette::Button,QColor(45,51,59));
    dark_palette->setColor(QPalette::ButtonText, Qt::white);
    dark_palette->setColor(QPalette::Link,QColor(42,130,218));
    dark_palette->setColor(QPalette::Highlight,QColor(42,130,218));
    dark_palette->setColor(QPalette::HighlightedText,Qt::black);

    if(prog_sett.last_theme == true)
    {
        qApp->setPalette(*dark_palette);
        ui->main_tab->setStyleSheet(/*"background-color: rgb(45, 51, 59);"*/
                                    "QPushButton{"
                                    "border-color: navy;"
                                    "color: rgb(255, 255, 255);"
                                    "background-color: rgb(52, 125, 57);}"
                                    "QPushButton : hover {color: rgb(255, 255, 255);"
                                    "background-color: rgb(72, 145, 77);}"
                                    "QPushButton : pressed{"
                                    "color: rgb(255, 255, 255);"
                                    "background-color: rgb(32, 105, 37);}"
                                    "QPushButton : disabled {"
                                    "background-color : grey;"
                                    "color: black;}");
        ui->groupBox_2->setStyleSheet("color : rgb(255, 255, 255)");

        QPixmap pixmap(":/images/restart_white.png");
        QIcon restartIconWhite(pixmap);

        ui->restart_from_selftest_btn->setIcon(restartIconWhite);
        ui->restart_from_update_btn->setIcon(restartIconWhite);
        ui->restart_from_heater_btn->setIcon(restartIconWhite);
        ui->restart_from_poe_btn->setIcon(restartIconWhite);
        ui->restart_from_ps_btn->setIcon(restartIconWhite);
        ui->restart_from_inout_btn->setIcon(restartIconWhite);
        ui->restart_from_data_btn->setIcon(restartIconWhite);
        ui->restart_from_ups_btn->setIcon(restartIconWhite);
        ui->restart_from_mac_btn->setIcon(restartIconWhite);
        ui->restart_from_label_btn->setIcon(restartIconWhite);
        ui->restart_from_report_btn->setIcon(restartIconWhite);

        prog_sett.last_theme = false;

    }else{
        qApp->setPalette(style()->standardPalette());
        ui->main_tab->setStyleSheet("");
        ui->device_list->setStyleSheet("");
        ui->groupBox_2->setStyleSheet("color : rgb(0,0,0)");

        QPixmap pixmap(":/images/restart_black.png");
        QIcon restartIconBlack(pixmap);

        ui->restart_from_selftest_btn->setIcon(restartIconBlack);
        ui->restart_from_update_btn->setIcon(restartIconBlack);
        ui->restart_from_heater_btn->setIcon(restartIconBlack);
        ui->restart_from_poe_btn->setIcon(restartIconBlack);
        ui->restart_from_ps_btn->setIcon(restartIconBlack);
        ui->restart_from_inout_btn->setIcon(restartIconBlack);
        ui->restart_from_data_btn->setIcon(restartIconBlack);
        ui->restart_from_ups_btn->setIcon(restartIconBlack);
        ui->restart_from_mac_btn->setIcon(restartIconBlack);
        ui->restart_from_label_btn->setIcon(restartIconBlack);
        ui->restart_from_report_btn->setIcon(restartIconBlack);

        prog_sett.last_theme = true;
    }
    save_last_config();
    return themeDark;
}

void MainWindow::set_theme()
{
    QPalette *dark_palette = new QPalette;

    dark_palette->setColor(QPalette::Window, QColor(28,33,40));
    dark_palette->setColor(QPalette::WindowText,Qt::white);
    dark_palette->setColor(QPalette::Base,QColor(45,51,59));
    dark_palette->setColor(QPalette::AlternateBase, QColor(45,51,59));
    dark_palette->setColor(QPalette::ToolTipBase,QColor(45,51,59));
    dark_palette->setColor(QPalette::ToolTipText,Qt::white);
    dark_palette->setColor(QPalette::Text,Qt::white);
    dark_palette->setColor(QPalette::Button,QColor(45,51,59));
    dark_palette->setColor(QPalette::ButtonText, Qt::white);
    dark_palette->setColor(QPalette::Link,QColor(42,130,218));
    dark_palette->setColor(QPalette::Highlight,QColor(42,130,218));
    dark_palette->setColor(QPalette::HighlightedText,Qt::black);

    if(prog_sett.last_theme == false)
    {
        qApp->setPalette(*dark_palette);  //установка темной темы
        ui->main_tab->setStyleSheet("QPushButton{"
                                    "border-color: navy;"
                                    "background-color: rgb(52, 125, 57);"
                                    "color: rgb(255, 255, 255);}"

                                    "QPushButton : disabled {"
                                    "border : none;"
                                    "background-color : rgb(12,75, 7);"
                                    "color: black;}"

                                    "QPushButton : hover {color: rgb(255, 255, 255);"
                                    "background-color: rgb(92, 165, 97);}"

                                    "QPushButton : pressed{"
                                    "color: rgb(255, 255, 255);"
                                    "background-color: rgb(22, 95, 27);}"
                                    );

        ui->groupBox_2->setStyleSheet("color : rgb(255, 255, 255)");

        QPalette *graphics_palette = new QPalette;
        graphics_palette->setColor(QPalette::ToolTipText,Qt::black);
        graphics_palette->setColor(QPalette::ButtonText, Qt::black);
        graphics_palette->setColor(QPalette::WindowText,Qt::black);
        graphics_palette->setColor(QPalette::Text,Qt::black);
        ui->graphicsView->setPalette(*graphics_palette);
    }else{
        qApp->setPalette(style()->standardPalette());
        ui->main_tab->setStyleSheet("");
        ui->device_list->setStyleSheet("");
        ui->groupBox_2->setStyleSheet("color : rgb(0,0,0)");
        QPalette *graphics_palette = new QPalette;
        graphics_palette->setColor(QPalette::ToolTipText,Qt::black);
        graphics_palette->setColor(QPalette::ButtonText, Qt::black);
        graphics_palette->setColor(QPalette::WindowText,Qt::black);
        graphics_palette->setColor(QPalette::Text,Qt::black);
        ui->graphicsView->setPalette(*graphics_palette);

    }
    save_last_config();
}

//Код, который добавил Suvorov
//! Выделение строки в device_list из предыдущего сеанса
void MainWindow::selectRowInDeviceList(int row)
{
    ui->device_list->setCurrentRow(row);
}
