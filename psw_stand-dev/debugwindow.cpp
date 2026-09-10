#include "debugwindow.h"
#include "ui_debugwindow.h"
#include "TestThread.h"

DebugWindow::DebugWindow(QWidget *parent) :
    QWidget(parent),
    ui(new Ui::DebugWindow)
{
    ui->setupUi(this);
}

void DebugWindow::debug_set_ac1_state(bool state)
{
    if(state)
    {
        ui->ac1_state->setText("ON");
        ui->ac1_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->ac1_state->setText("OFF");
        ui->ac1_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_ac2_state(bool state)
{
    if(state)
    {
        ui->ac2_state->setText("ON");
        ui->ac2_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->ac2_state->setText("OFF");
        ui->ac2_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_sensor1_state(bool state)
{
    if(state)
    {
        ui->sensor1_state->setText("ON");
        ui->sensor1_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->sensor1_state->setText("OFF");
        ui->sensor1_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_sensor2_state(bool state)
{
    if(state)
    {
        ui->sensor2_state->setText("ON");
        ui->sensor2_state->setStyleSheet("color: rgb(0, 170, 0);");

    }
    else
    {
        ui->sensor2_state->setText("OFF");
        ui->sensor2_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_simbat24_charge_state(bool state)
{
    if(state)
    {
        ui->simbat24_charge_state->setText("ON");
        ui->simbat24_charge_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
        else
    {
        ui->simbat24_charge_state->setText("OFF");
        ui->simbat24_charge_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_simbat24_discharge_state(bool state)
{
    if(state)
    {
        ui->simbat24_discharge_state->setText("ON");
        ui->simbat24_discharge_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->simbat24_discharge_state->setText("OFF");
        ui->simbat24_discharge_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_simbat48_charge_state(bool state)
{
    if(state)
    {
        ui->simbat48_charge_state->setText("ON");
        ui->simbat48_charge_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->simbat48_charge_state->setText("OFF");
        ui->simbat48_charge_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_simbat48_discharge_state(bool state)
{
    if(state)
    {
        ui->simbat48_discharge_state->setText("ON");
        ui->simbat48_discharge_state->setStyleSheet("color: rgb(0, 170, 0);");
        //debuglog("Включение Simbat48");
    }
    else
    {
        ui->simbat48_discharge_state->setText("OFF");
        ui->simbat48_discharge_state->setStyleSheet("color: rgb(200, 0, 0);");
        //debuglog("Отключение Simbat48");

    }
}

void DebugWindow::debug_set_heater1_state(bool state)
{
    if(state)
    {
        ui->heater1_state->setText("ON");
        ui->heater1_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->heater1_state->setText("OFF");
        ui->heater1_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_heater2_state(bool state)
{
    if(state)
    {
        ui->heater2_state->setText("ON");
        ui->heater2_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->heater2_state->setText("OFF");
        ui->heater2_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_io02_out1_state(bool state)
{
    if(state)
    {
        ui->io02_out1_state->setText("ON");
        ui->io02_out1_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->io02_out1_state->setText("OFF");
        ui->io02_out1_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_io02_out2_state(bool state)
{
    if(state)
    {
        ui->io02_out2_state->setText("ON");
        ui->io02_out2_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->io02_out2_state->setText("OFF");
        ui->io02_out2_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_io02_in1_state(bool state)
{
    if(state)
    {
        ui->io02_in1_state->setText("ON");
        ui->io02_in1_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->io02_in1_state->setText("OFF");
        ui->io02_in1_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_rs485_state(bool state)
{
    if(state)
    {
        ui->rs485_state->setText("ON");
        ui->rs485_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->rs485_state->setText("OFF");
        ui->rs485_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void DebugWindow::debug_set_i2c_state(bool state)
{
    if(state)
    {
        ui->i2c_state->setText("ON");
        ui->i2c_state->setStyleSheet("color: rgb(0, 170, 0);");
    }
    else
    {
        ui->i2c_state->setText("OFF");
        ui->i2c_state->setStyleSheet("color: rgb(200, 0, 0);");
    }
}

void TestThread::debug_set_ac1_state(bool state)
{
    debug_window->debug_set_ac1_state(state);
}

void TestThread::debug_set_ac2_state(bool state)
{
    debug_window->debug_set_ac2_state(state);
}

void TestThread::debug_set_heater1_state(bool state)
{
    debug_window->debug_set_heater1_state(state);
}

void TestThread::debug_set_heater2_state(bool state)
{
    debug_window->debug_set_heater1_state(state);
}

void TestThread::debug_set_i2c_state(bool state)
{
    debug_window->debug_set_i2c_state(state);
}

void TestThread::debug_set_io02_in1_state(bool state)
{
    debug_window->debug_set_io02_in1_state(state);
}

void TestThread::debug_set_io02_out1_state(bool state)
{
    debug_window->debug_set_io02_out1_state(state);
}

void TestThread::debug_set_io02_out2_state(bool state)
{
    debug_window->debug_set_io02_out2_state(state);
}

void TestThread::debug_set_rs485_state(bool state)
{
    debug_window->debug_set_rs485_state(state);
}

void TestThread::debug_set_sensor1_state(bool state)
{
    debug_window->debug_set_sensor1_state(state);
}

void TestThread::debug_set_sensor2_state(bool state)
{
    debug_window->debug_set_sensor2_state(state);
}

void TestThread::debug_set_simbat24_charge_state(bool state)
{
    debug_window->debug_set_simbat24_charge_state(state);
}

void TestThread::debug_set_simbat24_discharge_state(bool state)
{
    debug_window->debug_set_simbat24_discharge_state(state);
}

void TestThread::debug_set_simbat48_charge_state(bool state)
{
    debug_window->debug_set_simbat48_charge_state(state);
}

void TestThread::debug_set_simbat48_discharge_state(bool state)
{
    debug_window->debug_set_simbat48_discharge_state(state);
}

void TestThread::debuglog(QString text)
{
    debug_window->debuglog(text);
}

DebugWindow::~DebugWindow()
{
    delete ui;
}
