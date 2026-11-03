# COM Port Overlay

Небольшая панель для Windows, которая показывает выбранные COM-порты одной строкой над часами.

## Возможности

- Показывает подключённые COM-порты компактной строкой.
- Автоматически скрывается в системный трей, если нет портов, отмеченных для показа, и возвращается при подключении такого порта.
- Позволяет скрывать выбранные порты в настройках.
- Поддерживает запуск при входе в Windows.
- Имеет собственный значок приложения и значок в системном трее.

## Скачать и запустить

Скачайте `ComPortOverlay.exe` из раздела [Releases](https://github.com/idrey92/ComPortOverlay/releases/latest) и запустите. Установка не требуется. Нужен .NET Framework 4.x.

Кнопка с шестерёнкой открывает настройки. Там можно выбрать порты для скрытия и включить запуск при входе в Windows.

## Сборка из исходного кода

В командной строке разработчика Visual Studio или из среды с компилятором .NET Framework выполните:

    csc /target:winexe /optimize+ /win32icon:ComPortOverlay.ico /out:ComPortOverlay.exe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Management.dll ComPortOverlay.cs

Значок `ComPortOverlay.ico` содержит варианты 16, 32, 48 и 256 пикселей. Сборка использует стандартные компоненты .NET Framework.
