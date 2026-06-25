# TikTok Shop PoC — Setup Guide

## สรุปสำหรับนักพัฒนาคนถัดไป

โปรเจคนี้เป็น **Multi-Tenant TikTok Shop Integration** ที่ทำงานแบบ Dual-Engine (Pull + Push)
สร้างด้วย ASP.NET Core 8 / Clean Architecture (3 Layers)

---

## 🗂️ โครงสร้างโปรเจค

```
TikTokShopPoC/
├── TikTokShop.Domain/          ← Interfaces + Models (ไม่มี Logic)
├── TikTokShop.Service/         ← Business Logic ทั้งหมด
│   ├── Stores/TenantStore.cs   ← โหลดข้อมูลร้านค้าจาก Config
│   ├── Helpers/                ← TikTok Signature Helper
│   └── ImplementServices/      ← 4 Services แยกตาม Responsibility
│       ├── AuthService.cs      ← OAuth Token Exchange
│       ├── OrderService.cs     ← Pull Orders + Fetch Detail
│       ├── ShopService.cs      ← Token Health Check
│       └── WebhookService.cs   ← Push Engine (Webhook)
└── TikTokShop.WebAPI/          ← Controllers + Middleware
    ├── Controllers/            ← 4 Controllers แยกตาม Domain
    ├── Middleware/             ← Webhook Signature Verification
    └── Extensions/             ← DI Registration
```

---

## ⚙️ Prerequisites (สิ่งที่ต้องติดตั้งก่อน)

| ซอฟต์แวร์ | Version | ลิงก์ |
|-----------|---------|-------|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com/download |
| IDE | VS 2022 หรือ Rider หรือ VSCode | - |
| ngrok (สำหรับทดสอบ Webhook) | ล่าสุด | https://ngrok.com |
| TikTok Partner Account | - | https://partner.tiktokshop.com |

---

## 🔑 Step 1: เตรียม TikTok Credentials

### 1.1 ขอ AppKey + AppSecret
1. เข้า [TikTok Partner Center](https://partner.tiktokshop.com/datahub/app)
2. สร้าง App → เลือก **Shop API** Permission
3. จด **App Key** และ **App Secret**

### 1.2 ขอ Access Token (OAuth Flow)
1. รัน App ขึ้นมาก่อน (Step 3)
2. เข้า TikTok Seller Center → เชื่อมต่อ App
3. TikTok จะ Redirect มาที่ `/api/auth/callback?code=xxx`
4. App จะ Log AccessToken, ShopCipher, ShopId ออกมาใน Console

### 1.3 ขอ ShopCipher + ShopId
หลังได้ AccessToken แล้ว เรียก:
```
GET /api/shops/{tenantCode}
```
ใน Response จะมี:
- `shops[].cipher` → คือ **ShopCipher**
- `shops[].id` → คือ **ShopId**

---

## 📝 Step 2: ตั้งค่า Local Config (ห้าม Commit!)

Copy ไฟล์ Template มาใช้:

```bash
copy TikTokShop.WebAPI\appsettings.Development.json.example TikTokShop.WebAPI\appsettings.Development.json
```

แล้วเปิด `appsettings.Development.json` และใส่ค่าจริง:

```json
{
  "TikTok": {
    "AppKey": "ใส่ App Key จาก Partner Center",
    "AppSecret": "ใส่ App Secret จาก Partner Center",
    "BaseUrl": "https://open-api.tiktokglobalshop.com"
  },
  "TikTokTenants": {
    "PoC_MobileShop_01": {
      "ShopName": "ชื่อร้านค้าของคุณ",
      "AccessToken": "ROW_xxx... (ได้จาก OAuth)",
      "ShopCipher": "ROW_xxx... (ได้จาก /api/shops)",
      "ShopId": "749xxx... (ได้จาก /api/shops)"
    }
  }
}
```

> **หมายเหตุ:** ไฟล์นี้อยู่ใน `.gitignore` แล้ว — จะไม่ถูก Commit ขึ้น GitHub เด็ดขาด

### เพิ่มร้านค้าใหม่
ไม่ต้องแตะ Code เลย แค่เพิ่ม Block ใหม่ใน `TikTokTenants`:
```json
"TikTokTenants": {
  "PoC_MobileShop_01": { ... },
  "MyNewShop": {
    "ShopName": "ร้านใหม่",
    "AccessToken": "ROW_xxx",
    "ShopCipher": "ROW_xxx",
    "ShopId": "749xxx"
  }
}
```

---

## 🚀 Step 3: รัน App

```bash
cd TikTokShopPoC
dotnet run --project TikTokShop.WebAPI
```

เปิด Swagger UI: **http://localhost:5000/swagger**

---

## 🌐 Step 4: ตั้งค่า Webhook (ถ้าต้องการทดสอบ Push Engine)

### 4.1 เปิด Public URL ด้วย ngrok
```bash
ngrok http 5000
```
จะได้ URL เช่น `https://abc123.ngrok-free.app`

### 4.2 ลงทะเบียน Webhook URL ใน TikTok Partner Center
- Webhook URL: `https://abc123.ngrok-free.app/api/webhook`
- เลือก Event: **Order Status Changed**

### 4.3 ทดสอบผ่าน Swagger
ใส่ Header: `Authorization: POC_PASS` เพื่อ Bypass Signature Check (Development Only)

---

## 📡 API Endpoints

| Method | Route | คำอธิบาย |
|--------|-------|----------|
| `GET` | `/api/auth/callback?code=...` | รับ OAuth Code แลก Token |
| `GET` | `/api/orders/{tenantCode}` | ดึงรายการออเดอร์ |
| `GET` | `/api/orders/detail/{shopId}/{orderId}` | ดูรายละเอียดออเดอร์ (ไม่รอ Webhook) |
| `GET` | `/api/shops/{tenantCode}` | ตรวจสอบสถานะ Token |
| `POST` | `/api/webhook` | รับ Webhook Event จาก TikTok |
| `GET` | `/health` | Health Check |
| `GET` | `/swagger` | Swagger UI |

---

## 🔄 เมื่อ Access Token หมดอายุ

Token มีอายุประมาณ **24 ชั่วโมง** (Sandbox) หรือ **30 วัน** (Production)

แนวทางแก้ไข PoC:
1. เรียก `/api/auth/callback` ใหม่ผ่าน OAuth Flow
2. อัปเดต `AccessToken` ใหม่ใน `appsettings.Development.json`
3. Restart App

แนวทาง Production:
- ใช้ RefreshToken (มีอายุ 180 วัน) ต่ออายุอัตโนมัติ
- เก็บ Token ใน Database + Schedule Task ต่ออายุก่อนหมด

---

## 🗺️ Architecture Overview

```
HTTP Request
     │
     ▼
WebhookSignatureMiddleware  (ตรวจ Signature เฉพาะ POST /api/webhook)
     │
     ▼
Controller (Auth / Order / Shop / Webhook)
     │
     ▼
Service Interface (IAuthService / IOrderService / IShopService / IWebhookService)
     │
     ▼
Service Implementation (อ่าน TenantStore + เรียก TikTok API)
     │
     ▼
TikTok Open API
```
