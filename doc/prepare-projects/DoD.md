Przygotuj prosty system składający się z backendu oraz frontendowego UI, który umożliwia użytkownikowi wysyłanie wielu promptów do przetworzenia oraz śledzenie ich statusu.

Backend powinien być napisany w C# i udostępniać API do dodawania promptów oraz pobierania ich aktualnych stanów. Każdy prompt powinien zostać zapisany w bazie danych.

Oddzielny proces przetwarzający powinien obsługiwać zadania i wykonywać je przy użyciu jednej z dostępnych bibliotek do komunikacji z modelami językowymi, np. z lokalnym modelem lub usługą zewnętrzną. Każde zadanie musi przechodzić przez stany: oczekujące, przetwarzane, zakończone lub nieudane.

Frontend w React ma umożliwiać dodanie wielu promptów oraz wyświetlać listę wszystkich z aktualnymi statusami i wynikami. Odświeżanie może odbywać się za pomocą prostego pollingu.

Mile widziana jest orkiestracja projektu, tak aby cały system dało się łatwo uruchomić jednym poleceniem, oraz dołączenie krótkiej instrukcji, mini dokumentacji, wyjaśniającej jak uruchomić środowisko i poszczególne komponenty.