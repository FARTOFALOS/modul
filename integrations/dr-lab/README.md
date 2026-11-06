# Интеграционные компоненты DR Lab

Версия с рабочего компьютера 06.11.2026. Это компоненты для каталога lab существующего DR Lab, не самостоятельный аналитический сервер.

`tv_fetch.mjs` и `tv_passive.mjs` читают все готовые chart-targets TradingView, без setSymbol/setResolution/активации. Требуется локальный проект tradingview-mcp (переменная TV_MCP_DIR), его src/connection.js и CDP9222. Node22+.

`dr_voice.py` / `dr_speak.ps1` требуют Windows System.Speech и модуль scene21 установленного DR Lab. Старт `dr_voice.start()`, после успешного обновления каждого raw-cache `dr_voice.observe(inst,raw)`; формат raw: instrument, fetched_at UTC ISO, bars[[epochOpen,open,high,low,close]]. Три инструмента, закрытая M5; никаких заявок.

Рабочая интеграция в DR Lab: normal launcher включает --passive-live-refresh; live.start_minute_refresh() каждые60сек читает NQ/ES/YM независимо от выбранной вкладки; исключения одного источника не блокируют другие; атомарный кэш, прежняя дата при ошибке. M1 группируется в M5 только с полным минутным составом для закрытых свечей. HistoryOnly не включает сбор/голос. 23.5 опрашивает каждую минуту и вне активного торгового окна, чтобы возобновиться к следующей сессии. Открытый TradingView не перезапускается.

Эти изменения server/live/launcher относятся к отдельному репозиторию dr-idr и не являются частью NinjaTrader installer. Нельзя считать копирование четырёх файлов установкой всего DR Lab. Читайте CURRENT-STACK и исходный проект; не переносите исторические данные в публичный git.
