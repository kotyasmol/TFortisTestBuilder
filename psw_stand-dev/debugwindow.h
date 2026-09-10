#ifndef DEBUGWINDOW_H
#define DEBUGWINDOW_H

#include <QWidget>

namespace Ui {
class DebugWindow;
}

class DebugWindow : public QWidget
{
    Q_OBJECT

public slots:


public:
    explicit DebugWindow(QWidget *parent = nullptr);

    void debuglog(QString text);
    void debug_set_ac1_state(bool state);
    void debug_set_ac2_state(bool state);
    void debug_set_sensor1_state(bool state);
    void debug_set_sensor2_state(bool state);
    void debug_set_simbat24_charge_state(bool state);
    void debug_set_simbat24_discharge_state(bool state);
    void debug_set_simbat48_charge_state(bool state);
    void debug_set_simbat48_discharge_state(bool state);
    void debug_set_heater1_state(bool state);
    void debug_set_heater2_state(bool state);
    void debug_set_io02_out1_state(bool state);
    void debug_set_io02_out2_state(bool state);
    void debug_set_io02_in1_state(bool state);
    void debug_set_rs485_state(bool state);
    void debug_set_i2c_state(bool state);

    ~DebugWindow();

private:
    Ui::DebugWindow *ui;


};

#endif // DEBUGWINDOW_H
