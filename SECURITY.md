# Security audit — 2026-10-02

Это исторический snapshot, не актуальная гарантия отсутствия CVE. Финальная
проверка 2026-10-03 повторила runtime/tests и offline Gitleaks; новый image scan
не выполнялся. Указанные ниже image advisory counts нельзя выдавать за свежий PASS.
Production proxy error JSON также проверяется на отступы: opt-in
`tests/security-http.ps1 -Production -ProxyFailureProbe` временно останавливает
только API указанного isolated test project и восстанавливает его в `finally`.

Проверены исходники, dependency graph, Git history/worktree, контейнеры,
фактические Windows listeners и два реально запущенных Docker Compose окружения.
Это проверка данного проекта, а не гарантия отсутствия всех уязвимостей ОС
или полноценный внешний penetration test.

## Итог проверок

| Проверка | Результат |
| --- | --- |
| Locked restore / build | PASS, 0 build warnings / errors |
| Automated tests | PASS, 64/64, без skipped tests |
| Local HTTP suite | PASS, 15/15 |
| Production configuration over local HTTPS | PASS, 14/14 |
| Original payloads and saved response comparison | PASS в обоих окружениях |
| PostgreSQL persistence | По 248 новых rows: 238 + 9 + 1 SQL-looking attribute; rejected inputs не записаны |
| Persistence after container restart | PASS |
| Fresh PostgreSQL volume initialization | PASS; app role не superuser/createdb/createrole/replication |
| pgAdmin no-login / preconfigured DB | PASS; normal Host 200, unknown Host 403 |
| NuGet direct/transitive advisories | Не обнаружены для обоих projects |
| Original fixtures/results | SHA256 до/после совпадают; файлы не изменены |

HTTP suite проверяет неправильный/отсутствующий/дублирующийся API key,
закрытый production Swagger, malformed JSON, media type, body size (включая
chunked), cross-site requests, DOM complexity и безопасную запись SQL-looking
content. Automated tests дополнительно проверяют rate/concurrency limits,
trusted proxy handling, timeout/cancellation и redaction внутренних ошибок.
TLS chain и hostname проверены через export локального Caddy CA, без `--insecure`
и без установки CA в Windows trust store. API key не найден в proxy logs.

## Фактическая публикация портов

| Service | Host listener |
| --- | --- |
| Local API | 127.0.0.1:8090 |
| Local pgAdmin | 127.0.0.1:8080 |
| Docker PostgreSQL | Host port не опубликован |
| Отдельный Windows PostgreSQL 18 | 127.0.0.1:5432 и [::1]:5432 |
| Production test proxy | Только localhost:9080/9443, test stack остановлен после проверки |
| Public deployment template | Только TCP 80/443; API/DB без host ports, pgAdmin отсутствует |

Windows Firewall включён для всех profiles, default policy BlockInbound.
Системные RPC/SMB listeners Windows существуют отдельно от проекта; они не
отключались автоматически. Внешняя доступность через router/NAT, публичный DNS,
сертификат ACME и firewall выбранного hosting server здесь не проверены.

## Что исправлено

- Закрыты внешние local bindings; Windows PostgreSQL configuration сохранён
  перед изменением в `postgresql.conf.security-backup-20261002-111108` рядом
  с original config. Database files и Docker volumes не удалялись.
- Host allowlist и cross-site guard; production HTTPS/API key обязательны.
- Production exception details скрыты; Development сохраняет контракт задания.
- Input/DOM/output/storage bounds, Regex/SQL/request timeouts и bounded rate limiter.
- Parameterized SQL и transactions сохранены; API не загружает URL и не исполняет HTML/JS.
- Local API source mount read-only; API/pgAdmin non-root без capabilities,
  no-new-privileges; production API также read-only и без outbound internet.
- Отдельный public Compose с новыми секретами, least-privilege DB role,
  HTTPS proxy, log redaction и resource limits. Local test credentials не используются.
- Secrets/certificates исключены из Git и build context; добавлены NuGet lock files.
- Исправлены доступные image dependency advisories: OS libraries, pgAdmin urllib3,
  Caddy Go/x/net/x/crypto/gRPC. Удалён ненужный pgAdmin pip; Go-based gosu
  заменён ограниченным setpriv wrapper без изменения PostgreSQL data format.

## Остаточные advisories — не скрыты

Docker Scout 1.19.0, проверка High/Critical без `--only-fixed`:

| Runtime | High/Critical |
| --- | --- |
| Development API SDK image | 0 |
| Production API image | 0 |
| Patched pgAdmin image | 0 |
| Patched Caddy final filesystem | 0 |
| Patched PostgreSQL final filesystem | 4 High / 0 Critical, без доступного fix в используемом Debian release |

Оставшиеся PostgreSQL advisories: CVE-2026-102010 (gcc-14),
[CVE-2026-86140](https://security-tracker.debian.org/tracker/CVE-2026-86140)
и [CVE-2026-74860](https://security-tracker.debian.org/tracker/CVE-2026-74860) (libxml2),
[CVE-2026-85091](https://security-tracker.debian.org/tracker/CVE-2026-85091) (zlib).
Сканер фиксирует наличие packages, а не доказанную эксплуатацию через этот API.
БД не опубликована, app role ограничена, SQL параметризован; API не выполняет
XML queries. Это снижает поверхность атаки, но не отменяет advisories.
Не подменялись системные библиотеки неподдерживаемыми сборками ради нулевого счётчика.
Перед public deployment повторите scan и оцените оставшийся риск/обновлённый vendor image.

Layer-based scan дополнительно продолжал отмечать заменённые Go binaries из
нижних immutable layers (старые gosu/Caddy). Для проверки **конечной файловой
системы** экспортированы только newly-created, never-started audit containers
без пользовательских volumes/secrets, затем их rootfs просканирован отдельно.
Старый Go-based gosu отсутствует; `caddy build-info` подтверждает Go 1.26.8,
x/crypto 0.56.0, x/net 0.58.0 и gRPC 1.83.2. Raw reports не подавлены и сохранены
в ignored `TestResults/security/*.sarif` вместе с runtime reports.

Gitleaks history/worktree находит публичные AES fixture keys из employer payloads;
они не являются production API key или DB passwords. Реальные deployment secrets
не включены в scan corpus для будущего Git content и не отправлены в GitHub.
AES ECB/NoPadding оставлен по заданию: для нового защищённого протокола нужен
authenticated encryption, но менять employer contract здесь нельзя.

## Перед публикацией сервиса

Следуйте public deployment section в README, а не локальному `compose.yml`.
Проверьте actual DNS/TLS/firewall, настройте защищённые backup, мониторинг диска,
secret rotation и внешний DDoS protection. Storage budget не является total disk
quota для WAL/temp files. Cooperative timeout не прерывает CPU code принудительно.
Разделение режимов и ограничения не заменяют обновления ОС/Docker и incident monitoring.

Публикация Git repository сама по себе не открывает порты и не запускает приложение.
На момент завершения audit исправления были только локально: commit/push
не выполнялись. Их публикация выполняется отдельно по запросу владельца repository.
