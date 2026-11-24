# VoiceConnection

WatchTorrent ovozli suhbatlari endi alohida `VoiceConnection` projectidagi native NTgCalls 3.0.7/WebRTC orqali uzatiladi. Eski `VoiceAudio` olib tashlangan. SIPSorcery faqat mavjud data channel, film boshqaruvi va dastlabki signaling uchun qolgan; suhbat ovozini kodlamaydi va ijro qilmaydi. Windows video playerining PCM chiqishi o‘z NAudio dependency’sini saqlaydi.

Bu Telegram Desktop binary’si yoki uning to‘liq `tgcalls` buildi emas. [NTgCalls](https://github.com/pytgcalls/ntgcalls/tree/v3.0.7) — Telegram qo‘ng‘iroq protokollari, native WebRTC, ICE va Opus ishlatadigan alohida ochiq loyiha. Windows va Android adapterlari bir xil 13.0.0 protokolini ishlatadi. Revision, yuklash manzillari va SHA256 `upstream.json`da belgilangan; litsenziya va dependency notices app bilan paketlanadi.

## Build

```powershell
./scripts/build-voice.ps1 -Target Both
./scripts/build.ps1 -Target Android `
  -DotNetPath ./.tools/dotnet/dotnet.exe `
  -AndroidSdkDirectory ./.tools/android-sdk -JavaSdkDirectory ./.tools/jdk
```

`build-voice.ps1 -Target Android` faqat tekshirilgan Android AAR’ni tayyorlaydi. Windows targeti MSVC/CMake orqali kichik C ABI adapterini haqiqiy NTgCalls DLL’iga link qiladi. Binary va AAR’lar `native/artifacts/voice/` ichiga tushadi. Qo‘shimcha pullik xizmat yoki obuna yo‘q.

Native audio tarmoq callback’idan MAUI audio qurilmasiga uzatilmaydi: WebRTC o‘z audio va transport threadlarida ishlaydi. C# faqat chegaralangan signaling/state event navbatini o‘qiydi. Mikrofonni ochish worker’da bajariladi; native shutdown UI thread’ni kutdirmaydi. Mikrofon dastlab o‘chiq, app uni yoqishda ruxsat so‘raydi.

## Tarmoq

Standart yo‘l — P2P va ochiq Google STUN. Sessiyaning 256 bayt kaliti taklif havolasiga yozilmaydi; DTLS data channel ochilgandan keyin yuboriladi. Taklifdagi `audioEngine=voiceconnection-1` eski build bilan tasodifiy ulanishni bloklaydi.

2026-11-24 kuni ulangan Samsung Galaxy S8 / Android 9 ARM64’da quyidagilar real native kutubxonalar bilan o‘tdi:

- Kompyuter va telefon bir xil Wi-Fi’da: ikkala tomonda 30 soniya capture va playback, mute/unmute.
- Telefon mobil internetda, kompyuter boshqa internetda: telefon capture 30 s / playback 31 s, kompyuter capture/playback 30 s, mute/unmute.
- Windows’da ikkita haqiqiy native sessiya bilan ICE loopback.
- Ilovaning to‘liq `DirectPeer` oqimi ham Wi-Fi va mobil internetda o‘tdi: DTLS kanal, shifrlangan native signaling, ikki tomonlama audio, 6 ta ping va mute/unmute. Mobil sinovda ikki tomonda 31 soniya audio kadrlar qayd etildi.
- Yakuniy APK’da bitta Android processni qayta ishga tushirmasdan ketma-ket ikkita to‘liq qo‘ng‘iroq ham o‘tdi. O‘rnatilgan APK SHA256’i berilgan artifact bilan tengligi tekshirildi.

Bu audio kadrlar oqimini tekshiradi; eshitiladigan sifatni, uzoq qo‘ng‘iroqni yoki barcha operator/routerlarni kafolatlamaydi. Ayrim qattiq NAT/firewalllarda bevosita P2P bloklanishi mumkin. Tekshirilgan ochiq TURN serveri javob bermadi, shuning uchun ishlamaydigan yoki pullik relay standart konfiguratsiyaga qo‘shilmadi.

O‘z relay serveri keyinchalik kerak bo‘lsa, `VoiceRelay.Configure(new VoiceRelay(host, port, username, password))` yoki `WATCHTORRENT_TURN_HOST`, `WATCHTORRENT_TURN_PORT`, `WATCHTORRENT_TURN_USERNAME`, `WATCHTORRENT_TURN_PASSWORD` orqali beriladi. Bir xil sozlama control ICE va native audio ICE’ga qo‘llanadi. Hozir native relay adapteri UDP TURN’ni qo‘llaydi; TCP/TLS-only tarmoqlar tekshirilmagan.

## Qurilma regression sinovi

Debug APK’da alohida `VoiceProbeActivity` bor; Release build’da yo‘q. TCP/ADB faqat sinov signalingini olib yuradi, audio real native ICE orqali tarmoqdan o‘tadi. Sinov audio yozuvini saqlamaydi; logda holat va audio kadrlar hisoblagichlari qoladi.

```powershell
adb reverse tcp:47900 tcp:47900
dotnet run --project native/voice/smoke -- D:/projects/watchtorrent/native/artifacts/voice/win-x64/voiceconnection_native.dll --device-host --full-peer
# Boshqa terminalda:
adb shell am start -n com.companyname.watchtorrent/com.companyname.watchtorrent.VoiceProbeActivity --ez full_peer true
adb shell run-as com.companyname.watchtorrent cat files/voice-probe.txt
```

`--full-peer` ilovaning haqiqiy `DirectPeer` va `NativeVoiceAudio` kodini ishlatadi: data channel, kalit almashish, native signaling, ping va ovoz birgalikda tekshiriladi. Windows console hostida faqat UI dispatcher hamda sinovda ishlatilmaydigan film DTO’lari almashtirilgan.

Hozir native platformalar Windows x64 va Android. iOS adapteri hali yo‘q; iOS build eski ovoz dvigateliga yashirin qaytish o‘rniga tushunarli xato beradi.
