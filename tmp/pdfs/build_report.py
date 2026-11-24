from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
                                PageBreak, KeepTogether, HRFlowable)
from reportlab.lib.colors import HexColor
from xml.sax.saxutils import escape
from pathlib import Path

OUT = Path('output/pdf/ЛР6_разбор_функций_PubSub.pdf')
FONT = Path(r'C:\Windows\Fonts\arial.ttf')
FONT_B = Path(r'C:\Windows\Fonts\arialbd.ttf')
FONT_I = Path(r'C:\Windows\Fonts\ariali.ttf')
MONO = Path(r'C:\Windows\Fonts\consola.ttf')
pdfmetrics.registerFont(TTFont('Arial', str(FONT)))
pdfmetrics.registerFont(TTFont('Arial-Bold', str(FONT_B)))
pdfmetrics.registerFont(TTFont('Arial-Italic', str(FONT_I)))
pdfmetrics.registerFont(TTFont('Consolas', str(MONO)))
pdfmetrics.registerFontFamily('Arial', normal='Arial', bold='Arial-Bold', italic='Arial-Italic', boldItalic='Arial-Bold')

NAVY = HexColor('#17324D')
BLUE = HexColor('#2B6E9B')
TEAL = HexColor('#178A8A')
LIGHT = HexColor('#EEF4F8')
PALE = HexColor('#F6F8FA')
INK = HexColor('#25313B')
MUTED = HexColor('#607080')
ORANGE = HexColor('#C77721')
GREEN = HexColor('#24734A')
RED = HexColor('#A54343')

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name='CoverTitle', fontName='Arial-Bold', fontSize=25, leading=31, textColor=NAVY, alignment=TA_LEFT, spaceAfter=9*mm))
styles.add(ParagraphStyle(name='Deck', fontName='Arial', fontSize=12, leading=18, textColor=MUTED, spaceAfter=8*mm))
styles.add(ParagraphStyle(name='H1x', fontName='Arial-Bold', fontSize=17, leading=22, textColor=NAVY, spaceBefore=5*mm, spaceAfter=3*mm, keepWithNext=True))
styles.add(ParagraphStyle(name='H2x', fontName='Arial-Bold', fontSize=12, leading=16, textColor=BLUE, spaceBefore=3*mm, spaceAfter=1.5*mm, keepWithNext=True))
styles.add(ParagraphStyle(name='Bodyx', fontName='Arial', fontSize=9.3, leading=14, textColor=INK, spaceAfter=2.3*mm))
styles.add(ParagraphStyle(name='Smallx', fontName='Arial', fontSize=8, leading=11, textColor=MUTED, spaceAfter=1.5*mm))
styles.add(ParagraphStyle(name='Bulletx', fontName='Arial', fontSize=9.1, leading=13.3, leftIndent=11, firstLineIndent=-7, bulletIndent=0, textColor=INK, spaceAfter=1.5*mm))
styles.add(ParagraphStyle(name='Codex', fontName='Consolas', fontSize=8, leading=11, leftIndent=3*mm, rightIndent=3*mm, backColor=PALE, borderColor=HexColor('#D8E1E8'), borderWidth=.5, borderPadding=5, textColor=NAVY, spaceBefore=1*mm, spaceAfter=2.5*mm))
styles.add(ParagraphStyle(name='Cellx', fontName='Arial', fontSize=7.5, leading=10, textColor=INK))
styles.add(ParagraphStyle(name='CellHeadx', fontName='Arial-Bold', fontSize=7.7, leading=10, textColor=colors.white))
styles.add(ParagraphStyle(name='NoteX', fontName='Arial', fontSize=8.7, leading=13, textColor=NAVY, backColor=LIGHT, borderColor=HexColor('#C8D9E5'), borderWidth=.6, borderPadding=7, spaceBefore=1*mm, spaceAfter=3*mm))


def P(text, style='Bodyx'):
    return Paragraph(text, styles[style])
def bullet(text):
    return Paragraph('• ' + text, styles['Bulletx'])
def code(text):
    return P(escape(text).replace('\n','<br/>'), 'Codex')
def h1(text): return P(text, 'H1x')
def h2(text): return P(text, 'H2x')
def callout(label, text, color=BLUE):
    p = Paragraph(f'<b><font color="{color.hexval()}">{label}</font></b> &nbsp; {text}', styles['NoteX'])
    return p

def grid(rows, widths, header=True):
    tbl=Table(rows, colWidths=widths, repeatRows=1 if header else 0, hAlign='LEFT')
    commands=[('VALIGN',(0,0),(-1,-1),'TOP'),('GRID',(0,0),(-1,-1),.35,HexColor('#D8E1E8')),
              ('LEFTPADDING',(0,0),(-1,-1),6),('RIGHTPADDING',(0,0),(-1,-1),6),
              ('TOPPADDING',(0,0),(-1,-1),5),('BOTTOMPADDING',(0,0),(-1,-1),5)]
    if header:
        commands += [('BACKGROUND',(0,0),(-1,0),NAVY),('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.white,PALE])]
    else:
        commands += [('ROWBACKGROUNDS',(0,0),(-1,-1),[colors.white,PALE])]
    tbl.setStyle(TableStyle(commands))
    return tbl

def cell(text, head=False):
    return Paragraph(text, styles['CellHeadx' if head else 'Cellx'])

def page(canvas, doc):
    canvas.saveState()
    w,h=A4
    if doc.page > 1:
        canvas.setStrokeColor(HexColor('#D7E0E7')); canvas.setLineWidth(.5)
        canvas.line(18*mm,h-14*mm,w-18*mm,h-14*mm)
        canvas.setFont('Arial',7.5); canvas.setFillColor(MUTED)
        canvas.drawString(18*mm,h-11*mm,'ЛР №6  |  Pub/Sub в проекте Event Management')
    canvas.setStrokeColor(HexColor('#D7E0E7')); canvas.line(18*mm,13*mm,w-18*mm,13*mm)
    canvas.setFont('Arial',7.5); canvas.setFillColor(MUTED)
    canvas.drawString(18*mm,8.5*mm,'Разбор по исходному коду проекта')
    canvas.drawRightString(w-18*mm,8.5*mm,f'{doc.page}')
    canvas.restoreState()

story=[]
story += [Spacer(1,20*mm), P('Лабораторная работа №6', 'CoverTitle'),
          P('Как устроены функции Pub/Sub в коде проекта', 'CoverTitle'),
          P('Пошаговый разбор Publisher, Broker, подписчиков, очередей, повторной доставки, интеграции с сервером и тестов.', 'Deck'),
          HRFlowable(width='100%', thickness=2, color=TEAL, spaceAfter=8*mm),
          P('<b>Проект:</b> Event Management, вариант 12 — управление мероприятиями.<br/><b>Читателю:</b> студенту, которому нужно объяснить реализацию на защите и показать полный цикл события.', 'Bodyx'),
          Spacer(1,6*mm), callout('Главная цепочка', 'Сервер формирует событие → Publisher пробует Broker → Broker выбирает подписчиков → у каждого подписчика своя очередь и независимый обработчик.', TEAL),
          Spacer(1,8*mm), P('В этом разборе «функция» означает метод или обработчик маршрута, который участвует в требованиях ЛР №6. Методы предыдущих лабораторных по базе данных описаны только в той мере, в какой они запускают публикацию событий.', 'Smallx'), ]

story += [h1('1. Карта проекта и путь события'),
          P('Код Pub/Sub расположен в каталоге <b>Messaging</b>. Сервер создаёт один runtime шины, в котором находятся два брокера и подписчики. Клиент вызывает HTTP-операцию регистрации или отмены; сервер сохраняет изменение предметной области и только после успешной операции формирует событие.'),
          code('Client → Server → EventMessage.Create(...) → EventPublisher.PublishAsync(...)\n                                      ↓\n                          Primary Broker → очередь Subscriber → HandleAsync(...)\n                                      ↘ при ошибке\n                           Secondary Broker'),
          h2('События предметной области'),
          grid([[cell('Тип',True),cell('Когда создаётся',True),cell('Подписчики',True)],
                [cell('ParticipantRegistered'),cell('После успешной регистрации участника'),cell('Logger, RegistrationService, NotificationService, AnalyticsService')],
                [cell('TicketIssued'),cell('Вместе с успешной регистрацией; билет моделируется событием, отдельной таблицы билетов нет'),cell('Logger, NotificationService, AnalyticsService')],
                [cell('RegistrationCancelled'),cell('После успешной отмены регистрации'),cell('Logger, RegistrationService, AnalyticsService')]], [43*mm,65*mm,64*mm]),
          Spacer(1,3*mm), callout('Важно', 'Асинхронны именно обработчики подписчиков. Метод Publisher ждёт принятия сообщения брокером и возвращает результат публикации; очереди обрабатываются фоновыми Task.', ORANGE),
          h1('2. Модель события: EventMessage.cs'),
          h2('EventMessage'),
          P('Это неизменяемая record-модель одного сообщения. Поля: <b>Id</b> — уникальный GUID сообщения; <b>EventType</b> — тип события для маршрутизации; <b>EntityId</b> — ID регистрации; <b>ClientId</b> — источник/клиент для журнала; <b>Timestamp</b> — UTC-время создания; <b>Details</b> — дополнительные данные, например ID мероприятия и участника.'),
          h2('EventMessage.Create(type, entityId, clientId, details)'),
          bullet('Проверяет, что тип входит в разрешённый перечень из трёх событий, EntityId положителен, а ClientId не пустой.'),
          bullet('При неверных данных выбрасывает ArgumentException. Это не позволяет собрать некорректное событие.'),
          bullet('Создаёт новый GUID и ставит DateTimeOffset.UtcNow. Возвращает готовый EventMessage.'),
          h2('PublishResult'),
          P('Результат Publisher: <b>Success</b> — удалось ли опубликовать; <b>Broker</b> — имя принявшего Broker; <b>Error</b> — последняя ошибка, если оба брокера отказали. Это результат принятия брокером, а не итог обработки всеми подписчиками.'),
          h1('3. Publisher: PubSub.cs'),
          h2('EventPublisher(params InMemoryBroker[] brokers)'),
          P('Получает список брокеров при создании. В PubSubRuntime список задаётся как Primary, затем Secondary, поэтому порядок попыток определён конфигурацией.'),
          h2('PublishAsync(EventMessage message)'),
          bullet('Идёт по Broker в переданном порядке и вызывает broker.Publish(message).'),
          bullet('Если публикация прошла, пишет PUBLISHED в консоль и сразу возвращает PublishResult с именем этого Broker.'),
          bullet('Если Broker выбросил исключение (например, недоступен или нет подписчиков), ловит его, журналирует FAIL и пробует следующий Broker.'),
          bullet('Если все брокеры отклонили событие, возвращает Success=false и последнюю ошибку. Метод сам напрямую подписчиков не вызывает.'),
          callout('Переключение Broker', 'Переключение выполняется этим циклом: исключение на Primary приводит к следующей итерации со Secondary. Повторная публикация прекращается при первом успешном Broker.', TEAL),
          h2('Log(message, text)'),
          P('Внутренний общий логгер выводит строку в Console с временем UTC, ClientId и EventType. Его используют Publisher и Broker для публикации, старта обработки, успехов и ошибок. Это консольный журнал; файл логов данный класс не создаёт.'),
          ]

story += [h1('4. Subscriber: EventSubscriber'),
          h2('Конструктор EventSubscriber(name, delayMs)'),
          P('Хранит имя компонента и искусственную задержку. Начальное состояние Available=true; счётчики Failures и HandledCount хранят статистику обработки.'),
          h2('Свойства Name, Available, Failures'),
          bullet('<b>Name</b> возвращает имя подписчика и используется Broker как ключ подписки.'),
          bullet('<b>Available</b> позволяет симулировать доступность/отказ. volatile помогает видеть актуальное состояние из фоновой задачи.'),
          bullet('<b>Failures</b> читает счётчик ошибок потокобезопасно через Volatile.Read.'),
          h2('HandledCount(type)'),
          P('Возвращает число успешно учтённых сообщений указанного типа из ConcurrentDictionary; при отсутствии ключа возвращает 0.'),
          h2('FailNext(count)'),
          P('Атомарно задаёт число искусственных ошибок, которые должны произойти перед успешной обработкой. Меню клиента №12 использует это для демонстрации повторов.'),
          h2('HandleAsync(message)'),
          bullet('Сразу выбрасывает IOException, если подписчик недоступен.'),
          bullet('Делает Task.Delay(delayMs), имитируя разное время обработки: Logger 1 с, RegistrationService 3 с, NotificationService 2 с, AnalyticsService 0,5 с.'),
          bullet('После задержки повторно проверяет доступность и счётчик искусственных ошибок. Ошибка увеличивает Failures и выбрасывает IOException.'),
          bullet('При успехе увеличивает счётчик события и возвращает понятный результат: Logger — «записано в журнал», RegistrationService — «регистрация учтена», NotificationService — «уведомление подготовлено», AnalyticsService — статистику типа события.'),
          callout('Как увидеть успех Logger', 'В окне сервера должна появиться строка «Logger SUCCESS записано в журнал». В меню клиента 8 проверьте рост Processed по нужному типу. Здесь Logger имитирует запись текстом и счётчиком в памяти, а не пишет отдельный файл.', ORANGE),
          h1('5. Broker и подписки: InMemoryBroker'),
          h2('InMemoryBroker(name)'),
          P('Создаёт брокер с именем, пустым реестром подписок и состоянием доступности. В проекте создаются Primary и Secondary.'),
          h2('Subscribe(subscriber, types...)'),
          P('Создаёт запись подписки для имени Subscriber: какие типы ему интересны, какая очередь сообщений ему принадлежит и какой обработчик использовать. Доступ к словарю защищён lock. В PubSubRuntime каждый подписчик регистрируется на обоих брокерах.'),
          h2('SetAvailable(value)'),
          P('Меняет доступность Broker. При включении вызывает Resume(), чтобы возобновить очереди, ожидавшие восстановления брокера.'),
          h2('Resume()'),
          P('Обходит все подписки и просит каждую начать обработку накопленной очереди, если Broker и Subscriber доступны.'),
          h2('Pending(subscriber)'),
          P('Возвращает количество сообщений в очереди указанного Subscriber. Используется экраном статуса для показа PendingPrimary и PendingSecondary.'),
          h2('Publish(message)'),
          bullet('Проверяет доступность самого Broker и базовую корректность события.'),
          bullet('Находит только подписки, содержащие EventType. Если подходящих подписчиков нет, выбрасывает InvalidOperationException.'),
          bullet('Журналирует получение события и для каждого подходящего подписчика вызывает Enqueue(message). Так одно событие попадает нескольким получателям.'),
          ]

story += [h1('6. Очередь и повторная доставка: Subscription'),
          P('Subscription — внутренняя запись Broker для одного Subscriber. В ней есть отдельная Queue<EventMessage>, lock-объект gate и флаг running. Отдельные очереди изолируют медленных или недоступных подписчиков друг от друга.'),
          h2('Enqueue(message)'),
          P('Добавляет сообщение в хвост очереди. Затем вызывает Start(). Если подписчик недоступен, Start не запускает обработчик, поэтому сообщение остаётся ожидающим.'),
          h2('Resume()'),
          P('Вызывает Start() для повторного запуска очереди после того, как подписчик или Broker восстановлен.'),
          h2('Start()'),
          P('Не допускает двух обработчиков на одной очереди одновременно. Ничего не запускает, если уже есть worker, очередь пуста, Broker недоступен или подписчик недоступен. Иначе ставит running=true и запускает DrainAsync в Task.Run.'),
          h2('DrainAsync()'),
          bullet('Берёт сообщение с головы через Peek(), не удаляя его до успеха.'),
          bullet('До трёх раз вызывает HandleAsync. Перед каждой попыткой журналирует START; при ошибке журналирует FAIL и ждёт 300 мс перед следующей попыткой.'),
          bullet('При успехе журналирует SUCCESS и только затем удаляет сообщение через Dequeue().'),
          bullet('После трёх неудач ставит Subscriber.Available=false, оставляет сообщение в очереди и завершает worker. Другие подписки выполняются в собственных фоновых задачах.'),
          code('Публикация → очередь Subscriber\n                         ├─ Attempt 1 → FAIL\n                         ├─ пауза 300 мс\n                         ├─ Attempt 2 → FAIL\n                         ├─ пауза 300 мс\n                         ├─ Attempt 3 → SUCCESS → Dequeue\n                         └─ если все FAIL: UNAVAILABLE, сообщение остаётся в очереди'),
          h2('Как восстановить ожидающее сообщение'),
          P('HTTP-управление доступностью вызывает PubSubRuntime.SetSubscriberAvailable(name, true). Он включает Subscriber и вызывает Resume() у Primary и Secondary. Очередь снова стартует; после успешной обработки сообщение удаляется. Проверяется через меню клиента 8: Pending уменьшается до нуля.'),
          callout('Граница гарантии', 'Очереди находятся в памяти. Они сохраняют сообщение при временной недоступности Subscriber, пока процесс сервера работает, но теряются при перезапуске сервера. Это временная очередь, не постоянное хранилище.', ORANGE),
          h1('7. Правила подписки: Subscriptions.For(name)'),
          P('Возвращает список имён событий для данного типа Subscriber. Logger слушает все три типа; RegistrationService — регистрацию и отмену; NotificationService — регистрацию и выдачу билета; AnalyticsService — все три. Неизвестное имя приводит к ArgumentException. Broker использует этот список для фильтрации в Publish.'),
          h1('8. Координатор: PubSubRuntime'),
          h2('Конструктор PubSubRuntime()'),
          P('Создаёт Primary, Secondary и Publisher(primary, secondary), после чего добавляет Logger, RegistrationService и NotificationService с нужными задержками.'),
          h2('Add(name, delay) / AddAnalytics()'),
          P('Add защищает словарь от параллельных изменений, не добавляет дубликаты, создаёт EventSubscriber, получает список типов через Subscriptions.For, подписывает его на оба Broker и сохраняет в реестре. AddAnalytics добавляет AnalyticsService с задержкой 500 мс. Publisher при этом не меняется.'),
          h2('SetSubscriberAvailable(name, available)'),
          P('Ищет подписчика, меняет состояние. При восстановлении возобновляет очереди обоих брокеров.'),
          h2('FailNext(name, count)'),
          P('Ограничивает count диапазоном 0-10, находит Subscriber и задаёт число симулируемых ошибок. При неизвестном имени возвращает ошибку.'),
          h2('Get(name)'),
          P('Внутренний поиск по словарю. Если подписчик не зарегистрирован, выбрасывает ArgumentException с понятным сообщением.'),
          h2('SetBrokerAvailable(name, available)'),
          P('Выбирает Primary или Secondary и меняет его состояние. Неизвестное имя отклоняется.'),
          h2('Status()'),
          P('Формирует объект статуса: доступность обоих брокеров; для каждого Subscriber — имя, доступность, типы подписки, количество ошибок, обработанные сообщения по типам и длины очередей в обоих брокерах. Это ответ API /api/pubsub и данные меню 8.'),
          ]

story += [h1('9. Связь с HTTP-сервером: Server/Program.cs'),
          h2('Создание runtime и Publisher'),
          P('При запуске приложения создаётся PubSubRuntime bus, а локальная переменная publisher получает bus.Publisher. Это один экземпляр на процесс сервера.'),
          h2('ClientId(HttpContext)'),
          P('Определяет ID клиента по заголовку X-Client-Id, затем по query-параметру clientId, иначе использует Client. ID попадает в событие и журналы.'),
          h2('Локальная функция Publish(HttpContext, messages...)'),
          bullet('Для каждого EventMessage вызывает publisher.PublishAsync.'),
          bullet('Сохраняет имя Broker, успешно принявшего сообщение, в HTTP-заголовок X-Event-Broker.'),
          bullet('Если публикация не удалась, пишет ошибку в серверный лог.'),
          bullet('Устанавливает X-Event-Publication: PUBLISHED, если опубликованы все переданные сообщения, иначе FAILED. Клиент читает эти заголовки и отображает результат.'),
          h2('Маршруты управления Pub/Sub'),
          grid([[cell('Маршрут',True),cell('Что вызывает',True),cell('Назначение',True)],
                [cell('GET /api/pubsub'),cell('bus.Status()'),cell('Показать доступность, подписки, очереди и счётчики')],
                [cell('POST /api/pubsub/subscribers/analytics'),cell('bus.AddAnalytics()'),cell('Подключить нового подписчика аналитики')],
                [cell('POST /api/pubsub/subscribers/{name}/availability'),cell('bus.SetSubscriberAvailable(...)'),cell('Смоделировать отказ/восстановление Subscriber')],
                [cell('POST /api/pubsub/subscribers/{name}/fail-next'),cell('bus.FailNext(...)'),cell('Задать ошибки обработки для проверки retry')],
                [cell('POST /api/pubsub/brokers/{name}/availability'),cell('bus.SetBrokerAvailable(...)'),cell('Отключить/восстановить Broker')]], [68*mm,48*mm,56*mm]),
          h2('POST /api/registrations'),
          P('Сначала вызывает существующую операцию базы Register. Только при результате успеха и наличии ID регистрации создаёт ParticipantRegistered и TicketIssued, добавляя в Details ID мероприятия и участника, затем вызывает Publish. При ошибке предметной операции событий нет.'),
          h2('DELETE /api/registrations/{id}'),
          P('Вызывает отмену в базе. Если отмена успешна, создаёт RegistrationCancelled и отправляет через Publisher.'),
          h2('Остальные CRUD маршруты'),
          P('Создание мероприятия, участника, площадки и чтение данных сохраняют функциональность предыдущих лабораторных. Они не публикуют события в этой версии; регистрация и отмена являются операциями, порождающими события.'),
          h1('10. Клиент и демонстрационные пункты меню'),
          grid([[cell('Пункт',True),cell('Действие',True),cell('Что демонстрирует',True)],
                [cell('5 / 6'),cell('Регистрация / отмена'),cell('Появление ParticipantRegistered + TicketIssued или RegistrationCancelled')],
                [cell('8'),cell('GET /api/pubsub'),cell('Подписки, доступность, Pending и Processed')],
                [cell('9'),cell('Доступность RegistrationService'),cell('Очередь при отказе и обработка после восстановления')],
                [cell('10'),cell('Добавление AnalyticsService'),cell('Новый Subscriber без изменения Publisher')],
                [cell('11'),cell('Доступность Primary'),cell('Переключение публикации на Secondary при отказе Primary')],
                [cell('12'),cell('Задать число ошибок RegistrationService'),cell('Три попытки доставки и обработка после retry')]], [22*mm,67*mm,83*mm]),
          ]

story += [h1('11. Unit-тесты и что каждый доказывает'),
          grid([[cell('Тест',True),cell('Функции проекта',True),cell('Проверка',True)],
                [cell('Event_HasRequiredDataAndRejectsInvalidInput'),cell('EventMessage.Create'),cell('GUID, entity/client/details, timestamp; неверный тип и ID отклоняются')],
                [cell('Broker_DeliversEachTypeOnlyToSubscribers'),cell('Subscribe, Publish, Enqueue, HandleAsync'),cell('Три события маршрутизируются только подписанным Subscriber; одно событие получают несколько обработчиков')],
                [cell('UnavailableSubscriber_KeepsEventWhileOthersContinue'),cell('Start, DrainAsync, Resume, Pending'),cell('Недоступный Subscriber не блокирует Logger; событие остаётся в очереди, затем доставляется после восстановления')],
                [cell('Subscriber_RetriesAndSucceedsOnThirdAttempt'),cell('FailNext, HandleAsync, DrainAsync'),cell('Две искусственные ошибки, затем успешная третья попытка')],
                [cell('Publisher_FallsBackToSecondBroker'),cell('PublishAsync, SetAvailable'),cell('При отключённом Primary публикация принимается Secondary')],
                [cell('Publisher_ReportsMissingSubscriptionAndBothBrokersUnavailable'),cell('PublishAsync, Publish'),cell('Publisher возвращает понятный неуспех без подходящих подписчиков и когда недоступны оба брокера')],
                [cell('NewSubscriber_ReceivesFutureEventsWithoutPublisherChange'),cell('Subscribe, PublishAsync'),cell('Поздно подключённый Analytics получает следующие события с прежним Publisher')]], [62*mm,45*mm,65*mm]),
          Spacer(1,4*mm), P('В Server.Tests/PubSubTests.cs есть также вспомогательная функция Until(condition): она проверяет асинхронный результат циклом с короткой паузой и таймаутом 5 секунд, чтобы тесты не делали фиксированный долгий sleep.'),
          h1('12. Сопоставление с частями задания'),
          grid([[cell('Часть условия',True),cell('Реализация',True),cell('Статус по коду',True)],
                [cell('1. Базовая модель'),cell('EventMessage, EventPublisher, InMemoryBroker, Subscribe, Publish'),cell('Есть')],
                [cell('2. Несколько Subscriber и событий'),cell('3 основных Subscriber + Analytics; 3 события; фильтрация и логирование'),cell('Есть')],
                [cell('3. Асинхронность и отказ'),cell('Task.Run, Task.Delay, Available, retries, отдельные очереди'),cell('Есть; очереди в памяти')],
                [cell('4. Новый Subscriber и тесты'),cell('AddAnalytics; тест без изменения Publisher; сравнение в README'),cell('Есть')],
                [cell('5. Несколько Broker'),cell('Primary/Secondary; временные очереди; retry; Analytics; клиентские ID'),cell('Есть; без дисковой персистентности')]], [44*mm,92*mm,36*mm]),
          h1('13. Полный поток на защите'),
          P('1. Запустить БД и Server; открыть Client. 2. Создать площадку, мероприятие, участника. 3. Зарегистрировать участника. 4. В консоли Server найти EVENT RECEIVED, START и SUCCESS для подписчиков. 5. В меню 8 проверить счётчики. 6. В меню 9 выключить RegistrationService, зарегистрировать другого участника: остальные Subscriber должны завершить работу, а у RegistrationService появится Pending. 7. Восстановить его и убедиться, что Pending стал 0. 8. Добавить Analytics через меню 10 и проверить следующее событие. 9. Выключить Primary через меню 11 и проверить, что публикация прошла через Secondary. 10. Задать две ошибки через меню 12 и показать Attempt 1/2 FAIL, Attempt 3 SUCCESS.'),
          h1('14. Ограничения и точные формулировки'),
          bullet('Broker и очереди реализованы объектами C# в том же процессе, не внешним брокером и не сетевыми сервисами Subscriber.'),
          bullet('Очереди временные и очищаются при перезапуске процесса. Код гарантирует повтор при отказе обработчика в рамках работающего процесса, а не постоянное хранение на диске.'),
          bullet('Два Broker работают как последовательный failover для новой публикации. Это не репликация содержимого очередей между брокерами.'),
          bullet('Logger в примере подтверждает обработку консольным сообщением и счётчиком. Он не создаёт отдельный файл журнала.'),
          bullet('Обработка выполняется асинхронно независимо для каждой очереди; один Subscriber получает свою последовательную очередь.'),
          h1('Исходные файлы, на которые опирается разбор'),
          P('Messaging/EventMessage.cs; Messaging/PubSub.cs; Messaging/Subscriptions.cs; Messaging/PubSubRuntime.cs; Server/Program.cs; Client/Program.cs; Client/ClientApplication.cs; Server.Tests/PubSubTests.cs; README.md.', 'Smallx')]


doc = SimpleDocTemplate(str(OUT), pagesize=A4, rightMargin=18*mm, leftMargin=18*mm, topMargin=20*mm, bottomMargin=19*mm,
                        title='ЛР 6 - разбор функций Pub/Sub', author='Codex', subject='Разбор функций лабораторной работы 6 по исходному коду')
doc.build(story, onFirstPage=page, onLaterPages=page)
print(OUT.resolve())
