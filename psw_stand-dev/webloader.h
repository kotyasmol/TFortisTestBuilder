// WebLoader.h
#ifndef WEBLOADER_H
#define WEBLOADER_H

#include <QObject>
#include <QString>
#include <QMap>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QTimer>
#include <QEventLoop>
#include <QFile>

class WebLoader : public QObject
{
    Q_OBJECT
public:
    explicit WebLoader(QObject *parent = nullptr);

    bool loadPageAndWait(const QString &url, int waitSeconds, QString &resultContent);
    bool saveToFile(const QString &content, const QString &filename);
    bool containsText(const QString &content, const QString &text);
    QString getContext(const QString &content, const QString &text, int contextChars = 50);

    QString getLastError() const;

private slots:
    void onReplyFinished(QNetworkReply *reply);

private:
    QNetworkAccessManager *m_manager;
    QString m_lastError;
    QEventLoop *m_loop;
    QNetworkReply *m_reply;
    QString m_receivedContent;
};

#endif // WEBLOADER_H
