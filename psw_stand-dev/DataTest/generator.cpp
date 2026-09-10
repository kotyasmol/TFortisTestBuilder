#include "mainwindow.h"
#include "ui_mainwindow.h"
#include "DataTestThread.h"

/************START**************************/

void MainWindow::start_generation_tools(){
    struct ports_pair_t ports_pair[PORT_NUM];
    int ports[PORT_NUM];
    int j;

    if(ui->gen_sw->isChecked()){

        syslog("Запуск программной генерации трафика",I);

        //clear all flags
        for(int i=0;i<PORT_NUM;i++){
            ports_pair[i].in_valid = ports_pair[i].out_valid = 0;
            ports[i]=0;
        }

        ui->gen_result->clear();

        /* Retrieve the device list */
        gen_timeout.start(5000);//
        //syslog("Generation Start",I);
        capture_result.transmitted_pkts = 0;
        capture_result.recieved_bytes=0;
        capture_result.recieved_pkts=0;
        capture_result.tiemout_pkts=0;

        get_checkbox_state(ports,TYPE_SOFT_GEN);

        //составляем пары теста
        for(int i=0;i<PORT_NUM;i++){
            ports_pair[i].in_valid=0;
            ports_pair[i].out_valid=0;
            qDebug() << i << ports[i];
        }

        j = 0;

        for(int i=0;i<PORT_NUM;i++){
            if(ports[i]){
                if(ports_pair[j].in_valid==0){
                    ports_pair[j].in_ip = prog_sett.card_ip[i];
                    ports_pair[j].in_valid = 1;
                }
                else if(ports_pair[j].out_valid==0){
                    ports_pair[j].out_ip = prog_sett.card_ip[i];
                    ports_pair[j].out_valid = 1;
                    j++;
                }
            }
        }
        //если нечетное количество
        if((ports_pair[j].in_valid == 1)&&(ports_pair[j].out_valid == 0)){
            ports_pair[j].out_ip = prog_sett.card_ip[0];//работаем с 1-й картой
            ports_pair[j].out_valid = 1;
        }

        //для тестирования минимум 1 пара
        if(j<1){
            syslog("Число выбранных портов должно быть >= 2",E);
            ui->gen_result->setText("<font color=\"red\">Fail</font>");
            return;
        }

        pTestThread->set_ports(ports_pair);
        pTestThread->set_type(TYPE_SOFT_GEN);//тип генератора
        pTestThread->data_start();
        ui->gen_start->setDisabled(true);
        ui->gen_stop->setDisabled(false);
    }

    if(ui->gen_hw->isChecked()){
        if(telnet_dev->is_connected()){
            syslog("Запуск аппаратной генерации трафика",I);

            //clear all flags
            for(int i=0;i<PORT_NUM;i++)
                ports_pair[i].in_valid = ports_pair[i].out_valid = 0;

            ui->gen_result->clear();

            /* Retrieve the device list */
            gen_timeout.start(5000);//
            capture_result.transmitted_pkts = 0;
            capture_result.recieved_bytes=0;
            capture_result.recieved_pkts=0;
            capture_result.tiemout_pkts=0;

            get_checkbox_state(ports,TYPE_HARD_GEN);

            //составляем пары теста
            for(int i=0;i<PORT_NUM;i++){
                ports_pair[i].in_valid=0;
                ports_pair[i].out_valid=0;
            }
            j = 0;
            for(int i=0;i<PORT_NUM;i++){
                if(ports[i]){
                    if(ports_pair[j].in_valid==0){
                        ports_pair[j].in = i;
                        ports_pair[j].in_valid = 1;
                    }
                    else if(ports_pair[j].in_valid==1 && ports_pair[j].out_valid==0){
                        ports_pair[j].out = i;
                        ports_pair[j].out_valid = 1;
                        j++;
                    }
                }
            }

            //если нечетное количество
            if((ports_pair[j].in_valid == 1)&&(ports_pair[j].out_valid == 0)){
                if(ports_pair[j].in != ports_pair[j-1].in)
                    ports_pair[j].out = ports_pair[j-1].in;
                else
                    ports_pair[j].out = ports_pair[j-1].out;
                ports_pair[j].out_valid = 1;
            }

            //для тестирования минимум 1 пара
            if(j<1){
                qDebug() << "Число выбранных портов должно быть >= 2";
                syslog("Число выбранных портов должно быть >= 2",E);
                ui->gen_result->setText("<font color=\"red\">Fail</font>");
                return;
            }

            for(int i=0;i<PORT_NUM;i++){
                qDebug() << "port " << i << ports[i];
            }

            pTestThread->set_ports(ports_pair);
            pTestThread->set_type(TYPE_HARD_GEN);//тип генератора
            pTestThread->data_start();
            ui->gen_start->setDisabled(true);
            ui->gen_stop->setDisabled(false);
        }
        else{
            syslog("Нет подключения по Telnet к технологическому коммутатору",E);
        }
    }
}

/*******************STOP**************************/
void MainWindow::stop_generation_tools(){
    struct ports_pair_t ports_pair[PORT_NUM];
    QString str;

    if(pTestThread->is_running()){
        gen_timeout.start(100);
        return;
    }

    gen_timeout.stop();

    ui->gen_start->setDisabled(false);
    ui->gen_stop->setDisabled(true);

    if(ui->gen_sw->isChecked()){

        pTestThread->get_ports(ports_pair);

        pTestThread->data_stop();

        for(int i=0;i<STAND_PORT_NUM;i++){
            if(ports_pair[i].in_valid && ports_pair[i].out_valid){
                str.clear();
                str.append("Передача данных: ");
                str.append(ports_pair[i].in_ip);
                str.append("->");
                str.append(ports_pair[i].out_ip);

                capture_result.recieved_pkts = pTestThread->get_recieved_pkt(i);
                capture_result.transmitted_pkts = pTestThread->get_transmitted_pkt(i);
                capture_result.speed = pTestThread->get_recieved_speed(i);
                qDebug() << "timeout pkts" << capture_result.tiemout_pkts;

                if(capture_result.recieved_pkts>=(CAPTURE_LEN-capture_result.tiemout_pkts)){
                    str.append(" Успешно");
                    syslog(str,C);

                    str.sprintf("<font color=\"green\">Ok (%lu)</font> %lu Kbps",capture_result.recieved_pkts,capture_result.speed*8);
                    ui->gen_result->setText(str);
                }
                else{
                    str.append(" не успешно");
                    syslog(str,E);

                    str.sprintf("<font color=\"red\">Fail (%lu)</font>",capture_result.recieved_pkts);
                    ui->gen_result->setText(str);
                    break;
                }
            }
        }
    }
    else{
        pTestThread->get_ports(ports_pair);
        for(int i=0;i<STAND_PORT_NUM;i++){
            if(ports_pair[i].in_valid && ports_pair[i].out_valid){
                if(pTestThread->pDataTestThread->get_ports_status(i)==TEST_OK){
                    str.sprintf("Передача данных с порта %d на %d - успешно",ports_pair[i].in+1, ports_pair[i].out+1);
                    syslog(str,C);
                }
                else{
                    str.sprintf("Передача данных с порта %d на %d - не успешно",ports_pair[i].in+1, ports_pair[i].out+1);
                    syslog(str,E);
                }
            }
        }
    }
}

//установка пресета
void MainWindow::set_generation_preset(){
    open_config_name(&test_config,prog_sett.config_dir[ui->device_list->currentIndex().row()]);
    if(ui->gen_sw->isChecked())
        set_checkbox_state(test_config.poe_line_test,TYPE_SOFT_GEN);
    if(ui->gen_hw->isChecked()){
        telnet_config_sw(test_config.data_test_ports,prog_sett.port_dut);
    }
}

void MainWindow::generator_print_msg(QString str){
    syslog(str,I);
}

//возвращает массив состояния чекбоксов
void MainWindow::get_checkbox_state(int *ports, int type){
    if(type == TYPE_SOFT_GEN){
        //программная генерация трафика
        if(ui->p1_gen_cb->isChecked())
            ports[0] = 1;
        else
            ports[0] = 0;

        if(ui->p2_gen_cb->isChecked())
            ports[1] = 1;
        else
            ports[1] = 0;

        if(ui->p3_gen_cb->isChecked())
            ports[2] = 1;
        else
            ports[2] = 0;

        if(ui->p4_gen_cb->isChecked())
            ports[3] = 1;
        else
            ports[3] = 0;

        if(ui->p5_gen_cb->isChecked())
            ports[4] = 1;
        else
            ports[4] = 0;

        if(ui->p6_gen_cb->isChecked())
            ports[5] = 1;
        else
            ports[5] = 0;

        if(ui->p7_gen_cb->isChecked())
            ports[6] = 1;
        else
            ports[6] = 0;

        if(ui->p8_gen_cb->isChecked())
            ports[7] = 1;
        else
            ports[7] = 0;

        if(ui->p9_gen_cb->isChecked())
            ports[8] = 1;
        else
            ports[8] = 0;

        if(ui->p10_gen_cb->isChecked())
            ports[9] = 1;
        else
            ports[9] = 0;
    }

    if(type == TYPE_HARD_GEN){
        //аппаратная генерация трафика
        if(ui->p1_gen_hw->isChecked())
            ports[0] = 1;
        else
            ports[0] = 0;

        if(ui->p2_gen_hw->isChecked())
            ports[1] = 1;
        else
            ports[1] = 0;

        if(ui->p3_gen_hw->isChecked())
            ports[2] = 1;
        else
            ports[2] = 0;

        if(ui->p4_gen_hw->isChecked())
            ports[3] = 1;
        else
            ports[3] = 0;

        if(ui->p5_gen_hw->isChecked())
            ports[4] = 1;
        else
            ports[4] = 0;

        if(ui->p6_gen_hw->isChecked())
            ports[5] = 1;
        else
            ports[5] = 0;

        if(ui->p7_gen_hw->isChecked())
            ports[6] = 1;
        else
            ports[6] = 0;

        if(ui->p8_gen_hw->isChecked())
            ports[7] = 1;
        else
            ports[7] = 0;

        if(ui->p9_gen_hw->isChecked())
            ports[8] = 1;
        else
            ports[8] = 0;

        if(ui->p10_gen_hw->isChecked())
            ports[9] = 1;
        else
            ports[9] = 0;

        if(ui->p11_gen_hw->isChecked())
            ports[10] = 1;
        else
            ports[10] = 0;

        if(ui->p12_gen_hw->isChecked())
            ports[11] = 1;
        else
            ports[11] = 0;

        if(ui->p13_gen_hw->isChecked())
            ports[12] = 1;
        else
            ports[12] = 0;

        if(ui->p14_gen_hw->isChecked())
            ports[13] = 1;
        else
            ports[13] = 0;

        if(ui->p15_gen_hw->isChecked())
            ports[14] = 1;
        else
            ports[14] = 0;

        if(ui->p16_gen_hw->isChecked())
            ports[15] = 1;
        else
            ports[15] = 0;
    }
}

//возвращает массив состояния чекбоксов
void MainWindow::set_checkbox_state(int *ports, int type){
    if(type == TYPE_SOFT_GEN){
        //программная генерация трафика
        if(ports[0])
            ui->p1_gen_cb->setChecked(true);
        else
            ui->p1_gen_cb->setChecked(false);

        if(ports[1])
            ui->p2_gen_cb->setChecked(true);
        else
            ui->p2_gen_cb->setChecked(false);

        if(ports[2])
            ui->p3_gen_cb->setChecked(true);
        else
            ui->p3_gen_cb->setChecked(false);

        if(ports[3])
            ui->p4_gen_cb->setChecked(true);
        else
            ui->p4_gen_cb->setChecked(false);

        if(ports[4])
            ui->p5_gen_cb->setChecked(true);
        else
            ui->p5_gen_cb->setChecked(false);

        if(ports[5])
            ui->p6_gen_cb->setChecked(true);
        else
            ui->p6_gen_cb->setChecked(false);

        if(ports[6])
            ui->p7_gen_cb->setChecked(true);
        else
            ui->p7_gen_cb->setChecked(false);

        if(ports[7])
            ui->p8_gen_cb->setChecked(true);
        else
            ui->p8_gen_cb->setChecked(false);

        if(ports[8])
            ui->p9_gen_cb->setChecked(true);
        else
            ui->p9_gen_cb->setChecked(false);

        if(ports[9])
            ui->p10_gen_cb->setChecked(true);
        else
            ui->p10_gen_cb->setChecked(false);
    }

    //аппаратная генерация трафика
    if(type == TYPE_HARD_GEN){

        if(ports[0])
            ui->p1_gen_hw->setChecked(true);
        else
            ui->p1_gen_hw->setChecked(false);

        if(ports[1])
            ui->p2_gen_hw->setChecked(true);
        else
            ui->p2_gen_hw->setChecked(false);

        if(ports[2])
            ui->p3_gen_hw->setChecked(true);
        else
            ui->p3_gen_hw->setChecked(false);

        if(ports[3])
            ui->p4_gen_hw->setChecked(true);
        else
            ui->p4_gen_hw->setChecked(false);

        if(ports[4])
            ui->p5_gen_hw->setChecked(true);
        else
            ui->p5_gen_hw->setChecked(false);

        if(ports[5])
            ui->p6_gen_hw->setChecked(true);
        else
            ui->p6_gen_hw->setChecked(false);

        if(ports[6])
            ui->p7_gen_hw->setChecked(true);
        else
            ui->p7_gen_hw->setChecked(false);

        if(ports[7])
            ui->p8_gen_hw->setChecked(true);
        else
            ui->p8_gen_hw->setChecked(false);

        if(ports[8])
            ui->p9_gen_hw->setChecked(true);
        else
            ui->p9_gen_hw->setChecked(false);

        if(ports[9])
            ui->p10_gen_hw->setChecked(true);
        else
            ui->p10_gen_hw->setChecked(false);
    }
}
