#include "mainwindow.h"
#include "ui_mainwindow.h"
#include <QTextCodec>
#include <iostream>
#include "debugwindow.h"
#include "ui_debugwindow.h"
//логирование событий
void MainWindow::syslog(QString text, int level)
{
    QTextStream out(stdout);
    out.setCodec("CP1251");
    out << text << "\n";

    QString color = "#000000";
    // Устанавливаем цвет строки
    if(!prog_sett.last_theme)
    {
        switch(level)
        {
        case E:  color = "#ffffff"; break; //белый
        case I:  color = "#b4b4b4"; break; //серый
        case W:  color = "#ffffff"; break; //белый
        case D:  color = "#b4b4b4"; break; //серый
        case C:  color = "#b4b4b4"; break; //серый
        case S:  color = "#ffffff"; break; //белый
        default: color = "#b4b4b4"; break; //серый
        }
    }else{
        switch(level)
        {
        case E: color = "#ff0000"; break;
        case I:  color = "#000000"; break;
        case W:  color = "#ff0000"; break;
        case D:  color = "#000000"; break;
        case C:  color = "#0000CD"; break;
        case S:  color = "#000000"; break;

        default: color = "#000000"; break;

        }
    }

    if((level != D))
    {
        QDateTime dateTimeNow = QDateTime::currentDateTime();
        QString lineForWriteToWindow;
        QString lineForWriteToFile = QString(tr("%1 %2\n"))
                .arg(dateTimeNow.toString("dd.MM.yyyy HH:mm:ss"))
                .arg(text);
        if(level != S)
        {
            lineForWriteToWindow = QString(tr("<font color=\"%1\">%2 %3</font>"))
                    .arg(color)
                    .arg(dateTimeNow.toString("dd.MM.yyyy HH:mm:ss"))
                    .arg(text);
        }else
        {

            lineForWriteToWindow = QString(tr("<b><font color=\"%1\">%2 %3"))
                    .arg(color)
                    .arg(dateTimeNow.toString("dd.MM.yyyy HH:mm:ss"))
                    .arg(text);
            lineForWriteToWindow.append("</font></b>");
        }


        writeLineToLogFile(lineForWriteToFile);  // Записываем строку в лог-файл
        writeLineToWindow(lineForWriteToWindow); // Записываем строку в окно вывода лога
        debug_window->debuglog(text);
    }
}

void DebugWindow::debuglog(QString text)
{
    QTextStream out(stdout);
    out.setCodec("CP1251");
    out << text << "\n";

    QDateTime dateTimeNow = QDateTime::currentDateTime();
    QString lineForWriteToWindow;

    lineForWriteToWindow = QString(tr("%1 %2"))
            .arg(dateTimeNow.toString("dd.MM.yyyy HH:mm:ss"))
            .arg(text);

    ui->debug_output->append(lineForWriteToWindow);
    ui->debug_output->setMouseTracking(true);

    QScrollBar* sb = ui->debug_output->verticalScrollBar();
    sb->setValue(sb->maximum());
}

void MainWindow::debuglog(QString text)
{
    debug_window->debuglog(text);
}

void MainWindow::writeLineToLogFile(QString line)
{
    QFile file("log.txt");
    if (file.open(QIODevice::Append | QIODevice::Text))
    {
        file.write(line.toUtf8());
    }
    file.close();
}

void MainWindow::writeLineToWindow(QString line)
{
    ui->output_line->append(line);
    ui->output_line->setMouseTracking(true);

    QScrollBar* sb = ui->output_line->verticalScrollBar();
    sb->setValue(sb->maximum());
}

void MainWindow::exit_app(void){
    myDebug() << "exit_app";
    close();
}

void MainWindow::errorString(QString text){
    syslog(text,E);
}


