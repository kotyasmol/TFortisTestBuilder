#ifndef FORM_H
#define FORM_H

#include <QWidget>
#include "TestThread.h"
#include <QGraphicsScene>
#include <QGraphicsTextItem>

namespace Ui {
class Form;
}

class Form : public QWidget
{
    Q_OBJECT

public:
    explicit Form(configmodel test_config, settingsmodel prog_sett,QWidget *parent = 0);
    ~Form();

private:
    Ui::Form *ui;
    QGraphicsScene *scene;
    QGraphicsTextItem *text;
};

#endif // FORM_H
