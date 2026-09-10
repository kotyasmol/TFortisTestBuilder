#ifndef RPS_TEST_H
#define RPS_TEST_H

#include "configmodel.h"
#include "modbus_dev.h"
#include "settingsmodel.h"
#include <QRunnable>
#include <QObject>
#include <QMap>

enum RpsStandLoad
{
    noLoad=0,
    load16ohm,
    load68ohm,
    load22ohm
};

enum RpsStandStage
{
    None = 0,
    AKB1Polarity,
    AKB2Polarity,
    BtnColdStart,
    BtnStop,
    LedCPU,
    LedBat,
    Led52V,
    LedAlarm,
    LedNorm,
    BtnPower,
    BtnRKN,
    PreheatingJumper
};

enum RpsStandPolarity
{
    Normal = 0,
    Inverse = 1
};


enum RpsStandPhase
{
    Preheating = 0,
    RKN,
    Selftest,
    Charging
};

#define RPS_RLOAD_MIN 10
#define RPS_RLOAD_MAX 267

#endif // RPS_TEST_H
