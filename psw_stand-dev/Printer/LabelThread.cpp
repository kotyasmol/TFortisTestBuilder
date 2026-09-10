#include <QDebug>
#include "LabelThread.h"

//печать этикеток-идентификаторов


LabelThread::LabelThread(QObject *parent) :  QThread(parent){
    moveToThread(this);
}

void LabelThread::init(){

}

void LabelThread::run(){
    QString str;
    int cnt;

    qDebug() << "label_num" <<  label_num;
    show_label_print_rezult("");

    for(int i=0;i<label_num;i++){
        status = 0;
        cnt = 0;
        //get ID
        get_next_ident();
        while((status == 0)&&(cnt < 500)){
            Sleep(10);
            cnt++;
        }
        if(status){
            //ok
        }else{
            show_label_print_rezult("Error.Get ID");
        }

        //print label
        print_status = 0;
        cnt = 0;
        print_id_label(id);
        while((print_status == 0)&&(cnt < 500)){
            Sleep(10);
            cnt++;
        }

        if(print_status){
            str.sprintf("Ok. %d",i);
            show_label_print_rezult(str);
        }
        else{
            show_label_print_rezult("Error.Printer");
        }

    }
}

void LabelThread::set_prog_sett(settingsmodel sett){
    prog_sett = sett;
}

LabelThread::~LabelThread()
{

}
