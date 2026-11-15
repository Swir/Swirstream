@echo off
setlocal
cd /d "%~dp0"
set "RESULT=0"
set "REPORTS=%~dp0test-results\run-%RANDOM%-%RANDOM%"
mkdir "%REPORTS%"
if not "%errorlevel%"=="0" goto failed
if not exist "%~dp0SwirStreamAI.exe" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --portal-scanner-test --report "%REPORTS%\portal-scanner.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --public-playlist-test --report "%REPORTS%\public-playlist.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --public-provider-test --report "%REPORTS%\public-provider.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --source-explorer-test --report "%REPORTS%\source-explorer.json"
if not "%errorlevel%"=="0" goto failed
echo Lokalne testy SwirStream AI 0.9.8. Okna testowe moga pojawic sie na chwile.
start "" /wait "%~dp0SwirStreamAI.exe" --xtream-link-test --report "%REPORTS%\xtream-link.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --provider-failure-test --report "%REPORTS%\provider-failure.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --provider-test --report "%REPORTS%\provider.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --portal-protocol-test --report "%REPORTS%\portal-protocol.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --portal-catalog-test --report "%REPORTS%\portal-catalog.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --private-epg-test --report "%REPORTS%\private-epg.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --self-test --report "%REPORTS%\self.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --discovery-test --report "%REPORTS%\discovery.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --web-test --report "%REPORTS%\web.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --playlist-test --report "%REPORTS%\playlist.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --personal-test --report "%REPORTS%\personal.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --search-learning-test --report "%REPORTS%\search-learning.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --deep-test --report "%REPORTS%\deep.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --deep-web-test --report "%REPORTS%\deep-web.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --deep-identity-test --report "%REPORTS%\deep-identity.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --local-search-test --report "%REPORTS%\local-search.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --epg-test --report "%REPORTS%\epg.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --player-test --report "%REPORTS%\player.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --startup-resource-test --report "%REPORTS%\startup-resource.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --gate-test --report "%REPORTS%\gate.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --playback-progress-test --report "%REPORTS%\playback-progress.json"
if not "%errorlevel%"=="0" goto failed
start "" /wait "%~dp0SwirStreamAI.exe" --encrypted-guard-test --report "%REPORTS%\encrypted-guard.json"
if not "%errorlevel%"=="0" goto failed
echo TESTY LOKALNE OK. Nie jest to test aktualnej dostepnosci kanalow w internecie.
goto done
:failed
set "RESULT=1"
echo TESTY NIEUDANE - sprawdz najnowszy raport i komplet plikow aplikacji.
:done
echo Raporty: "%REPORTS%"
pause
exit /b %RESULT%

