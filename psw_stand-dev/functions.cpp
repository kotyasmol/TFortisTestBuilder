#include "functions.h"
#include "modbus_dev.h"

#include <QTextCodec>

QString Functions::BytesFromCP866toUnicode(QByteArray bytes)
{
    return QTextCodec::codecForName("CP866")->toUnicode(bytes);
}

QString Functions::convert_mb_dev_type(int type){
    switch(type){
    case DEV_EL60:   return "EL-60";
    case DEV_PS1:    return "PS-1";
    case DEV_PS2:    return "PS-2";
    case DEV_PS3:    return "PS-3";
    case DEV_EL60V5: return "EL-60V5";
    case DEV_IO02:   return "IO-02";
    case DEV_SIMBAT24: return "SB24";
    case DEV_SIMBAT48: return "SB48";
    default:         return "Unknown";
    }
}

//<------ в 25 строчке ошибка Комментарий от Васи 17.02.2023
