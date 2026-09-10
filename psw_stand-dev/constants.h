#ifndef CONSTANTS_H
#define CONSTANTS_H

const int PORT_NUM             = 16;
const int USERS_NUM            = 10; //максимальное число пользователей
const int MAX_DEVICES          = 100;
const int NUM_POE_LINES        = 16;  //число входов poe
const int TLP_INPUTS_NUM       = 10;
const int TLP_OUTPUTS_NUM      = 10;

const int MODBUS_TABLE_SIZE    = 30;//максимальный размер таблицы

const int MODBUS_SEARCH_TIME   = 30000;//время поиска плат в стенде

const int MODBUS_REPEAT_CNT    = 5;//колличество попыток записи переменной

//!Таймаут ожидания перехода на АКБ
const int TIMEOUT_GETTING_UPS_REZ = 12000;
//!Таймаут ожидания парсинга тестовой станицы
const int TIMEOUT_PARSING_TEST_PAGE = 12000;

//!Напряжение на АКБ при зарядке для  определения типа типа SIMBAT
const int SIMBAT_GET_TYPE_VOLTAGE = 40000;

//! Результат запроса тестовой страницы
enum class TestPageRequestResult
{
    SUCCESS = true,
    FAILURE = false
};

// Уровни сообщений для syslog
enum ErrorLevels
{
    D = 0, // debug
    E = 3, // error
    I = 6, // info
    W = 4, // warning
    C = 10, // comment
    S = 12  //subtest start/end
};

enum StandType
{
    typeOld = 0,
    typeAPK03,
    typeAPK02,
    typeRPS,
    typeRPSNew
};

enum SimbatType
{
    NotConnected,
    SIMBAT24,
    SIMBAT48
};

enum AvrProgTypes
{
    ATMELICE = 0,
    AVRMK2   = 1,
    AS4      = 2
};

enum TestErrors
{
    //коды ошибок тестирования
    ERROR_NONE                      =   0,      //без ошибок
    ERROR_INPUT1                    =   1,      //ошибка входа
    ERROR_INPUT2                    =   2,
    ERROR_INPUT3                    =   3,
    ERROR_INPUT4                    =   4,
    ERROR_INPUT5                    =   5,
    ERROR_INPUT6                    =   6,
    ERROR_INPUT7                    =   7,
    ERROR_INPUT8                    =   8,
    ERROR_INPUT9                    =   9,
    ERROR_INPUT10                   =   10,
    ERROR_OUTPUT1                   =   11,     //ошибка выхода
    ERROR_OUTPUT2                   =   12,
    ERROR_OUTPUT3                   =   13,
    ERROR_OUTPUT4                   =   14,
    ERROR_OUTPUT5                   =   15,
    ERROR_OUTPUT6                   =   16,
    ERROR_OUTPUT7                   =   17,
    ERROR_OUTPUT8                   =   18,
    ERROR_OUTPUT9                   =   19,
    ERROR_OUTPUT10                  =   20,
    ERROR_POE1                      =   21,     //ошибка тестирования POE
    ERROR_POE2                      =   22,
    ERROR_POE3                      =   23,
    ERROR_POE4                      =   24,
    ERROR_POE5                      =   25,
    ERROR_POE6                      =   26,
    ERROR_POE7                      =   27,
    ERROR_POE8                      =   28,
    ERROR_POE9                      =   29,
    ERROR_POE10                     =   30,
    ERROR_POE11                     =   31,
    ERROR_POE12                     =   32,
    ERROR_POE13                     =   33,
    ERROR_POE14                     =   34,
    ERROR_POE15                     =   35,
    ERROR_POE16                     =   36,
    ERROR_DATATEST1                 =   37,     //ошибка передачи данных
    ERROR_DATATEST2                 =   38,
    ERROR_DATATEST3                 =   39,
    ERROR_DATATEST4                 =   40,
    ERROR_DATATEST5                 =   41,
    ERROR_DATATEST6                 =   42,
    ERROR_DATATEST7                 =   43,
    ERROR_DATATEST8                 =   44,
    ERROR_DATATEST9                 =   45,
    ERROR_DATATEST10                =   46,
    ERROR_DATATEST11                =   47,
    ERROR_DATATEST12                =   48,
    ERROR_DATATEST13                =   49,
    ERROR_DATATEST14                =   50,
    ERROR_DATATEST15                =   51,
    ERROR_DATATEST16                =   52,
    ERROR_DATATEST_CONFIGURATION    =   53,     //неверная конфигурация
    ERROR_RS485                     =   54,     //ошибка тестирования RS485
    ERROR_GET_TEST_PAGE             =   55,     //ошибка получения тестовой страницы
    ERROR_EMBEDTEST                 =   56,     //ошибка прохождения самотестирования
    ERROR_DOWNLOAD_UPDATE           =   57,     //ошибка загрузки обновления
    ERROR_MAC                       =   58,     //не удалось получить ответ от сервера и устновить МАС-адрес
    ERROR_UPS                       =   59,     //ошибка UPS-теста
    ERROR_LOADHELP                  =   60,     //ошибка загрузки файлов справки
    ERROR_PRINTER                   =   61,     //ошибка принтера
    ERROR_GETTING_SERIAL            =   62,     //ошибка получения предварительного серийного номера
    ERROR_STOP                      =   63,     //принудительная остановка теста
    ERROR_BUILDIN_SERIAL            =   64,     //ошика получения серийного номера для ремонта
    ERROR_PARSE_TEST_PAGE           =   65,     //ошибка парсинга тестовой страницы
    ERROR_BAD_VERSIONS_UPDATING     =   66,     //не совпадает версия ПО
    ERROR_DOWNLOAD_TEST_UPDATING    =   67,     //не удалось загрузить 192.168.0.1/test.shtml
    ERROR_REPORT                    =   68,     //ошибка передачи отчета на сервер
    HEATING_ERROR                   =   69,     //ошибка  проверки нагревателя
    ERROR_I2C                       =   70      //ошибка проверки I2C/
};

//для отображения в колонке результата

enum ColorsWidget
{
    NO_ACTIVE = 0,
    GREY      = 1,
    GREEN     = 2,
    RED       = 3,
};

enum ResultTest
{
    OK   = 1,
    FAIL = 0
};

#endif // CONSTANTS_H
