; Скрипт для установки AntiBlock Manager

#define MyAppName "AntiBlock Manager"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Kasic"
#define MyAppExeName "AntiBlockManager.exe"

[Setup]
; Уникальный ID приложения. НИКОГДА не меняйте его, если хотите, чтобы обновления устанавливались поверх старой версии, а не рядом с ней.
AppId={{8F3C1A2E-6B7D-4E5A-9C0F-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Папка по умолчанию для установки
DefaultDirName={autopf}\{#MyAppName}
; Имя итогового файла установщика
OutputBaseFilename=AntiBlockManager-Setup-v{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Файл иконки для отображения в "Установка и удаление программ"
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
; Подключаем русский язык
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
; Опция для создания ярлыка на рабочем столе
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Копируем ВСЕ файлы из папки publish в папку установки приложения
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Ярлык в меню "Пуск"
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; Ярлык на рабочем столе (создается, если выбрана соответствующая опция)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Предложить запустить программу после установки
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent