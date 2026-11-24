# WatchTorrent’ni iPhone’da ishga tushirish

Mac’da .NET 10 SDK va Xcode o‘rnatilgan bo‘lishi kerak. Xcode’ni bir marta ochib, iOS platformasi va so‘ralgan komponentlarni o‘rnating. .NET iOS workload’iga mos Xcode versiyasini tanlang. [Xcode mosligi](https://aka.ms/xcode-requirement)

## Qurilmani tayyorlash

1. Xcode → Settings → Apple Accounts orqali Apple Account bilan kiring. Shaxsiy telefonda sinash uchun Personal Team ham ishlaydi. [Apple akkaunti va Personal Team](https://developer.apple.com/help/account/basics/about-your-developer-account)
2. iPhone’ni USB bilan ulang, qulfini oching va **Trust** ni bosing. Xcode qurilmani tanigandan keyin iOS 16 yoki yangiroq versiyada Settings → Privacy & Security → Developer Mode’ni yoqing va so‘ralsa telefonni qayta ishga tushiring. [Developer Mode](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device)
3. WatchTorrent uchun bundle ID `uz.salimovone.watchtorrent`. Bu ID va iPhone uchun development provisioning profile hali bo‘lmasa, Xcode’da vaqtinchalik **iOS App** loyihasi yarating. Signing & Capabilities’da **Automatically manage signing**, o‘z Team’ingiz va aynan shu bundle ID’ni tanlang. iPhone’ni run destination qilib, bir marta ishga tushiring. Shu profil va Apple Development sertifikatini .NET ham ishlata oladi. Bundle ID’ni almashtirsangiz, quyidagi build buyrug‘iga `-p:ApplicationId=YANGI_ID` qo‘shing va launch buyrug‘ida shu ID’ni ishlating.

## Yig‘ish va ishga tushirish

Repo ildizida:

```sh
dotnet --version
xcodebuild -version
dotnet workload install maui-ios
dotnet build WatchTorrent/WatchTorrent.csproj -f net10.0-ios -c Debug -r ios-arm64
xcrun devicectl list devices
```

Ro‘yxatdagi iPhone identifikatorini kiriting:

```sh
DEVICE_ID="IPHONE_IDENTIFIKATORI"
xcrun devicectl device install app --device "$DEVICE_ID" WatchTorrent/bin/Debug/net10.0-ios/ios-arm64/WatchTorrent.app
xcrun devicectl device process launch --device "$DEVICE_ID" uz.salimovone.watchtorrent
```

HTTPS taklif Safari’da ochilganda **Ilovada ochish** tugmasini bosing yoki havolani ilovadagi **Ulanish → Havolani kiritish** bo‘limiga joylang. Mikrofon ruxsati uni birinchi marta yoqqanda so‘raladi. Mahalliy tarmoqdagi peer’lar uchun Local Network ruxsatini bering. Yuklangan kinolar Files → On My iPhone → WatchTorrent ichida saqlanadi.
