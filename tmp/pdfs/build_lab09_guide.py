from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak, KeepTogether, Preformatted
from xml.sax.saxutils import escape
from pathlib import Path

root = Path(__file__).resolve().parents[2]
out = root / 'output' / 'pdf' / 'ЛР9_объяснение_и_ответы.pdf'
out.parent.mkdir(parents=True, exist_ok=True)
pdfmetrics.registerFont(TTFont('Arial', r'C:\Windows\Fonts\arial.ttf'))
pdfmetrics.registerFont(TTFont('ArialBold', r'C:\Windows\Fonts\arialbd.ttf'))
pdfmetrics.registerFontFamily('Arial', normal='Arial', bold='ArialBold', italic='Arial', boldItalic='ArialBold')
ink = colors.HexColor('#182433')
blue = colors.HexColor('#174c73')
muted = colors.HexColor('#526575')
body = ParagraphStyle('body', fontName='Arial', fontSize=10, leading=14.3, textColor=ink, spaceAfter=6)
small = ParagraphStyle('small', parent=body, fontSize=9, leading=12, spaceAfter=0)
title = ParagraphStyle('title', fontName='ArialBold', fontSize=23, leading=28, textColor=ink, spaceAfter=10)
sub = ParagraphStyle('sub', parent=body, fontSize=11, leading=16, textColor=muted, spaceAfter=12)
h1 = ParagraphStyle('h1', fontName='ArialBold', fontSize=15, leading=19, textColor=blue, spaceBefore=5, spaceAfter=9, keepWithNext=True)
h2 = ParagraphStyle('h2', fontName='ArialBold', fontSize=10.5, leading=14, textColor=ink, spaceBefore=6, spaceAfter=4, keepWithNext=True)
code = ParagraphStyle('code', fontName='Arial', fontSize=8.5, leading=11.8, textColor=ink, leftIndent=8, rightIndent=6, backColor=colors.HexColor('#eef3f6'), borderPadding=7, spaceBefore=3, spaceAfter=9)
qstyle = ParagraphStyle('question', parent=h2, fontSize=11, leading=15, spaceBefore=7, spaceAfter=4)
qbody = ParagraphStyle('answer', parent=body, leading=15, spaceAfter=8)
story = []
def p(text, style=body): story.append(Paragraph(text, style))
def h(text): p(text, h1)
def subh(text): p(text, h2)
def block(text): story.append(Preformatted(text.strip(), code))
def question(q, a): story.append(KeepTogether([Paragraph(q, qstyle), Paragraph(a, qbody)]))
def grid(rows, widths):
    cells = [[Paragraph(escape(str(v)), small) for v in row] for row in rows]
    t = Table(cells, colWidths=widths, repeatRows=1, hAlign='LEFT')
    t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),colors.HexColor('#dceaf2')),('GRID',(0,0),(-1,-1),0.4,colors.HexColor('#bdcbd5')),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),6),('RIGHTPADDING',(0,0),(-1,-1),6),('TOPPADDING',(0,0),(-1,-1),5),('BOTTOMPADDING',(0,0),(-1,-1),5)]))
    story.append(t); story.append(Spacer(1, 8))
def page(canvas, doc):
    canvas.saveState(); w, height = A4
    canvas.setStrokeColor(colors.HexColor('#dce5eb')); canvas.line(20*mm, 17*mm, w-20*mm, 17*mm)
    canvas.setFont('Arial', 8); canvas.setFillColor(muted)
    canvas.drawString(20*mm, 12*mm, 'ЛР 9  |  Управление мероприятиями')
    canvas.drawRightString(w-20*mm, 12*mm, str(doc.page))
    canvas.restoreState()

p('Лабораторная работа 9', title)
p('Развертывание приложения управления мероприятиями в Kubernetes', sub)
p('<b>Вариант 12.</b> Объяснение реализации, результатов экспериментов и ответы на контрольные вопросы.', body)
p('Цель работы - развернуть сервер из предыдущей лабораторной в локальном Kubernetes, организовать доступ клиента через Service, проверить масштабирование и восстановление Pod, применить ConfigMap и разобрать ошибку конфигурации.')
grid([['Компонент','Роль в работе'],['Клиент C#','Консольное меню отправляет HTTP-запросы и показывает ответы.'],['Сервер C#','ASP.NET Core API выполняет операции, обращается к БД и ведёт журнал.'],['Kubernetes','Docker Desktop, кластер kind с одним узлом; Deployment управляет серверными Pod.'],['PostgreSQL','Основная и резервная БД работают в Docker Compose и сохраняют данные вне серверных Pod.']], [48*mm, 122*mm])
p('<b>Главный результат:</b> сервер запущен в Kubernetes; при проверке масштабирования Deployment поддерживал три работающих Pod, а после удаления одного создал замену. Результаты в документе относятся к выполненным экспериментам 17 ноября 2026 года.')

h('1. Как устроено приложение')
p('Клиент обращается к локальному порту, который команда kubectl port-forward связывает с Service. Service выбирает серверные Pod по метке app=riot-server. Сервер принимает HTTP-запрос, вызывает логику предметной области и получает доступ к двум PostgreSQL по адресу host.docker.internal. Базы запускаются в Docker Compose, а не в Kubernetes.')
block('Клиент C# → localhost:8081 → kubectl port-forward\n           → Service riot-server:8080 → серверный Pod:8080\n           → PostgreSQL primary / replica в Docker Compose')
p('Пример операции: клиент создаёт площадку, участника и мероприятие. При регистрации сервер проверяет запрос и проводит учебное голосование. Если голосование успешно, операция регистрирует участника и резервирует место в БД. Успешная регистрация сопровождается публикацией событий. Отмена удаляет регистрацию и возвращает место.')
p('Pub/Sub и узлы консенсуса из прошлых работ моделируются внутри процесса сервера. У каждого Pod собственная память и собственная учебная модель; три Pod не образуют единый распределённый кластер консенсуса. Это ограничение важно при демонстрации отказов.')

h('2. Образ и запуск сервера')
p('Используется Dockerfile предыдущей лабораторной. На этапе build образ SDK восстанавливает зависимости и публикует сервер и библиотеку Messaging. Во втором этапе итоговые файлы копируются в образ ASP.NET Runtime. Контейнер запускает Server.dll. Образ для ЛР9 собран под именем riot-server:lab09.')
p('Контейнер слушает 0.0.0.0:8080 благодаря ASPNETCORE_URLS. Адрес 0.0.0.0 позволяет принимать соединения извне контейнера. Порт containerPort в YAML описывает порт приложения, но не публикует его на компьютере самостоятельно.')
block('docker build -t riot-server:lab09 -f Server/Dockerfile .\nkubectl config current-context\nkubectl cluster-info\nkubectl get nodes')
p('В проведённом эксперименте контекст - docker-desktop, версия Kubernetes - 1.36.1, один узел desktop-control-plane был Ready. При использовании kind локальный образ дополнительно импортирован в хранилище контейнеров узла; одного imagePullPolicy для передачи образа недостаточно.')

h('3. Deployment, Service и ConfigMap')
subh('Deployment: что именно создаётся')
p('Файл deployment.yaml описывает Deployment riot-server. Изначально в нём задано replicas: 1. Deployment через ReplicaSet создаёт Pod по шаблону, следит за их количеством и при удалении управляемого Pod создаёт замену. selector.matchLabels и template.metadata.labels содержат одинаковую метку app: riot-server.')
block('replicas: 1\nimage: riot-server:lab09\nimagePullPolicy: IfNotPresent\ncontainerPort: 8080')
p('IfNotPresent разрешает использовать образ, уже находящийся на узле. В ходе эксперимента число экземпляров менялось командой kubectl scale. Это изменяет текущее состояние Deployment в кластере; в исходном deployment.yaml значение по-прежнему равно 1. Повторное применение этого файла может вернуть количество к заданному в нём.')
subh('Service: стабильная точка доступа')
p('Файл service.yaml создаёт Service типа ClusterIP. Его selector app: riot-server совпадает с метками серверных Pod. Порт Service 8080 перенаправляется на targetPort 8080 контейнера. По селектору Kubernetes формирует список конечных точек; несовпадение меток оставит Service без подходящих Pod.')
block('kubectl apply -f configmap.yaml -f deployment.yaml -f service.yaml\nkubectl rollout status deployment/riot-server\nkubectl get pods,services\nkubectl describe service riot-server\nkubectl port-forward service/riot-server 8081:8080')
p('Последняя команда выбирает один подходящий Pod и держит туннель к нему, пока процесс работает. Она не распределяет каждый запрос между всеми Pod; для проверки разных экземпляров использовались отдельные перенаправления. Если выбранный Pod прекращает работу, port-forward нужно запустить снова.')
subh('ConfigMap и Secret')
p('В configmap.yaml задан неконфиденциальный параметр APP_MODE=kubernetes. Deployment передаёт его через configMapKeyRef; при запуске Program.cs читает переменную окружения и выводит Application mode: kubernetes. Если переменная не задана, используется default. При изменении ConfigMap работающий процесс не получает новое значение автоматически: нужны новые Pod, например через kubectl rollout restart.')
p('Строки подключения к БД передаются из Secret riot-database через secretKeyRef. Имя Secret и ключи primary и replica должны совпадать с Deployment. Сам Secret создаётся отдельно; пароль не помещается в ConfigMap и манифесты. Kubernetes Secret не следует считать автоматически зашифрованным хранилищем без настройки защиты кластера.')

h('4. Данные и параллельные запросы')
p('Сервер обращается к основной и резервной PostgreSQL. При обычной успешной записи в основную базу ReplicatedDatabase инициирует копирование снимка в реплику. Database.CopyFrom сначала получает транзакционную advisory-блокировку PostgreSQL в целевой БД, затем читает снимок источника и записывает его в рамках транзакции. Это упорядочивает конкурирующие копирования на одной целевой базе.')
p('Для регистрации используется транзакция и блокировка строки мероприятия в PostgreSQL. Благодаря этому конкурирующие операции не должны одновременно занять одно последнее место. В эксперименте через три разных Pod отправили три заявки на одно место: получены ответы 200, 409 и 409; после запросов снимки основной и резервной БД совпали.')
p('Эта проверка демонстрирует конкретный сценарий при доступных обеих БД. Состояние активной копии и имитация доступности хранятся отдельно в памяти каждого Pod; полноценная согласованная обработка отказа БД между несколькими серверными Pod не доказана. Проверку прежнего переключения копий проводят с одним экземпляром.')

h('5. Эксперименты и наблюдения')
grid([['Действие','Фактический результат'],['Исходное состояние','Задан и запущен один Pod.'],['Масштабирование 1 → 3','Deployment достиг трёх работающих Pod.'],['Масштабирование 3 → 2','Остались два работающих Pod; третий временно находился в Terminating.'],['Возврат к 3','Снова запущены три Pod.'],['Удаление одного Pod','Удалён gf5xf; замена 9mmxx создана и перешла в Running. После восстановления HTTP-запрос вернул 200.'],['Некорректный запрос','Создание мероприятия с пустым именем вернуло HTTP 400.']], [62*mm, 108*mm])
p('Почему восстановилось число Pod? В Deployment указано желаемое число, а контроллеры сравнивают его с фактическим состоянием и создают недостающий экземпляр. В переходный момент старый Pod может завершаться, пока новый ещё создаётся, поэтому кратковременное число объектов отличается от желаемого.')
subh('Проверка параметра')
p('В ConfigMap значение временно менялось на lab09-test. После перезапуска Deployment журнал нового Pod показал Application mode: lab09-test. Затем возвращено kubernetes и снова выполнен перезапуск; в журнале нового Pod подтверждено исходное значение.')
subh('Намеренная ошибка образа')
p('У сервера временно указан тег riot-server:missing-lab09. Новый Pod получил ErrImagePull; в событиях describe обнаружены сообщения о неудачной загрузке и ImagePullBackOff. После возврата riot-server:lab09 Deployment снова достиг нужного состояния. Это пример диагностики через состояние Pod и события, а не только по успешному применению YAML.')
p('При настройке среды попытка kubeadm завершилась ошибкой ожидания cgroup-каталогов kubelet. Для нового учебного кластера был выбран kind в Docker Desktop; после запуска узел перешёл в Ready. Проблема относилась к запуску среды, а не к коду сервера.')

h('6. Проверки и команды для защиты')
p('Выполнены 14 тестов клиента и 46 серверных тестов. Отдельно проверены реальный HTTP-доступ к серверу, создание площадки, участника и мероприятия, регистрация и отмена, проверка ошибочного запроса и одновременные регистрации через разные Pod. После последнего перезапуска три контрольных GET-запроса вернули HTTP 200.')
block('dotnet test Client.Tests\ndotnet test Server.Tests\nkubectl get deployments,pods,services\nkubectl logs deployment/riot-server\nkubectl scale deployment riot-server --replicas=3\nkubectl delete pod <имя-pod>\nkubectl rollout status deployment/riot-server')
p('Для клиента в отдельном терминале задают SERVER_URL=http://localhost:8081 и запускают dotnet run --project Client. Port-forward должен работать в другом терминале. Если порт занят или туннель закрылся, выбирают свободный локальный порт и согласуют его с SERVER_URL.')
p('Для изучения состояния используют kubectl get, для причин неготовности - kubectl describe pod и события, для поведения приложения - kubectl logs. Running само по себе не доказывает, что сервер может выполнить операцию и связаться с БД: нужен контрольный запрос.')

h('7. Docker, Compose и Kubernetes')
grid([['Критерий','Docker / Compose','Kubernetes'],['Основная задача','Создание образа и совместный запуск контейнеров.','Управление развёртыванием и желаемым состоянием.'],['Конфигурация','Dockerfile и compose.yaml.','Манифесты ресурсов, например Deployment и Service.'],['Масштабирование','Compose может запускать несколько контейнеров сервиса.','Deployment поддерживает заданное количество Pod.'],['Доступ','Публикация портов на хост.','Service внутри кластера; локально можно использовать port-forward.'],['Диагностика','docker compose ps и logs.','kubectl get, describe, logs и события.']], [37*mm, 65*mm, 68*mm])
p('Масштабирование не гарантирует ускорения: несколько Pod используют общие ресурсы компьютера и зависят от одних и тех же БД. Отказ одной локальной машины может остановить весь учебный кластер. Восстановление Pod не равно восстановлению данных PostgreSQL.')

story.append(PageBreak())
h('8. Ответы на контрольные вопросы')
qa = [
('1. Что такое Kubernetes и для чего он используется?', 'Это платформа оркестрации контейнеризированных приложений. Она управляет развёртыванием, количеством экземпляров, сетевым доступом и восстановлением ресурсов.'),
('2. Чем Kubernetes отличается от Docker?', 'Docker создаёт образы и запускает контейнеры. Kubernetes управляет контейнеризированными нагрузками в кластере и стремится поддерживать заданное состояние.'),
('3. Что такое Kubernetes Cluster?', 'Это среда из управляющих компонентов и одного или нескольких узлов, на которых запускаются нагрузки. В работе используется один локальный узел Docker Desktop.'),
('4. Что такое Pod?', 'Минимальная единица развёртывания Kubernetes. Она содержит один или несколько тесно связанных контейнеров и получает сетевой адрес внутри кластера.'),
('5. Чем Pod отличается от Docker Container?', 'Контейнер изолирует запущенный процесс. Pod является оболочкой Kubernetes для одного или нескольких контейнеров, которые совместно используют сеть и заданные ресурсы.'),
('6. Что такое Deployment?', 'Ресурс, описывающий желаемое состояние приложения. Он управляет ReplicaSet, который создаёт и заменяет Pod.'),
('7. Для чего используется параметр replicas?', 'Он задаёт желаемое количество Pod Deployment. Контроллеры стремятся поддерживать это число при наличии необходимых ресурсов.'),
('8. Что такое Service?', 'Ресурс со стабильной сетевой точкой доступа к набору Pod, обычно выбранных по меткам. В работе используется ClusterIP.'),
('9. Почему клиенту неудобно обращаться к Pod напрямую?', 'Pod может быть удалён и создан заново с другим IP. Service позволяет использовать постоянный логический адрес и автоматически выбирать подходящие конечные точки.'),
('10. Для чего используются метки и селекторы?', 'Метки - пары ключ-значение на объектах. Селекторы находят объекты с нужными метками; например, Service выбирает серверные Pod по app=riot-server.'),
('11. Что такое YAML и для чего он используется в Kubernetes?', 'Это текстовый формат структурированных данных. В манифестах YAML описывают ресурсы, их имена и желаемые параметры.'),
('12. Как проверить состояние Pod?', 'Командой kubectl get pods; вариант -o wide дополнительно показывает IP и узел. Состояние Running не заменяет проверку реального запроса.'),
('13. Как получить подробную информацию о Pod?', 'Командой kubectl describe pod &lt;имя-pod&gt;. Она показывает параметры, состояние контейнера и события, в том числе ошибки загрузки образа.'),
('14. Что происходит при удалении Pod, управляемого Deployment?', 'ReplicaSet замечает нехватку экземпляра и пытается создать замену. Новый Pod может иметь другое имя и другой IP.'),
('15. Почему восстановление Pod не гарантирует сохранность данных?', 'Пересоздание Pod восстанавливает процесс, но не исправляет отказ внешней БД и не возвращает данные, утраченные из непостоянного хранилища.'),
('16. Что такое ConfigMap?', 'Ресурс для неконфиденциальной конфигурации. Его значения можно передать приложению через переменные окружения или файлы.'),
('17. Для чего используются переменные окружения?', 'Они передают параметры процессу при запуске без изменения исходного кода. В работе APP_MODE выводится в журнал.'),
('18. Для чего используется kubectl port-forward?', 'Команда связывает локальный порт с портом выбранного Pod, в том числе выбранного через Service. После завершения выбранного Pod перенаправление нужно перезапустить.'),
('19. Почему несколько Pod на одном компьютере не защищают от отказа компьютера?', 'Все они зависят от одного физического узла и его ресурсов. При его отказе могут одновременно стать недоступны все экземпляры.'),
('20. Чем Docker Compose отличается от Kubernetes?', 'Compose описывает и запускает группу контейнеров, обычно локально. Kubernetes управляет ресурсами кластера, контроллерами и желаемым состоянием Pod.')]
for q, a in qa: question(q, a)

doc = SimpleDocTemplate(str(out), pagesize=A4, rightMargin=20*mm, leftMargin=20*mm, topMargin=18*mm, bottomMargin=22*mm, title='Лабораторная работа 9 объяснение и ответы', author='')
doc.build(story, onFirstPage=page, onLaterPages=page)
print(out)
