# WatchTorrent

Windows va Android uchun MAUI torrent player. Yuklash ilova ichidagi **libtorrent-rasterbar 2.1.2** orqali ishlaydi. Tashqi torrent dasturi, server yoki Web UI login kerak emas. iOS targeti mavjud, lekin yangi VoiceConnection ovoz adapteri hali qo‘shilmagani uchun iOS app build hozir bloklangan.

## Projectlar

- `WatchTorrent`: MAUI UI, VLC player, fon xizmati va download koordinatori.
- `WatchTorrent.Core`: torrent/fayl modeli, magnet parsing va engine kontrakti.
- `WatchTorrent.Libtorrent`: C# adapter, AOT uchun JSON source generation va cancellable stream.
- `native`: libtorrent ustidagi kichik C ABI; Windows DLL, Android SO, iOS statik archive.
- `TorrentReader`: katalog/qidiruv/sahifa ma'lumotlari.
- `WatchTorrent.EngineTests`: magnet, UI pending holati, engine komandalar va stream testlari.
- `VoiceConnection`: Windows/Android uchun asosiy native NTgCalls/WebRTC ovoz dvigateli. Build va real telefon sinovlari: [VoiceConnection](native/voice/README.md). Eski managed ovoz yo‘li olib tashlangan.

## Build

`global.json` .NET SDK **10.0.404** ni tanlaydi. Platformaning MAUI workloadi va C++ build vositalari kerak. `scripts/build-native.ps1` vcpkg'ni `.tools/vcpkg` ichiga yuklaydi, belgilangan revision va dependency versiyalarini ishlatadi. Birinchi native build Boost/OpenSSL sabab vaqt oladi; keyingi buildlar cache'dan foydalanadi.

Windows (Visual Studio 2026 C++ toolchain):

```powershell
./scripts/build-native.ps1 -Target Windows
./scripts/build-voice.ps1 -Target Windows
./scripts/build.ps1 -Target Tests
./scripts/build.ps1 -Target Windows -RestoreWorkloads
```

Android uchun JDK 17, SDK API/build-tools 36.0.0 va NDK **27.2.12479018** kerak. NDK manzilini `ANDROID_NDK_HOME` bilan bering yoki `-AndroidNdkDirectory` parametrini ishlating:

```powershell
./scripts/build-native.ps1 -Target AndroidArm64
./scripts/build-voice.ps1 -Target Android
./scripts/build.ps1 -Target Android -AndroidRid android-arm64 -RestoreWorkloads
```

32-bit ARM uchun `AndroidArm` / `android-arm`; x64 emulator uchun `AndroidX64` / `android-x64` ni ishlating. Har APK o'z arxitekturasiga mos SO'ni ichiga oladi. SO 16 KB page alignment bilan link qilinadi. NDK C++ runtime va native dependency'lar SO ichiga statik link qilinadi.

Mac + Xcode bilan torrent native kutubxonalarini yig‘ish mumkin. VoiceConnection iOS adapteri tayyor bo‘lmaguncha iOS app build yakunlanmaydi:

```powershell
./scripts/build-native.ps1 -Target iOS
./scripts/build-native.ps1 -Target iOSSimulatorArm64
```

Intel Mac simulator uchun `iOSSimulatorX64` mavjud. iOS archive'iga libtorrent, Boost va OpenSSL statik dependency'lari ham birlashtiriladi; MAUI uni `__Internal` P/Invoke orqali chaqiradi. iPhone/App Store buildlari Apple signing/provisioning talab qiladi. Simulator va unsigned CI compile uchun ular kerak emas. Xcode versiyasi o'rnatilgan iOS workloadiga mos bo'lishi kerak.

Native natijalar `native/artifacts/<rid>/`, tayyor app `artifacts/` ichiga chiqadi; ular Git'ga qo'shilmaydi. Native kutubxona yo'q bo'lsa, app build tushunarli xato bilan to'xtaydi. Litsenziyalar native artifact bilan saqlanadi. Lokal .NET SDK `.tools/dotnet` ichida bo'lsa, `build.ps1`ga `-DotNetPath ./.tools/dotnet/dotnet.exe` qo'shing.

## Interfeys tili

Sozlamalar → Til bo‘limida `Telefon tili (avtomatik)`, `O‘zbekcha` yoki `English` tanlanadi. Birinchi ochilishda qurilma tili olinadi; o‘zbekcha va inglizchadan boshqa tillarda inglizcha ishlatiladi. Qo‘lda tanlangan til saqlanadi va qayta ochilganda ham qo‘llanadi. `Telefon tili`ga qaytish qo‘lda tanlovni o‘chiradi. Matnlar app’ni qayta ishga tushirmasdan almashadi; yuklamalar, playback va ovozli suhbat saqlanadi. Tarjima resurslari app ichida, tashqi tarjima xizmati kerak emas.

## Yuklash va playback

- Libtorrent sessiyasi app ichida ishlaydi; barcha qurilmalarda bitta engine.
- Oddiy yuklash libtorrent'ning odatiy picker'i bilan ishlaydi. Playback/seek paytida o'qiladigan joydan keyingi bo'laklarga deadline beriladi; peerlar pause/start bilan uzilmaydi.
- Player baytlarni `read_piece_alert` orqali oladi va VLC'ga shaxsiy loopback HTTP Range endpoint orqali beradi. Yangi seek eski range/o'qishni bekor qiladi; yuklangan joyga qaytish eski yetishmayotgan bo'lakni kutmaydi. Hali kelmagan sparse disk baytlari VLC'ga berilmaydi.
- Pause/continue/remove darhol pending matni va spinner ko'rsatadi, qayta bosish bloklanadi, xatolarda tugma qayta ochiladi.
- Manifest va fast-resume holati atomik saqlanadi. App fonga o'tganda va har 30 soniyada save qilinadi. Save faol torrentlarni pauza qilmaydi.
- Yuklab olingan fayllar avvalgi papkada qoladi. Avvalgi engine sessiya formatlari import qilinmaydi: magnetni qayta qo'shib o'sha papkani tanlasangiz, libtorrent mavjud fayllarni tekshirib davom etadi.
- Android'da mavjud foreground service/wake lock saqlanadi. iOS app'ni suspend qilganda native torrent ishlashi ham to'xtashi mumkin; libtorrent operatsion tizimning fon cheklovlarini chetlab o'tmaydi.

## Tekshiruv va CI

Player gesturelari Android, Windows va iOS'da bitta boshqaruvdan foydalanadi: bir marta bosish boshqaruvni ochadi/yopadi, ikki marta bosish chap/o'ngda 10 soniya seek, o'rtada pauza/davom; 500 ms bosib turish vaqtincha 2x qiladi. Qo'yib yuborish, pointer cancellation, sirt qayta yaratilishi va fonga o'tish hold'ni tugatadi. iOS'da ham chap/o'ng vertikal surish yorug'lik/media ovozini boshqaradi; player landshaft rejimini so'raydi va yopilganda oldingi ekran holatini qaytaradi. VLC ichki mouse/key boshqaruvi o'chirilgan: buyruqlar ilova orqali o'tadi.

Birga tomosha qilishda tezlik alohida `rateApply` buyrug'i bilan kelishiladi: bu seek/pauza qilmaydi. Tez qo'yib yuborilganda navbatdagi eski 2x buyrug'i bekor qilinadi. Birga tomosha qiladigan ikkala qurilma ham yangi build'da bo'lishi kerak. Tezlik 180 ms davomida bosqichma-bosqich o'zgaradi; qayta hold/release eski o'tishni bekor qiladi. PCM chiqishi uzluksiz, chegaralangan navbat orqali bitta audio qurilmasiga uzatiladi; tezlik almashishi audio qurilmasini qayta ochmaydi yoki buferni tozalamaydi. Seek/stop esa eski PCM'ni alohida bekor qiladi. O'qish kursori bo'yicha avtomatik pauza ishlatilmaydi: VLC o'z buferini boshqaradi. Gesture va sessiya regression testlari managed suite'ga kiritilgan. Qurilmada tekshirish: 1x va 1.25x'dan takroriy hold/release, release oynadan tashqarida, ikkinchi barmoq/cancel, hold paytida fonga o'tish, sekin torrent va ikki qurilmada birga tomosha qilish.

Apple qo'llovi hozir `net10.0-ios`: Apple silicon Mac'da iOS ilovasi sifatida ishlash yo'li ham shu koddan foydalanadi. Alohida macOS/Mac Catalyst target mavjud emas. iOS PiP va HDR/SDR almashtirish hali amalga oshirilmagan; Android bilan to'liq funksiya tengligi deb hisoblamang. Apple qurilmasidagi playback silliqligi va UI uchun Mac/Xcode hamda real qurilmadagi sinov kerak; Windows'dagi reference-assembly compile buning o'rnini bosmaydi.

Managed assertion suite `scripts/build.ps1 -Target Tests` bilan bajariladi. `WatchTorrent.EngineTests` executable'iga `--vlc-path <libvlc-papkasi>` berilsa, haqiqiy VLC bilan yuklanmagan joydan cached seek va takroriy hold/release paytidagi uzluksiz PCM sinovlari ham bajariladi. Native Windows build lokal seeder/downloader bilan metadata, real piece baytlari, pause holatini saqlab qayta tiklash va fayllarni saqlab remove qilishni tekshiradi. `--native` managed smoke test real DLL/session bilan P/Invoke va manifest tiklanishini tekshiradi.

`.github/workflows/build.yml` testlardan keyin Windows, Android ARM64/ARM/x64 va iOS device/simulator native kutubxonalarini yig'ib, app bilan link qilishni tekshiradi. Tayyor Windows app, Android sinov imzoli APK'lar va iOS native archive'lar Actions artifactlariga chiqadi. Android artifact Play Store release keystore'i bilan imzolanmagan.

Haqiqiy internet swarm tezligi seedlar, disk va tarmoqqa bog'liq; engine almashtirish doimiy maksimal tezlikni kafolatlamaydi.

API/build asoslari: [libtorrent hujjatlari](https://libtorrent.org/reference.html), [libtorrent build](https://libtorrent.org/building.html), [vcpkg](https://github.com/microsoft/vcpkg).
