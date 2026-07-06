# JESKO Server — o'rnatish va sozlash yo'riqnomasi

Bu server endi **alohida Windows dasturi** bo'lib ishlaydi:
- Soat yonida (system tray) **"J"** ikonkasi chiqadi — server ishlayotganini bildiradi.
- Ikonkani ikki marta bossangiz **Sozlamalar** oynasi ochiladi.
- Ma'lumotlar bazasi **SQLite** (bitta fayl) — boshqa hech narsa o'rnatish kerak emas.
- Kompyuter yonib, foydalanuvchi kirganda server **avtomatik** ishga tushadi.
- Tarmoq: **HTTP**, standart port **5050**, barcha tarmoq interfeyslarida tinglaydi.
- **YANGI:** Server o'zini tarmoqqa "e'lon" qiladi (UDP, port **51999**). Shu tufayli
  Desktop ilova serverni **avtomatik topadi** — IP manzilni qo'lda yozish shart emas.

---

## 1. Dasturchi kompyuterida: build va setup yasash

Kerak bo'ladi (faqat setup yasash uchun, bir marta):
- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
- **Inno Setup** — https://jrsoftware.org/isdl.php

Qadamlar:
1. `installer\publish.bat` ni ishga tushiring.
   - Bu `installer\publish\` papkasiga **self-contained** (ichida .NET runtime bor) fayllarni yig'adi.
   - Natijada `JESKO.Server.exe` hosil bo'ladi — bu boshqa PC'da .NET'siz ishlaydi.
2. `installer\StoreServer.iss` faylini **Inno Setup** da oching va **Compile (F9)** bosing.
3. `installer\Output\JESKO-Server-Setup.exe` hosil bo'ladi — bu yagona o'rnatuvchi fayl.

> Buyruq qatoridan publish (xohlasangiz):
> ```
> dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o installer\publish
> ```

---

## 2. Do'kon (server) kompyuterida: o'rnatish

1. `JESKO-Server-Setup.exe` ni server bo'ladigan kompyuterga ko'chiring va ishga tushiring (**Administrator** sifatida).
2. O'rnatish quyidagilarni avtomatik bajaradi:
   - Dasturni `C:\Program Files\JESKO Server\` ga o'rnatadi.
   - **Avto-ishga tushish** yorlig'ini qo'shadi (Startup).
   - **Windows Firewall** da serverga kiruvchi ulanishlarga ruxsat beradi
     (bu qoida dasturning barcha kiruvchi ulanishlariga ruxsat beradi — HTTP 5050 ham,
     avtomatik aniqlash uchun UDP 51999 ham shu qoida ostida ishlaydi).
3. O'rnatish oxirida server ishga tushadi — soat yonida **"J"** ikonkasi paydo bo'ladi.

### Birinchi kirish (login)
Yangi o'rnatishda standart admin: **login: `admin`**, **parol: `admin123`**.

---

## 3. Server manzili (ko'pincha shart emas — avtomatik topiladi)

**Eng oson yo'l (yangi):** Desktop ilovani bir tarmoqdagi (bitta WiFi/router) kompyuterga
o'rnating va oching. Ilova login oynasida serverni **avtomatik topadi** va o'zi ulanadi.
Hech qanday IP yozish kerak emas. Agar tarmoqda bir nechta server topilsa, ilova ro'yxat
ko'rsatadi — keraklisini tanlaysiz.

**Agar avtomatik topilmasa (zaxira yo'l):** server manzilini qo'lda yozasiz. Manzilni bilish uchun
soat yonidagi **"J"** ikonkasini ikki marta bosing → **Sozlamalar** oynasi ochiladi:
- Yuqorida shu kompyuterning **IP manzillari** ko'rsatiladi (masalan `192.168.1.2`).
- Pastda ilovalarga yoziladigan **to'liq manzil** chiqadi, masalan: `http://192.168.1.2:5050/`
- "Manzilni nusxalash" tugmasi bilan nusxalab olishingiz mumkin.

Qo'lda yozish kerak bo'lsa:
- **Mobil ilova**da: Login ekrani → yuqori o'ngdagi ⚙ → "Server manzili" ga yozasiz
  (faqat IP yozsangiz ham bo'ladi, masalan `192.168.1.2` — port avtomatik 5050 bo'ladi).
- **Desktop ilova**da: Login oynasi → yuqori o'ngdagi **🖧 Server** tugma → server manzilini yozasiz.

---

## 4. Tarmoq (WiFi/LAN) bo'yicha muhim eslatmalar

- Server kompyuteri va barcha qurilmalar **bitta WiFi/router**ga ulangan bo'lishi shart.
- Avtomatik topish ham aynan shu bitta tarmoq ichida ishlaydi.
- Statik IP **endi majburiy emas** (avtomatik topish IP o'zgarsa ham ishlaydi), ammo
  qo'lda manzil yozadigan eski qurilmalar bo'lsa, statik IP berish baribir foydali.
  - Router sozlamasidan yoki Windows: Tarmoq sozlamalari → IPv4 → qo'lda IP.
- Agar ulanmasa: server PC da Windows Firewall qoidasi borligini tekshiring
  (installer buni o'zi qo'shadi; kerak bo'lsa antivirus firewallini ham tekshiring).

## 5. Kompyuter o'chib-yonsa

- Avto-ishga tushish yorlig'i tufayli, foydalanuvchi Windows'ga kirgach server o'zi ishga tushadi.
- "Power on → server ishlaydi" bo'lishi uchun Windows'da **avtomatik login**ni yoqing
  (do'kon kompyuteri uchun qulay). Buni Windows hisob sozlamalaridan yoqasiz.

## 6. Portni o'zgartirish (kamdan-kam kerak)

Sozlamalar oynasida portni o'zgartirib "Saqlash" bosing, so'ng ikonka → **Chiqish** va dasturni
qayta oching. Port o'zgarsa ham Desktop ilova avtomatik topishda yangi portni o'zi oladi.
Qo'lda manzil yozgan qurilmalarda esa manzilni yangilang.

---

## Texnik tafsilotlar
- Sozlama fayli: `C:\ProgramData\StoreSystem\server-config.json`
- Ma'lumotlar bazasi: `C:\ProgramData\StoreSystem\store.db` (SQLite)
- Swagger (test uchun): server PC da `http://localhost:5050/swagger`
- Aloqa tekshiruvi: `http://<server-ip>:5050/api/ping` — `{ "ok": true, ... }` qaytaradi.
- Avtomatik topish: UDP port **51999**. Desktop `JESKO_DISCOVERY_V1?` broadcast yuboradi,
  server `JESKO_DISCOVERY_V1!` + JSON (nom, port, versiya) bilan javob beradi.
