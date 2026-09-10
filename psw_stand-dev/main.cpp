#include "mainwindow.h"
//#include "devicebase.h"
#include <QApplication>
#include <QtGui>
#include <QTextCodec>
#include <QStyleFactory>
///
class Application final : public QApplication {
public:
    Application(int& argc, char** argv) : QApplication(argc, argv) {}
    virtual bool notify(QObject *receiver, QEvent *event) override {
        try
        {
            return QApplication::notify(receiver, event);
        }
        catch (std::exception &e)
        {

            FILE *f = fopen("crash_report.txt", "a");
            if (f != nullptr)
            {
                fprintf(f, "Error %s sending event %s to object %s (%s)",
                        e.what(), typeid(*event).name(), qPrintable(receiver->objectName()),
                        typeid(*receiver).name());
                fclose(f);
            }

            qFatal("Error %s sending event %s to object %s (%s)",
                   e.what(), typeid(*event).name(), qPrintable(receiver->objectName()),
                   typeid(*receiver).name());
        }
        catch (...)
        {
            FILE *f = fopen("crash_report.txt", "a");
            if (f != nullptr)
            {
                fprintf(f, "Error <unknown> sending event %s to object %s (%s)",
                        typeid(*event).name(), qPrintable(receiver->objectName()),
                        typeid(*receiver).name());
                fclose(f);
            }

            qFatal("Error <unknown> sending event %s to object %s (%s)",
                   typeid(*event).name(), qPrintable(receiver->objectName()),
                   typeid(*receiver).name());
        }

        // qFatal aborts, so this isn't really necessary
        // but you might continue if you use a different logging lib
        return false;
    }
};

int main(int argc, char *argv[])
{	    
    QTextCodec::setCodecForLocale(QTextCodec::codecForName("UTF-8"));

    QTextStream outStream(stdout);
    outStream.setCodec(QTextCodec::codecForName("cp866"));
    outStream << QString("Русский текст в консоли") << flush;
    Application a(argc, argv);
    a.addLibraryPath(a.applicationDirPath()+"/plugins");
    qApp->setStyle(QStyleFactory::create("Fusion"));
    MainWindow w(0,argc,argv);
    w.show();
    return a.exec();
}
