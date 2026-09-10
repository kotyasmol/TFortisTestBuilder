#ifndef LABELTHREAD_H
#define LABELTHREAD_H

#include <QObject>
#include "TestThread.h"

class LabelThread :  public QThread{
    Q_OBJECT

public:
    LabelThread(QObject *parent = 0);
    virtual void run();
    void stop();
    void init();
    void set_prog_sett(settingsmodel sett);
    ~LabelThread();

    int label_num;
    int status;
    long id;
    int print_status;

signals:
    void test_finished(int errorcode);
    void get_next_ident();
    void print_id_label(int id);
    void show_label_print_rezult(QString str);


public slots:

private:
    struct settingsmodel prog_sett;

};

#endif // LABELTHREAD_H
