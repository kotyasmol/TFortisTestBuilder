#-------------------------------------------------
#
# Project created by QtCreator 2014-01-05T21:10:56
#
#-------------------------------------------------

CONFIG += c++11


DEPENDPATH += .
INCLUDEPATH += ../..
QMAKE_LIBDIR +=  ./build/

#У Саши было так:
#INCLUDEPATH += "./pcap/Include"
#INCLUDEPATH += "./pcap/Lib"
#LIBS += -L"./pcap/Lib"

#Я сделал так:
win32: LIBS += -L$$PWD/Libs/pcap/Lib/ -lwpcap
LIBS += -L$$PWD/Libs/pcap/Lib/x64/ -lwpcap
INCLUDEPATH += $$PWD/Libs/pcap/Include
INCLUDEPATH += C:/Program Files/OpenSSL-Win64/include

DEPENDPATH += $$PWD/Libs/pcap/Include

include(qttelnet-2.1/qttelnet.pri)

LIBS +=   libwsock32
LIBS +=  -lwinspool
#LIBS +=  -lwpcap

QT       += core
QT       += gui
QT       += network
QT       += xml
QT       += sql
QT       += widgets
QT       += printsupport
QT       += serialport
QT       += serialbus


#greaterThan(QT_MAJOR_VERSION, 4): QT += widgets

TARGET = tfortis_stand
TEMPLATE = app


SOURCES += main.cpp\
        debugwindow.cpp \
        dfumultiloadthread.cpp \
        mainwindow.cpp \
        prog.cpp \
        settings.cpp \
        log.cpp \
        mac_sender.cpp \
        updating.cpp \
        TestThread.cpp \
        teleport.cpp \
        test.cpp \
        telnet.cpp \
        serial_port.cpp \
        modbus_dev.cpp \
        Dialogs/devicelistdialog.cpp \
        Dialogs/input_dialogs.cpp \
        Dialogs/connect_diagram.cpp \
        functions.cpp \
        rps_test.cpp \
        test_ups.cpp \
        DataTest/BercutThread.cpp \
        DataTest/DataTestThread.cpp \
        DataTest/generator.cpp \
        ui.cpp \
        Printer/LabelThread.cpp \
        Printer/printer.cpp \
        Printer/reportprinter.cpp \
        Network/net_database.cpp \
        Network/network.cpp \
        stand_modbus.cpp \
        webloader.cpp

HEADERS += mainwindow.h \
        TestThread.h \
        debugwindow.h \
        dfumultiloadthread.h \
        modbus_dev.h \
        prog.h \
        telnet.h \
        Dialogs/devicelistdialog.h \
        Dialogs/connect_diagram.h \
        configmodel.h \
        functions.h \
        settingsmodel.h \
        constants.h \
        rps_test.h \
        ui.h \
        DataTest/BercutThread.h \
        DataTest/DataTestThread.h \
        configmodel.h \
        constants.h \
        Printer/LabelThread.h \
        Printer/reportprinter.h \
        webloader.h

FORMS   += mainwindow.ui \
        debugwindow.ui \
        form.ui \
        reportprinter.ui \
        Dialogs/devicelistdialog.ui \
        dialog.ui


win32 {
    RC_FILE += file.rc
    OTHER_FILES += file.rc
    LIBS += -lsetupapi -luuid -ladvapi32
}


OTHER_FILES +=

DISTFILES += \
    README.md \
    set_mac_pro.bat

RESOURCES += \
    resource.qrc \



