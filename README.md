# 🎟️ RedeemCode — Unturned Redeem Code System (MySQL)

> ระบบเคมโค้ดสำหรับ Unturned (RocketMod) เก็บใน **MySQL** ผู้เล่นพิมพ์ `/code <โค้ด>` แล้วได้ไอเทมที่ตั้งไว้
> 1 คนใช้ได้ครั้งเดียว/โค้ด · จำกัดจำนวนรวมได้ · 1 โค้ดมีหลายไอเทม · จัดการโค้ดผ่าน DB เอง (ต่อ Discord bot ได้)

![Game](https://img.shields.io/badge/game-Unturned-2f9e44)
![Framework](https://img.shields.io/badge/framework-RocketMod-blue)
![DB](https://img.shields.io/badge/db-MySQL-orange)

## How it works
- ผู้เล่น `/code <โค้ด>` → ปลั๊กอิน query MySQL บน **background thread** (ไม่ค้างเซิร์ฟ) → แจกของบน main thread
- ตรวจ: โค้ดมีจริง / เปิดใช้ / ไม่หมดอายุ / คนนี้ยังไม่เคยใช้ / ยังไม่เต็มโควตา → แล้วจองสิทธิ์แบบ atomic (กัน race) → แจกของ
- กระเป๋าเต็ม → ของตกที่เท้า (ไม่หาย)

## Install
1. ตรวจว่าเซิร์ฟมี `MySql.Data.dll` (มีอยู่แล้วถ้าใช้ปลั๊กอิน MySQL อื่น เช่น Teleportation/VirtualGarage)
2. วาง `bin/RedeemCode.dll` ที่ `Rocket/Plugins/RedeemCode/`
3. สตาร์ทเซิร์ฟ 1 ครั้ง → ได้ `RedeemCode.configuration.xml` → ใส่ `ConnectionString`
4. รีสตาร์ท — ปลั๊กอินสร้างตารางให้อัตโนมัติ

## Permission
ใส่ `redeemcode.use` ให้กลุ่มผู้เล่นใน `Permissions.config.xml`

## Config (`RedeemCode.configuration.xml`)
| Field | Default | ความหมาย |
|-------|---------|----------|
| `Database.ConnectionString` | `SERVER=localhost;DATABASE=unturned;UID=root;PASSWORD=...` | การต่อ MySQL |
| `Database.TablePrefix` | `rc_` | prefix ตาราง |
| `MaxAmountPerItem` | `100` | กันตั้งจำนวนต่อไอเทมเกิน (clamp) |
| `Msg*` | EN/TH | ข้อความต่าง ๆ (placeholder `{code}`) |

## Database schema (สร้างอัตโนมัติ)
```sql
-- <prefix> = rc_ ตามค่า default
CREATE TABLE rc_codes (
  id INT AUTO_INCREMENT PRIMARY KEY,
  code VARCHAR(64) NOT NULL UNIQUE,
  max_uses INT NOT NULL DEFAULT 0,      -- 0 = ไม่จำกัดจำนวนรวม
  uses INT NOT NULL DEFAULT 0,
  enabled TINYINT(1) NOT NULL DEFAULT 1,
  expires_at DATETIME NULL,             -- NULL = ไม่หมดอายุ
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE rc_code_items (
  id INT AUTO_INCREMENT PRIMARY KEY,
  code_id INT NOT NULL,
  item_id INT UNSIGNED NOT NULL,                  -- kind=0: item id · kind=1: vehicle id
  amount INT UNSIGNED NOT NULL DEFAULT 1,
  quality TINYINT UNSIGNED NOT NULL DEFAULT 100,  -- คุณภาพ/ความทนทาน 0-100 (ของไอเทม)
  state LONGTEXT NULL,                            -- base64 ของ item.state (อะไหล่/กระสุน) — NULL = ค่า default
  rot TINYINT UNSIGNED NOT NULL DEFAULT 0,        -- สงวนไว้ (rotation)
  kind TINYINT UNSIGNED NOT NULL DEFAULT 0,       -- 0 = ไอเทม, 1 = ยานพาหนะ (vehicle)
  INDEX (code_id)
);
CREATE TABLE rc_redemptions (
  id INT AUTO_INCREMENT PRIMARY KEY,
  code_id INT NOT NULL,
  steam_id BIGINT UNSIGNED NOT NULL,
  redeemed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_code_player (code_id, steam_id)
);
```

## สร้างโค้ดตัวอย่าง (ใส่ใน DB / ให้ Discord bot ทำ)
```sql
-- โค้ด adc14s: 100 คนแรก, ได้ไม้(id 81) x5 และ หิน(id 78) x2
INSERT INTO rc_codes (code, max_uses) VALUES ('adc14s', 100);
SET @cid = LAST_INSERT_ID();
INSERT INTO rc_code_items (code_id, item_id, amount) VALUES
  (@cid, 81, 5),
  (@cid, 78, 2);
```
ผู้เล่นพิมพ์ `/code adc14s` → ได้ของ (โค้ดไม่สนตัวพิมพ์เล็ก/ใหญ่)

## แจกยานพาหนะ (Vehicle rewards)
> โค้ดแจก **ยานพาหนะ** ได้ด้วย โดยตั้ง `kind = 1` แล้วใส่ **vehicle id** ลงใน `item_id`
> (Unturned แยก id ของไอเทมกับยานพาหนะคนละชุดกัน — `kind` เป็นตัวบอกว่า `item_id` หมายถึงอะไร)
>
> A reward row grants a vehicle when `kind = 1`; `item_id` then holds a **vehicle id**
> (item ids and vehicle ids are separate id spaces in Unturned). `kind = 0` (default) = item, as before.

```sql
-- โค้ด ridefree: 50 คนแรก, ได้รถ Roadster (vehicle id 94) x1
INSERT INTO rc_codes (code, max_uses) VALUES ('ridefree', 50);
SET @cid = LAST_INSERT_ID();
INSERT INTO rc_code_items (code_id, item_id, amount, kind) VALUES
  (@cid, 94, 1, 1);   -- kind=1 → item_id (94) คือ vehicle id
```
- ยานพาหนะจะ spawn ใกล้ ๆ ตัวผู้เล่น (ด้านหน้าเล็กน้อย) และ **ล็อก/เป็นเจ้าของให้ผู้เล่นที่ใช้โค้ด**
- `amount` > 1 = spawn หลายคัน · `quality` / `state` ใช้กับไอเทมเท่านั้น (ยานพาหนะไม่เข้ากระเป๋า จึงไม่มีเรื่องของตก)
- ถ้า vehicle id ไม่มีจริง ปลั๊กอินจะ log warning แล้วข้าม (เหมือนกรณี item id ผิด)
- ร้านค้า (shop API/web) ที่ขายยานพาหนะก็ใช้กลไกเดียวกันนี้ (`kind = 1`)

## Notes
- `max_uses = 0` = ไม่จำกัดจำนวนรวม (แต่ยัง 1 คน/ครั้งเดียวเสมอ)
- `enabled = 0` ปิดโค้ดชั่วคราว · ใส่ `expires_at` ให้หมดอายุตามเวลา
- `amount` = จำนวนชิ้นที่แจก (clamp ด้วย `MaxAmountPerItem`) · ยานพาหนะ (`kind=1`) ก็ spawn ตาม `amount`
- หา item id / vehicle id: ใช้ปลั๊กอิน DropMerger `/itemid` หรือ Unturned wiki (vehicle id อยู่คนละชุดกับ item id)

Built by imaximum.tech.
