# WatchTorrent

Windows, Android va iOS uchun MAUI torrent player. Barcha platformalar ilova ichidagi **libtorrent-rasterbar 2.1.2** orqali yuklaydi. Tashqi torrent dasturi, server yoki Web UI login kerak emas.

## Projectlar

- `WatchTorrent`: MAUI UI, VLC player, fon xizmati va download koordinatori.
- `WatchTorrent.Core`: torrent/fayl modeli, magnet parsing va engine kontrakti.
- `WatchTorrent.Libtorrent`: C# adapter, AOT uchun JSON source generation va cancellable stream.
- `native`: libtorrent ustidagi kichik C ABI; Windows DLL, Android SO, iOS statik archive.
- `TorrentReader`: katalog/qidiruv/sahifa ma'lumotlari.
- `WatchTorrent.EngineTests`: magnet, UI pending holati, engine komandalar va stream testlari.

## Build

`global.json` .NET SDK **10.0.404** ni tanlaydi. Platformaning MAUI workloadi va C++ build vositalari kerak. `scripts/build-native.ps1` vcpkg'ni `.tools/vcpkg` ichiga yuklaydi, belgilangan revision va dependency versiyalarini ishlatadi. Birinchi native build Boost/OpenSSL sabab vaqt oladi; keyingi buildlar cache'dan foydalanadi.

Windows (Visual Studio 2026 C++ toolchain):

```powershell
./scripts/build-native.ps1 -Target Windows
./scripts/build.ps1 -Target Tests
./scripts/build.ps1 -Target Windows -RestoreWorkloads
```

Android uchun JDK 17, SDK API/build-tools 36.0.0 va NDK **27.2.12479018** kerak. NDK manzilini `ANDROID_NDK_HOME` bilan bering yoki `-AndroidNdkDirectory` parametrini ishlating:

```powershell
./scripts/build-native.ps1 -Target AndroidArm64
./scripts/build.ps1 -Target Android -AndroidRid android-arm64 -RestoreWorkloads
```

32-bit ARM uchun `AndroidArm` / `android-arm`; x64 emulator uchun `AndroidX64` / `android-x64` ni ishlating. Har APK o'z arxitekturasiga mos SO'ni ichiga oladi. SO 16 KB page alignment bilan link qilinadi. NDK C++ runtime va native dependency'lar SO ichiga statik link qilinadi.

Mac + Xcode bilan:

```powershell
./scripts/build-native.ps1 -Target iOS
./scripts/build-native.ps1 -Target iOSSimulatorArm64
./scripts/build.ps1 -Target iOSSimulator -RestoreWorkloads
```

Intel Mac simulator uchun `iOSSimulatorX64` mavjud. iOS archive'iga libtorrent, Boost va OpenSSL statik dependency'lari ham birlashtiriladi; MAUI uni `__Internal` P/Invoke orqali chaqiradi. iPhone/App Store buildlari Apple signing/provisioning talab qiladi. Simulator va unsigned CI compile uchun ular kerak emas. Xcode versiyasi o'rnatilgan iOS workloadiga mos bo'lishi kerak.

Native natijalar `native/artifacts/<rid>/`, tayyor app `artifacts/` ichiga chiqadi; ular Git'ga qo'shilmaydi. Native kutubxona yo'q bo'lsa, app build tushunarli xato bilan to'xtaydi. Litsenziyalar native artifact bilan saqlanadi. Lokal .NET SDK `.tools/dotnet` ichida bo'lsa, `build.ps1`ga `-DotNetPath ./.tools/dotnet/dotnet.exe` qo'shing.

## Yuklash va playback

- Libtorrent sessiyasi app ichida ishlaydi; barcha qurilmalarda bitta engine.
- Oddiy yuklash libtorrent'ning odatiy picker'i bilan ishlaydi. Playback/seek paytida o'qiladigan joydan keyingi bo'laklarga deadline beriladi; peerlar pause/start bilan uzilmaydi.
- Player baytlarni `read_piece_alert` orqali oladi. Hali kelmagan sparse disk baytlari VLC'ga berilmaydi. Kutish cancellation bilan to'xtaydi.
- Pause/continue/remove darhol pending matni va spinner ko'rsatadi, qayta bosish bloklanadi, xatolarda tugma qayta ochiladi.
- Manifest va fast-resume holati atomik saqlanadi. App fonga o'tganda va har 30 soniyada save qilinadi. Save faol torrentlarni pauza qilmaydi.
- Yuklab olingan fayllar avvalgi papkada qoladi. Avvalgi engine sessiya formatlari import qilinmaydi: magnetni qayta qo'shib o'sha papkani tanlasangiz, libtorrent mavjud fayllarni tekshirib davom etadi.
- Android'da mavjud foreground service/wake lock saqlanadi. iOS app'ni suspend qilganda native torrent ishlashi ham to'xtashi mumkin; libtorrent operatsion tizimning fon cheklovlarini chetlab o'tmaydi.

## Tekshiruv va CI

Managed assertion suite `scripts/build.ps1 -Target Tests` bilan bajariladi. Native Windows build lokal seeder/downloader bilan metadata, real piece baytlari, pause holatini saqlab qayta tiklash va fayllarni saqlab remove qilishni tekshiradi. `--native` managed smoke test real DLL/session bilan P/Invoke va manifest tiklanishini tekshiradi.

`.github/workflows/build.yml` testlardan keyin Windows, Android ARM64/ARM/x64 va iOS device/simulator native kutubxonalarini yig'ib, app bilan link qilishni tekshiradi. Tayyor Windows app, Android sinov imzoli APK'lar va iOS native archive'lar Actions artifactlariga chiqadi. Android artifact Play Store release keystore'i bilan imzolanmagan.

Haqiqiy internet swarm tezligi seedlar, disk va tarmoqqa bog'liq; engine almashtirish doimiy maksimal tezlikni kafolatlamaydi.

API/build asoslari: [libtorrent hujjatlari](https://libtorrent.org/reference.html), [libtorrent build](https://libtorrent.org/building.html), [vcpkg](https://github.com/microsoft/vcpkg).