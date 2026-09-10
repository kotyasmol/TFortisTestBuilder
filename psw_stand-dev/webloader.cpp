// WebLoader.cpp
#include "webloader.h"
#include <QNetworkRequest>
#include <QCoreApplication>
#include <QThread>
#include <QDebug>

WebLoader::WebLoader(QObject *parent) : QObject(parent)
{
    m_manager = new QNetworkAccessManager(this);
    m_loop = new QEventLoop(this);
    connect(m_manager, &QNetworkAccessManager::finished, this, &WebLoader::onReplyFinished);
}

bool WebLoader::loadPageAndWait(const QString &url, int waitSeconds, QString &resultContent)
{
    QNetworkRequest request;
    request.setUrl(QUrl(url));

    // Устанавливаем заголовки как у обычного браузера
    request.setRawHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
    request.setRawHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
    request.setRawHeader("Accept-Language", "en-US,en;q=0.5");
    request.setRawHeader("Connection", "keep-alive");
    request.setRawHeader("Upgrade-Insecure-Requests", "1");

    m_receivedContent.clear();
    m_reply = m_manager->get(request);

    // Ждем завершения запроса
    m_loop->exec();

    if (m_receivedContent.isEmpty()) {
        m_lastError = "No content received";
        return false;
    }

    // Ждем указанное время после получения ответа
    if (waitSeconds > 0) {
        QThread::sleep(waitSeconds);
    }

    resultContent = m_receivedContent;
    return true;
}

void WebLoader::onReplyFinished(QNetworkReply *reply)
{
    if (reply->error() == QNetworkReply::NoError) {
        m_receivedContent = QString::fromUtf8(reply->readAll());
    } else {
        m_lastError = reply->errorString();
        qWarning() << "Network error:" << m_lastError;
    }

    reply->deleteLater();
    m_loop->quit();
}

bool WebLoader::saveToFile(const QString &content, const QString &filename)
{
    QFile file(filename);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        m_lastError = "Cannot open file for writing: " + filename;
        return false;
    }

    QTextStream out(&file);
    out << content;
    file.close();

    return true;
}

bool WebLoader::containsText(const QString &content, const QString &text)
{
    return content.contains(text);
}

QString WebLoader::getContext(const QString &content, const QString &text, int contextChars)
{
    int index = content.indexOf(text);
    if (index == -1) {
        return QString();
    }

    int start = qMax(0, index - contextChars);
    int length = qMin(2 * contextChars + text.length(), content.length() - start);

    return content.mid(start, length);
}

QString WebLoader::getLastError() const
{
    return m_lastError;
}
