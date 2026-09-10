#ifndef FUNCTIONS_H
#define FUNCTIONS_H

#include <QString>
#include "settingsmodel.h"


namespace Functions
{
//! Перевод ByteArray CP866 в строку unicode
QString BytesFromCP866toUnicode(QByteArray bytes);

//конвертирование типа устройства в стенде в строку названия
QString convert_mb_dev_type(int type);
}

#endif // FUNCTIONS_H
